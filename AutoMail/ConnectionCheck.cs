using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AutoMail
{
    /// <summary>
    /// "OAF_AutoMail.exe --check": verifies every company's connections and prerequisites without processing a
    /// document or sending an email. Each step is logged as CHECK OK / CHECK FAIL; the exit code is 1 on any failure.
    /// </summary>
    internal static class ConnectionCheck
    {
        /// <summary>A failed check whose message says everything; logged without a stack trace.</summary>
        private sealed class CheckFailed : Exception
        {
            public CheckFailed(string message) : base(message) { }
        }

        private static string ReportFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Report");

        /// <summary>Returns the number of failed checks.</summary>
        public static int Run(RunFilter filter)
        {
            Log.Info("", "Connection check started: nothing is processed and no email is sent.");
            List<CompanyCredential> companies;
            try
            {
                companies = CredentialStore.Load().Where(c => filter.IncludesCompany(c.Database)).ToList();
            }
            catch (Exception ex)
            {
                Log.Error("", "CHECK FAIL credentials: could not read connectionStrings", ex);
                return 1;
            }
            if (companies.Count == 0)
            {
                Log.Error("", "CHECK FAIL credentials: no companies in connectionStrings (or none match --company)");
                return 1;
            }

            int failed = companies.Sum(Check);
            if (failed == 0) Log.Info("", $"Connection check finished: all checks passed for {companies.Count} company(ies).");
            else Log.Error("", $"Connection check finished: {failed} check(s) failed; see the CHECK FAIL lines above.");
            return failed;
        }

        private static int Check(CompanyCredential company)
        {
            string name = company.Database;
            int failed = 0;
            bool Step(string what, Func<string> test)
            {
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    string detail = test();
                    Log.Info(name, $"CHECK OK   {what}: {detail} ({timer.ElapsedMilliseconds} ms)");
                    return true;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (ex is CheckFailed) Log.Error(name, $"CHECK FAIL {what}: {ex.Message}");
                    else Log.Error(name, $"CHECK FAIL {what}", ex);
                    return false;
                }
            }

            HanaDb db = null;
            try
            {
                Step("HANA connection", () => { db = new HanaDb(company); return $"{company.Server}, schema {name}"; });

                string sender = null, password = null;
                if (db != null)
                {
                    Step("company details (OADM)", () =>
                    {
                        DataRow row = db.Query(@"SELECT ""Country"", ""Fax"", ""U_SenderMail"", ""U_SenderMailPwd"" FROM ""OADM""").Rows[0];
                        sender = Convert.ToString(row["U_SenderMail"]).Trim();
                        password = Convert.ToString(row["U_SenderMailPwd"]).Trim();
                        string copy = Convert.ToString(row["Fax"]).Trim();
                        if (!EmailValidator.IsValid(sender)) throw new CheckFailed($"OADM.U_SenderMail '{sender}' is not a valid email address");
                        if (password.Length == 0) throw new CheckFailed("OADM.U_SenderMailPwd is empty");
                        return $"country {Convert.ToString(row["Country"]).Trim()}, sender {sender}, payments copy {(copy.Length == 0 ? "(none)" : copy)}";
                    });
                }

                Step("Service Layer login", () =>
                {
                    using (var serviceLayer = new ServiceLayerClient(company)) serviceLayer.Login();
                    return company.ResolvedServiceLayerUrl;
                });

                List<string> procedures = ProcedureInstaller.Required(name);
                List<string> reports = DocumentJob.All
                    .Where(j => (j.CanAttach && Settings.AttachJobs(name).Contains(j.Name)) || Settings.EmailJobs(name).Contains(j.Name))
                    .Select(j => j.ReportFile).ToList();
                if (Settings.NotificationJobs(name).Contains(NotificationJobs.OutgoingPaymentsName))
                    reports.Add(Settings.PaymentsReport(name));

                if (db != null && procedures.Count > 0)
                {
                    Step("HANA procedures", () =>
                    {
                        List<string> missing = procedures.Where(p => !db.ProcedureExists(p)).ToList();
                        if (missing.Count == 0) return $"{procedures.Count} found";
                        // Say per procedure whether the next run can create it (CreateFields=Y + a script in Procedures\).
                        string Detail(string p) => File.Exists(ProcedureInstaller.ScriptPath(p))
                            ? (company.CreateFields ? $"{p} (script found, created on the next run)" : $"{p} (script found, but CreateFields=N)")
                            : $"{p} (no script {ProcedureInstaller.ScriptPath(p)})";
                        throw new CheckFailed($"missing in schema {name}: {string.Join(", ", missing.Select(Detail))}");
                    });
                }
                if (reports.Count > 0)
                {
                    Step("Crystal reports", () =>
                    {
                        List<string> missing = reports.Distinct().Where(r => !File.Exists(Path.Combine(ReportFolder, r))).ToList();
                        if (missing.Count > 0) throw new CheckFailed($"missing in {ReportFolder}: {string.Join(", ", missing)}");
                        return $"{reports.Distinct().Count()} found";
                    });
                }

                if (sender != null && password != null && password.Length > 0)
                {
                    Step("SMTP login", () =>
                    {
                        string reply = Mailer.TestLogin(sender, password);
                        if (!reply.StartsWith("235")) throw new CheckFailed($"{Settings.SmtpHost}:{Settings.SmtpPort} refused {sender}: {reply}");
                        return $"{Settings.SmtpHost}:{Settings.SmtpPort} as {sender}";
                    });
                }
            }
            finally
            {
                db?.Dispose();
            }
            return failed;
        }
    }
}
