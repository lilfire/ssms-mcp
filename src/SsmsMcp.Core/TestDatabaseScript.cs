using System;

namespace SsmsMcp.Core;

public static class TestDatabaseScript
{
    public static string Offline(string database, bool rollbackImmediate)
    {
        ValidateName(database);
        return $"ALTER DATABASE {Identifier(database)} SET OFFLINE{(rollbackImmediate ? " WITH ROLLBACK IMMEDIATE" : string.Empty)};";
    }

    public static string Online(string database)
    {
        ValidateName(database);
        return $"ALTER DATABASE {Identifier(database)} SET ONLINE;";
    }

    public static string Kill(int sessionId)
    {
        if (sessionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId));

        return $"KILL {sessionId};";
    }

    public static string LockTable(string schema, string table)
    {
        ValidateName(schema);
        ValidateName(table);
        return $"SELECT TOP (1) 1 FROM {Identifier(schema)}.{Identifier(table)} WITH (TABLOCKX, HOLDLOCK);";
    }

    public static string Restore(string database, string backupPath, bool rollbackImmediate,
        System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> files)
    {
        ValidateName(database);
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new ArgumentException("Backupfil mangler.", nameof(backupPath));

        string rollback = rollbackImmediate
            ? $"IF (SELECT state FROM sys.databases WHERE name = {Literal(database)}) = 0\n" +
              $"    ALTER DATABASE {Identifier(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\n"
            : string.Empty;
        string moves = string.Join(string.Empty, System.Linq.Enumerable.Select(files,
            file => $", MOVE {Literal(file.Key)} TO {Literal(file.Value)}"));
        if (moves.Length == 0)
            throw new ArgumentException("Databasefilene mangler.", nameof(files));

        return $"{rollback}RESTORE DATABASE {Identifier(database)} FROM DISK = {Literal(backupPath)} WITH REPLACE, RECOVERY{moves};\n" +
            $"ALTER DATABASE {Identifier(database)} SET MULTI_USER;";
    }

    public static string Drop(string database, bool rollbackImmediate)
    {
        ValidateName(database);
        string rollback = rollbackImmediate
            ? $"IF (SELECT state FROM sys.databases WHERE name = {Literal(database)}) = 0\n" +
              $"    ALTER DATABASE {Identifier(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\n"
            : string.Empty;
        return $"{rollback}DROP DATABASE {Identifier(database)};";
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.IndexOf('\0') >= 0)
            throw new ArgumentException("Databasenavn må være mellom 1 og 128 tegn.", nameof(name));

        if (string.Equals(name, "master", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "model", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "msdb", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "tempdb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Systemdatabaser er ikke testdatabaser.", nameof(name));
    }

    private static string Identifier(string value) => $"[{value.Replace("]", "]]")}]";

    private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
}
