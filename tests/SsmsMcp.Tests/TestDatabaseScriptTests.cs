using Microsoft.SqlServer.TransactSql.ScriptDom;
using SsmsMcp.Core;

namespace SsmsMcp.Tests;

public sealed class TestDatabaseScriptTests
{
    [Theory]
    [InlineData("master")]
    [InlineData("model")]
    [InlineData("msdb")]
    [InlineData("tempdb")]
    public void Offline_rejects_system_databases(string database)
    {
        Assert.Throws<ArgumentException>(() => TestDatabaseScript.Offline(database, true));
    }

    [Fact]
    public void Administrative_scripts_escape_names_and_backup_paths()
    {
        string offline = TestDatabaseScript.Offline("Test]Db", true);
        string online = TestDatabaseScript.Online("Test]Db");
        string tableLock = TestDatabaseScript.LockTable("d]bo", "Test]Table");
        string restore = TestDatabaseScript.Restore("Test]Db", @"C:\Back'up\copy.bak", true,
            new Dictionary<string, string> { ["Data'File"] = @"D:\Data\target.mdf" });
        string drop = TestDatabaseScript.Drop("Test]Db", true);

        Assert.Contains("ALTER DATABASE [Test]]Db] SET OFFLINE WITH ROLLBACK IMMEDIATE", offline);
        Assert.Contains("FROM [d]]bo].[Test]]Table] WITH (TABLOCKX, HOLDLOCK)", tableLock);
        Assert.Contains("C:\\Back''up\\copy.bak", restore);
        Assert.Contains("MOVE N'Data''File' TO N'D:\\Data\\target.mdf'", restore);
        foreach (string script in new[] { offline, online, tableLock, restore, drop,
                     TestDatabaseScript.Kill(42) })
        {
            TSql160Parser parser = new(true);
            using StringReader reader = new(script);
            parser.Parse(reader, out IList<ParseError> errors);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Restore_requires_move_destinations()
    {
        Assert.Throws<ArgumentException>(() => TestDatabaseScript.Restore("TestDb", "backup.bak", false,
            Array.Empty<KeyValuePair<string, string>>()));
    }
}
