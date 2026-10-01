using System;
using System.Data;
using System.Threading;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using SsmsMcp.Core;

namespace SsmsMcp.Extension;

public sealed class TestTableLock : IDisposable
{
    private readonly object _sync = new();
    private readonly TestDatabaseExecutor _testDatabases;
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;
    private Timer? _timer;
    private string? _lockId;

    public TestTableLock(TestDatabaseExecutor testDatabases)
    {
        _testDatabases = testDatabases;
    }

    public JObject Hold(ActiveContext context, string database, string schema, string table,
        int maximumSeconds, string? password)
    {
        if (maximumSeconds < 1 || maximumSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(maximumSeconds), "Låsen må vare mellom 1 og 300 sekunder.");

        _testDatabases.VerifyCopy(context, database, password);
        string sql = TestDatabaseScript.LockTable(schema, table);
        lock (_sync)
        {
            if (_connection is not null)
                throw new InvalidOperationException("En MCP-styrt tabellås er allerede aktiv.");

            SqlConnectionStringBuilder builder = SqlExecutor.CreateConnectionString(context, password);
            builder.InitialCatalog = database;
            builder.Pooling = false;
            SqlConnection connection = new(builder.ConnectionString);
            try
            {
                connection.Open();
                EnsureUserTable(connection, schema, table);
                SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                try
                {
                    using SqlCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    command.CommandTimeout = 10;
                    command.ExecuteNonQuery();
                    _connection = connection;
                    _transaction = transaction;
                    _lockId = Guid.NewGuid().ToString("N");
                    DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(maximumSeconds);
                    _timer = new Timer(_ => ReleaseOnTimeout(), null,
                        TimeSpan.FromSeconds(maximumSeconds), Timeout.InfiniteTimeSpan);
                    return new JObject { ["lockId"] = _lockId, ["database"] = database,
                        ["schema"] = schema, ["table"] = table, ["sessionId"] = connection.ServerProcessId,
                        ["expiresAt"] = expiresAt };
                }
                catch
                {
                    transaction.Dispose();
                    throw;
                }
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }

    public JObject Release(string? lockId = null)
    {
        lock (_sync)
        {
            if (_lockId is not null && lockId is not null &&
                !string.Equals(lockId, _lockId, StringComparison.Ordinal))
                throw new InvalidOperationException("Lås-ID er ukjent eller allerede frigitt.");

            bool released = _connection is not null;
            _timer?.Dispose();
            _timer = null;
            try
            {
                _transaction?.Rollback();
            }
            catch (SqlException)
            {
                // SQL Server kan allerede ha avbrutt transaksjonen.
            }
            catch (InvalidOperationException)
            {
                // Tilkoblingen kan allerede være lukket etter en offline-test.
            }
            finally
            {
                _transaction?.Dispose();
                _connection?.Dispose();
                _transaction = null;
                _connection = null;
                _lockId = null;
            }

            return new JObject { ["released"] = released };
        }
    }

    public void Dispose()
    {
        Release();
    }

    private void ReleaseOnTimeout()
    {
        try
        {
            Release();
        }
        catch (Exception)
        {
            // En tidsstyrt frigivelse må ikke avslutte SSMS-prosessen.
        }
    }

    private static void EnsureUserTable(SqlConnection connection, string schema, string table)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = @"SELECT COUNT(*) FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE s.name = @schema AND t.name = @table AND t.is_ms_shipped = 0";
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        if ((int)command.ExecuteScalar()! != 1)
            throw new InvalidOperationException("Testtabellen finnes ikke som en brukertabell.");
    }
}
