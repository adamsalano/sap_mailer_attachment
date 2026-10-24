using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;

namespace AutoMail
{
    /// <summary>
    /// Renders Crystal reports to PDF from an in-memory DataTable. Each .rpt is loaded once and reused,
    /// and every report is closed on Dispose, so the Crystal print-job limit is never reached.
    /// </summary>
    internal sealed class ReportRenderer : IDisposable
    {
        // The Crystal native engine is not thread-safe: concurrent loads/exports from the parallel company and job
        // workers crashed the process with an access violation in mfc140u.dll. Every Crystal call goes through this lock.
        private static readonly object CrystalLock = new object();

        // Reports already warned about missing fields in this process (guarded by CrystalLock).
        private static readonly HashSet<string> WarnedReports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly string _reportFolder;
        private readonly Dictionary<string, ReportDocument> _reports = new Dictionary<string, ReportDocument>(StringComparer.OrdinalIgnoreCase);

        public ReportRenderer(string reportFolder)
        {
            _reportFolder = reportFolder;
        }

        /// <summary>Milliseconds the last RenderPdf spent waiting for the Crystal lock, and rendering.</summary>
        public long LastWaitMs { get; private set; }
        public long LastRenderMs { get; private set; }

        public byte[] RenderPdf(string reportFile, DataTable data)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            lock (CrystalLock)
            {
                LastWaitMs = timer.ElapsedMilliseconds;
                timer.Restart();
                try
                {
                    return Render(reportFile, data);
                }
                finally
                {
                    LastRenderMs = timer.ElapsedMilliseconds;
                }
            }
        }

        private byte[] Render(string reportFile, DataTable data)
        {
            {
                ReportDocument report = GetReport(reportFile);
                DataSet source = ToReportDataSet(report, data, reportFile);

                report.SetDataSource(source);
                foreach (ReportDocument subreport in report.Subreports)
                    subreport.SetDataSource(source);

                using (Stream pdf = report.ExportToStream(ExportFormatType.PortableDocFormat))
                using (var buffer = new MemoryStream())
                {
                    pdf.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private ReportDocument GetReport(string reportFile)
        {
            if (!_reports.TryGetValue(reportFile, out ReportDocument report))
            {
                string path = Path.Combine(_reportFolder, reportFile);
                if (!File.Exists(path)) throw new FileNotFoundException("Crystal report not found", path);
                report = new ReportDocument();
                report.Load(path);
                _reports[reportFile] = report;
            }
            return report;
        }

        /// <summary>
        /// The reports were designed against the XML files the old code wrote: one table (named "Table1" for the
        /// HANA reports, "Table" for the SQL Server-era ones) with string, decimal and DateTime columns, and
        /// empty strings / zeros instead of nulls. Reproduce that shape so the existing .rpt files bind unchanged.
        /// </summary>
        private static DataSet ToReportDataSet(ReportDocument report, DataTable data, string reportFile)
        {
            string tableName = report.Database.Tables.Count > 0 ? report.Database.Tables[0].Location : "Table1";

            // sources[i] = index of the data column that fills report column i, or -1 for an always-blank column.
            var table = new DataTable(tableName);
            var sources = new List<int>();
            foreach (DataColumn column in data.Columns)
            {
                table.Columns.Add(column.ColumnName, ReportType(column.DataType));
                sources.Add(column.Ordinal);
            }

            // Crystal refuses to render when a field the report uses is missing from the data. Some converted HANA
            // procedures return e.g. "NumAtCard" where the report expects "ODPI.NumAtCard": fill such fields from the
            // column with the same unprefixed name, otherwise leave them blank, and say so once per report.
            if (report.Database.Tables.Count > 0)
            {
                var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FieldDefinition field in report.Database.Tables[0].Fields) fields.Add(field.Name);
                var notes = new List<string>();
                foreach (string field in fields.Where(f => !table.Columns.Contains(f)).ToList())
                {
                    string shortName = field.Substring(field.LastIndexOf('.') + 1);
                    DataColumn match = data.Columns.Contains(shortName) && !fields.Contains(shortName) ? data.Columns[shortName] : null;
                    table.Columns.Add(field, match == null ? typeof(string) : ReportType(match.DataType));
                    sources.Add(match?.Ordinal ?? -1);
                    notes.Add(match == null ? $"{field} (blank)" : $"{field} <- {shortName}");
                }
                if (notes.Count > 0 && WarnedReports.Add(reportFile))
                    Log.Warn("", $"Report \"{reportFile}\" uses fields the procedure does not return: {string.Join(", ", notes)}. " +
                                 "Alias the procedure columns to match the report.");
            }

            foreach (DataRow source in data.Rows)
            {
                DataRow row = table.NewRow();
                for (int i = 0; i < sources.Count; i++)
                {
                    object value = sources[i] < 0 ? DBNull.Value : source[sources[i]];
                    Type type = table.Columns[i].DataType;
                    if (value == DBNull.Value)
                        row[i] = type == typeof(string) ? "" : type == typeof(decimal) ? (object)0m : DBNull.Value;
                    else if (type == typeof(decimal))
                        row[i] = Convert.ToDecimal(value);
                    else if (type == typeof(DateTime))
                        row[i] = value;
                    else
                        row[i] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                table.Rows.Add(row);
            }

            var set = new DataSet("NewDataSet");
            set.Tables.Add(table);
            return set;
        }

        private static Type ReportType(Type type)
        {
            if (type == typeof(DateTime)) return typeof(DateTime);
            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float) ||
                type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                return typeof(decimal);
            return typeof(string);
        }

        public void Dispose()
        {
            lock (CrystalLock)
            {
                foreach (ReportDocument report in _reports.Values)
                {
                    try
                    {
                        report.Close();
                        report.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("", "Failed to close Crystal report: " + ex.Message);
                    }
                }
                _reports.Clear();
            }
        }
    }
}
