namespace QookiL.Core;

/// <summary>一个被共享的项目：文件或文件夹。</summary>
public sealed class SharedItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsFolder { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset LastWrite { get; init; }

    public static SharedItem? FromPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return new SharedItem
                {
                    Name = string.IsNullOrEmpty(name) ? full : name,
                    FullPath = full,
                    IsFolder = true,
                };
            }
            if (File.Exists(full))
            {
                var fi = new FileInfo(full);
                return new SharedItem
                {
                    Name = fi.Name,
                    FullPath = full,
                    IsFolder = false,
                    SizeBytes = fi.Length,
                    LastWrite = fi.LastWriteTime,
                };
            }
        }
        catch
        {
            // 路径非法、无权限等，直接视为不可共享
        }
        return null;
    }
}
