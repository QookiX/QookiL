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
        // 防火墙辅助模式：主实例以管理员身份启动本程序（--firewall-setup），
        // 这里不建窗口，设置完防火墙规则后直接退出。
        var cmdline = Environment.GetCommandLineArgs();
        if (cmdline.Contains(FirewallHelper.SetupArg, StringComparer.Ordinal))
        {
            var exe = Environment.ProcessPath ?? cmdline[0];
            Environment.Exit(FirewallHelper.ApplyRules(exe));
        }

        MainWin = new MainWindow();
        MainWin.Activate();
    }
}
