using System;

namespace SsmsMcp.Core;

public static class DatabaseCopyScript
{
    public static string BackupPath(string backupDirectory, string operationId)
    {
        return JoinPath(backupDirectory, $"ssms-mcp-{operationId}.bak");
    }

    public static string Build(string sourceDatabase, string targetDatabase, string backupPath,
        string dataDirectory, string operationId)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabase) || sourceDatabase.Length > 128 ||
            string.IsNullOrWhiteSpace(targetDatabase) || targetDatabase.Length > 128)
            throw new ArgumentException("Databasenavn må være mellom 1 og 128 tegn.");

        if (string.Equals(sourceDatabase, targetDatabase, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Kopien må ha et annet databasenavn enn kilden.");

        if (string.Equals(sourceDatabase, "master", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceDatabase, "model", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceDatabase, "msdb", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceDatabase, "tempdb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Systemdatabaser kan ikke kopieres med dette verktøyet.");

        string filePrefix = JoinPath(dataDirectory, $"ssms-mcp-{operationId}-");
        string sourceLiteral = Literal(sourceDatabase);
        string targetLiteral = Literal(targetDatabase);
        string backupLiteral = Literal(backupPath);
        string prefixLiteral = Literal(filePrefix);
        string sourceIdentifier = Identifier(sourceDatabase);
        string targetIdentifier = Identifier(targetDatabase);

        return $@"SET NOCOUNT ON;
IF DB_ID({sourceLiteral}) IS NULL THROW 50000, N'Kildedatabasen finnes ikke.', 1;
IF DB_ID({targetLiteral}) IS NOT NULL THROW 50001, N'Måldatabasen finnes allerede.', 1;
IF EXISTS (SELECT 1 FROM sys.master_files WHERE database_id = DB_ID({sourceLiteral}) AND type NOT IN (0, 1))
    THROW 50002, N'Kilden har filtyper som dette verktøyet ikke støtter.', 1;
DECLARE @moves nvarchar(max) = N'';
SELECT @moves = @moves + N', MOVE N''' + REPLACE(name, N'''', N'''''') + N''' TO N''' +
    REPLACE({prefixLiteral} + CONVERT(nvarchar(20), file_id) +
    CASE WHEN type = 1 THEN N'.ldf' WHEN file_id = 1 THEN N'.mdf' ELSE N'.ndf' END,
        N'''', N'''''') + N''''
FROM sys.master_files WHERE database_id = DB_ID({sourceLiteral});
IF @moves = N'' THROW 50003, N'Ingen databasefiler ble funnet.', 1;
BACKUP DATABASE {sourceIdentifier} TO DISK = {backupLiteral} WITH COPY_ONLY, INIT;
IF DB_ID({targetLiteral}) IS NOT NULL THROW 50001, N'Måldatabasen finnes allerede.', 1;
DECLARE @restore nvarchar(max) = {Literal($"RESTORE DATABASE {targetIdentifier} FROM DISK = ")} +
    {Literal(backupLiteral)} + N' WITH RECOVERY' + @moves;
EXEC (@restore);";
    }

    private static string JoinPath(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.Length > 1024 ||
            directory.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
            !(directory.StartsWith("/", StringComparison.Ordinal) ||
              directory.StartsWith(@"\\", StringComparison.Ordinal) ||
              directory.Length >= 3 && char.IsLetter(directory[0]) && directory[1] == ':' &&
              (directory[2] == '\\' || directory[2] == '/')))
            throw new ArgumentException("Angi en absolutt katalogbane på SQL Server-verten.");

        char separator = directory.StartsWith("/", StringComparison.Ordinal) ? '/' : '\\';
        return $"{directory.TrimEnd('/', '\\')}{separator}{fileName}";
    }

    private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";

    private static string Identifier(string value) => $"[{value.Replace("]", "]]")}]";
}
