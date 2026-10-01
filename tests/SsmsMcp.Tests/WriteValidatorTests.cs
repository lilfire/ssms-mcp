using SsmsMcp.Core;

namespace SsmsMcp.Tests;

public sealed class WriteValidatorTests
{
    private readonly WriteValidator _validator = new();

    [Theory]
    [InlineData("delete", "DELETE FROM dbo.Items WHERE Id = 1")]
    [InlineData("update", "UPDATE dbo.Items SET Name = 'Ny' WHERE Id = 1")]
    [InlineData("insert", "INSERT INTO dbo.Items (Id, Name) VALUES (1, 'Ny')")]
    [InlineData("insert", "INSERT INTO dbo.Items (Id) SELECT Id FROM dbo.Source")]
    public void Validate_single_matching_write_accepts_it(string action, string sql)
    {
        _validator.Validate(sql, action);
    }

    [Theory]
    [InlineData("delete", "UPDATE dbo.Items SET Name = 'Ny'")]
    [InlineData("update", "INSERT INTO dbo.Items (Id) VALUES (1)")]
    [InlineData("insert", "DELETE FROM dbo.Items")]
    [InlineData("delete", "DELETE FROM dbo.Items; DELETE FROM dbo.Other")]
    [InlineData("update", "UPDATE dbo.Items SET Name = 'Ny'\nGO\nUPDATE dbo.Other SET Name = 'Ny'")]
    [InlineData("insert", "INSERT INTO dbo.Items (Id) VALUES (NEXT VALUE FOR dbo.Counter)")]
    [InlineData("delete", "DELETE FROM dbo.Items OUTPUT DELETED.Id INTO dbo.Archive")]
    [InlineData("insert", "INSERT INTO dbo.Items EXEC dbo.MakeItems")]
    [InlineData("update", "EXEC dbo.UpdateItems")]
    public void Validate_other_or_extra_write_rejects_it(string action, string sql)
    {
        Assert.ThrowsAny<Exception>(() => _validator.Validate(sql, action));
    }
}
