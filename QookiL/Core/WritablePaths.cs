namespace QookiL.Core;

/// <summary>
/// 可写目录探测。
/// 某些启动方式（受限终端、沙箱环境、部分安全软件）下进程无权写系统临时目录，
/// 而 ASP.NET Core 处理上传时会把请求体缓冲到临时文件，写不进去上传就直接失败
/// （现象是手机端提示“Access to the path …\Temp\… is denied”）。
/// 启动时探测一个确实能写的目录，并把进程级 TMP/TEMP 指过去。
/// </summary>
public static class WritablePaths
{
    private static string? _tempDir;

    /// <summary>实际使用的临时目录（只探测一次，结果缓存）。</summary>
    public static string TempDir => _tempDir ??= Probe();

    private static string Probe()
    {
        var candidates = new List<string>
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QookiL", "temp"),
            Path.Combine(AppContext.BaseDirectory, "temp"),
        };

        foreach (var dir in candidates)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, "qookil-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return dir;
            }
            catch
            {
                // 这个目录不可写，换下一个候选
            }
        }
        return AppContext.BaseDirectory; // 最后的兜底：程序所在目录
    }

    /// <summary>
    /// 把进程级 TMP/TEMP 指向可写目录。必须在启动早期调用：
    /// 之后 Kestrel / ASP.NET Core 的表单缓冲才会落到这个目录。
    /// </summary>
    public static void RedirectProcessTemp()
    {
        try
        {
            var dir = TempDir;
            Environment.SetEnvironmentVariable("TMP", dir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("TEMP", dir, EnvironmentVariableTarget.Process);
        }
        catch
        {
            // 改不了环境变量也不致命：上传失败时会返回明确错误
        }
    }
}
