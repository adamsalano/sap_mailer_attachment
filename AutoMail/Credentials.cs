using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using Sap.Data.Hana;

namespace AutoMail
{
    /// <summary>One company from the connectionStrings section of the app config.</summary>
    internal sealed class CompanyCredential
    {
        public string Server;
        public string Database;
        public string SapUser;
        public string SapPassword;
        public string DbUser;
        public string DbPassword;
        public bool CreateFields;
        public string ServiceLayerUrl;

        /// <summary>Host name without the tenant prefix (NDB@) or port.</summary>
        public string HanaHost
        {
            get
            {
                string s = Server;
                int at = s.IndexOf('@');
                if (at >= 0) s = s.Substring(at + 1);
                int colon = s.IndexOf(':');
                return colon >= 0 ? s.Substring(0, colon) : s;
            }
        }

        public string HanaConnectionString
        {
            get
            {
                var b = new HanaConnectionStringBuilder
                {
                    UserName = DbUser,
                    Password = DbPassword,
                    CurrentSchema = Database
                };
                int at = Server.IndexOf('@');
                if (at >= 0)
                {
                    b.Database = Server.Substring(0, at);
                    b.Server = Server.Substring(at + 1);
                }
                else
                {
                    b.Server = Server;
                }
                return b.ConnectionString;
            }
        }

        public string ResolvedServiceLayerUrl
        {
            get
            {
                string url = string.IsNullOrWhiteSpace(ServiceLayerUrl) ? Settings.ServiceLayerUrl : ServiceLayerUrl;
                return url.Replace("{host}", HanaHost).TrimEnd('/') + "/";
            }
        }
    }

    /// <summary>
    /// Loads company credentials from the connectionStrings section of the app config (one entry per company,
    /// named after the database). Run "OAF_AutoMail.exe --protect-credentials" once on the server to encrypt it.
    /// </summary>
    internal static class CredentialStore
    {
        public static List<CompanyCredential> Load()
        {
            List<CompanyCredential> companies = LoadFromConfig(ConfigurationManager.ConnectionStrings);
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            if (companies.Count > 0 && !config.ConnectionStrings.SectionInformation.IsProtected)
                Log.Warn("", "Credentials read from plain-text app config. Run with --protect-credentials to encrypt them.");
            return companies;
        }

        /// <summary>Encrypts the connectionStrings section of OAF_AutoMail.exe.config in place (machine-bound DPAPI).</summary>
        public static int Protect()
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            int configured = LoadFromConfig(config.ConnectionStrings.ConnectionStrings).Count;
            SectionInformation section = config.ConnectionStrings.SectionInformation;
            if (configured > 0 && !section.IsProtected)
            {
                section.ProtectSection("DataProtectionConfigurationProvider");
                section.ForceSave = true;
                config.Save(ConfigurationSaveMode.Modified);
            }
            return configured;
        }

        /// <summary>
        /// Reads entries like
        /// &lt;add name="OAF_KE" connectionString="Server=NDB@host:30013;SapUser=..;SapPassword=..;DbUser=..;DbPassword=..;CreateFields=Y"/&gt;.
        /// The entry name is the company database. Entries without SapUser (e.g. machine.config's LocalSqlServer) are ignored.
        /// Quote a value that contains ';' or '=' with single quotes.
        /// </summary>
        private static List<CompanyCredential> LoadFromConfig(ConnectionStringSettingsCollection entries)
        {
            var result = new List<CompanyCredential>();
            foreach (ConnectionStringSettings entry in entries)
            {
                var b = new DbConnectionStringBuilder { ConnectionString = entry.ConnectionString };
                if (!b.ContainsKey("SapUser")) continue;

                string Value(string key) => b.TryGetValue(key, out object v) ? Convert.ToString(v).Trim() : "";
                var credential = new CompanyCredential
                {
                    Server = Value("Server"),
                    Database = entry.Name,
                    SapUser = Value("SapUser"),
                    SapPassword = Value("SapPassword"),
                    DbUser = Value("DbUser"),
                    DbPassword = Value("DbPassword"),
                    CreateFields = Value("CreateFields").Equals("Y", StringComparison.OrdinalIgnoreCase),
                    ServiceLayerUrl = Value("ServiceLayerUrl").Length > 0 ? Value("ServiceLayerUrl") : null
                };
                if (credential.Server.Length == 0 || credential.SapPassword.Length == 0 || credential.DbUser.Length == 0 ||
                    credential.DbPassword.Length == 0)
                {
                    Log.Error("", $"Connection string '{entry.Name}' is missing Server, SapPassword, DbUser or DbPassword; skipped.");
                    continue;
                }
                result.Add(credential);
            }
            return result;
        }
    }
}
