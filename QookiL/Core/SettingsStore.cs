using System.Text.Json;

namespace QookiL.Core;

/// <summary>持久化设置。</summary>
public sealed class AppSettings
{
    public int Port { get; set; } = 5188;
    public string UploadDir { get; set; } = "";
    public bool AutoStart { get; set; }
    public List<string> SharedPaths { get; set; } = new();
}

public static class SettingsStore
{
    private static string? _dir;

    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>设置目录：依次尝试 APPDATA → LOCALAPPDATA → 程序目录，取第一个可写的。</summary>
    private static string Dir => _dir ??= ResolveDir();

    private static string[] Candidates() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QookiL"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QookiL"),
        Path.Combine(AppContext.BaseDirectory, "settings"),
    };

    private static string ResolveDir()
    {
        foreach (var c in Candidates())
        {
            try
            {
                Directory.CreateDirectory(c);
                var probe = Path.Combine(c, ".write-test");
                File.WriteAllText(probe, " ");
                File.Delete(probe);
                return c;
            }
            catch
            {
                // 该位置不可写，试下一个
            }
        }
        // 全部不可写：仍返回 APPDATA（保存会静默失败，但应用不崩）
        return Candidates()[0];
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            // 旧版本可能把设置写进了其他候选位置，全部找一遍
            foreach (var dir in Candidates())
            {
                var file = Path.Combine(dir, "settings.json");
                if (!File.Exists(file)) continue;
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file));
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 保存失败不影响使用
        }
    }
}
