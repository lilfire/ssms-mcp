namespace SsmsMcp.Server;

internal static class SsmsHttpConstants
{
    public const int FIRST_PORT = 51741;
    public const int PORT_COUNT = 16;
    public const int MAX_REQUEST_LENGTH = 1048576;
    public const int MAX_SESSION_COUNT = 32;
    public const int TOO_MANY_REQUESTS_STATUS = 429;
    public const string LOOPBACK_HOST = "127.0.0.1";
    public const string MCP_PATH = "/mcp";
    public const string SESSION_HEADER = "Mcp-Session-Id";
    public const string PROTOCOL_HEADER = "MCP-Protocol-Version";
    public const string ORIGIN_HEADER = "Origin";
    public const string AUTHORIZATION_HEADER = "Authorization";
    public const string BEARER_PREFIX = "Bearer ";
    public const string MODERN_PROTOCOL_VERSION = "2026-07-28";
    public const string LOG_DIRECTORY = "SsmsMcpServer";
    public const string LOG_FILE = "extension.log";
}
