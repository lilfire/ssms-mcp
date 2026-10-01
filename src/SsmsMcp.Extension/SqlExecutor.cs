using System;
using System.Data;
using System.Text;
using System.Threading;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SsmsMcp.Extension;

public sealed class SqlExecutor
{
    private readonly int _rowLimit = 100;
    private readonly int _byteLimit = 1048576 - 256;
    private readonly int _timeoutSeconds = 10;

    public JObject InspectRecovery(ActiveContext context, string sql, string? password, bool useShared)
    {
        JObject result = Execute(context, sql, password, useShared);
        JArray? rows = result["rows"] as JArray;
        if (rows is null || rows.Count == 0)
            throw new InvalidOperationException("Brukerdatabasen ble ikke funnet eller er ikke synlig.");

        JArray row = (JArray)rows[0];
        string recoveryModel = row[1]?.Value<string>() ?? string.Empty;
        bool unavailable = row[2]?.Value<string>() != "ONLINE" || row[3]?.Value<bool>() == true;
        bool hasDependency = row[4]?.Type != JTokenType.Null || row[5]?.Value<bool>() == true ||
            row[6]?.Value<bool>() == true || row[12]?.Value<bool>() == true;
        bool hasRecentLogBackups = row[11]?.Value<long>() > 0;
        string assessment = recoveryModel == "SIMPLE" ? "already_simple" :
            recoveryModel != "FULL" || unavailable ? "manual_review" : hasDependency ? "keep_full" :
            hasRecentLogBackups ? "review_log_backups" : "decision_required";
        result["assessment"] = assessment;
        result["assessmentNote"] = assessment switch
        {
            "already_simple" => "Databasen bruker allerede SIMPLE.",
            "keep_full" => "Replikering, Always On eller log shipping er registrert. Behold FULL og undersøk avhengighetene.",
            "manual_review" => "Recovery-modell eller databasetilstand må vurderes manuelt.",
            "review_log_backups" => "Loggbackuper er registrert de siste 30 dagene. Avklar behovet for gjenoppretting " +
                "til et bestemt tidspunkt og endre backupjobbene før eventuell overgang til SIMPLE.",
            _ => "SIMPLE kan bare velges dersom gjenoppretting til et bestemt tidspunkt ikke kreves, " +
                "tap av endringer siden siste databackup er akseptabelt, og fullbackupplanen er avklart. " +
                "Backuphistorikk viser ikke nødvendigvis eksterne backupjobber eller forretningskrav."
        };
        return result;
    }

    public JObject SetRecoverySimple(ActiveContext context, string sql, string? password)
    {
        SqlConnectionStringBuilder builder = CreateConnectionString(context, password);
        builder.InitialCatalog = "master";
        builder.Pooling = false;
        using SqlConnection connection = new(builder.ConnectionString);
        connection.Open();
        using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.CommandTimeout = _timeoutSeconds;
            command.ExecuteNonQuery();
        }

        using SqlCommand check = connection.CreateCommand();
        check.CommandText = "SELECT recovery_model_desc FROM sys.databases WHERE name = @database";
        check.Parameters.AddWithValue("@database", context.Database);
        string recoveryModel = check.ExecuteScalar() as string
            ?? throw new InvalidOperationException("Databasen ble ikke funnet etter endringen.");
        if (recoveryModel != "SIMPLE")
            throw new InvalidOperationException("Recovery-modellen ble ikke SIMPLE etter endringen.");

        return new JObject
        {
            ["database"] = context.Database,
            ["recoveryModel"] = recoveryModel,
            ["changed"] = true,
            ["note"] = "Loggbackupkjeden er brutt. Kontroller fullbackupplan og fjern eventuelle loggbackupjobber."
        };
    }

    public void CopyDatabase(ActiveContext context, string sql, string? password, CancellationToken cancellationToken)
    {
        SqlConnectionStringBuilder builder = CreateConnectionString(context, password);
        builder.InitialCatalog = "master";
        builder.Pooling = false;
        using SqlConnection connection = new(builder.ConnectionString);
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }

    public JObject ExecuteWrite(ActiveContext context, string sql, string? password, bool useShared)
    {
        IDbConnection? connection = useShared && context.Session?.State == ConnectionState.Open ? context.Session : null;
        if (useShared && connection is null)
            throw new InvalidOperationException("Den verifiserte SQL-sesjonen ble lukket før spørringen startet.");

        bool shared = connection is not null;
        if (!shared)
        {
            SqlConnectionStringBuilder builder = CreateConnectionString(context, password);
            connection = new SqlConnection(builder.ConnectionString);
        }

        try
        {
            if (!shared)
                connection!.Open();

            using IDbCommand command = connection!.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _timeoutSeconds;
            int affectedRows = command.ExecuteNonQuery();
            return new JObject
            {
                ["affectedRows"] = affectedRows,
                ["sessionMode"] = shared ? "same_spid" : "separate"
            };
        }
        finally
        {
            if (!shared)
                connection?.Dispose();
        }
    }

    public JObject Execute(ActiveContext context, string sql, string? password, bool useShared,
        string? schema = null, string? name = null)
    {
        IDbConnection? connection = useShared && context.Session?.State == ConnectionState.Open ? context.Session : null;
        if (useShared && connection is null)
            throw new InvalidOperationException("Den verifiserte SQL-sesjonen ble lukket før spørringen startet.");

        bool shared = connection is not null;
        if (!shared)
        {
            SqlConnectionStringBuilder builder = CreateConnectionString(context, password);
            connection = new SqlConnection(builder.ConnectionString);
        }

        try
        {
            if (!shared)
                connection!.Open();

            using IDbCommand command = connection!.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _timeoutSeconds;
            if (schema is not null && name is not null)
            {
                IDbDataParameter schemaParameter = command.CreateParameter();
                schemaParameter.ParameterName = "@schema";
                schemaParameter.Value = schema;
                command.Parameters.Add(schemaParameter);
                IDbDataParameter nameParameter = command.CreateParameter();
                nameParameter.ParameterName = "@name";
                nameParameter.Value = name;
                command.Parameters.Add(nameParameter);
            }
            using IDataReader reader = command.ExecuteReader(CommandBehavior.SingleResult);
            JArray columns = new();
            for (int index = 0; index < reader.FieldCount; index++)
                columns.Add(reader.GetName(index));

            if (Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(columns, Formatting.None)) > _byteLimit)
                throw new InvalidOperationException("Kolonnenavnene overskrider svargrensen på 1 MiB.");

            JArray rows = new();
            bool truncated = false;
            while (reader.Read())
            {
                if (rows.Count >= _rowLimit)
                {
                    truncated = true;
                    break;
                }

                JArray row = new();
                for (int index = 0; index < reader.FieldCount; index++)
                    row.Add(reader.IsDBNull(index) ? JValue.CreateNull() : JToken.FromObject(reader.GetValue(index)));

                rows.Add(row);
                JObject check = new()
                {
                    ["columns"] = columns,
                    ["rows"] = rows,
                    ["rowCount"] = rows.Count,
                    ["truncated"] = true,
                    ["sessionMode"] = shared ? "same_spid" : "separate"
                };
                if (Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(check, Formatting.None)) > _byteLimit)
                {
                    rows.RemoveAt(rows.Count - 1);
                    truncated = true;
                    break;
                }
            }

            return new JObject
            {
                ["columns"] = columns,
                ["rows"] = rows,
                ["rowCount"] = rows.Count,
                ["truncated"] = truncated,
                ["sessionMode"] = shared ? "same_spid" : "separate"
            };
        }
        finally
        {
            if (!shared)
                connection?.Dispose();
        }
    }

    internal static SqlConnectionStringBuilder CreateConnectionString(ActiveContext context, string? password)
    {
        SqlConnectionStringBuilder builder = new(context.ConnectionString);

        builder.DataSource = context.Server;
        builder.InitialCatalog = context.Database;
        builder.ConnectTimeout = 10;
        builder.ApplicationName = "SSMS MCP Server";
        builder.Password = string.Empty;
        if (context.Authentication == "1" || context.Authentication == "2")
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Passord kreves for den separate SQL-tilkoblingen.");

            builder.IntegratedSecurity = false;
            builder.UserID = context.UserName;
            builder.Password = password;
            builder.Authentication = context.Authentication == "1"
                ? SqlAuthenticationMethod.SqlPassword : SqlAuthenticationMethod.ActiveDirectoryPassword;
        }
        else if (builder.Authentication == SqlAuthenticationMethod.NotSpecified)
        {
            switch (context.Authentication)
            {
                case "0":
                    builder.IntegratedSecurity = true;
                    break;
                case "3":
                    builder.IntegratedSecurity = false;
                    builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryIntegrated;
                    break;
                case "4":
                    builder.IntegratedSecurity = false;
                    builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
                    builder.UserID = context.UserName;
                    break;
                default:
                    throw new InvalidOperationException($"Ukjent påloggingsmetode ({context.Authentication}) for separat SQL-sesjon.");
            }
        }

        return builder;
    }
}
