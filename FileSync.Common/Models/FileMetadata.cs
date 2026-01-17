using System;

namespace FileSync.Common.Models;

public class FileMetadata
{
    public string RelativePath { get; set; } = string.Empty;
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTime CreationTimeUtc { get; set; }
    public bool IsDeleted { get; set; }
    public long Size { get; set; }
}
