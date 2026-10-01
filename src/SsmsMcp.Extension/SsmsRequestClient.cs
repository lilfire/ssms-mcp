using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SsmsMcp.Extension;

public sealed class SsmsRequestClient : IDisposable
{
    private readonly AsyncPackage _package;
    private readonly RequestHandler _handler;
    private readonly SemaphoreSlim _requests = new(1, 1);

    public SsmsRequestClient(AsyncPackage package)
    {
        _package = package;
        _handler = new RequestHandler(package);
    }

    public string SessionStatus => _handler.SessionStatus;

    public string Send(string requestJson)
    {
        _requests.Wait();
        try
        {
            object result = _package.JoinableTaskFactory.Run(
                () => _handler.HandleAsync(JObject.Parse(requestJson), CancellationToken.None));
            return JsonConvert.SerializeObject(result);
        }
        catch (Exception error)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SsmsMcpServer");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "extension.log"),
                    $"{DateTimeOffset.Now:O} Forespørselsfeil: {error}\r\n");
            }
            catch (Exception)
            {
                // Logging må ikke skjule den opprinnelige feilen.
            }
            throw;
        }
        finally
        {
            _requests.Release();
        }
    }

    public void Dispose()
    {
        _handler.Dispose();
        _requests.Dispose();
    }
}
