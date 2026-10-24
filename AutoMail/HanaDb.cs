using System;
using System.Data;
using Sap.Data.Hana;

namespace AutoMail
{
    /// <summary>Read-only access to the company schema over the HANA ADO.NET provider.</summary>
    internal sealed class HanaDb : IDisposable
    {
        private readonly HanaConnection _connection;

        public HanaDb(CompanyCredential credential)
        {
            _connection = new HanaConnection(credential.HanaConnectionString);
            _connection.Open();
        }

        /// <summary>Runs a query or CALL; use ? placeholders for <paramref name="args"/>.</summary>
        public DataTable Query(string sql, params object[] args)
        {
            using (HanaCommand cmd = CreateCommand(sql, args))
            using (var adapter = new HanaDataAdapter(cmd))
            {
                var table = new DataTable();
                adapter.Fill(table);
                return table;
            }
        }

        public object Scalar(string sql, params object[] args)
        {
            using (HanaCommand cmd = CreateCommand(sql, args))
            {
                return cmd.ExecuteScalar();
            }
        }

        /// <summary>Runs a statement that returns no rows (used for CREATE PROCEDURE).</summary>
        public void Execute(string sql)
        {
            using (HanaCommand cmd = CreateCommand(sql, new object[0]))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public bool ProcedureExists(string name)
        {
            object count = Scalar(
                @"SELECT COUNT(*) FROM ""SYS"".""PROCEDURES"" WHERE ""SCHEMA_NAME"" = CURRENT_SCHEMA AND ""PROCEDURE_NAME"" = ?", name);
            return Convert.ToInt32(count) > 0;
        }

        private HanaCommand CreateCommand(string sql, object[] args)
        {
            var cmd = new HanaCommand(sql, _connection) { CommandTimeout = 300 };
            foreach (object arg in args)
                cmd.Parameters.Add(new HanaParameter { Value = arg ?? DBNull.Value });
            return cmd;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
