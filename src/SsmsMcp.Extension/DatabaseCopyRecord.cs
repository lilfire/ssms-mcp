using System;
using System.Collections.Generic;

namespace SsmsMcp.Extension;

public sealed class DatabaseCopyRecord
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string SourceDatabase { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public int DatabaseId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DatabaseCopyFile> Files { get; set; } = new();
}
