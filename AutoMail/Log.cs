using System;
using System.IO;

namespace AutoMail
{
    /// <summary>Daily text log, same location and file name pattern as the original logger.</summary>
    internal static class Log
    {
        private static readonly object Sync = new object();

        // Tells apart lines from separately scheduled instances that share the daily file.
        private static readonly int ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

        private static int _warnings, _errors;

        /// <summary>Warnings and errors written by this process, for the end-of-run summary.</summary>
        public static int Warnings => _warnings;
        public static int Errors => _errors;

        public static void Info(string company, string message) => Write("INFO", company, message);

        public static void Warn(string company, string message)
        {
            System.Threading.Interlocked.Increment(ref _warnings);
            Write("WARN", company, message);
        }

        public static void Error(string company, string message, Exception ex = null)
        {
            System.Threading.Interlocked.Increment(ref _errors);
            // Service Layer and mailer errors already say what was rejected and why; a stack trace only adds noise there.
            string detail = ex is ServiceLayerException || ex is MailerException ? ex.Message : ex?.ToString();
            Write("ERROR", company, ex == null ? message : message + " :: " + detail);
        }

        private static void Write(string level, string company, string message)
        {
            try
            {
                string dir = Settings.LogDirectory;
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "Email_Logger_" + DateTime.Now.ToString("ddMMyyyy") + ".txt");
                // One entry per line so the file can be grepped; stack traces are folded with " | ".
                string flat = (message ?? "").Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " | ");
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] [pid {ProcessId}] {company} - {flat}{Environment.NewLine}";
                lock (Sync)
                {
                    // Separately scheduled instances may share the file; retry briefly on contention.
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                            using (var writer = new StreamWriter(stream))
                                writer.Write(line);
                            break;
                        }
                        catch (IOException) when (attempt < 5)
                        {
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never throw from logging.
                System.Diagnostics.Debug.WriteLine("Unable to write to log file: " + ex.Message);
            }
        }
    }
}
