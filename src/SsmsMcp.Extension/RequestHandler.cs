using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using SsmsMcp.Core;

namespace SsmsMcp.Extension;

public sealed class RequestHandler : IDisposable
{
    private readonly AsyncPackage _package;
    private readonly ActiveContextReader _contextReader;
    private readonly SelectValidator _validator = new();
    private readonly WriteValidator _writeValidator = new();
    private readonly SqlExecutor _executor = new();
    private readonly DatabaseCopyRegistry _registry = new();
    private readonly TestDatabaseExecutor _testDatabases;
    private readonly TestTableLock _tableLock;
    private readonly SemaphoreSlim _queries = new(1, 1);
    private readonly Dictionary<string, PreparedRequest> _pending = new();
    private volatile IDbConnection? _verifiedSession;
    private readonly string _objectsSql = "SELECT s.name AS schema_name, o.name AS object_name, o.type_desc FROM sys.objects AS o JOIN sys.schemas AS s ON s.schema_id = o.schema_id WHERE o.type IN ('U', 'V') ORDER BY s.name, o.name";
    private readonly string _describeSql = "SELECT c.name AS column_name, t.name AS data_type, c.max_length, c.is_nullable FROM sys.columns AS c JOIN sys.objects AS o ON o.object_id = c.object_id JOIN sys.schemas AS s ON s.schema_id = o.schema_id JOIN sys.types AS t ON t.user_type_id = c.user_type_id WHERE s.name = @schema AND o.name = @name AND o.type IN ('U', 'V') ORDER BY c.column_id";

    public RequestHandler(AsyncPackage package)
    {
        _package = package;
        _contextReader = new ActiveContextReader(package);
        _testDatabases = new TestDatabaseExecutor(_registry);
        _tableLock = new TestTableLock(_testDatabases);
    }

    public string SessionStatus => _verifiedSession?.State == ConnectionState.Open
        ? "Samme SPID er verifisert for én SQL-tilkobling." : "Separat SQL-sesjon brukes til samme SPID er verifisert.";

    public async Task<object> HandleAsync(JObject request, CancellationToken cancellationToken)
    {
        string operation = request.Value<string>("operation") ?? throw new ArgumentException("Operasjon mangler.");
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (operation == "cancel")
        {
            _pending.Remove(Required(request, "token"));
            return new { canceled = true };
        }

        if (operation == "release_lock")
            return _tableLock.Release(request.Value<string>("lockId"));

        string? action = request.Value<string>("action");
        bool openQueryIfNeeded = operation == "prepare" && action != "probe";
        bool allowDisconnected = action is "set_database_online" or "reset_database_copy" ||
            operation == "execute" && IsOfflineSafeToken(request.Value<string>("token"));
        ActiveContext context = await _contextReader.ReadAsync(openQueryIfNeeded, allowDisconnected);
        return operation switch
        {
            "context" => ContextResult(context),
            "prepare" => Prepare(request, context),
            "execute" => await ExecuteAsync(request, context, cancellationToken),
            _ => throw new ArgumentException("Ukjent MCP-operasjon.", nameof(request))
        };
    }

    public void Dispose()
    {
        _tableLock.Dispose();
    }

    private bool IsOfflineSafeToken(string? token)
    {
        return token is not null && _pending.TryGetValue(token, out PreparedRequest? prepared) &&
            prepared.Operation is "set_database_online" or "reset_database_copy";
    }

    private object ContextResult(ActiveContext context)
    {
        return new
        {
            context.Server,
            context.Database,
            context.Authentication,
            context.UserName,
            context.Text,
            context.Selection,
            context.TextTruncated,
            sessionMode = ReferenceEquals(context.Session, _verifiedSession) && context.Session?.State == ConnectionState.Open
                ? "same_spid" : "separate",
            processId = Process.GetCurrentProcess().Id
        };
    }

    private object Prepare(JObject request, ActiveContext context)
    {
        string action = Required(request, "action");
        string? schema = null;
        string? name = null;
        string sql;
        switch (action)
        {
            case "objects":
                sql = _objectsSql;
                break;
            case "describe":
                schema = Required(request, "schema");
                name = Required(request, "name");
                sql = _describeSql;
                break;
            case "preview":
                schema = Required(request, "schema");
                name = Required(request, "name");
                sql = PreviewSql(schema, name);
                break;
            case "select":
                sql = Required(request, "sql");
                _validator.Validate(sql);
                break;
            case "delete":
            case "update":
            case "insert":
                sql = Required(request, "sql");
                _writeValidator.Validate(sql, action);
                break;
            case "probe":
                if (context.Session?.State != ConnectionState.Open)
                    throw new InvalidOperationException("Ingen levende SQL-sesjon finnes i det aktive vinduet.");
                sql = "SELECT @@SPID AS spid, OBJECT_ID('tempdb..#ssms_mcp_probe') AS probe_object_id";
                break;
            case "inspect_recovery":
                sql = RecoveryModelScript.Inspect(context.Database);
                break;
            case "set_recovery_simple":
                sql = RecoveryModelScript.SetSimple(context.Database);
                break;
            case "copy_database":
                name = Required(request, "targetDatabase");
                string backupDirectory = Required(request, "backupDirectory", 1024);
                string dataDirectory = Required(request, "dataDirectory", 1024);
                string operationId = Guid.NewGuid().ToString("N");
                schema = DatabaseCopyScript.BackupPath(backupDirectory, operationId);
                sql = DatabaseCopyScript.Build(context.Database, name, schema, dataDirectory, operationId);
                break;
            case "set_database_offline":
                name = Required(request, "database");
                _registry.Get(context.Server, name);
                sql = TestDatabaseScript.Offline(name, request.Value<bool?>("rollbackImmediate") ?? false);
                break;
            case "set_database_online":
                name = Required(request, "database");
                _registry.Get(context.Server, name);
                sql = TestDatabaseScript.Online(name);
                break;
            case "kill_session":
                name = Required(request, "database");
                _registry.Get(context.Server, name);
                sql = TestDatabaseScript.Kill(request.Value<int>("sessionId"));
                break;
            case "hold_lock":
                name = Required(request, "database");
                _registry.Get(context.Server, name);
                schema = Required(request, "schema");
                sql = TestDatabaseScript.LockTable(schema, Required(request, "table"));
                int maximumSeconds = request.Value<int>("maximumSeconds");
                if (maximumSeconds < 1 || maximumSeconds > 300)
                    throw new ArgumentOutOfRangeException("maximumSeconds", "Låsen må vare mellom 1 og 300 sekunder.");
                break;
            case "reset_database_copy":
                name = Required(request, "database");
                DatabaseCopyRecord record = _registry.Get(context.Server, name);
                bool rollback = request.Value<bool?>("rollbackImmediate") ?? false;
                sql = request.Value<bool?>("delete") == true
                    ? TestDatabaseScript.Drop(name, rollback)
                    : TestDatabaseScript.Restore(name, record.BackupPath, rollback,
                        record.Files.ConvertAll(file => new KeyValuePair<string, string>(file.LogicalName, file.PhysicalName)));
                break;
            default:
                throw new ArgumentException("Ukjent MCP-handling.", nameof(request));
        }

        if (context.IsExecuting)
            throw new InvalidOperationException("Det aktive SSMS-vinduet kjører allerede en spørring.");

        foreach (string expired in new List<string>(_pending.Keys))
            if (_pending[expired].ExpiresAt <= DateTimeOffset.UtcNow)
                _pending.Remove(expired);

        if (_pending.Count >= 16)
            throw new InvalidOperationException("For mange ventende MCP-kall.");

        bool useShared = !IsSeparateAction(action) && ReferenceEquals(context.Session, _verifiedSession) &&
            context.Session?.State == ConnectionState.Open;
        string token = Guid.NewGuid().ToString("N");
        _pending.Add(token, new PreparedRequest(token, context, action, sql, schema, name, useShared, request));
        return new
        {
            token,
            action,
            context.Server,
            context.Database,
            sql,
            schema = action == "copy_database" ? null : schema,
            name = action == "copy_database" ? null : name,
            targetDatabase = IsSeparateAction(action) ? name : null,
            backupPath = action == "copy_database" ? schema : null,
            maximumSeconds = action == "hold_lock" ? request.Value<int?>("maximumSeconds") : null,
            sessionMode = action == "probe" ? "probe" : IsSeparateAction(action) ? "separate" : useShared ? "same_spid" : "separate",
            processId = Process.GetCurrentProcess().Id
        };
    }

    private async Task<object> ExecuteAsync(JObject request, ActiveContext current, CancellationToken cancellationToken)
    {
        string token = Required(request, "token");
        if (!_pending.TryGetValue(token, out PreparedRequest? prepared))
            throw new InvalidOperationException("MCP-kallet er ukjent eller allerede brukt.");

        _pending.Remove(token);
        if (prepared.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new TimeoutException("Godkjenningen brukte for lang tid.");

        EnsureSameActiveContext(prepared.Context, current, !IsSeparateAction(prepared.Operation));
        if (prepared.UseShared && !ReferenceEquals(current.Session, _verifiedSession))
            throw new InvalidOperationException("Den verifiserte SQL-sesjonen ble endret før kjøring.");

        if (!await _queries.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Et annet MCP-kall bruker SQL-tilkoblingen.");

        try
        {
            if (prepared.Operation == "probe")
            {
                if (current.Session?.State != ConnectionState.Open)
                    throw new InvalidOperationException("Ingen levende SQL-sesjon finnes i det aktive vinduet.");

                JObject probe = await Task.Run(() => _executor.Execute(current, prepared.Sql, null, true), cancellationToken);
                JToken? probeId = probe["rows"]?[0]?[1];
                bool verified = probeId is not null && probeId.Type != JTokenType.Null && current.Session is DbConnection;
                if (verified)
                    SetVerifiedSession((DbConnection)current.Session!);
                else if (ReferenceEquals(current.Session, _verifiedSession))
                    ClearVerifiedSession();

                probe["verified"] = verified;
                probe["sessionMode"] = verified ? "same_spid" : "unverified";
                return probe;
            }

            string? password = null;
            if (!prepared.UseShared &&
                (current.Authentication == "1" || current.Authentication == "2"))
            {
                using PasswordDialog dialog = new(current.Server, current.UserName);
                if (dialog.ShowDialog(DialogOwner()) != DialogResult.OK)
                    throw new OperationCanceledException("SQL-pålogging ble avbrutt i SSMS.");

                password = dialog.Password;
            }

            // Passorddialogen kan også gi tid til å bytte vindu eller starte en spørring.
            EnsureSameActiveContext(prepared.Context,
                await _contextReader.ReadAsync(allowDisconnected:
                    prepared.Operation is "set_database_online" or "reset_database_copy"),
                !IsSeparateAction(prepared.Operation));

            if (prepared.Operation is "delete" or "update" or "insert" or "set_recovery_simple")
            {
                using WriteConfirmationDialog dialog = new(current.Server, current.Database,
                    prepared.Operation, prepared.Sql);
                if (dialog.ShowDialog(DialogOwner()) != DialogResult.Yes)
                    throw new OperationCanceledException("Skrivekommandoen ble ikke bekreftet i SSMS.");

                EnsureSameActiveContext(prepared.Context, await _contextReader.ReadAsync());
                if (prepared.Operation == "set_recovery_simple")
                    return await Task.Run(() => _executor.SetRecoverySimple(current, prepared.Sql, password),
                        cancellationToken);

                return await Task.Run(() => _executor.ExecuteWrite(current, prepared.Sql, password,
                    prepared.UseShared), cancellationToken);
            }

            if (prepared.Operation == "copy_database")
            {
                await Task.Run(() => _executor.CopyDatabase(current, prepared.Sql, password, cancellationToken), cancellationToken);
                await Task.Run(() => _testDatabases.RegisterCopy(current, prepared.Name!, prepared.Schema!,
                    password));
                return new { copied = true, sourceDatabase = current.Database,
                    targetDatabase = prepared.Name, backupPath = prepared.Schema };
            }

            if (prepared.Operation == "set_database_offline")
                return await Task.Run(() => _testDatabases.SetOffline(current, prepared.Name!,
                    prepared.Parameters.Value<bool?>("rollbackImmediate") ?? false, password, cancellationToken),
                    cancellationToken);

            if (prepared.Operation == "set_database_online")
                return await Task.Run(() => _testDatabases.SetOnline(current, prepared.Name!, password,
                    cancellationToken), cancellationToken);

            if (prepared.Operation == "kill_session")
                return await Task.Run(() => _testDatabases.KillSession(current, prepared.Name!,
                    prepared.Parameters.Value<int>("sessionId"), password, cancellationToken), cancellationToken);

            if (prepared.Operation == "hold_lock")
                return await Task.Run(() => _tableLock.Hold(current, prepared.Name!, prepared.Schema!,
                    prepared.Parameters.Value<string>("table")!, prepared.Parameters.Value<int>("maximumSeconds"),
                    password), cancellationToken);

            if (prepared.Operation == "reset_database_copy")
                return await Task.Run(() => _testDatabases.ResetCopy(current, prepared.Name!,
                    prepared.Parameters.Value<bool?>("delete") ?? false,
                    prepared.Parameters.Value<bool?>("rollbackImmediate") ?? false,
                    password, cancellationToken), cancellationToken);

            if (prepared.Operation == "inspect_recovery")
                return await Task.Run(() => _executor.InspectRecovery(current, prepared.Sql, password,
                    prepared.UseShared), cancellationToken);

            return await Task.Run(() => _executor.Execute(current, prepared.Sql, password, prepared.UseShared,
                prepared.Schema, prepared.Name), cancellationToken);
        }
        finally
        {
            _queries.Release();
        }
    }

    private static string PreviewSql(string schema, string name)
    {
        string escapedSchema = schema.Replace("]", "]]");
        string escapedName = name.Replace("]", "]]");
        return $"SELECT TOP (101) * FROM [{escapedSchema}].[{escapedName}]";
    }

    private static SsmsDialogOwner DialogOwner()
    {
        IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("SSMS-hovedvinduet er utilgjengelig for passorddialogen.");

        return new SsmsDialogOwner(handle);
    }

    private static string Required(JObject request, string key, int maximumLength = 256)
    {
        string value = request.Value<string>(key) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength && key != "sql")
            throw new ArgumentException($"{key} mangler eller er for langt.", nameof(request));

        return value;
    }

    private static void EnsureSameActiveContext(ActiveContext expected, ActiveContext current,
        bool requireConnection = true)
    {
        if (!ReferenceEquals(current.Editor, expected.Editor) || current.Server != expected.Server ||
            current.Database != expected.Database ||
            requireConnection && !ReferenceEquals(current.Session, expected.Session))
            throw new InvalidOperationException("Aktivt SQL-vindu eller tilkobling ble endret før kjøring.");

        if (current.IsExecuting)
            throw new InvalidOperationException("Det aktive SSMS-vinduet kjører allerede en spørring.");

        if (requireConnection && current.Session?.State != ConnectionState.Open && expected.Session is not null)
            throw new InvalidOperationException("SQL-tilkoblingen ble lukket før kjøring.");
    }

    private static bool IsSeparateAction(string action)
    {
        return action is "copy_database" or "set_database_offline" or "set_database_online" or
            "kill_session" or "hold_lock" or "reset_database_copy" or "set_recovery_simple";
    }

    private void SetVerifiedSession(DbConnection session)
    {
        ClearVerifiedSession();
        _verifiedSession = session;
        session.StateChange += OnVerifiedSessionStateChange;
    }

    private void ClearVerifiedSession()
    {
        if (_verifiedSession is DbConnection previous)
            previous.StateChange -= OnVerifiedSessionStateChange;

        _verifiedSession = null;
    }

    private void OnVerifiedSessionStateChange(object sender, StateChangeEventArgs change)
    {
        if (change.CurrentState == ConnectionState.Open || !ReferenceEquals(sender, _verifiedSession))
            return;

        ((DbConnection)sender).StateChange -= OnVerifiedSessionStateChange;
        _verifiedSession = null;
    }
}
