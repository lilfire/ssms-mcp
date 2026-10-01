using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SsmsMcp.Server;

namespace SsmsMcp.Tests;

public sealed class HttpIntegrationTests
{
    [Fact]
    public async Task Direct_http_accepts_current_protocol_client()
    {
        using TcpListener portReservation = new(IPAddress.Loopback, 0);
        portReservation.Start();
        int port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        using SsmsHttpServer server = new(new FakeSsmsRequestClient(), "integration-secret", port);
        server.Start();
        Assert.NotNull(server.Endpoint);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer integration-secret" }
        });

        await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("ssms_context", cancellationToken: timeout.Token);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains("test-server", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Direct_http_rejects_requests_without_token_and_with_foreign_origin()
    {
        using TcpListener portReservation = new(IPAddress.Loopback, 0);
        portReservation.Start();
        int port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        using SsmsHttpServer server = new(new FakeSsmsRequestClient(), "integration-secret", port);
        server.Start();
        using HttpClient client = new();

        using HttpRequestMessage missingToken = new(HttpMethod.Post, server.Endpoint)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage firstResponse = await client.SendAsync(missingToken);
        Assert.Equal(HttpStatusCode.Forbidden, firstResponse.StatusCode);

        using HttpRequestMessage foreignOrigin = new(HttpMethod.Post, server.Endpoint)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        foreignOrigin.Headers.TryAddWithoutValidation("Authorization", "Bearer integration-secret");
        foreignOrigin.Headers.TryAddWithoutValidation("Origin", "https://example.org");
        using HttpResponseMessage secondResponse = await client.SendAsync(foreignOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Direct_http_exposes_tools_and_preserves_client_approval()
    {
        using TcpListener portReservation = new(IPAddress.Loopback, 0);
        portReservation.Start();
        int port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        using SsmsHttpServer server = new(new FakeSsmsRequestClient(), "integration-secret", port);
        server.Start();
        Assert.NotNull(server.Endpoint);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer integration-secret" }
        });
        bool prompted = false;
        McpClientOptions options = new()
        {
            ProtocolVersion = "2025-11-25",
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() }
            },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, _) =>
                {
                    prompted = true;
                    Assert.Contains("SELECT 1", request?.Message);
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["approved"] = JsonSerializer.SerializeToElement(true)
                        }
                    });
                }
            }
        };

        await using McpClient client = await McpClient.CreateAsync(transport, options, cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, tool => tool.Name == "ssms_context");
        var result = await client.CallToolAsync("ssms_run_select", new Dictionary<string, object?>
        {
            ["sql"] = "SELECT 1"
        }, cancellationToken: timeout.Token);
        Assert.True(prompted);
        Assert.NotEqual(true, result.IsError);
    }
}
