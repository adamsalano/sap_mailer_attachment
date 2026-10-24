using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AutoMail
{
    /// <summary>Command-line filters, so different companies or documents can run as separate scheduled tasks.</summary>
    internal sealed class RunFilter
    {
        public ISet<string> Companies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public ISet<string> Jobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IncludesCompany(string name) => Companies.Count == 0 || Companies.Contains(name);
        public bool IncludesJob(string name) => Jobs.Count == 0 || Jobs.Contains(name);
    }

    /// <summary>
    /// Runs every enabled job for every company. Companies run in parallel, and inside a company the jobs run
    /// in parallel in groups: jobs that update the same SAP table share a group and run one after another,
    /// so two Service Layer sessions never PATCH the same document at once.
    /// </summary>
    internal sealed class AutoMailRunner
    {
        private readonly RunFilter _filter;

        public AutoMailRunner(RunFilter filter)
        {
            _filter = filter;
        }

        public void Run()
        {
            Stopwatch runTimer = Stopwatch.StartNew();
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Log.Info("", $"AutoMail started: build {System.IO.File.GetLastWriteTime(exe):yyyy-MM-dd HH:mm}, " +
                         $"{Environment.MachineName}\\{Environment.UserName}, " +
                         $"companies={(_filter.Companies.Count == 0 ? "all" : string.Join(",", _filter.Companies))}, " +
                         $"jobs={(_filter.Jobs.Count == 0 ? "all" : string.Join(",", _filter.Jobs))}, " +
                         $"batch={Settings.BatchSize}, parallel={Settings.MaxParallelCompanies}x{Settings.MaxParallelJobsPerCompany}");
            try
            {
                RunCompanies();
            }
            finally
            {
                Log.Info("", $"AutoMail finished in {runTimer.Elapsed.TotalSeconds:0}s: {Log.Errors} error(s), {Log.Warnings} warning(s)");
            }
        }

        private void RunCompanies()
        {
            List<CompanyCredential> companies;
            try
            {
                companies = CredentialStore.Load().Where(c => _filter.IncludesCompany(c.Database)).ToList();
            }
            catch (Exception ex)
            {
                Log.Error("", "Could not load credentials", ex);
                return;
            }
            if (companies.Count == 0)
            {
                Log.Warn("", "No companies to run (check connectionStrings in OAF_AutoMail.exe.config and --company).");
                return;
            }

            if (Settings.TestRecipient != null)
                Log.Warn("", $"TestRecipient is set: every email goes to {Settings.TestRecipient} instead of the real recipients.");

            if (Settings.ProcessPerCompany && companies.Count > 1)
            {
                RunWorkerProcesses(companies);
                return;
            }

            Parallel.ForEach(companies, new ParallelOptions { MaxDegreeOfParallelism = Settings.MaxParallelCompanies }, company =>
            {
                Stopwatch timer = Stopwatch.StartNew();
                Log.Info(company.Database, "Run started");
                try
                {
                    RunCompany(company);
                }
                catch (Exception ex)
                {
                    Log.Error(company.Database, "Company run aborted", ex);
                }
                Log.Info(company.Database, $"Run finished in {timer.Elapsed.TotalSeconds:0}s");
            });
        }

        /// <summary>
        /// Runs each company in its own OAF_AutoMail.exe --company=X process. Crystal can only render one report at a
        /// time per process, so this multiplies PDF throughput, and a native crash only stops that one company.
        /// </summary>
        private void RunWorkerProcesses(List<CompanyCredential> companies)
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string jobs = _filter.Jobs.Count > 0 ? $" \"--jobs={string.Join(",", _filter.Jobs)}\"" : "";
            Parallel.ForEach(companies, new ParallelOptions { MaxDegreeOfParallelism = Settings.MaxParallelCompanies }, company =>
            {
                Stopwatch timer = Stopwatch.StartNew();
                var start = new ProcessStartInfo(exe, $"\"--company={company.Database}\"{jobs}") { UseShellExecute = false, CreateNoWindow = true };
                try
                {
                    using (Process worker = Process.Start(start))
                    {
                        Log.Info(company.Database, $"Worker process {worker.Id} started");
                        worker.WaitForExit();
                        if (worker.ExitCode != 0)
                            Log.Error(company.Database, $"Worker process {worker.Id} exited with code 0x{worker.ExitCode:X8} after " +
                                                        $"{timer.Elapsed.TotalSeconds:0}s (0xC0000005 = crash); its last [pid {worker.Id}] lines show where it stopped");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(company.Database, "Could not start worker process", ex);
                }
            });
        }

        private void RunCompany(CompanyCredential company)
        {
            string name = company.Database;
            List<DocumentJob> attachJobs = DocumentJob.All
                .Where(j => j.CanAttach && Settings.AttachJobs(name).Contains(j.Name) && _filter.IncludesJob(j.Name)).ToList();
            List<DocumentJob> emailJobs = DocumentJob.All
                .Where(j => Settings.EmailJobs(name).Contains(j.Name) && _filter.IncludesJob(j.Name)).ToList();
            if (attachJobs.Count > 0 && !Settings.AttachFromDate(name).HasValue)
            {
                Log.Error(name, "AttachJobs are enabled but appSetting AttachFromDate is not set; attaching skipped.");
                attachJobs.Clear();
            }
            if (emailJobs.Count > 0 && !Settings.EmailFromDate(name).HasValue)
            {
                Log.Error(name, "EmailJobs are enabled but appSetting EmailFromDate is not set; document emails skipped.");
                emailJobs.Clear();
            }
            ISet<string> notifications = Settings.NotificationJobs(name);
            bool payments = notifications.Contains(NotificationJobs.OutgoingPaymentsName) && _filter.IncludesJob(NotificationJobs.OutgoingPaymentsName);
            bool transfers = notifications.Contains(NotificationJobs.TransferNotificationsName) && _filter.IncludesJob(NotificationJobs.TransferNotificationsName);

            string Jobs(IEnumerable<string> jobs, DateTime? from) =>
                jobs.Any() ? $"{string.Join(",", jobs)} from {from:yyyy-MM-dd}" : "none";
            Log.Info(name, $"Server {company.Server}, Service Layer {company.ResolvedServiceLayerUrl}; " +
                           $"attach: {Jobs(attachJobs.Select(j => j.Name), Settings.AttachFromDate(name))}; " +
                           $"email: {Jobs(emailJobs.Select(j => j.Name), Settings.EmailFromDate(name))}; " +
                           $"payments: {(payments ? $"from {Settings.OutgoingPaymentsFromDate(name):yyyy-MM-dd}" : "off")}; " +
                           $"transfers: {(transfers ? $"from {Settings.TransferNotificationsFromDate(name):yyyy-MM-dd}" : "off")}");

            // Group work by the SAP table it updates.
            var groups = new Dictionary<string, List<KeyValuePair<string, Action<CompanyContext>>>>();
            Action<string, string, Action<CompanyContext>> add = (table, jobName, work) =>
            {
                if (!groups.TryGetValue(table, out var list)) groups[table] = list = new List<KeyValuePair<string, Action<CompanyContext>>>();
                list.Add(new KeyValuePair<string, Action<CompanyContext>>(jobName, work));
            };
            foreach (DocumentJob job in DocumentJob.All)
            {
                bool attach = attachJobs.Contains(job);
                bool email = emailJobs.Contains(job);
                if (attach || email)
                    add(job.Table, job.Name, ctx => new DocumentJobRunner(ctx).Run(job, attach, email));
            }
            if (payments) add("OVPM", NotificationJobs.OutgoingPaymentsName, NotificationJobs.OutgoingPayments);
            if (transfers) add("OWTR", NotificationJobs.TransferNotificationsName, NotificationJobs.TransferNotifications);

            if (groups.Count == 0)
            {
                Log.Warn(name, "No jobs enabled for this company.");
                return;
            }

            if (company.CreateFields)
            {
                using (CompanyContext setup = CompanyContext.Open(company, loadCompanyDetails: false))
                {
                    UserFields.Ensure(setup, attachJobs, emailJobs, payments, transfers);
                    ProcedureInstaller.Ensure(setup.Db, name);
                }
            }

            Parallel.ForEach(groups.Values, new ParallelOptions { MaxDegreeOfParallelism = Settings.MaxParallelJobsPerCompany }, group =>
            {
                CompanyContext ctx;
                try
                {
                    ctx = CompanyContext.Open(company);
                }
                catch (Exception ex)
                {
                    Log.Error(name, $"Could not connect for {string.Join(", ", group.Select(g => g.Key))}", ex);
                    return;
                }
                using (ctx)
                {
                    foreach (var job in group)
                    {
                        try
                        {
                            job.Value(ctx);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(name, $"{job.Key}: job aborted", ex);
                        }
                    }
                }
            });
        }
    }
}
