using System;
using System.Collections.Generic;
using System.Data;
using System.Net;

namespace AutoMail
{
    /// <summary>Email-only jobs that don't follow the document PDF pattern.</summary>
    internal static class NotificationJobs
    {
        public const string OutgoingPaymentsName = "OutgoingPayments";
        public const string TransferNotificationsName = "TransferNotifications";

        public const string PaymentsProcedure = "_OAF_L_Outgoing_Payments";

        /// <summary>Emails a remittance advice for each outgoing payment exported to CITI.</summary>
        public static void OutgoingPayments(CompanyContext ctx)
        {
            DateTime? from = Settings.OutgoingPaymentsFromDate(ctx.Name);
            if (from == null)
            {
                Log.Error(ctx.Name, "OutgoingPayments: appSetting OutgoingPaymentsFromDate is not set; job skipped.");
                return;
            }
            if (!ctx.Db.ProcedureExists(PaymentsProcedure))
            {
                Log.Error(ctx.Name, $"OutgoingPayments: procedure \"{PaymentsProcedure}\" does not exist; job skipped.");
                return;
            }
            List<string> copy = Mailer.ParseAddresses(new[] { ctx.CompanyCopyEmail }, out _);
            if (copy.Count == 0)
            {
                Log.Error(ctx.Name, "OutgoingPayments: no valid copy email; set it in Administration > System Initialization > " +
                                    "Company Details > Fax. Job skipped.");
                return;
            }
            Mailer mailer = ctx.Mailer;
            string replyTo = Settings.PaymentsReplyTo(ctx.Name);

            DataTable payments = ctx.Db.Query($@"
                SELECT H.""DocEntry"", H.""DocNum"", H.""CardName"", C.""E_Mail""
                FROM ""OVPM"" H INNER JOIN ""OCRD"" C ON H.""CardCode"" = C.""CardCode""
                WHERE H.""DocDate"" >= '{from.Value:yyyy-MM-dd}'
                  AND H.""U_CITIExportDate"" IS NOT NULL
                  AND IFNULL(H.""U_MailSend"",'N') <> 'Y'
                ORDER BY H.""DocEntry""{ctx.Limit}");

            int sent = 0, failed = 0;
            foreach (DataRow payment in payments.Rows)
            {
                int docEntry = Convert.ToInt32(payment["DocEntry"]);
                string docNum = Convert.ToString(payment["DocNum"]);
                string cardName = Convert.ToString(payment["CardName"]).Trim();
                string vendorEmail = Convert.ToString(payment["E_Mail"]).Trim();
                try
                {
                    DataTable data = ctx.Db.Query($@"CALL ""{PaymentsProcedure}""(?)", docEntry);
                    if (data.Rows.Count == 0)
                    {
                        Log.Warn(ctx.Name, $"OutgoingPayments {docNum}: procedure returned no rows; not sent");
                        continue;
                    }
                    byte[] pdf = ctx.Reports.RenderPdf(Settings.PaymentsReport(ctx.Name), data);

                    List<string> to = Mailer.ParseAddresses(new[] { vendorEmail }, out List<string> invalid);
                    List<string> cc = copy;
                    if (to.Count == 0)
                    {
                        string reason = vendorEmail.Length == 0 ? "No vendor email set" : $"Invalid vendor email ({vendorEmail})";
                        Log.Warn(ctx.Name, $"OutgoingPayments {docNum}: {reason}; sent to the company copy address only. " +
                                           "Update Business Partner Master Data > General > Email.");
                        to = copy;
                        cc = null;
                    }
                    else if (invalid.Count > 0)
                    {
                        Log.Warn(ctx.Name, $"OutgoingPayments {docNum}: invalid vendor email(s) ignored: {string.Join(", ", invalid)}");
                    }

                    string sender = $"{ctx.Country} OAF Remittance";
                    string body = EmailTemplate.Render(sender, $"Payment Remittance Advice {docNum}",
                        $"<p style=\"margin:0 0 12px 0;\">Hello {WebUtility.HtmlEncode(cardName)},</p>" +
                        $"<p style=\"margin:0 0 12px 0;\">Please find attached the <b>Payment Remittance Advice No. {WebUtility.HtmlEncode(docNum)}</b>.</p>" +
                        "<p style=\"margin:0 0 12px 0;\">It may take up to <b>3 working days</b> for the amount to be credited to your account. " +
                        $"If you experience delays beyond this, reply to this email or contact <a href=\"mailto:{replyTo}\" style=\"color:#00843D;\">{replyTo}</a>.</p>" +
                        "<p style=\"margin:0;\">Thanks &amp; Regards,<br/>Payments Team</p>",
                        new[]
                        {
                            EmailTemplate.Row("Remittance Advice", docNum),
                            EmailTemplate.Row("Vendor", cardName),
                            EmailTemplate.Row("Country", ctx.Country)
                        },
                        $"Questions about this payment? Reply to this email or write to {replyTo}.");
                    mailer.Send(sender, to, cc,
                                $"[{ctx.Country}] OAF Payment Remittance Advice# {docNum}", body,
                                $"{ctx.Country} Remittance Advice {docNum}.pdf", pdf, replyTo);

                    ctx.ServiceLayer.Update($"VendorPayments({docEntry})", new Dictionary<string, object> { { "U_MailSend", "Y" } });
                    sent++;
                    Log.Info(ctx.Name, $"OutgoingPayments {docNum}: sent to {Mailer.DescribeRecipients(to, cc)}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error(ctx.Name, $"OutgoingPayments {docNum}: failed", ex);
                }
            }
            Log.Info(ctx.Name, $"OutgoingPayments: {payments.Rows.Count} pending, {sent} sent, {failed} failed");
        }

        /// <summary>Notifies requester and approver when an ITR is reserved/cancelled or a transfer is loaded/cancelled.</summary>
        public static void TransferNotifications(CompanyContext ctx)
        {
            DateTime? from = Settings.TransferNotificationsFromDate(ctx.Name);
            if (from == null)
            {
                Log.Error(ctx.Name, "TransferNotifications: appSetting TransferNotificationsFromDate is not set; job skipped.");
                return;
            }
            Mailer mailer = ctx.Mailer;
            string d = from.Value.ToString("yyyy-MM-dd");
            const string columns = @"T0.""DocEntry"", T0.""DocNum"", T0.""Comments"", T0.""Filler"", T0.""U_ApproveEmail"", T0.""U_ReqestEmail""";

            DataTable docs = ctx.Db.Query($@"
                SELECT 'ITR' AS ""Doc"", 'InventoryTransferRequests' AS ""Entity"", 'U_ReservMail' AS ""Field"", 'ITR-R' AS ""StatusCode"", {columns}
                  FROM ""OWTQ"" T0
                 WHERE IFNULL(T0.""U_ReservMail"",'No') = 'No' AND T0.""DocStatus"" = 'O' AND T0.""DocDate"" >= '{d}'
                UNION ALL
                SELECT 'ITR', 'InventoryTransferRequests', 'U_CancelMail', 'ITR-C', {columns}
                  FROM ""OWTQ"" T0
                 WHERE IFNULL(T0.""U_CancelMail"",'No') = 'No' AND T0.""DocStatus"" = 'C' AND T0.""DocDate"" >= '{d}'
                   AND EXISTS (SELECT 1 FROM ""WTQ1"" W WHERE W.""DocEntry"" = T0.""DocEntry"" AND W.""TargetType"" = -1)
                UNION ALL
                SELECT 'IT', 'StockTransfers', 'U_ClosedMail', 'IT-T', {columns}
                  FROM ""OWTR"" T0
                 WHERE IFNULL(T0.""U_ClosedMail"",'No') = 'No' AND T0.""DocStatus"" = 'O' AND T0.""DocDate"" >= '{d}'
                UNION ALL
                SELECT 'IT', 'StockTransfers', 'U_CancelMail', 'IT-C', {columns}
                  FROM ""OWTR"" T0
                 WHERE IFNULL(T0.""U_CancelMail"",'No') = 'No' AND T0.""CANCELED"" = 'Y' AND T0.""DocDate"" >= '{d}'
                ORDER BY ""DocEntry""{ctx.Limit}");

            int sent = 0, failed = 0;
            foreach (DataRow row in docs.Rows)
            {
                string doc = Convert.ToString(row["Doc"]);
                string entity = Convert.ToString(row["Entity"]);
                string field = Convert.ToString(row["Field"]);
                string statusCode = Convert.ToString(row["StatusCode"]);
                int docEntry = Convert.ToInt32(row["DocEntry"]);
                string docNum = Convert.ToString(row["DocNum"]);
                string odooDoc = Convert.ToString(row["Comments"]).Trim();
                string fromWhs = Convert.ToString(row["Filler"]).Trim().ToUpper();
                string requester = Convert.ToString(row["U_ReqestEmail"]).Trim();
                string approver = Convert.ToString(row["U_ApproveEmail"]).Trim();
                string label = $"TransferNotifications {doc} {docNum} ({statusCode})";

                try
                {
                    List<string> to = Mailer.ParseAddresses(new[] { requester, approver }, out List<string> invalid);
                    if (invalid.Count > 0)
                        Log.Warn(ctx.Name, $"{label}: invalid email(s) ignored: {string.Join(", ", invalid)}");

                    if (to.Count > 0)
                    {
                        string status;
                        string outcome;
                        switch (statusCode)
                        {
                            case "ITR-R": status = "RESERVED"; outcome = "Expect the delivery to the Duka."; break;
                            case "ITR-C": status = "CANCELED FROM RESERVATION"; outcome = "this means that it will not be delivered to you."; break;
                            case "IT-T": status = "LOADED TO TRANSIT"; outcome = "Expect the delivery to the Duka."; break;
                            default: status = "CANCELED FROM TRANSIT"; outcome = "this means that it will not be delivered to you."; break;
                        }

                        string sender = $"{ctx.Country} OAF WMS";
                        string body = EmailTemplate.Render(sender, $"Replenishment request {status.ToLowerInvariant()}",
                            "<p style=\"margin:0 0 12px 0;\">Hi,</p>" +
                            $"<p style=\"margin:0 0 12px 0;\">Your Duka replenishment request from <b>{WebUtility.HtmlEncode(fromWhs)}</b> " +
                            $"has been <b>{status}</b> - {WebUtility.HtmlEncode(outcome)}</p>" +
                            $"<p style=\"margin:0;\">Thanks,<br/>{WebUtility.HtmlEncode(ctx.Country)} Logistics Team</p>",
                            new[]
                            {
                                EmailTemplate.Row("Status", status),
                                EmailTemplate.Row("SAP document", $"{doc} No. {docNum}"),
                                EmailTemplate.Row("Odoo transfer request", odooDoc),
                                EmailTemplate.Row("From warehouse", fromWhs),
                                EmailTemplate.Row("Requested by", requester),
                                EmailTemplate.Row("Approved by", approver)
                            },
                            EmailTemplate.DoNotReply);
                        mailer.Send(sender, to, null,
                                    $"{ctx.Country} - {fromWhs} {status} SAP {doc} No. {docNum} Based on Odoo Doc No. {odooDoc}", body);
                        sent++;
                        Log.Info(ctx.Name, $"{label}: sent to {Mailer.DescribeRecipients(to)}");
                    }
                    else
                    {
                        Log.Warn(ctx.Name, $"{label}: no requester or approver email; marked as sent");
                    }

                    ctx.ServiceLayer.Update($"{entity}({docEntry})", new Dictionary<string, object> { { field, "Yes" } });
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error(ctx.Name, $"{label}: failed", ex);
                }
            }
            Log.Info(ctx.Name, $"TransferNotifications: {docs.Rows.Count} pending, {sent} sent, {failed} failed");
        }
    }
}
