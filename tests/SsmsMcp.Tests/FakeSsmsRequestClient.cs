using System.Text.Json;
using SsmsMcp.Server;

namespace SsmsMcp.Tests;

public sealed class FakeSsmsRequestClient : ISsmsRequestClient
{
    public Task<string> SendAsync(object request, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(request));
        string operation = document.RootElement.GetProperty("operation").GetString() ?? string.Empty;
        if (operation == "context")
            return Task.FromResult("{\"server\":\"test-server\",\"database\":\"master\"}");
        if (operation == "prepare")
            return Task.FromResult("{\"token\":\"test-token\",\"Server\":\"test-server\",\"Database\":\"master\",\"sql\":\"SELECT 1\",\"sessionMode\":\"separate\",\"action\":\"select\"}");
        if (operation == "execute")
            return Task.FromResult("{\"rowCount\":1}");
        return Task.FromResult("{\"canceled\":true}");
    }
}
