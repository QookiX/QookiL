namespace QookiL.Core;

/// <summary>接收文件夹解析与可写性判断。</summary>
public static class UploadDirs
{
    /// <summary>
    /// 目录存在/可创建，并且确实能写入。
    /// 只调 CreateDirectory 不够：受限环境下可能出现“目录已存在、写入却被拒”，
    /// 那样服务能启动、但每次上传写文件都会失败。
    /// </summary>
    public static bool EnsureWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, "qookil-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解析实际使用的接收目录：用户选择 → 系统「下载」→ 用户目录下的「接收」→ 程序目录下的「接收」，
    /// 取第一个可写的。全部不可写时原样返回请求值，由调用方提示错误。
    /// </summary>
    public static (string Dir, bool WasFallback, string Note) Resolve(string? requested)
    {
        var want = requested?.Trim() ?? "";
        if (want.Length > 0 && EnsureWritable(want))
            return (want, false, "");

        var downloads = KnownFolders.TryGetDownloads();
        if (!string.IsNullOrWhiteSpace(downloads) && EnsureWritable(downloads))
            return (downloads, true, "（原接收文件夹不可访问，已改用系统「下载」文件夹）");

        var candidates = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            candidates.Add(Path.Combine(localAppData, "QookiL", "接收"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "接收"));

        foreach (var dir in candidates)
        {
            if (EnsureWritable(dir))
                return (dir, true, $"（系统「下载」文件夹不可写，已改用 {dir}）");
        }

        return (want.Length > 0 ? want : downloads ?? "", false, "");
    }
}
