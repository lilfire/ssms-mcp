using SsmsMcp.Core;

namespace SsmsMcp.Tests;

public sealed class SelectValidatorTests
{
    private readonly SelectValidator _validator = new();

    [Theory]
    [InlineData("SELECT TOP (10) name FROM sys.objects")]
    [InlineData("WITH names AS (SELECT name FROM sys.objects) SELECT name FROM names")]
    public void Validate_single_read_query_accepts_it(string sql)
    {
        _validator.Validate(sql);
    }

    [Theory]
    [InlineData("SELECT * INTO dbo.Copied FROM dbo.Source")]
    [InlineData("SELECT 1; DELETE FROM dbo.Users")]
    [InlineData("EXEC dbo.DoSomething")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Counter")]
    public void Validate_write_or_multiple_statements_rejects_it(string sql)
    {
        Assert.ThrowsAny<Exception>(() => _validator.Validate(sql));
    }
}
