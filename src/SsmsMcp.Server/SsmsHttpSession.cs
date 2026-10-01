using System;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace SsmsMcp.Server;

internal sealed class SsmsHttpSession : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private long _lastUsedTicks = DateTime.UtcNow.Ticks;
    private int _disposed;

    public SsmsHttpSession(string id, StreamableHttpServerTransport transport, McpServer server)
    {
        Id = id;
        Transport = transport;
        Server = server;
        RunTask = Task.Run(() => server.RunAsync(_stop.Token));
    }

    public string Id { get; }

    public StreamableHttpServerTransport Transport { get; }

    public McpServer Server { get; }

    public Task RunTask { get; }

    public DateTime LastUsedUtc => new(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

    public void Touch()
    {
        Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stop.Cancel();
        _ = Task.Run(async () =>
        {
            try
            {
                await Server.DisposeAsync();
                await RunTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine($"SSMS MCP: Øktfeil: {error}");
            }
            finally
            {
                _stop.Dispose();
            }
        });
    }
}
