using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace AutoMail
{
    /// <summary>A send that failed for a known reason; logged without a stack trace.</summary>
    internal sealed class MailerException : Exception
    {
        public MailerException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>One SMTP client per company run, reused for every message.</summary>
    internal sealed class Mailer : IDisposable
    {
        private readonly SmtpClient _client;
        private readonly string _sender;
        // Set after a server-level failure (e.g. rejected login) so the remaining documents are not each retried
        // with the same bad credentials, which can get the mailbox locked.
        private string _disabledReason;

        public Mailer(string senderAddress, string senderPassword)
        {
            if (!EmailValidator.IsValid(senderAddress))
                throw new InvalidOperationException($"Sender email '{senderAddress}' is not valid (OADM.U_SenderMail).");

            _sender = senderAddress;
            _client = new SmtpClient(Settings.SmtpHost, Settings.SmtpPort)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(senderAddress, senderPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
        }

        public void Send(string displayName, IEnumerable<string> to, IEnumerable<string> cc, string subject, string htmlBody,
                         string attachmentName = null, byte[] attachment = null, string replyTo = null)
        {
            if (_disabledReason != null)
                throw new MailerException("not sent; email is disabled for the rest of this run: " + _disabledReason);

            using (var message = new MailMessage())
            {
                message.From = new MailAddress(_sender, displayName);
                message.Priority = MailPriority.High;
                message.Subject = subject;
                message.IsBodyHtml = true;
                message.Body = htmlBody;
                foreach (string address in to) message.To.Add(address);
                if (cc != null) foreach (string address in cc) message.CC.Add(address);
                if (replyTo != null) message.ReplyToList.Add(replyTo);
                if (message.To.Count == 0) throw new InvalidOperationException("No recipients.");
                string testRecipient = Settings.TestRecipient;
                if (testRecipient != null)
                {
                    // Keep the real recipients visible in the subject so the routing can still be checked.
                    message.Subject = $"[TEST to: {message.To}" + (message.CC.Count > 0 ? $"; cc: {message.CC}" : "") + "] " + subject;
                    message.To.Clear();
                    message.CC.Clear();
                    message.To.Add(testRecipient);
                }
                if (attachment != null) message.Attachments.Add(new Attachment(new MemoryStream(attachment), attachmentName, "application/pdf"));

                try
                {
                    _client.Send(message);
                }
                catch (SmtpException ex) when (!(ex is SmtpFailedRecipientException))
                {
                    // SmtpClient reports a rejected login as "net_io_connectionclosed"; say what it usually means.
                    _disabledReason = ex.InnerException is IOException
                        ? $"{Settings.SmtpHost}:{Settings.SmtpPort} closed the connection, which usually means it refused the login " +
                          $"for {_sender}. Check OADM.U_SenderMailPwd (for Gmail it must be a valid app password)"
                        : $"{Settings.SmtpHost}:{Settings.SmtpPort} failed: {ex.StatusCode} {ex.Message}";
                    throw new MailerException(_disabledReason, ex);
                }
            }
        }

        /// <summary>
        /// Logs in to the SMTP server (EHLO, STARTTLS, AUTH LOGIN) without sending anything and returns the server's
        /// reply to the password: "235 ..." means accepted. SmtpClient hides that reply, which is why this is by hand.
        /// </summary>
        public static string TestLogin(string user, string password)
        {
            using (var tcp = new System.Net.Sockets.TcpClient(Settings.SmtpHost, Settings.SmtpPort))
            {
                Stream stream = tcp.GetStream();
                stream.ReadTimeout = stream.WriteTimeout = 15000;
                Expect(Exchange(stream, null), "220");
                Expect(Exchange(stream, "EHLO " + Dns.GetHostName()), "250");
                Expect(Exchange(stream, "STARTTLS"), "220");
                using (var tls = new System.Net.Security.SslStream(stream))
                {
                    tls.AuthenticateAsClient(Settings.SmtpHost);
                    Expect(Exchange(tls, "EHLO " + Dns.GetHostName()), "250");
                    Expect(Exchange(tls, "AUTH LOGIN"), "334");
                    Expect(Exchange(tls, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user))), "334");
                    string reply = Exchange(tls, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)));
                    Exchange(tls, "QUIT");
                    return reply;
                }
            }
        }

        private static void Expect(string reply, string code)
        {
            if (!reply.StartsWith(code)) throw new MailerException($"{Settings.SmtpHost}:{Settings.SmtpPort} answered: {reply}");
        }

        /// <summary>Sends one command (none = just read the greeting) and returns the last line of the reply.</summary>
        private static string Exchange(Stream stream, string command)
        {
            if (command != null)
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(command + "\r\n");
                stream.Write(bytes, 0, bytes.Length);
            }
            // Read byte by byte: a buffered reader would swallow bytes that belong to the TLS handshake after STARTTLS.
            while (true)
            {
                var line = new System.Text.StringBuilder();
                int b;
                while ((b = stream.ReadByte()) >= 0 && b != '\n') if (b != '\r') line.Append((char)b);
                if (b < 0 || line.Length < 4 || line[3] == ' ') return line.ToString();
            }
        }

        /// <summary>Recipients for the log; makes clear when TestRecipient received the message instead.</summary>
        public static string DescribeRecipients(IEnumerable<string> to, IEnumerable<string> cc = null)
        {
            string text = string.Join("; ", to) + (cc == null || !cc.Any() ? "" : " cc " + string.Join("; ", cc));
            return Settings.TestRecipient == null ? text : $"{Settings.TestRecipient} (TEST; would have gone to {text})";
        }

        /// <summary>Splits ';' or ',' separated address lists into distinct valid addresses.</summary>
        public static List<string> ParseAddresses(IEnumerable<string> lists, out List<string> invalid)
        {
            var valid = new List<string>();
            invalid = new List<string>();
            foreach (string list in lists.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                foreach (string raw in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string address = raw.Trim();
                    if (address.Length == 0) continue;
                    if (!EmailValidator.IsValid(address)) invalid.Add(address);
                    else if (!valid.Contains(address, StringComparer.OrdinalIgnoreCase)) valid.Add(address);
                }
            }
            return valid;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    internal static class EmailValidator
    {
        // Simple pattern: no spaces, must have @ and .
        private static readonly Regex EmailRegex =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsValid(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                return false;
            try
            {
                var address = new MailAddress(email);
                return address.Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
