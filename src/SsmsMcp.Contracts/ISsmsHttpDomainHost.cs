namespace SsmsMcp.Contracts;

public interface ISsmsHttpDomainHost
{
    string Start(SsmsRequestBridge bridge, string token);

    void Stop();
}
