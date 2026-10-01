using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SsmsMcp.Contracts;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
            Assembly.LoadFrom(args[0]);

        string directory = args.Length > 1 ? args[1] : AppDomain.CurrentDomain.BaseDirectory;
        AppDomainSetup setup = new AppDomainSetup
        {
            ApplicationBase = directory,
            ConfigurationFile = Path.Combine(directory, "SsmsMcp.Server.config")
        };
        AppDomain domain = AppDomain.CreateDomain("SSMS MCP probe", null, setup);
        try
        {
            var host = (ISsmsHttpDomainHost)domain.CreateInstanceFromAndUnwrap(
                Path.Combine(directory, "SsmsMcp.Server.dll"), "SsmsMcp.Server.SsmsHttpDomainHost");
            string endpoint = host.Start(new SsmsRequestBridge(_ => "{\"server\":\"probe\"}"), "probe-token");
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "probe-token");
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                    client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
                    string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"probe\",\"version\":\"1.0\"}}}";
                    string sessionId;
                    using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await client.PostAsync(endpoint, content))
                    {
                        string result = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Status={(int)response.StatusCode}; Initialize={result.Contains("ssms-mcp")}");
                        if (!response.IsSuccessStatusCode || !result.Contains("ssms-mcp"))
                            return 1;
                        sessionId = response.Headers.GetValues("Mcp-Session-Id").Single();
                    }

                    client.DefaultRequestHeaders.Add("Mcp-Session-Id", sessionId);
                    using (var content = new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await client.PostAsync(endpoint, content))
                        response.EnsureSuccessStatusCode();

                    using (var content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"ssms_context\",\"arguments\":{}}}", Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await client.PostAsync(endpoint, content))
                    {
                        string result = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"ToolStatus={(int)response.StatusCode}; Bridge={result.Contains("probe")}");
                        if (!response.IsSuccessStatusCode || !result.Contains("probe"))
                            return 1;
                    }
                }
            }
            finally
            {
                host.Stop();
            }
        }
        finally
        {
            AppDomain.Unload(domain);
        }
        return 0;
    }

}
