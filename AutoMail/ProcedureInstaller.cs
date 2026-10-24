using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoMail
{
    /// <summary>
    /// Creates the HANA procedures the enabled jobs need when a company schema does not have them yet, from
    /// Procedures\&lt;procedure name&gt;.sql next to the exe. Existing procedures are never changed.
    /// </summary>
    internal static class ProcedureInstaller
    {
        public static string Folder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Procedures");

        public static string ScriptPath(string procedure) => Path.Combine(Folder, procedure + ".sql");

        /// <summary>Procedure names the jobs enabled for this company call (after Procedure.&lt;Job&gt; overrides).</summary>
        public static List<string> Required(string company)
        {
            var names = DocumentJob.All
                .Where(j => (j.CanAttach && Settings.AttachJobs(company).Contains(j.Name)) || Settings.EmailJobs(company).Contains(j.Name))
                .Select(j => Settings.Procedure(j.Name, j.Procedure))
                .ToList();
            if (Settings.NotificationJobs(company).Contains(NotificationJobs.OutgoingPaymentsName))
                names.Add(NotificationJobs.PaymentsProcedure);
            return names.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>Creates every required procedure that is missing and has a script; logs the rest.</summary>
        public static void Ensure(HanaDb db, string company)
        {
            foreach (string procedure in Required(company).Where(p => !db.ProcedureExists(p)))
            {
                string path = ScriptPath(procedure);
                if (!File.Exists(path))
                {
                    Log.Warn(company, $"Procedure \"{procedure}\" is missing and there is no script {path}; its job will be skipped.");
                    continue;
                }
                try
                {
                    db.Execute(ReadScript(procedure, path));
                    Log.Info(company, $"Created HANA procedure \"{procedure}\" from {path}");
                }
                catch (Exception ex)
                {
                    Log.Error(company, $"Could not create procedure \"{procedure}\" from {path}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// The script must create exactly the procedure its file is named after, with unqualified table names so it
        /// lands in (and reads from) the company schema the connection is on.
        /// </summary>
        private static string ReadScript(string procedure, string path)
        {
            string sql = File.ReadAllText(path).Trim();
            if (sql.IndexOf($"CREATE PROCEDURE \"{procedure}\"", StringComparison.OrdinalIgnoreCase) != 0)
                throw new InvalidOperationException($"the script must start with CREATE PROCEDURE \"{procedure}\"");
            // HANA rejects a statement terminator after the procedure's final END.
            return sql.TrimEnd(';', ' ', '\r', '\n', '\t');
        }
    }
}
