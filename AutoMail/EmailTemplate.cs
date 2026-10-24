using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace AutoMail
{
    /// <summary>
    /// Shared HTML layout for every email: header bar, title, message, a details table and a footer.
    /// Table-based with inline styles so it renders the same in Outlook and Gmail.
    /// </summary>
    internal static class EmailTemplate
    {
        private const string Brand = "#00843D";

        /// <summary>Footer for system emails nobody monitors replies to; also shows a red banner at the top.</summary>
        public const string DoNotReply =
            "This is an automated message from OAF WMS. Please do not reply to this email; replies to this address are not monitored.";

        private const string DoNotReplyBanner =
            @"<tr><td style=""background:#FEE2E2;border-bottom:2px solid #DC2626;padding:10px 24px;color:#B91C1C;font-size:14px;font-weight:700;letter-spacing:.5px;text-align:center;"">" +
            "&#9888; DO NOT REPLY TO THIS EMAIL &mdash; this mailbox is not monitored</td></tr>";

        /// <param name="header">Text in the coloured header bar, e.g. "KE OAF WMS".</param>
        /// <param name="title">Heading above the message.</param>
        /// <param name="messageHtml">Body paragraphs; already HTML (encode any data before passing it in).</param>
        /// <param name="details">Label/value rows shown under the message; values are HTML-encoded here.</param>
        /// <param name="footer">Plain text shown small and grey at the bottom.</param>
        public static string Render(string header, string title, string messageHtml,
                                    IEnumerable<KeyValuePair<string, string>> details, string footer)
        {
            var rows = new StringBuilder();
            if (details != null)
            {
                foreach (var row in details)
                {
                    rows.Append("<tr>")
                        .Append("<td style=\"padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#6b7280;width:40%;\">")
                        .Append(WebUtility.HtmlEncode(row.Key)).Append("</td>")
                        .Append("<td style=\"padding:6px 12px;border-bottom:1px solid #e5e7eb;color:#111827;font-weight:600;\">")
                        .Append(WebUtility.HtmlEncode(row.Value)).Append("</td>")
                        .Append("</tr>");
                }
            }

            return
$@"<!DOCTYPE html>
<html><body style=""margin:0;padding:0;background:#f3f4f6;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f3f4f6;padding:24px 0;"">
<tr><td align=""center"">
  <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#ffffff;border:1px solid #e5e7eb;font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#111827;"">
    <tr><td style=""background:{Brand};padding:14px 24px;color:#ffffff;font-size:16px;font-weight:600;letter-spacing:.3px;"">{WebUtility.HtmlEncode(header)}</td></tr>
    {(footer == DoNotReply ? DoNotReplyBanner : "")}
    <tr><td style=""padding:24px 24px 8px 24px;font-size:18px;font-weight:600;"">{WebUtility.HtmlEncode(title)}</td></tr>
    <tr><td style=""padding:0 24px 16px 24px;"">{messageHtml}</td></tr>
    {(rows.Length == 0 ? "" : $@"<tr><td style=""padding:0 24px 24px 24px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e5e7eb;border-collapse:collapse;"">{rows}</table></td></tr>")}
    <tr><td style=""padding:14px 24px;background:#f9fafb;border-top:1px solid #e5e7eb;color:{(footer == DoNotReply ? "#B91C1C;font-weight:600" : "#6b7280")};font-size:12px;"">{WebUtility.HtmlEncode(footer)}</td></tr>
  </table>
</td></tr>
</table>
</body></html>";
        }

        public static KeyValuePair<string, string> Row(string label, string value) => new KeyValuePair<string, string>(label, value ?? "");
    }
}
