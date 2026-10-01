using System;
using System.Data;

namespace SsmsMcp.Extension;

public sealed class ActiveContext
{
    public ActiveContext(object editor, string server, string database, string authentication, string userName,
        string connectionString, string text, string selection, bool textTruncated, bool isExecuting, IDbConnection? session)
    {
        Editor = editor;
        Server = server;
        Database = database;
        Authentication = authentication;
        UserName = userName;
        ConnectionString = connectionString;
        Text = text;
        Selection = selection;
        TextTruncated = textTruncated;
        IsExecuting = isExecuting;
        Session = session;
    }

    public object Editor { get; }
    public string Server { get; }
    public string Database { get; }
    public string Authentication { get; }
    public string UserName { get; }
    public string ConnectionString { get; }
    public string Text { get; }
    public string Selection { get; }
    public bool TextTruncated { get; }
    public bool IsExecuting { get; }
    public IDbConnection? Session { get; }
}
