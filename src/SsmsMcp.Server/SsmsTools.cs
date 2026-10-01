using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SsmsMcp.Server;

[McpServerToolType]
public sealed class SsmsTools
{
    private readonly ISsmsRequestClient _client;

    public SsmsTools(ISsmsRequestClient client)
    {
        _client = client;
    }

    [McpServerTool(Name = "ssms_instances"), Description("Vis prosess-ID og vindustittel for kjørende SSMS-instanser. MCP-adressen for hver instans vises i dens statusvindu.")]
    public string ListInstances()
    {
        Process[] processes = Process.GetProcessesByName("SSMS");
        try
        {
            return JsonSerializer.Serialize(processes.Select(process => new
            {
                processId = process.Id,
                title = process.MainWindowTitle
            }));
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    [McpServerTool(Name = "ssms_context"), Description("Hent server, database, aktiv SQL-tekst og markering fra SSMS.")]
    public Task<string> GetContextAsync(CancellationToken cancellationToken)
    {
        return _client.SendAsync(new { operation = "context" }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_list_objects"), Description("List tabeller og visninger i aktiv database.")]
    public Task<string> ListObjectsAsync(McpServer server, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "objects" }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_describe_object"), Description("Beskriv kolonner i en tabell eller visning i aktiv database.")]
    public Task<string> DescribeObjectAsync(McpServer server, string schema, string name, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "describe", schema, name }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_preview_rows"), Description("Vis inntil 100 rader fra en tabell eller visning etter godkjenning i MCP-klienten.")]
    public Task<string> PreviewRowsAsync(McpServer server, string schema, string name, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "preview", schema, name }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_run_select"), Description("Kjør én avgrenset SELECT-setning etter godkjenning i MCP-klienten.")]
    public Task<string> RunSelectAsync(McpServer server, string sql, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "select", sql }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_run_delete"), Description("Kjør én DELETE-setning. Krever bekreftelse i SSMS før kjøring.")]
    public Task<string> RunDeleteAsync(McpServer server, string sql, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "delete", sql },
            cancellationToken, confirmInSsms: true);
    }

    [McpServerTool(Name = "ssms_run_update"), Description("Kjør én UPDATE-setning. Krever bekreftelse i SSMS før kjøring.")]
    public Task<string> RunUpdateAsync(McpServer server, string sql, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "update", sql },
            cancellationToken, confirmInSsms: true);
    }

    [McpServerTool(Name = "ssms_run_insert"), Description("Kjør én INSERT-setning. Krever bekreftelse i SSMS før kjøring.")]
    public Task<string> RunInsertAsync(McpServer server, string sql, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "insert", sql },
            cancellationToken, confirmInSsms: true);
    }

    [McpServerTool(Name = "ssms_probe_session"), Description("Verifiser samme SPID: opprett først #ssms_mcp_probe i aktivt SSMS-vindu.")]
    public Task<string> ProbeSessionAsync(McpServer server, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "probe" }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_copy_database"), Description("Kopier aktiv brukerdatabase til et nytt navn på samme SQL Server-instans via COPY_ONLY-backup og RESTORE WITH MOVE. Krever kataloger på SQL Server-verten og godkjenning.")]
    public Task<string> CopyDatabaseAsync(McpServer server, string targetDatabase, string backupDirectory,
        string dataDirectory, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new
        {
            operation = "prepare", action = "copy_database", targetDatabase, backupDirectory, dataDirectory
        }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_inspect_recovery"), Description("Undersøk recovery-modell, backuphistorikk og kjente avhengigheter for databasen i aktiv SQL-fane. Vurder krav til gjenoppretting til tidspunkt og backupplan før endring; historikk alene avgjør ikke dette.")]
    public Task<string> InspectRecoveryAsync(McpServer server, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "inspect_recovery" }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_set_recovery_simple"), Description("Endre databasen i aktiv SQL-fane fra FULL til SIMPLE etter vurdering av gjenopprettingskrav og backupplan. Bryter loggbackupkjeden og krever bekreftelse i SSMS.")]
    public Task<string> SetRecoverySimpleAsync(McpServer server, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "set_recovery_simple" },
            cancellationToken, confirmInSsms: true);
    }

    [McpServerTool(Name = "ssms_set_database_offline"), Description("Ta en registrert testdatabase offline via en egen master-tilkobling. Valgfri umiddelbar tilbakerulling.")]
    public Task<string> SetDatabaseOfflineAsync(McpServer server, string database, bool rollbackImmediate = false,
        CancellationToken cancellationToken = default)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "set_database_offline",
            database, rollbackImmediate }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_set_database_online"), Description("Sett en registrert testdatabase online via master og bekreft tilstanden.")]
    public Task<string> SetDatabaseOnlineAsync(McpServer server, string database,
        CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "set_database_online", database },
            cancellationToken);
    }

    [McpServerTool(Name = "ssms_kill_session"), Description("Avslutt en SPID etter kontroll av at den tilhører en registrert testdatabase.")]
    public Task<string> KillSessionAsync(McpServer server, string database, int sessionId,
        CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "kill_session", database,
            sessionId }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_hold_lock"), Description("Hold en eksklusiv tabellås i en vedvarende SQL-transaksjon i høyst 300 sekunder.")]
    public Task<string> HoldLockAsync(McpServer server, string database, string schema, string table,
        int maximumSeconds, CancellationToken cancellationToken)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "hold_lock",
            database, schema, table, maximumSeconds }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_release_lock"), Description("Frigi en MCP-styrt tabellås og rull tilbake transaksjonen.")]
    public Task<string> ReleaseLockAsync(string lockId, CancellationToken cancellationToken)
    {
        return _client.SendAsync(new { operation = "release_lock", lockId }, cancellationToken);
    }

    [McpServerTool(Name = "ssms_reset_database_copy"), Description("Gjenopprett en registrert databasekopi fra original backup, eller slett kopien.")]
    public Task<string> ResetDatabaseCopyAsync(McpServer server, string database, bool delete = false,
        bool rollbackImmediate = false, CancellationToken cancellationToken = default)
    {
        return ExecutePreparedAsync(server, new { operation = "prepare", action = "reset_database_copy",
            database, delete, rollbackImmediate }, cancellationToken);
    }

    private async Task<string> ExecutePreparedAsync(McpServer server, object request,
        CancellationToken cancellationToken, bool confirmInSsms = false)
    {
        string preparedJson = await _client.SendAsync(request, cancellationToken);
        using JsonDocument prepared = JsonDocument.Parse(preparedJson);
        JsonElement details = prepared.RootElement;
        string token = details.GetProperty("token").GetString() ?? throw new InvalidOperationException("MCP-token mangler.");

        try
        {
            if (!confirmInSsms && server.ClientCapabilities?.Elicitation?.Form is not null)
                await ConfirmInClientAsync(server, details, cancellationToken);

            return await _client.SendAsync(new { operation = "execute", token }, cancellationToken);
        }
        catch (Exception)
        {
            try
            {
                await _client.SendAsync(new { operation = "cancel", token }, CancellationToken.None);
            }
            catch (Exception)
            {
                // Et allerede brukt eller utløpt token trenger ikke rydding.
            }

            throw;
        }
    }

    private static async Task ConfirmInClientAsync(McpServer server, JsonElement details, CancellationToken cancellationToken)
    {
        string serverName = details.GetProperty("Server").GetString() ?? string.Empty;
        string database = details.GetProperty("Database").GetString() ?? string.Empty;
        string sql = details.GetProperty("sql").GetString() ?? string.Empty;
        string mode = details.GetProperty("sessionMode").GetString() ?? string.Empty;
        string parameters = string.Empty;
        if (details.TryGetProperty("schema", out JsonElement schema) && schema.ValueKind == JsonValueKind.String)
            parameters = $"\nSkjema: {schema.GetString()}\nObjekt: {details.GetProperty("name").GetString()}";

        string actionName = details.TryGetProperty("action", out JsonElement action)
            ? action.GetString() ?? string.Empty : string.Empty;
        bool copiesDatabase = actionName == "copy_database";
        if (copiesDatabase)
            parameters = $"\nMåldatabase: {details.GetProperty("targetDatabase").GetString()}" +
                $"\nBackupfil: {details.GetProperty("backupPath").GetString()}";
        else if (actionName is "set_database_offline" or "set_database_online" or "kill_session" or
            "hold_lock" or "reset_database_copy")
        {
            parameters = $"\nTestdatabase: {details.GetProperty("targetDatabase").GetString()}";
            if (actionName == "hold_lock")
                parameters += $"\nMaksimal låsetid: {details.GetProperty("maximumSeconds").GetInt32()} sekunder";
        }

        ElicitResult answer = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Godkjenn {(copiesDatabase ? "databasekopiering" : actionName is "objects" or "describe" or "preview" or "select" or "probe" ? "lesing" : "SQL-handling")} fra SSMS MCP Server?\nServer: {serverName}\nDatabase: {database}\nSesjon: {mode}{parameters}\nSQL:\n{sql}",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["approved"] = new ElicitRequestParams.BooleanSchema
                    {
                        Description = copiesDatabase ? "Godkjenn backup og opprettelse av ny database" :
                            "Godkjenn denne SQL-handlingen",
                        Default = false
                    }
                }
            }
        }, cancellationToken);

        if (answer.Action != "accept" || answer.Content is null ||
            !answer.Content.TryGetValue("approved", out JsonElement approval) || approval.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("SQL-spørringen ble avslått i MCP-klienten.");
    }
}
