using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SsmsMcp.Extension;

public sealed class DatabaseCopyRegistry
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SsmsMcpServer", "database-copies.json");
    private readonly object _sync = new();

    public DatabaseCopyRecord Get(string server, string database)
    {
        lock (_sync)
        {
            return Read().FirstOrDefault(record =>
                string.Equals(record.Server, server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.Database, database, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Databasen er ikke registrert som en kopi laget av ssms_copy_database.");
        }
    }

    public void Add(DatabaseCopyRecord record)
    {
        lock (_sync)
        {
            List<DatabaseCopyRecord> records = Read();
            records.RemoveAll(existing =>
                string.Equals(existing.Server, record.Server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Database, record.Database, StringComparison.OrdinalIgnoreCase));
            records.Add(record);
            Write(records);
        }
    }

    public void Remove(string server, string database)
    {
        lock (_sync)
        {
            List<DatabaseCopyRecord> records = Read();
            records.RemoveAll(record =>
                string.Equals(record.Server, server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.Database, database, StringComparison.OrdinalIgnoreCase));
            Write(records);
        }
    }

    private List<DatabaseCopyRecord> Read()
    {
        return File.Exists(_path)
            ? JsonConvert.DeserializeObject<List<DatabaseCopyRecord>>(File.ReadAllText(_path))
                ?? throw new InvalidOperationException("Registeret over databasekopier er ugyldig.")
            : new List<DatabaseCopyRecord>();
    }

    private void Write(List<DatabaseCopyRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(records));
            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, null);
            else
                File.Move(temporaryPath, _path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
