using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace AutoMail
{
    /// <summary>Creates the user-defined fields the enabled jobs need, through Service Layer.</summary>
    internal static class UserFields
    {
        private sealed class Field
        {
            public string Table;
            public string Name;
            public string Description;
            public int Size;
            public string[] Values;
        }

        private static readonly string[] YesNo = { "Yes", "No" };

        public static void Ensure(CompanyContext ctx, IEnumerable<DocumentJob> attachJobs, IEnumerable<DocumentJob> emailJobs,
                                  bool payments, bool transfers)
        {
            var fields = new List<Field>
            {
                new Field { Table = "OADM", Name = "SenderMail", Description = "Sender Mail Id", Size = 50 },
                new Field { Table = "OADM", Name = "SenderMailPwd", Description = "Sender Mail Password", Size = 50 },
                new Field { Table = "OWHS", Name = "RcvMailId", Description = "Receiver Mail Id", Size = 254 },
            };
            fields.AddRange(attachJobs.Select(j => new Field { Table = j.Table, Name = "PDFAttach", Description = "PDF Attached", Size = 5, Values = YesNo }));
            fields.AddRange(emailJobs.Select(j => new Field { Table = j.Table, Name = "MailSend", Description = "Mail Send Or Not", Size = 5, Values = YesNo }));
            if (payments)
                fields.Add(new Field { Table = "OVPM", Name = "MailSend", Description = "Mail Send Or Not", Size = 1, Values = new[] { "Y", "N" } });
            if (transfers)
            {
                foreach (string table in new[] { "OWTQ", "OWTR" })
                {
                    fields.Add(new Field { Table = table, Name = "ApproveEmail", Description = "Approver Mail Id", Size = 254 });
                    fields.Add(new Field { Table = table, Name = "ReqestEmail", Description = "Requester Mail Id", Size = 254 });
                    fields.Add(new Field { Table = table, Name = "ReservMail", Description = "Reserve Email Sent", Size = 5, Values = YesNo });
                    fields.Add(new Field { Table = table, Name = "CancelMail", Description = "Cancel Email Sent", Size = 5, Values = YesNo });
                    fields.Add(new Field { Table = table, Name = "ClosedMail", Description = "Closed/Cancel Email Sent", Size = 5, Values = YesNo });
                }
            }

            var existing = new HashSet<string>(
                ctx.Db.Query(@"SELECT ""TableID"", ""AliasID"" FROM ""CUFD""").Rows.Cast<DataRow>()
                    .Select(r => Convert.ToString(r["TableID"]) + "." + Convert.ToString(r["AliasID"])),
                StringComparer.OrdinalIgnoreCase);

            foreach (Field field in fields)
            {
                string key = field.Table + "." + field.Name;
                if (!existing.Add(key)) continue;

                var body = new Dictionary<string, object>
                {
                    { "TableName", field.Table },
                    { "Name", field.Name },
                    { "Description", field.Description },
                    { "Type", "db_Alpha" },
                    { "SubType", "st_None" },
                    { "EditSize", field.Size }
                };
                if (field.Values != null)
                    body["ValidValuesMD"] = field.Values.Select(v => new Dictionary<string, object> { { "Value", v }, { "Description", v } }).ToArray();

                try
                {
                    ctx.ServiceLayer.Post("UserFieldsMD", body);
                    Log.Info(ctx.Name, $"Created user field {field.Table}.U_{field.Name}");
                }
                catch (Exception ex)
                {
                    Log.Error(ctx.Name, $"Could not create user field {field.Table}.U_{field.Name}: {ex.Message}");
                }
            }
        }
    }
}
