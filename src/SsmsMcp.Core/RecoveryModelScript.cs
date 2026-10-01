using System;

namespace SsmsMcp.Core;

public static class RecoveryModelScript
{
    public static string Inspect(string database)
    {
        ValidateName(database);
        string name = Literal(database);
        return $@"SELECT d.name AS database_name, d.recovery_model_desc, d.state_desc,
    d.is_read_only, d.group_database_id, d.is_published, d.is_merge_published,
    d.log_reuse_wait_desc,
    (SELECT MAX(b.backup_finish_date) FROM msdb.dbo.backupset AS b
     WHERE b.database_name = d.name AND b.type = 'D' AND b.is_copy_only = 0) AS last_full_backup,
    (SELECT MAX(b.backup_finish_date) FROM msdb.dbo.backupset AS b
     WHERE b.database_name = d.name AND b.type = 'I') AS last_differential_backup,
    (SELECT MAX(b.backup_finish_date) FROM msdb.dbo.backupset AS b
     WHERE b.database_name = d.name AND b.type = 'L') AS last_log_backup,
    (SELECT COUNT_BIG(*) FROM msdb.dbo.backupset AS b
     WHERE b.database_name = d.name AND b.type = 'L'
       AND b.backup_finish_date >= DATEADD(day, -30, GETDATE())) AS log_backups_last_30_days,
    CASE WHEN EXISTS (SELECT 1 FROM msdb.dbo.log_shipping_primary_databases AS p
                      WHERE p.primary_database = d.name)
              OR EXISTS (SELECT 1 FROM msdb.dbo.log_shipping_secondary_databases AS s
                         WHERE s.secondary_database = d.name)
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS log_shipping_configured
FROM sys.databases AS d WHERE d.name = {name} AND d.database_id > 4;";
    }

    public static string SetSimple(string database)
    {
        ValidateName(database);
        string name = Literal(database);
        return $@"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = {name}
    AND database_id > 4 AND state_desc = N'ONLINE' AND is_read_only = 0
    AND recovery_model_desc = N'FULL')
    THROW 50010, N'Databasen må være en skrivbar brukerdatabase i FULL og ONLINE.', 1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = {name}
    AND (group_database_id IS NOT NULL OR is_published = 1 OR is_merge_published = 1))
    THROW 50011, N'Always On eller replikering er konfigurert for databasen.', 1;
IF EXISTS (SELECT 1 FROM msdb.dbo.log_shipping_primary_databases WHERE primary_database = {name})
    OR EXISTS (SELECT 1 FROM msdb.dbo.log_shipping_secondary_databases WHERE secondary_database = {name})
    THROW 50012, N'Log shipping er konfigurert for databasen.', 1;
ALTER DATABASE {Identifier(database)} SET RECOVERY SIMPLE;";
    }

    private static void ValidateName(string database)
    {
        if (string.IsNullOrWhiteSpace(database) || database.Length > 128 || database.IndexOf('\0') >= 0)
            throw new ArgumentException("Ugyldig databasenavn.", nameof(database));
    }

    private static string Identifier(string value) => $"[{value.Replace("]", "]]")}]";

    private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
}
