using System.Threading;
using System.Threading.Tasks;

namespace SsmsMcp.Server;

public interface ISsmsRequestClient
{
    Task<string> SendAsync(object request, CancellationToken cancellationToken);
}
