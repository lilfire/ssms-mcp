using System;
using Newtonsoft.Json.Linq;

namespace SsmsMcp.Extension;

public sealed class PreparedRequest
{
    public PreparedRequest(string token, ActiveContext context, string operation, string sql,
        string? schema, string? name, bool useShared, JObject parameters)
    {
        Token = token;
        Context = context;
        Operation = operation;
        Sql = sql;
        Schema = schema;
        Name = name;
        UseShared = useShared;
        Parameters = (JObject)parameters.DeepClone();
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
    }

    public string Token { get; }
    public ActiveContext Context { get; }
    public string Operation { get; }
    public string Sql { get; }
    public string? Schema { get; }
    public string? Name { get; }
    public bool UseShared { get; }
    public JObject Parameters { get; }
    public DateTimeOffset ExpiresAt { get; }
}
