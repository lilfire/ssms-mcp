using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SsmsMcp.Server;

public sealed class SsmsHttpServer : IDisposable
{
    private readonly ISsmsRequestClient _client;
    private readonly string _token;
    private readonly int _firstPort;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, SsmsHttpSession> _sessions = new();
    private readonly TimeSpan _sessionIdleTimeout = TimeSpan.FromHours(2);
    private HttpListener? _listener;

    public SsmsHttpServer(ISsmsRequestClient client, string token, int firstPort = SsmsHttpConstants.FIRST_PORT)
    {
        _client = client;
        _token = string.IsNullOrWhiteSpace(token) ? throw new ArgumentException("HTTP-token mangler.", nameof(token)) : token;
        _firstPort = firstPort;
    }

    public string? Endpoint { get; private set; }

    public void Start()
    {
        for (int port = _firstPort; port < _firstPort + SsmsHttpConstants.PORT_COUNT; port++)
        {
            HttpListener listener = new();
            listener.Prefixes.Add($"http://{SsmsHttpConstants.LOOPBACK_HOST}:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                Endpoint = $"http://{SsmsHttpConstants.LOOPBACK_HOST}:{port}{SsmsHttpConstants.MCP_PATH}";
                _ = Task.Run(AcceptAsync);
                return;
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Ingen lokal HTTP-port er tilgjengelig for SSMS MCP Server.");
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Close();
        foreach (SsmsHttpSession session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested && _listener is not null)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                Task handling = Task.Run(() => HandleAsync(context));
                _ = handling.ContinueWith(failed => WriteDiagnostic(failed.Exception!),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            catch (HttpListenerException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        try
        {
            if (!Authorize(context.Request))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            switch (context.Request.HttpMethod)
            {
                case "POST":
                    await HandlePostAsync(context);
                    break;
                case "GET":
                    await HandleGetAsync(context);
                    break;
                case "DELETE":
                    HandleDelete(context);
                    break;
                default:
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    response.Headers["Allow"] = "POST, GET, DELETE";
                    break;
            }
        }
        catch (JsonException)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        catch (InvalidDataException)
        {
            response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
        }
        catch (Exception error)
        {
            WriteDiagnostic(error);
            try
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch (InvalidOperationException)
            {
            }
        }
        finally
        {
            response.Close();
        }
    }

    private bool Authorize(HttpListenerRequest request)
    {
        if (request.RemoteEndPoint is null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
            return false;

        Uri? url = request.Url;
        if (url is null || url.Host != SsmsHttpConstants.LOOPBACK_HOST ||
            url.AbsolutePath != SsmsHttpConstants.MCP_PATH && url.AbsolutePath != SsmsHttpConstants.MCP_PATH + "/")
            return false;

        string? origin = request.Headers[SsmsHttpConstants.ORIGIN_HEADER];
        if (origin is not null && origin != $"http://{SsmsHttpConstants.LOOPBACK_HOST}:{url.Port}")
            return false;

        string? authorization = request.Headers[SsmsHttpConstants.AUTHORIZATION_HEADER];
        if (authorization is null || !authorization.StartsWith(SsmsHttpConstants.BEARER_PREFIX, StringComparison.Ordinal))
            return false;

        byte[] expected = Encoding.UTF8.GetBytes(_token);
        byte[] received = Encoding.UTF8.GetBytes(authorization.Substring(SsmsHttpConstants.BEARER_PREFIX.Length));
        if (expected.Length != received.Length)
            return false;

        int difference = 0;
        for (int index = 0; index < expected.Length; index++)
            difference |= expected[index] ^ received[index];
        return difference == 0;
    }

    private async Task HandlePostAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        if (request.ContentType is null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
            return;
        }
        if (request.ContentLength64 > SsmsHttpConstants.MAX_REQUEST_LENGTH)
        {
            context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
            return;
        }

        string body = await ReadBodyAsync(request.InputStream);
        JsonRpcMessage message = JsonSerializer.Deserialize<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions)
            ?? throw new JsonException("MCP-meldingen mangler.");
        bool modern = request.Headers[SsmsHttpConstants.PROTOCOL_HEADER] == SsmsHttpConstants.MODERN_PROTOCOL_VERSION;
        SsmsHttpSession? session = ResolvePostSession(context, message, modern);
        if (session is null)
            return;

        try
        {
            session.Touch();
            if (!modern)
                context.Response.Headers[SsmsHttpConstants.SESSION_HEADER] = session.Id;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            bool wrote = await session.Transport.HandlePostRequestAsync(message, context.Response.OutputStream, _stop.Token);
            if (!wrote)
            {
                context.Response.ContentType = null;
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
            }
        }
        finally
        {
            if (modern)
                session.Dispose();
        }
    }

    private SsmsHttpSession? ResolvePostSession(HttpListenerContext context, JsonRpcMessage message, bool modern)
    {
        if (modern)
            return CreateSession(stateless: true);

        string? sessionId = context.Request.Headers[SsmsHttpConstants.SESSION_HEADER];
        if (!string.IsNullOrEmpty(sessionId))
        {
            if (_sessions.TryGetValue(sessionId, out SsmsHttpSession? existing))
                return existing;
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return null;
        }

        if (message is not JsonRpcRequest { Method: "initialize" })
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return null;
        }

        RemoveIdleSessions();
        if (_sessions.Count >= SsmsHttpConstants.MAX_SESSION_COUNT)
        {
            context.Response.StatusCode = SsmsHttpConstants.TOO_MANY_REQUESTS_STATUS;
            return null;
        }

        SsmsHttpSession session = CreateSession(stateless: false);
        _sessions[session.Id] = session;
        return session;
    }

    private async Task<string> ReadBodyAsync(Stream stream)
    {
        using StreamReader reader = new(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        StringBuilder body = new();
        char[] buffer = new char[4096];
        int length;
        while ((length = await reader.ReadAsync(buffer, 0, buffer.Length)) != 0)
        {
            if (body.Length + length > SsmsHttpConstants.MAX_REQUEST_LENGTH)
                throw new InvalidDataException("MCP-forespørselen er for stor.");
            body.Append(buffer, 0, length);
        }

        return body.ToString();
    }

    private async Task HandleGetAsync(HttpListenerContext context)
    {
        if (!_sessions.TryGetValue(context.Request.Headers[SsmsHttpConstants.SESSION_HEADER] ?? string.Empty, out SsmsHttpSession? session))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.SendChunked = true;
        session.Touch();
        await session.Transport.HandleGetRequestAsync(context.Response.OutputStream, _stop.Token);
    }

    private void HandleDelete(HttpListenerContext context)
    {
        string sessionId = context.Request.Headers[SsmsHttpConstants.SESSION_HEADER] ?? string.Empty;
        if (_sessions.TryRemove(sessionId, out SsmsHttpSession? session))
            session.Dispose();
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    private SsmsHttpSession CreateSession(bool stateless)
    {
        string id = stateless ? string.Empty : Guid.NewGuid().ToString("N");
        StreamableHttpServerTransport transport = new(NullLoggerFactory.Instance)
        {
            Stateless = stateless,
            SessionId = stateless ? null : id
        };
        SsmsTools tools = new(_client);
        McpServerPrimitiveCollection<McpServerTool> collection = new();
        foreach (MethodInfo method in typeof(SsmsTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                collection.Add(McpServerTool.Create(method, tools));
        }

        McpServerOptions options = new()
        {
            ServerInfo = new Implementation { Name = "ssms-mcp", Version = "1.0.0" },
            ToolCollection = collection
        };
        McpServer server = McpServer.Create(transport, options);
        return new SsmsHttpSession(id, transport, server);
    }

    private void RemoveIdleSessions()
    {
        DateTime cutoff = DateTime.UtcNow - _sessionIdleTimeout;
        foreach (SsmsHttpSession session in _sessions.Values)
        {
            if (session.LastUsedUtc < cutoff && _sessions.TryRemove(session.Id, out SsmsHttpSession? removed))
                removed.Dispose();
        }
    }

    private void WriteDiagnostic(Exception error)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SsmsHttpConstants.LOG_DIRECTORY);
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, SsmsHttpConstants.LOG_FILE),
                $"{DateTimeOffset.Now:O} HTTP-feil: {error}\r\n");
            System.Diagnostics.Trace.WriteLine($"SSMS MCP: HTTP-feil: {error}");
        }
        catch (Exception)
        {
            // Logging må aldri stoppe MCP-serveren.
        }
    }

}
