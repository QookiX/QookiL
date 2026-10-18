namespace QookiL.Core;

/// <summary>
/// 轻量诊断日志，写入可写临时目录下的 qookil-diag.log
/// （正常情况下是 %TEMP%，受限环境下自动回退到用户目录/程序目录）。
/// 记录防火墙检查、服务启停、上传失败等关键事件，排查「其他设备连不上」时可直接查看。
/// </summary>
public static class DiagLog
{
    private static string? _path;

    private static string LogPath => _path ??= Path.Combine(WritablePaths.TempDir, "qookil-diag.log");

    public static string LogFilePath => LogPath;

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
        }
        catch
        {
            // 日志失败不影响任何功能
        }
    }
}
