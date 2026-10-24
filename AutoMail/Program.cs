using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AutoMail
{
    class Program
    {
        /// <summary>
        /// Usage:
        ///   OAF_AutoMail.exe                                   run every company and every enabled job
        ///   OAF_AutoMail.exe --company=KE_LIVE,RW_LIVE         only these company databases
        ///   OAF_AutoMail.exe --jobs=OutgoingPayments           only these jobs
        ///   OAF_AutoMail.exe --check [--company=...]           test connections and prerequisites; processes nothing
        ///   OAF_AutoMail.exe --protect-credentials            encrypt the connectionStrings in OAF_AutoMail.exe.config
        /// </summary>
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            if (args.Any(a => a.Equals("--protect-credentials", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    int count = CredentialStore.Protect();
                    MessageBox.Show(count > 0
                                        ? $"connectionStrings encrypted for {count} company(ies) in OAF_AutoMail.exe.config."
                                        : "No companies found in the connectionStrings section of OAF_AutoMail.exe.config.",
                                    "OAF AutoMail");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "OAF AutoMail", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            var filter = new RunFilter();
            foreach (string arg in args)
            {
                if (arg.StartsWith("--company=", StringComparison.OrdinalIgnoreCase))
                    filter.Companies.UnionWith(SplitList(arg.Substring("--company=".Length)));
                else if (arg.StartsWith("--jobs=", StringComparison.OrdinalIgnoreCase))
                    filter.Jobs.UnionWith(SplitList(arg.Substring("--jobs=".Length)));
            }

            if (args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode = ConnectionCheck.Run(filter) == 0 ? 0 : 1;
                return;
            }

            // One instance per filter: overlapping runs of the same work would attach and email documents twice,
            // while differently filtered scheduled tasks may run side by side.
            string scope = string.Join("_", filter.Companies.OrderBy(c => c)) + "__" + string.Join("_", filter.Jobs.OrderBy(j => j));
            string mutexName = @"Global\OAF_AutoMail_" + Regex.Replace(scope, "[^A-Za-z0-9_]", "_");
            using (var mutex = new Mutex(true, mutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    Log.Warn("", $"Another AutoMail instance with the same filter is already running ({string.Join(" ", args)}); exiting.");
                    return;
                }
                try
                {
                    new AutoMailRunner(filter).Run();
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static string[] SplitList(string value)
        {
            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        }
    }
}
