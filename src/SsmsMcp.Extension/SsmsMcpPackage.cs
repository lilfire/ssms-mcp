using System;
using System.Diagnostics;
using System.ComponentModel.Design;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using System.Windows.Forms;
using SsmsMcp.Contracts;
using SsmsMcp.Server;

namespace SsmsMcp.Extension;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid("91eac56b-7ef5-44d0-a7ea-2110ee3acd0e")]
public sealed class SsmsMcpPackage : AsyncPackage
{
    private SsmsHttpDomainHost? _httpHost;
    private SsmsRequestBridge? _requestBridge;
    private string? _endpoint;
    private SsmsRequestClient? _requestClient;
    private string? _httpError;
    private ResolveEventHandler? _resolveBundledAssembly;
    private readonly Guid _commandSet = new("8824297e-336c-4fe9-912d-4e41abc607ee");

    public SsmsMcpPackage() => WriteStartupInfo("Pakkekonstruktør kalt.");

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        WriteStartupInfo("InitializeAsync kalt.");
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        WriteStartupInfo("Pakkeinitialisering startet.");
        _requestClient = new SsmsRequestClient(this);
        string? token = Environment.GetEnvironmentVariable("SSMS_MCP_TOKEN", EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(token))
            _httpError = "HTTP-token mangler. Kjør scripts/Install.ps1.";
        else
        {
            try
            {
                string directory = Path.GetDirectoryName(typeof(SsmsMcpPackage).Assembly.Location)
                    ?? throw new InvalidOperationException("Utvidelsens installasjonsmappe ble ikke funnet.");
                _resolveBundledAssembly = (_, args) =>
                {
                    string? requester = args.RequestingAssembly?.Location;
                    if (requester is { Length: > 0 } &&
                        !requester.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return null;

                    string? name = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name))
                        return null;
                    string path = Path.Combine(directory, name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
                AppDomain.CurrentDomain.AssemblyResolve += _resolveBundledAssembly;
                _httpHost = new SsmsHttpDomainHost();
                WriteStartupInfo("HTTP-vert opprettet.");
                _requestBridge = new SsmsRequestBridge(_requestClient.Send);
                _endpoint = _httpHost.Start(_requestBridge, token);
                WriteStartupInfo($"HTTP-server startet på {_endpoint}.");
            }
            catch (Exception error)
            {
                _httpError = error.ToString();
                WriteStartupError(error);
                try { _httpHost?.Stop(); }
                catch (Exception stopError) { WriteStartupError(stopError); }
                _httpHost = null;
                if (_resolveBundledAssembly is not null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= _resolveBundledAssembly;
                    _resolveBundledAssembly = null;
                }
            }
        }
        IMenuCommandService? commands = await GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;
        commands?.AddCommand(new MenuCommand(ShowStatus, new CommandID(_commandSet, 0x0100)));
        WriteStartupInfo(commands is null ? "Menytjenesten mangler." : "Statuskommando registrert.");
    }

    private static void WriteStartupInfo(string message)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SsmsMcpServer");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "extension.log"),
                $"{DateTimeOffset.Now:O} {message}\r\n");
        }
        catch (Exception)
        {
        }
    }

    private static void WriteStartupError(Exception error)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SsmsMcpServer");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "extension.log"),
                $"{DateTimeOffset.Now:O} Oppstartsfeil: {error}\r\n");
        }
        catch (Exception)
        {
        }
    }

    private void ShowStatus(object sender, EventArgs e)
    {
        string endpoint = _endpoint ?? _httpError ?? "HTTP-serveren er ikke startet.";
        string message = $"SSMS-prosess: {Process.GetCurrentProcess().Id}\r\nMCP-adresse: {endpoint}\r\n{_requestClient?.SessionStatus}";
        MessageBox.Show(message, "SSMS MCP Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpHost?.Stop();
            if (_resolveBundledAssembly is not null)
                AppDomain.CurrentDomain.AssemblyResolve -= _resolveBundledAssembly;
            _requestClient?.Dispose();
        }

        base.Dispose(disposing);
    }
}
