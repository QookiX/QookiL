using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using QookiL.Core;
using QookiL.Web;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using VirtualKey = Windows.System.VirtualKey;
using Launcher = Windows.System.Launcher;

namespace QookiL;

public sealed partial class MainWindow : Window
{
    private readonly SharedStore _store = new();
    private readonly ObservableCollection<SharedItemView> _shares = new();
    private readonly ObservableCollection<TransferLogView> _logs = new();
    private readonly ObservableCollection<TransferProgressView> _active = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _infoTimer;

    private ShareWebServer? _server;
    private AppSettings _settings = new();
    private FrameworkElement? _root;
    private bool _fwPromptShown;
    private Process? _fwWatchdog;
    private string _fwFlag = "";
    private bool _uploadDirAutoFallback;

    public MainWindow()
    {
        InitializeComponent();
        DiagLog.Write("[app] 启动：" + Environment.ProcessPath);

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _infoTimer = _dispatcher.CreateTimer();
        _infoTimer.Interval = TimeSpan.FromSeconds(5);
        _infoTimer.Tick += (_, _) => { InfoBar.IsOpen = false; _infoTimer.Stop(); };

        Title = "QookiL";
        try { AppWindow.Resize(new SizeInt32(1200, 830)); } catch { /* 某些环境下不可用 */ }
        try
        {
            // Windows 设置式标题栏：内容延伸进标题栏，Mica 贯穿整个窗口
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch { /* 回退系统默认标题栏 */ }
        try { SystemBackdrop = new MicaBackdrop(); } catch { /* Win10 无 Mica 时回退纯色 */ }
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { }
        LoadLogo();

        _settings = SettingsStore.Load();
        if (_settings.Port is >= 1 and <= 65535) PortBox.Value = _settings.Port;
        UploadDirBox.Text = SavedUploadDirOr(_settings.UploadDir) ?? DefaultUploadDir();

        SharesList.ItemsSource = _shares;
        LogList.ItemsSource = _logs;
        ActiveTransfers.ItemsSource = _active;

        // 恢复上次共享的文件（先恢复再设置开关，避免 Persist 把空列表写回设置）
        foreach (var path in _settings.SharedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = SharedItem.FromPath(path);
            if (item is null) continue;
            _store.Add(item);
            _shares.Add(SharedItemView.From(item));
        }
        UpdateEmptyHint();
        AutoStartToggle.IsOn = _settings.AutoStart;
        StrictFirewallToggle.IsOn = _settings.StrictFirewall;

        if (Content is FrameworkElement root)
        {
            _root = root;
            root.Loaded += async (_, _) =>
            {
                // 启动时确认防火墙放行（未放行/读不到状态都会弹窗引导，绝不静默跳过）。
                // 动态放行模式下规则本就只在服务运行期间存在，启动时不检查、由启动服务时统一处理。
                if (!_settings.StrictFirewall) await EnsureFirewallAsync();
                if (AutoStartToggle.IsOn && _server is null) await StartServerAsync();
            };
        }
        Closed += (_, _) => OnShutdown();
    }

    /* ================= 服务器 ================= */

    private void OnStartStop(object sender, RoutedEventArgs e)
    {
        if (_server is not null)
        {
            _ = StopServerAsync();
            return;
        }
        _ = StartServerAsync();
    }

    private async Task StartServerAsync()
    {
        var port = (int)Math.Clamp(PortBox.Value, 1, 65535);
        PortBox.Value = port;

        try
        {
            // 接收文件夹：用户选择 → 系统「下载」→ 用户目录 → 程序目录，取第一个真正可写的
            // （受限环境下「下载」可能不可写，此时自动回退并在提示条说明实际位置）
            var (uploadDir, uploadFallback, uploadNote) = UploadDirs.Resolve(UploadDirBox.Text);
            if (!UploadDirs.EnsureWritable(uploadDir))
                throw new IOException($"接收文件夹不可用：{uploadDir}");
            UploadDirBox.Text = uploadDir;
            _uploadDirAutoFallback = uploadFallback;

            // 先确认防火墙放行，否则局域网设备根本连不上
            // 动态放行模式：提权看门狗加规则并监视，服务停止/程序退出时自动撤销
            var exe = Environment.ProcessPath;
            var fwOk = true;
            if (!string.IsNullOrEmpty(exe))
            {
                fwOk = _settings.StrictFirewall
                    ? await StartFirewallWatchdogAsync(exe)
                    : await EnsureFirewallAsync();
            }

            var server = ShareWebServer.Start(port, _store, uploadDir, NetworkHelper.HostName);
            server.TransferLogged += OnTransferLogged;
            server.TransferProgressChanged += OnTransferProgress;
            _server = server;

            _settings.Port = port;
            Persist();

            StartStopText.Text = "停止服务";
            StartStopIcon.Glyph = "\uE71A";
            StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.SeaGreen);
            StatusText.Text = $"服务运行中 · 端口 {port}";
            StatusSub.Text = "同一局域网的设备扫码或输入下方地址即可互传";
            PortBox.IsEnabled = false;
            RefreshUrls();

            var note = uploadNote;
            if (fwOk)
                ShowInfo(InfoBarSeverity.Success,
                    $"服务已启动。手机等设备连接同一 Wi‑Fi，扫码或输入地址即可访问。{note}");
            else if (_settings.StrictFirewall)
                ShowInfo(InfoBarSeverity.Warning,
                    $"服务已启动，但动态放行未完成（管理员确认被取消或规则添加失败）：其他设备可能无法连接。停止后重新启动服务可重试。{note}");
            else
                ShowInfo(InfoBarSeverity.Warning,
                    $"服务已启动，但未通过防火墙放行：其他设备可能无法连接。点右上「⚙ 连接诊断」可查看原因和处理办法。{note}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException is { } inner ? $"（{inner.Message}）" : "";
            ShowInfo(InfoBarSeverity.Error, "启动失败：" + ex.Message + detail);
        }
    }

    /// <summary>
    /// 确认防火墙已放行：已放行返回 true；否则弹窗引导授权（UAC 一次）。
    /// 读取失败（Unknown）也会弹窗如实说明情况——绝不静默跳过。
    /// </summary>
    private async Task<bool> EnsureFirewallAsync()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return true;

        var info = await Task.Run(() => FirewallHelper.Check(exe));
        DiagLog.Write($"[fw] 检查：{info.Verdict}；{info.Details}");
        if (!info.NeedsSetup) return true;

        // 本次会话提示过就不再打扰（停止服务后重置，重新启动时如仍未放行会再问）
        if (_fwPromptShown) return false;
        _fwPromptShown = true;

        var lead = info.Verdict == FirewallVerdict.Unknown
            ? $"无法确认 Windows 防火墙状态：{info.Details}\n为确保局域网设备能访问，建议现在设置一次。"
            : $"Windows 防火墙尚未放行 QookiL：{info.Details}\n其他设备要连上，需要先添加放行规则。";

        var dialog = new ContentDialog
        {
            Title = "允许局域网访问",
            Content = lead + "\n\n点「允许」后系统会弹出管理员确认框（UAC），选择「是」即可，只需设置一次。",
            PrimaryButtonText = "允许（推荐）",
            CloseButtonText = "暂不",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root?.XamlRoot,
        };
        if (dialog.XamlRoot is null || await dialog.ShowAsync() != ContentDialogResult.Primary)
            return false;

        if (!await FirewallHelper.RunElevatedSetupAsync(exe)) return false;

        var after = await Task.Run(() => FirewallHelper.Check(exe));
        DiagLog.Write($"[fw] 设置后复查：{after.Verdict}；{after.Details}");
        return !after.NeedsSetup;
    }

    /// <summary>
    /// 动态放行：以管理员身份启动看门狗（加规则 → 监视 → 停止/退出时撤销规则），
    /// 使放行只存在于服务运行期间。每次启动服务需一次 UAC，这是严格模式的代价。
    /// </summary>
    private async Task<bool> StartFirewallWatchdogAsync(string exe)
    {
        try
        {
            _fwFlag = Path.Combine(Path.GetTempPath(), $"qookil-fw-{Environment.ProcessId}.flag");
            await File.WriteAllTextAsync(_fwFlag, "run");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"{FirewallHelper.WatchArg} {Environment.ProcessId} \"{_fwFlag}\"",
                Verb = "runas",
                UseShellExecute = true,
            };
            _fwWatchdog = Process.Start(psi);
            DiagLog.Write("[fw] 看门狗已启动，等待规则生效");

            // 等提权进程把规则加好（轮询复核，最多 15 秒）
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                var info = await Task.Run(() => FirewallHelper.Check(exe));
                if (!info.NeedsSetup) return true;
                if (_fwWatchdog is { HasExited: true }) break;
            }
            return false;
        }
        catch (System.ComponentModel.Win32Exception)   // UAC 被取消
        {
            return false;
        }
        catch (Exception ex)
        {
            DiagLog.Write("[fw] 看门狗启动失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>连接诊断：防火墙状态 / 本程序规则 / 本机地址 / 排查清单。</summary>
    private async void OnDiagnostics(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        var fw = string.IsNullOrEmpty(exe)
            ? new FirewallInfo(FirewallVerdict.Unknown, "进程路径未知")
            : await Task.Run(() => FirewallHelper.Check(exe));
        DiagLog.Write($"[diag] {fw.Verdict}；{fw.Details}");

        var sb = new System.Text.StringBuilder();
        sb.Append("放行模式：").AppendLine(_settings.StrictFirewall
            ? "动态放行（规则仅在服务运行期间存在，停止或退出程序时自动撤销）"
            : "一次性持久规则（Windows 标准做法；程序退出后端口无监听，外部无法访问）");
        sb.Append("临时目录：").AppendLine(WritablePaths.TempDir);
        var receiveDir = UploadDirBox.Text.Trim();
        sb.Append("接收文件夹：").Append(receiveDir)
          .AppendLine(UploadDirs.EnsureWritable(receiveDir) ? "（可写）" : "（不可写！请改选接收文件夹或检查权限）");
        sb.Append("防火墙：").AppendLine(fw.Details);
        sb.AppendLine();
        sb.AppendLine("本机地址（扫码优先使用排在前面的物理网卡）：");
        var addrs = NetworkHelper.GetLanIPv4s();
        if (addrs.Count == 0) sb.AppendLine("  未检测到局域网 IPv4 地址");
        foreach (var a in addrs.Take(8))
            sb.Append("  ").Append(a.Ip).Append("  ").Append(a.Adapter)
              .Append(a.IsVirtual ? "（虚拟网卡，其他设备连不上此地址）" : "").AppendLine();
        sb.AppendLine();
        sb.AppendLine("其他设备还是连不上时，依次检查：");
        sb.AppendLine("  1. 设备和电脑连的是不是同一个 Wi‑Fi（地址前三段需一致，如 192.168.1.x）");
        sb.AppendLine("  2. 路由器是否开了「AP 隔离」，或设备连的是「访客网络」");
        sb.AppendLine("  3. 电脑上的 360 / 火绒等安全软件的防火墙，需要单独放行");
        sb.AppendLine("  4. 电脑是否连了 VPN（流量被引走）");
        sb.AppendLine("  5. 换一个地址试试（虚拟网卡的地址连不上）");
        sb.AppendLine();
        sb.Append("诊断日志：").AppendLine(DiagLog.LogFilePath);

        var dialog = new ContentDialog
        {
            Title = "连接诊断",
            Content = new ScrollViewer
            {
                MaxHeight = 430,
                Content = new TextBlock
                {
                    Text = sb.ToString(),
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                },
            },
            CloseButtonText = "关闭",
            XamlRoot = _root?.XamlRoot,
        };
        try
        {
            if (dialog.XamlRoot is not null) await dialog.ShowAsync();
        }
        catch { /* 已有弹窗打开等场景，忽略 */ }
    }

    private async Task StopServerAsync()
    {
        var server = _server;
        _server = null;
        try
        {
            if (server is not null) await server.DisposeAsync();
        }
        catch { /* 忽略停止异常 */ }

        // 动态放行：删标志让看门狗撤销规则，并等它退出（避免紧接着重启时规则竞争）
        await StopFirewallWatchdogAsync();

        StartStopText.Text = "启动服务";
        StartStopIcon.Glyph = "\uE768";
        StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        StatusText.Text = "服务未启动";
        StatusSub.Text = "启动后，同一局域网的设备用浏览器即可访问";
        PortBox.IsEnabled = true;
        _fwPromptShown = false; // 重新点「启动服务」时如仍未放行可再次提示
        QrImage.Source = null;
        QrHint.Text = "启动服务后显示二维码";
        UrlPanel.Children.Clear();
    }

    /// <summary>通知看门狗撤销放行规则并等它退出（最多 4 秒）。</summary>
    private async Task StopFirewallWatchdogAsync()
    {
        try { if (_fwFlag.Length > 0) File.Delete(_fwFlag); } catch { }
        if (_fwWatchdog is { } wd && !wd.HasExited)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await wd.WaitForExitAsync(cts.Token);
            }
            catch { /* 超时也继续：看门狗仍会在主进程退出后自行撤销 */ }
        }
        _fwWatchdog = null;
        _fwFlag = "";
    }

    private void OnShutdown()
    {
        try { Persist(); } catch { }
        var server = _server;
        _server = null;
        try { server?.DisposeAsync().AsTask().Wait(2000); } catch { }

        // 动态放行：通知看门狗撤销规则（主进程随即退出，看门狗也会自行检测到）
        try { if (_fwFlag.Length > 0) File.Delete(_fwFlag); } catch { }
        try { _fwWatchdog?.WaitForExit(1500); } catch { }
    }

    /* ================= 地址与二维码 ================= */

    private void OnRefreshUrls(object sender, RoutedEventArgs e) => RefreshUrls();

    private void RefreshUrls()
    {
        UrlPanel.Children.Clear();
        var port = _server?.Port ?? (int)Math.Clamp(PortBox.Value, 1, 65535);
        var addresses = NetworkHelper.GetLanIPv4s();

        if (addresses.Count == 0)
        {
            QrHint.Text = "未检测到局域网 IPv4 地址，请检查 Wi‑Fi / 网线连接";
            if (_server is null) QrImage.Source = null;
            return;
        }

        var first = true;
        foreach (var addr in addresses.Take(5))
        {
            var url = $"http://{addr.Ip}:{port}";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            var text = new TextBlock
            {
                Text = url,
                VerticalAlignment = VerticalAlignment.Center,
                IsTextSelectionEnabled = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
            };
            ToolTipService.SetToolTip(text,
                $"{addr.Adapter}（{addr.Description}）{(addr.IsVirtual ? " · 虚拟网卡，其他设备连不上此地址" : "")}");

            row.Children.Add(text);
            row.Children.Add(MakeTinyButton("\uE8C8", "复制链接", () => CopyToClipboard(url)));
            row.Children.Add(MakeTinyButton("\uE8A7", "在浏览器打开", async () =>
            {
                try { await Launcher.LaunchUriAsync(new Uri(url)); } catch { }
            }));
            UrlPanel.Children.Add(row);

            if (first)
            {
                SetQrCode(url);
                first = false;
            }
        }
    }

    private static Button MakeTinyButton(string glyph, string tip, Action onClick)
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
        };
        ToolTipService.SetToolTip(btn, tip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private async void SetQrCode(string url)
    {
        try
        {
            var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(10);

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            QrImage.Source = bitmap;
            QrHint.Text = "扫码即可访问 · 或使用下方地址";
        }
        catch (Exception ex)
        {
            QrHint.Text = "二维码生成失败：" + ex.Message;
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            ShowInfo(InfoBarSeverity.Informational, "已复制：" + text);
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "复制失败：" + ex.Message);
        }
    }

    /* ================= 共享列表 ================= */

    private async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;
        AddPaths(files.Select(f => f.Path));
    }

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        AddPaths(new[] { folder.Path });
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "添加到共享列表";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = false;
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            AddPaths(items.Select(i => i.Path));
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;
        var missing = 0;
        foreach (var path in paths)
        {
            var item = SharedItem.FromPath(path);
            if (item is null) { missing++; continue; }
            if (!_store.Add(item)) { skipped++; continue; }
            _shares.Insert(0, SharedItemView.From(item));
            added++;
        }
        if (added > 0)
        {
            UpdateEmptyHint();
            Persist();
        }
        if (missing > 0)
            ShowInfo(InfoBarSeverity.Warning, $"{missing} 个路径不存在或无法访问，已跳过");
        else if (skipped > 0)
            ShowInfo(InfoBarSeverity.Informational, $"{skipped} 个项目已在共享列表中");
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            _store.Remove(id);
            var view = _shares.FirstOrDefault(v => v.Item.Id == id);
            if (view is not null) _shares.Remove(view);
            UpdateEmptyHint();
            Persist();
        }
    }

    private void SharesList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete) return;
        var selected = SharesList.SelectedItems.Cast<SharedItemView>().ToList();
        if (selected.Count == 0) return;
        foreach (var view in selected)
        {
            _store.Remove(view.Item.Id);
            _shares.Remove(view);
        }
        UpdateEmptyHint();
        Persist();
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (_shares.Count == 0) return;
        _store.Clear();
        _shares.Clear();
        UpdateEmptyHint();
        Persist();
    }

    private void UpdateEmptyHint() =>
        EmptyHint.Visibility = _shares.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /* ================= 接收文件夹 / 记录 / 设置 ================= */

    private async void OnPickUploadDir(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        UploadDirBox.Text = folder.Path;
        _uploadDirAutoFallback = false; // 用户手动选择，覆盖自动回退
        Persist();
    }

    private void OnTransferLogged(TransferLogEntry entry)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _logs.Insert(0, TransferLogView.From(entry));
            while (_logs.Count > 300)
                _logs.RemoveAt(_logs.Count - 1);
            LogEmptyHint.Visibility = _logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 手机上传的文件同时进入共享列表（带「手机上传」标记，双击可定位文件）
            if (entry.Kind == "上传" && File.Exists(entry.FullPath) &&
                SharedItem.FromPath(entry.FullPath) is { } item && _store.Add(item))
            {
                _shares.Insert(0, SharedItemView.From(item, received: true));
                UpdateEmptyHint();
                Persist();
            }
        });
    }

    /// <summary>进行中传输的进度（服务端按约 200ms 节流上报）。</summary>
    private void OnTransferProgress(TransferProgress p)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var index = -1;
            for (var i = 0; i < _active.Count; i++)
            {
                if (_active[i].Id == p.Id) { index = i; break; }
            }

            if (p.Finished)
            {
                if (index >= 0) _active.RemoveAt(index);
            }
            else if (index >= 0)
            {
                _active[index] = TransferProgressView.From(p);
            }
            else
            {
                _active.Insert(0, TransferProgressView.From(p));
                while (_active.Count > 20)
                    _active.RemoveAt(_active.Count - 1);
            }

            ActiveTransfers.Visibility = _active.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    /// <summary>传输记录单击：打开所在文件夹并选中该文件。</summary>
    private void OnLogItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TransferLogView v && !string.IsNullOrEmpty(v.FullPath))
            OpenInExplorer(v.FullPath);
    }

    /// <summary>共享列表双击：在资源管理器中定位。</summary>
    private void OnSharesDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SharedItemView v)
            OpenInExplorer(v.Item.FullPath);
    }

    /// <summary>用资源管理器打开：文件则打开所在文件夹并高亮，文件夹则直接打开。</summary>
    private void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                ShowInfo(InfoBarSeverity.Warning, "文件已不在磁盘上：" + path);
            }
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "打开文件夹失败：" + ex.Message);
        }
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        _logs.Clear();
        LogEmptyHint.Visibility = Visibility.Visible;
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e) => Persist();

    private void OnStrictFirewallToggled(object sender, RoutedEventArgs e) => Persist();

    private void Persist()
    {
        _settings.Port = (int)Math.Clamp(PortBox.Value, 1, 65535);
        // 等于默认值（系统「下载」文件夹）时不保存，下次启动重新解析，
        // 这样用户以后再移动「下载」文件夹，程序会自动跟随；
        // 自动回退出来的临时目录同样不保存，避免环境恢复后仍停在回退位置
        var upload = UploadDirBox.Text.Trim();
        _settings.UploadDir = (_uploadDirAutoFallback ||
            string.Equals(upload, DefaultUploadDir(), StringComparison.OrdinalIgnoreCase))
            ? ""
            : upload;
        _settings.AutoStart = AutoStartToggle.IsOn;
        _settings.StrictFirewall = StrictFirewallToggle.IsOn;
        _settings.SharedPaths = _shares.Select(v => v.Item.FullPath).ToList();
        SettingsStore.Save(_settings);
    }

    /* ================= 其他 ================= */

    /// <summary>默认接收文件夹：系统「下载」文件夹本身（跟随重定向）。</summary>
    private static string DefaultUploadDir()
    {
        var dl = KnownFolders.TryGetDownloads();
        if (!string.IsNullOrEmpty(dl) && Directory.Exists(dl)) return dl;

        // 已知文件夹 API 失败时的兜底：拼 UserProfile 路径
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return Path.GetTempPath();
        var guess = Path.Combine(profile, "Downloads");
        return Directory.Exists(guess) ? guess : profile;
    }

    /// <summary>恢复上次保存的接收文件夹：手动选过、仍然存在、且不是旧版默认值才沿用。</summary>
    private static string? SavedUploadDirOr(string? saved)
    {
        var dir = saved?.Trim();
        if (string.IsNullOrWhiteSpace(dir)) return null;
        // 旧版本把「下载\QookiL 接收」这类默认值存进了设置，视为未设置
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name is "QookiL 接收" or "LanShare 接收") return null;
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>目录可用（存在或可创建）。</summary>
    private static bool EnsureDir(string path) => UploadDirs.EnsureWritable(path);

    private void LoadLogo()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "qookil.png");
            if (File.Exists(path))
                TitleLogo.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 32 };
        }
        catch { /* 加载失败仅影响标题栏图标显示 */ }
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        _infoTimer.Stop();
        InfoBar.Severity = severity;
        InfoBar.Message = message;
        InfoBar.IsOpen = true;
        _infoTimer.Start();
    }
}
