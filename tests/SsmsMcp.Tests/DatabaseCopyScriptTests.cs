using Microsoft.SqlServer.TransactSql.ScriptDom;
using SsmsMcp.Core;

namespace SsmsMcp.Tests;

public sealed class DatabaseCopyScriptTests
{
    [Fact]
    public void Build_escapes_identifiers_and_literals()
    {
        string backupPath = DatabaseCopyScript.BackupPath(@"C:\Back'ups", "abc123");
        string sql = DatabaseCopyScript.Build("Sales]Old", "Sales'New", backupPath,
            @"D:\SQL'Data", "abc123");

        Assert.Contains("BACKUP DATABASE [Sales]]Old]", sql);
        Assert.Contains("RESTORE DATABASE [Sales''New]", sql);
        Assert.Contains("C:\\Back''ups\\ssms-mcp-abc123.bak", sql);
        Assert.Contains("D:\\SQL''Data\\ssms-mcp-abc123-", sql);
        Assert.Contains("WITH COPY_ONLY, INIT", sql);
        Assert.Contains("WITH RECOVERY", sql);
        Assert.Contains("type NOT IN (0, 1)", sql);

        TSql160Parser parser = new(true);
        using StringReader reader = new(sql);
        parser.Parse(reader, out IList<ParseError> errors);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Sales", "Sales")]
    [InlineData("master", "Copy")]
    public void Build_rejects_invalid_source_or_target(string source, string target)
    {
        Assert.Throws<ArgumentException>(() => DatabaseCopyScript.Build(source, target,
            @"C:\Backup\copy.bak", @"D:\Data", "abc123"));
    }

    [Fact]
    public void BackupPath_requires_absolute_server_directory()
    {
        Assert.Throws<ArgumentException>(() => DatabaseCopyScript.BackupPath("relative", "abc123"));
    }
}
