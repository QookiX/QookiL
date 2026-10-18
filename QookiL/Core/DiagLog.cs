namespace QookiL.Core;

/// <summary>
/// 轻量诊断日志：%TEMP%\qookil-diag.log。
/// 记录防火墙检查结论、服务启停等关键事件，排查「其他设备连不上」时可直接查看。
/// </summary>
public static class DiagLog
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "qookil-diag.log");

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
