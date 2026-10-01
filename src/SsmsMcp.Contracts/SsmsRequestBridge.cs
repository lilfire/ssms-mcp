using System;

namespace SsmsMcp.Contracts;

public sealed class SsmsRequestBridge : MarshalByRefObject
{
    private readonly Func<string, string> _sender;

    public SsmsRequestBridge(Func<string, string> sender) => _sender = sender;

    public string Send(string requestJson) => _sender(requestJson);

    public override object InitializeLifetimeService() => null!;
}
