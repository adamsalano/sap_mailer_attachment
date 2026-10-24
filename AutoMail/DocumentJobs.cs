using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;

namespace AutoMail
{
    /// <summary>
    /// A marketing/inventory document type that gets a Crystal PDF. The PDF can be attached to the SAP document
    /// (flag U_PDFAttach) and/or emailed to the warehouse receivers in OWHS.U_RcvMailId (flag U_MailSend).
    /// </summary>
    internal sealed class DocumentJob
    {
        public string Name;
        public string Table;
        public string LinesTable;
        /// <summary>Service Layer entity set used for the PATCH.</summary>
        public string Entity;
        public string Procedure;
        /// <summary>Report file name without .rpt; also the middle part of the attachment file name.</summary>
        public string ReportTitle;
        public string EmailTitle;
        public bool CanAttach = true;
        public string DateColumn = "DocDate";
        public string RecipientWarehouseColumn = "WhsCode";
        public string ExtraJoin = "";
        public string AttachFilter = "1=1";
        public string EmailFilter = "1=1";

        public string ReportFile => ReportTitle + ".rpt";

        private static string Production(string lines) =>
            $@"EXISTS (SELECT 1 FROM ""{lines}"" P WHERE P.""DocEntry"" = H.""DocEntry"" AND P.""BaseType"" = 202)";

        public static readonly DocumentJob[] All =
        {
            new DocumentJob { Name = "GoodsReceipt", Table = "OIGN", LinesTable = "IGN1", Entity = "InventoryGenEntries",
                Procedure = "_OAF_L_GoodsReceipt", ReportTitle = "OAF_Goods Receipt - Plus Booking", EmailTitle = "Goods Receipt",
                EmailFilter = "NOT " + Production("IGN1") },
            new DocumentJob { Name = "GoodsReceiptPO", Table = "OPDN", LinesTable = "PDN1", Entity = "PurchaseDeliveryNotes",
                Procedure = "_OAF_L_GoodsReceiptPO", ReportTitle = "OAF_PurchaseReceipt -Receipt", EmailTitle = "GRPO" },
            new DocumentJob { Name = "GoodsIssue", Table = "OIGE", LinesTable = "IGE1", Entity = "InventoryGenExits",
                Procedure = "_OAF_L_GoodsIssue", ReportTitle = "OAF_Goods Issue - Minus Booking", EmailTitle = "Goods Issue",
                EmailFilter = "NOT " + Production("IGE1") },
            new DocumentJob { Name = "SalesReturn", Table = "ORDN", LinesTable = "RDN1", Entity = "Returns",
                Procedure = "_OAF_L_Return", ReportTitle = "OAF_Sales Returns -Returns", EmailTitle = "Sales Return" },
            new DocumentJob { Name = "Delivery", Table = "ODLN", LinesTable = "DLN1", Entity = "DeliveryNotes",
                Procedure = "_OAF_L_Delivery", ReportTitle = "OAF_Delivery -Sales_Delivery", EmailTitle = "Delivery Note Sheet" },
            new DocumentJob { Name = "PurchaseReturn", Table = "ORPD", LinesTable = "RPD1", Entity = "PurchaseReturns",
                Procedure = "_OAF_L_GoodsReturn", ReportTitle = "OAF_PurchaseReturns", EmailTitle = "Purchase Return" },
            new DocumentJob { Name = "InventoryTransfer", Table = "OWTR", LinesTable = "WTR1", Entity = "StockTransfers",
                Procedure = "_OAF_L_InventoryTransfer", ReportTitle = "OAF_Inventory_Transfer", EmailTitle = "Inventory Transfer Sheet",
                RecipientWarehouseColumn = "FromWhsCod",
                ExtraJoin = @"LEFT JOIN ""OUSR"" U ON H.""UserSign"" = U.""USERID""",
                AttachFilter = @"U.""USER_CODE"" = 'COBI' AND IFNULL(H.""U_TransferType"",'') <> 'DUKA to WHSE'" },

            // Email only. Converted from the SQL Server versions; the procedure names default to the
            // SQL Server names and can be overridden with appSetting Procedure.<Name>.
            new DocumentJob { Name = "SalesOrder", Table = "ORDR", LinesTable = "RDR1", Entity = "Orders", CanAttach = false,
                Procedure = "OAF_L_TMS", ReportTitle = "OAF TMS v6_Email", EmailTitle = "Truck Management Sheet" },
            new DocumentJob { Name = "IssueForProduction", Table = "OIGE", LinesTable = "IGE1", Entity = "InventoryGenExits", CanAttach = false,
                Procedure = "OAF_L_Issue_Production", ReportTitle = "Issue For Production", EmailTitle = "Issue For Production Sheet",
                EmailFilter = Production("IGE1") },
            new DocumentJob { Name = "ReceiptFromProduction", Table = "OIGN", LinesTable = "IGN1", Entity = "InventoryGenEntries", CanAttach = false,
                Procedure = "OAF_L_Receipt_Production", ReportTitle = "Receipt From Production", EmailTitle = "Receipt From Production",
                EmailFilter = Production("IGN1") },
            new DocumentJob { Name = "InventoryCount", Table = "OINC", LinesTable = "INC1", Entity = "InventoryCountings", CanAttach = false,
                Procedure = "OAF_L_Inventory_Count", ReportTitle = "Inventory Count", EmailTitle = "Inventory Count", DateColumn = "CountDate" },
        };
    }

    internal sealed class DocumentJobRunner
    {
        private readonly CompanyContext _ctx;

        public DocumentJobRunner(CompanyContext ctx)
        {
            _ctx = ctx;
        }

        public void Run(DocumentJob job, bool attach, bool email)
        {
            string procedure = Settings.Procedure(job.Name, job.Procedure);
            if (!_ctx.Db.ProcedureExists(procedure))
            {
                Log.Error(_ctx.Name, $"{job.Name}: procedure \"{procedure}\" does not exist in this schema; job skipped. " +
                                     $"Create it or set appSetting Procedure.{job.Name}.");
                return;
            }
            if (email)
            {
                try { _ = _ctx.Mailer; }
                catch (Exception ex)
                {
                    Log.Error(_ctx.Name, $"{job.Name}: email disabled for this run: {ex.Message}");
                    email = false;
                    if (!attach) return;
                }
            }

            string attachCondition = attach
                ? $@"H.""{job.DateColumn}"" >= '{Settings.AttachFromDate(_ctx.Name).Value:yyyy-MM-dd}' AND IFNULL(H.""U_PDFAttach"",'No') <> 'Yes' AND ({job.AttachFilter})"
                : "1=0";
            string emailCondition = email
                ? $@"H.""{job.DateColumn}"" >= '{Settings.EmailFromDate(_ctx.Name).Value:yyyy-MM-dd}' AND IFNULL(H.""U_MailSend"",'No') <> 'Yes' AND ({job.EmailFilter})"
                : "1=0";

            DataTable pending = _ctx.Db.Query($@"
                SELECT H.""DocEntry"", H.""DocNum"", {(attach ? @"IFNULL(H.""AtcEntry"",0)" : "0")} AS ""AtcEntry"",
                       CASE WHEN {attachCondition} THEN 1 ELSE 0 END AS ""NeedAttach"",
                       CASE WHEN {emailCondition} THEN 1 ELSE 0 END AS ""NeedEmail""
                FROM ""{job.Table}"" H {job.ExtraJoin}
                WHERE ({attachCondition}) OR ({emailCondition})
                ORDER BY H.""DocEntry""{_ctx.Limit}");

            var jobTimer = System.Diagnostics.Stopwatch.StartNew();
            int attached = 0, emailed = 0, failed = 0;
            foreach (DataRow doc in pending.Rows)
            {
                int docEntry = Convert.ToInt32(doc["DocEntry"]);
                string docNum = Convert.ToString(doc["DocNum"]);
                bool needAttach = Convert.ToInt32(doc["NeedAttach"]) == 1;
                bool needEmail = Convert.ToInt32(doc["NeedEmail"]) == 1;

                DataTable data;
                try
                {
                    data = _ctx.Db.Query($@"CALL ""{procedure}""(?)", docEntry);
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error(_ctx.Name, $"{Describe(job, docNum, docEntry)}: procedure \"{procedure}\" failed", ex);
                    continue;
                }

                byte[] pdf = null;
                Func<byte[]> render = () => pdf ?? (pdf = _ctx.Reports.RenderPdf(job.ReportFile, data));

                if (needAttach)
                {
                    try
                    {
                        Attach(job, docEntry, docNum, Convert.ToInt32(doc["AtcEntry"]), render);
                        attached++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Error(_ctx.Name, $"{Describe(job, docNum, docEntry)}: attaching PDF failed", ex);
                    }
                }

                if (needEmail)
                {
                    try
                    {
                        if (Email(job, docEntry, docNum, data, render)) emailed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Error(_ctx.Name, $"{Describe(job, docNum, docEntry)}: email failed", ex);
                    }
                }
            }

            string level = failed > 0 ? "with errors" : "ok";
            string message = $"{job.Name}: {pending.Rows.Count} pending, {attached} attached, {emailed} emailed, {failed} failed in {jobTimer.Elapsed.TotalSeconds:0}s ({level})";
            if (failed > 0) Log.Warn(_ctx.Name, message); else Log.Info(_ctx.Name, message);
        }

        private void Attach(DocumentJob job, int docEntry, string docNum, int attachmentEntry, Func<byte[]> render)
        {
            string fileName = $"{_ctx.Name} {job.ReportTitle} {docNum}";
            string how;

            if (attachmentEntry > 0)
            {
                // A previous run may have appended the file but failed to set the flag; don't append it twice.
                // Read from HANA rather than Service Layer: one fewer HTTP round trip per document.
                bool alreadyThere = Convert.ToInt32(_ctx.Db.Scalar(@"
                    SELECT COUNT(*) FROM ""ATC1"" WHERE ""AbsEntry"" = ? AND LOWER(""FileName"") = LOWER(?)",
                    attachmentEntry, fileName)) > 0;
                how = alreadyThere ? $"already in entry {attachmentEntry}" : null;
            }
            else
            {
                // Attachment entries cannot be deleted, so when a previous run uploaded the PDF on its own entry and SAP
                // then rejected the document update (e.g. a validation in SBO_SP_TransactionNotification), reuse that
                // entry rather than leave another unused one behind.
                attachmentEntry = Convert.ToInt32(_ctx.Db.Scalar(@"
                    SELECT IFNULL(MAX(A.""AbsEntry""), 0) FROM ""ATC1"" A
                    WHERE A.""FileName"" = ? AND LOWER(A.""FileExt"") = 'pdf'
                      AND (SELECT COUNT(*) FROM ""ATC1"" B WHERE B.""AbsEntry"" = A.""AbsEntry"") = 1", fileName));
                how = attachmentEntry > 0 ? $"reused unlinked entry {attachmentEntry}" : null;
            }

            if (how == null)
            {
                byte[] bytes = render();
                bool append = attachmentEntry > 0;
                var upload = System.Diagnostics.Stopwatch.StartNew();
                attachmentEntry = _ctx.ServiceLayer.UploadAttachment(attachmentEntry, fileName + ".pdf", bytes, "application/pdf");
                how = $"{(append ? "appended to" : "uploaded as")} entry {attachmentEntry}, {bytes.Length / 1024} KB, " +
                      $"render {_ctx.Reports.LastRenderMs} ms (+{_ctx.Reports.LastWaitMs} ms queued), upload {upload.ElapsedMilliseconds} ms";
            }

            try
            {
                _ctx.ServiceLayer.Update($"{job.Entity}({docEntry})", new Dictionary<string, object>
                {
                    { "AttachmentEntry", attachmentEntry },
                    { "U_PDFAttach", "Yes" }
                });
            }
            catch (ServiceLayerException ex)
            {
                throw new ServiceLayerException($"PDF {how}, but SAP rejected the document update: {ex.Message}. " +
                                                "The entry is reused on the next run once the document can be saved.");
            }
            Log.Info(_ctx.Name, $"{Describe(job, docNum, docEntry)}: PDF attached ({how})");
        }

        private static string Describe(DocumentJob job, string docNum, int docEntry) => $"{job.Name} {docNum} (DocEntry {docEntry})";

        /// <summary>Returns true when a message was sent.</summary>
        private bool Email(DocumentJob job, int docEntry, string docNum, DataTable data, Func<byte[]> render)
        {
            if (data.Rows.Count == 0)
            {
                Log.Warn(_ctx.Name, $"{job.Name} {docNum}: procedure returned no rows; email skipped and document marked as sent");
                MarkMailed(job, docEntry);
                return false;
            }

            DataTable receivers = _ctx.Db.Query($@"
                SELECT DISTINCT W.""U_RcvMailId"", W.""WhsCode"", W.""WhsName""
                FROM ""{job.LinesTable}"" D INNER JOIN ""OWHS"" W ON D.""{job.RecipientWarehouseColumn}"" = W.""WhsCode""
                WHERE D.""DocEntry"" = ?", docEntry);
            List<string> to = Mailer.ParseAddresses(receivers.Rows.Cast<DataRow>().Select(r => Convert.ToString(r["U_RcvMailId"])), out List<string> invalid);
            if (invalid.Count > 0)
                Log.Warn(_ctx.Name, $"{job.Name} {docNum}: invalid warehouse receiver email(s) ignored: {string.Join(", ", invalid)}");
            if (to.Count == 0)
            {
                Log.Warn(_ctx.Name, $"{job.Name} {docNum}: no receiver email on the document's warehouses (OWHS.U_RcvMailId); marked as sent");
                MarkMailed(job, docEntry);
                return false;
            }

            object docDate = _ctx.Db.Scalar($@"SELECT ""{job.DateColumn}"" FROM ""{job.Table}"" WHERE ""DocEntry"" = ?", docEntry);
            string warehouses = string.Join(", ", receivers.Rows.Cast<DataRow>()
                .Select(r => $"{Convert.ToString(r["WhsCode"]).Trim()} - {Convert.ToString(r["WhsName"]).Trim()}").Distinct());
            string sender = $"{_ctx.Country} OAF WMS";
            string body = EmailTemplate.Render(sender, $"{job.EmailTitle} {docNum}",
                "<p style=\"margin:0 0 12px 0;\">Hi,</p>" +
                $"<p style=\"margin:0 0 12px 0;\">Please print the attached <b>{WebUtility.HtmlEncode(job.EmailTitle)} {WebUtility.HtmlEncode(docNum)}</b>.</p>" +
                "<p style=\"margin:0;\">Thanks,<br/>OAF WMS</p>",
                new[]
                {
                    EmailTemplate.Row("Document", job.EmailTitle),
                    EmailTemplate.Row("Number", docNum),
                    EmailTemplate.Row("Date", docDate is DateTime d ? d.ToString("dd MMM yyyy") : Convert.ToString(docDate)),
                    EmailTemplate.Row("Warehouse", warehouses),
                    EmailTemplate.Row("Country", _ctx.Country)
                },
                EmailTemplate.DoNotReply);
            _ctx.Mailer.Send(sender, to, null, $"{_ctx.Country} {job.EmailTitle} {docNum}", body,
                             $"{job.EmailTitle} {docNum}.pdf", render());

            MarkMailed(job, docEntry);
            Log.Info(_ctx.Name, $"{Describe(job, docNum, docEntry)}: emailed to {Mailer.DescribeRecipients(to)}");
            return true;
        }

        private void MarkMailed(DocumentJob job, int docEntry)
        {
            _ctx.ServiceLayer.Update($"{job.Entity}({docEntry})", new Dictionary<string, object> { { "U_MailSend", "Yes" } });
        }
    }
}
