using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SsmsMcp.Contracts;

namespace SsmsMcp.Server;

public sealed class SsmsHttpDomainHost : MarshalByRefObject, ISsmsHttpDomainHost
{
    private SsmsHttpServer? _server;

    public string Start(SsmsRequestBridge bridge, string token)
    {
        if (_server is not null)
            throw new InvalidOperationException("HTTP-serveren er allerede startet.");

        SsmsHttpServer server = new(new DomainRequestClient(bridge), token);
        server.Start();
        _server = server;
        return server.Endpoint!;
    }

    public void Stop()
    {
        _server?.Dispose();
        _server = null;
    }

    public override object InitializeLifetimeService() => null!;

    private sealed class DomainRequestClient : ISsmsRequestClient
    {
        private readonly SsmsRequestBridge _bridge;

        public DomainRequestClient(SsmsRequestBridge bridge) => _bridge = bridge;

        public Task<string> SendAsync(object request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string requestJson = JsonSerializer.Serialize(request);
            return Task.Run(() => _bridge.Send(requestJson), cancellationToken);
        }
    }
}
