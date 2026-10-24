using System;
using System.Data;
using System.IO;

namespace AutoMail
{
    /// <summary>
    /// Everything one worker needs for one company: its own HANA connection, Service Layer session,
    /// report cache and SMTP client. None of these are thread-safe, so parallel workers never share a context.
    /// </summary>
    internal sealed class CompanyContext : IDisposable
    {
        private static string ReportFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Report");

        private Mailer _mailer;
        private string _senderMail;
        private string _senderPassword;

        private CompanyContext(CompanyCredential credential)
        {
            Credential = credential;
        }

        /// <summary>Connects to HANA and logs in to Service Layer.</summary>
        public static CompanyContext Open(CompanyCredential credential, bool loadCompanyDetails = true)
        {
            var ctx = new CompanyContext(credential);
            try
            {
                ctx.Db = new HanaDb(credential);
                ctx.ServiceLayer = new ServiceLayerClient(credential);
                ctx.ServiceLayer.Login();
                ctx.Reports = new ReportRenderer(ReportFolder);
                if (loadCompanyDetails) ctx.LoadCompanyDetails();
                return ctx;
            }
            catch
            {
                ctx.Dispose();
                throw;
            }
        }

        public CompanyCredential Credential { get; }
        public HanaDb Db { get; private set; }
        public ServiceLayerClient ServiceLayer { get; private set; }
        public ReportRenderer Reports { get; private set; }

        /// <summary>Company database name; used as the log prefix and in attachment file names.</summary>
        public string Name => Credential.Database;

        /// <summary>OADM.Country, used in email subjects.</summary>
        public string Country { get; private set; }

        /// <summary>OADM.Fax, used as the copy address on remittance advices.</summary>
        public string CompanyCopyEmail { get; private set; }

        /// <summary>SQL suffix that applies the configured batch size.</summary>
        public string Limit => Settings.BatchSize > 0 ? " LIMIT " + Settings.BatchSize : "";

        public Mailer Mailer => _mailer ?? (_mailer = new Mailer(_senderMail, _senderPassword));

        public void LoadCompanyDetails()
        {
            DataTable oadm = Db.Query(@"SELECT ""Country"", ""Fax"", ""U_SenderMail"", ""U_SenderMailPwd"" FROM ""OADM""");
            DataRow row = oadm.Rows[0];
            Country = Convert.ToString(row["Country"]).Trim();
            CompanyCopyEmail = Convert.ToString(row["Fax"]).Trim();
            _senderMail = Convert.ToString(row["U_SenderMail"]).Trim();
            _senderPassword = Convert.ToString(row["U_SenderMailPwd"]).Trim();
        }

        public void Dispose()
        {
            _mailer?.Dispose();
            Reports?.Dispose();
            ServiceLayer?.Dispose();
            Db?.Dispose();
        }
    }
}
