using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace AutoMail
{
    /// <summary>
    /// Typed access to the appSettings in app.config. Settings that take a company can be overridden per
    /// company database by suffixing the key with ".DBNAME", e.g. EmailJobs.KE_LIVE.
    /// </summary>
    internal static class Settings
    {
        public static string LogDirectory => Get("LogDirectory", @"C:\LOG");

        /// <summary>Service Layer base URL; {host} is replaced with the company's HANA host.</summary>
        public static string ServiceLayerUrl => Get("ServiceLayerUrl", "https://{host}:50000/b1s/v1");
        public static bool TrustServiceLayerCertificate => GetBool("TrustServiceLayerCertificate", false);

        public static string SmtpHost => Get("SmtpHost", "smtp.gmail.com");
        public static int SmtpPort => GetInt("SmtpPort", 587);

        /// <summary>When set, every email goes only to this address (To and CC are replaced). For testing.</summary>
        public static string TestRecipient => Get("TestRecipient", null);

        /// <summary>Maximum documents processed per job per run; 0 means no limit.</summary>
        public static int BatchSize => GetInt("BatchSize", 500);

        /// <summary>How many companies run at the same time.</summary>
        public static int MaxParallelCompanies => Math.Max(1, GetInt("MaxParallelCompanies", 1));

        /// <summary>Run each company in its own worker process (Crystal renders one report at a time per process).</summary>
        public static bool ProcessPerCompany => GetBool("ProcessPerCompany", true);

        /// <summary>How many job groups run at the same time inside one company.</summary>
        public static int MaxParallelJobsPerCompany => Math.Max(1, GetInt("MaxParallelJobsPerCompany", 1));

        public static ISet<string> AttachJobs(string company) => GetSet("AttachJobs", company);
        public static ISet<string> EmailJobs(string company) => GetSet("EmailJobs", company);
        public static ISet<string> NotificationJobs(string company) => GetSet("NotificationJobs", company);

        public static DateTime? AttachFromDate(string company) => GetDate("AttachFromDate", company);
        public static DateTime? EmailFromDate(string company) => GetDate("EmailFromDate", company);
        public static DateTime? OutgoingPaymentsFromDate(string company) => GetDate("OutgoingPaymentsFromDate", company);
        public static DateTime? TransferNotificationsFromDate(string company) => GetDate("TransferNotificationsFromDate", company);

        /// <summary>Crystal report (in the Report folder) used for remittance advices.</summary>
        public static string PaymentsReport(string company) => Get("Report.OutgoingPayments", "Outgoing Payment_New.rpt", company);

        /// <summary>Reply-To address (and contact address in the body) of remittance advice emails.</summary>
        public static string PaymentsReplyTo(string company) => Get("PaymentsReplyTo", "AP.Finance@oneacrefund.org", company);

        /// <summary>Optional per-job override of the HANA procedure name, e.g. Procedure.SalesOrder.</summary>
        public static string Procedure(string jobName, string fallback) => Get("Procedure." + jobName, fallback);

        private static string Get(string key, string fallback, string company = null)
        {
            if (company != null)
            {
                string scoped = ConfigurationManager.AppSettings[key + "." + company];
                if (scoped != null) return scoped.Trim();
            }
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int GetInt(string key, int fallback)
        {
            return int.TryParse(Get(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static bool GetBool(string key, bool fallback)
        {
            return bool.TryParse(Get(key, null), out bool value) ? value : fallback;
        }

        private static DateTime? GetDate(string key, string company)
        {
            string value = Get(key, null, company);
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                return date;
            throw new ConfigurationErrorsException($"appSetting '{key}' must be a date in yyyy-MM-dd format, got '{value}'.");
        }

        private static ISet<string> GetSet(string key, string company)
        {
            return new HashSet<string>(
                (Get(key, "", company) ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
