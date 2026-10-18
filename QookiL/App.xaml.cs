using System.IO;
using Microsoft.UI.Xaml;
using QookiL.Core;

namespace QookiL;

// 注意：Main 入口由 WinUI 编译目标在 App.g.i.cs 中自动生成，无需手写。

public partial class App : Application
{
    public static Window? MainWin { get; private set; }

    // 排查用：未处理异常写入 %TEMP%\qookil-crash.log（正式版可移除）
    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "qookil-crash.log");

    public App()
    {
        // 必须在任何可能与临时目录打交道的代码之前：探测可写目录并重定向 TMP/TEMP，
        // 否则在受限环境（沙箱/受限终端）下上传会因临时文件写入被拒而失败。
        WritablePaths.RedirectProcessTemp();

        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(CrashLog,
                    $"{DateTime.Now:HH:mm:ss.fff} [XAML] {e.Exception}\n---\n");
            }
            catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(CrashLog,
                    $"{DateTime.Now:HH:mm:ss.fff} [DOMAIN] {e.ExceptionObject}\n---\n");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                File.AppendAllText(CrashLog,
                    $"{DateTime.Now:HH:mm:ss.fff} [TASK] {e.Exception}\n---\n");
                e.SetObserved();
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 防火墙辅助模式（均不建窗口，处理完直接退出）：
        //   --firewall-setup   一次性持久放行（默认模式，主实例首次授权）
        //   --firewall-watch   动态放行看门狗：加规则 → 监视 → 退出时撤销
        //   --firewall-remove  只删除本程序的放行规则
        var cmdline = Environment.GetCommandLineArgs();
        var exe = Environment.ProcessPath ?? cmdline[0];

        if (cmdline.Contains(FirewallHelper.SetupArg, StringComparer.Ordinal))
            Environment.Exit(FirewallHelper.ApplyRules(exe));

        if (cmdline.Contains(FirewallHelper.RemoveArg, StringComparer.Ordinal))
            Environment.Exit(FirewallHelper.RemoveRules(exe));

        var watchIdx = Array.IndexOf(cmdline, FirewallHelper.WatchArg);
        if (watchIdx >= 0 && watchIdx + 2 < cmdline.Length)
        {
            var pid = int.TryParse(cmdline[watchIdx + 1], out var p) ? p : -1;
            Environment.Exit(FirewallHelper.RunWatchdog(exe, pid, cmdline[watchIdx + 2]));
        }

        MainWin = new MainWindow();
        MainWin.Activate();
    }
}
