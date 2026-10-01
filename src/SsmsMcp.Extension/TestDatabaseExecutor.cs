using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using SsmsMcp.Core;

namespace SsmsMcp.Extension;

public sealed class TestDatabaseExecutor
{
    private readonly DatabaseCopyRegistry _registry;

    public TestDatabaseExecutor(DatabaseCopyRegistry registry)
    {
        _registry = registry;
    }

    public DatabaseCopyRecord RegisterCopy(ActiveContext context, string database, string backupPath,
        string? password)
    {
        using SqlConnection connection = OpenMaster(context, password);
        DatabaseCopyRecord record = ReadRecord(connection, context.Server, database, backupPath, context.Database);
        _registry.Add(record);
        return record;
    }

    public JObject SetOffline(ActiveContext context, string database, bool rollbackImmediate,
        string? password, CancellationToken cancellationToken)
    {
        using SqlConnection connection = OpenMaster(context, password);
        GetRegisteredCopy(connection, context.Server, database);
        ExecuteCommand(connection, TestDatabaseScript.Offline(database, rollbackImmediate), cancellationToken);
        return StateResult(connection, database, "OFFLINE");
    }

    public JObject SetOnline(ActiveContext context, string database, string? password,
        CancellationToken cancellationToken)
    {
        using SqlConnection connection = OpenMaster(context, password);
        GetRegisteredCopy(connection, context.Server, database);
        ExecuteCommand(connection, TestDatabaseScript.Online(database), cancellationToken);
        return StateResult(connection, database, "ONLINE");
    }

    public JObject KillSession(ActiveContext context, string database, int sessionId, string? password,
        CancellationToken cancellationToken)
    {
        using SqlConnection connection = OpenMaster(context, password);
        GetRegisteredCopy(connection, context.Server, database);
        using SqlCommand check = connection.CreateCommand();
        check.CommandText = @"SELECT COALESCE(s.program_name, N'') FROM sys.dm_exec_sessions AS s
WHERE s.session_id = @sessionId AND s.is_user_process = 1 AND s.session_id <> @@SPID
AND (s.database_id = DB_ID(@database) OR EXISTS
    (SELECT 1 FROM sys.dm_exec_requests AS r WHERE r.session_id = s.session_id
     AND r.database_id = DB_ID(@database)))";
        check.Parameters.AddWithValue("@sessionId", sessionId);
        check.Parameters.AddWithValue("@database", database);
        string? program = check.ExecuteScalar() as string;
        if (program is null)
            throw new InvalidOperationException("SPID finnes ikke som en brukersesjon i den registrerte testdatabasen.");

        ExecuteCommand(connection, TestDatabaseScript.Kill(sessionId), cancellationToken);
        return new JObject { ["killed"] = true, ["database"] = database,
            ["sessionId"] = sessionId, ["programName"] = program };
    }

    public JObject ResetCopy(ActiveContext context, string database, bool delete, bool rollbackImmediate,
        string? password, CancellationToken cancellationToken)
    {
        using SqlConnection connection = OpenMaster(context, password);
        DatabaseCopyRecord record = GetRegisteredCopy(connection, context.Server, database);
        string sql = delete ? TestDatabaseScript.Drop(database, rollbackImmediate) :
            TestDatabaseScript.Restore(database, record.BackupPath, rollbackImmediate,
                record.Files.ConvertAll(file => new KeyValuePair<string, string>(file.LogicalName, file.PhysicalName)));
        ExecuteCommand(connection, sql, cancellationToken);
        if (delete)
        {
            _registry.Remove(context.Server, database);
            return new JObject { ["deleted"] = true, ["database"] = database };
        }

        DatabaseCopyRecord refreshed = ReadRecord(connection, context.Server, database,
            record.BackupPath, record.SourceDatabase);
        _registry.Add(refreshed);
        JObject result = StateResult(connection, database, "ONLINE");
        result["restored"] = true;
        return result;
    }

    public DatabaseCopyRecord VerifyCopy(ActiveContext context, string database, string? password)
    {
        using SqlConnection connection = OpenMaster(context, password);
        return GetRegisteredCopy(connection, context.Server, database);
    }

    private DatabaseCopyRecord GetRegisteredCopy(SqlConnection connection, string server, string database)
    {
        TestDatabaseScript.ValidateName(database);
        DatabaseCopyRecord record = _registry.Get(server, database);
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT database_id, create_date FROM sys.databases WHERE name = @database";
        command.Parameters.AddWithValue("@database", database);
        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != record.DatabaseId || reader.GetDateTime(1) != record.CreatedAt)
            throw new InvalidOperationException("Databasekopien finnes ikke lenger eller er erstattet.");

        return record;
    }

    private static DatabaseCopyRecord ReadRecord(SqlConnection connection, string server, string database,
        string backupPath, string sourceDatabase)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = @"SELECT d.database_id, d.create_date, f.name, f.physical_name
FROM sys.databases AS d JOIN sys.master_files AS f ON f.database_id = d.database_id
WHERE d.name = @database ORDER BY f.file_id";
        command.Parameters.AddWithValue("@database", database);
        using SqlDataReader reader = command.ExecuteReader();
        DatabaseCopyRecord? record = null;
        while (reader.Read())
        {
            record ??= new DatabaseCopyRecord
            {
                Server = server, Database = database, SourceDatabase = sourceDatabase,
                BackupPath = backupPath, DatabaseId = reader.GetInt32(0), CreatedAt = reader.GetDateTime(1)
            };
            record.Files.Add(new DatabaseCopyFile
            {
                LogicalName = reader.GetString(2), PhysicalName = reader.GetString(3)
            });
        }

        return record ?? throw new InvalidOperationException("Databasekopien ble ikke funnet etter gjenoppretting.");
    }

    private static JObject StateResult(SqlConnection connection, string database, string expectedState)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT state_desc FROM sys.databases WHERE name = @database";
        command.Parameters.AddWithValue("@database", database);
        string state = command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("Testdatabasen finnes ikke.");
        if (!string.Equals(state, expectedState, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Testdatabasen er i tilstand {state}, forventet {expectedState}.");
        return new JObject { ["database"] = database, ["state"] = state };
    }

    private static SqlConnection OpenMaster(ActiveContext context, string? password)
    {
        SqlConnectionStringBuilder builder = SqlExecutor.CreateConnectionString(context, password);
        builder.InitialCatalog = "master";
        builder.Pooling = false;
        SqlConnection connection = new(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void ExecuteCommand(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }
}
