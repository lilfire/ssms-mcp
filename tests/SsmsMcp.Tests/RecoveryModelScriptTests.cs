using SsmsMcp.Core;

namespace SsmsMcp.Tests;

public sealed class RecoveryModelScriptTests
{
    [Fact]
    public void Inspect_escapes_database_name_and_reads_recovery_evidence()
    {
        string sql = RecoveryModelScript.Inspect("Sales']Db");

        Assert.Contains("d.name = N'Sales'']Db'", sql);
        Assert.Contains("recovery_model_desc", sql);
        Assert.Contains("log_shipping_configured", sql);
        Assert.Contains("last_log_backup", sql);
    }

    [Fact]
    public void Set_simple_requires_full_online_user_database_and_checks_dependencies()
    {
        string sql = RecoveryModelScript.SetSimple("Sales']Db");

        Assert.Contains("ALTER DATABASE [Sales']]Db] SET RECOVERY SIMPLE", sql);
        Assert.Contains("recovery_model_desc = N'FULL'", sql);
        Assert.Contains("state_desc = N'ONLINE'", sql);
        Assert.Contains("group_database_id IS NOT NULL", sql);
        Assert.Contains("log_shipping_primary_databases", sql);
    }

    [Fact]
    public void Recovery_scripts_reject_invalid_database_names()
    {
        Assert.Throws<ArgumentException>(() => RecoveryModelScript.Inspect(" "));
        Assert.Throws<ArgumentException>(() => RecoveryModelScript.SetSimple(new string('x', 129)));
    }
}
