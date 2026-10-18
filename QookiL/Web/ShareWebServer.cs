using System.Net;
using System.Text.Json;
using QookiL.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QookiL.Web;

/// <summary>一次传输记录（下载 / 上传）。FullPath 供界面「打开所在文件夹」使用。</summary>
public sealed record TransferLogEntry(DateTime Time, string Ip, string Kind, string Name, long Bytes, string FullPath = "");

/// <summary>传输进度（进行中/已完成）。Id 用于界面按同一传输更新进度行。</summary>
public sealed record TransferProgress(Guid Id, string Kind, string Name, string Ip, long Total, long Done, bool Finished)
{
    public double Percent => Total > 0 ? Math.Clamp(Done * 100.0 / Total, 0, 100) : (Finished ? 100 : 0);
}

/// <summary>
/// 内嵌 Kestrel 网页服务器：局域网设备用浏览器打开 http://电脑IP:端口 即可
/// 浏览/下载共享文件，也可以上传文件到接收文件夹。
/// </summary>
public sealed class ShareWebServer : IAsyncDisposable
{
    /// <summary>“接收的文件（上传）”虚拟根目录的 id。</summary>
    public const string UploadsRootId = "uploads";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private WebApplication? _app;
    private int _uploadsCount;

    public int Port { get; }
    public bool IsRunning => _app is not null;
    public event Action<TransferLogEntry>? TransferLogged;
    public event Action<TransferProgress>? TransferProgressChanged;

    private ShareWebServer(int port) => Port = port;

    public static ShareWebServer Start(int port, SharedStore store, string uploadDir, string? hostName = null)
    {
        var server = new ShareWebServer(port);
        server.StartInternal(store, uploadDir, hostName ?? Environment.MachineName);
        return server;
    }

    private void StartInternal(SharedStore store, string uploadDir, string hostName)
    {
        // 上传时 ASP.NET Core 会把请求体缓冲到临时文件；先确保 TMP/TEMP 指向真正可写的目录
        WritablePaths.RedirectProcessTemp();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Environment.ContentRootPath = AppContext.BaseDirectory;
        builder.WebHost.UseKestrel(options =>
        {
            options.ListenAnyIP(Port);          // 同时监听 IPv4 / IPv6，局域网可直接访问
            options.Limits.MaxRequestBodySize = null; // 不限制上传大小
            options.AddServerHeader = false;
        });
        builder.Services.Configure<FormOptions>(o =>
        {
            o.MultipartBodyLengthLimit = 8L * 1024 * 1024 * 1024; // 8GB
            o.MultipartHeadersLengthLimit = 32 * 1024;
            o.MemoryBufferThreshold = 1024 * 1024;                // 1MB 以内留在内存，少用临时文件
        });

        var app = builder.Build();

        // 下载完成后写入传输记录
        app.Use(async (ctx, next) =>
        {
            await next();
            try
            {
                if (ctx.Request.Path.StartsWithSegments("/dl") &&
                    ctx.Response.StatusCode is 200 or 206 &&
                    ctx.Items.TryGetValue("LogName", out var name))
                {
                    var fullPath = ctx.Items.TryGetValue("LogPath", out var p) ? p?.ToString() ?? "" : "";
                    TransferLogged?.Invoke(new TransferLogEntry(
                        DateTime.Now, ClientIp(ctx), "下载",
                        name?.ToString() ?? "", ctx.Response.ContentLength ?? 0, fullPath));
                }
            }
            catch
            {
                // 记录失败不影响响应
            }
        });

        app.MapGet("/", () => Results.Text(WebUi.IndexHtml, "text/html; charset=utf-8"));

        app.MapGet("/api/info", () =>
            Results.Json(new
            {
                host = hostName,
                port = Port,
                app = "QookiL/1.0",
                rev = store.Revision,
                uploads = _uploadsCount,
            }, JsonOpts));

        app.MapGet("/api/roots", () =>
        {
            var roots = store.Snapshot().Select(i => new
            {
                id = i.Id.ToString(),
                name = i.Name,
                kind = i.IsFolder ? "dir" : "file",
                size = i.SizeBytes,
                mtime = i.IsFolder ? null : i.LastWrite.ToString("yyyy-MM-dd HH:mm"),
            }).ToList();
            return Results.Json(new { host = hostName, uploadsId = UploadsRootId, roots }, JsonOpts);
        });

        app.MapGet("/api/items/{id}", (string id, string? path) =>
        {
            if (!TryResolveRoot(store, uploadDir, id, out var root, out var rootName))
                return Results.NotFound(new { message = "共享不存在，可能已被移除" });

            var full = SafePath.Resolve(root, path);
            if (full is null)
                return Results.BadRequest(new { message = "非法路径" });
            if (!Directory.Exists(full))
                return Results.NotFound(new { message = "文件夹不存在" });

            try
            {
                var entries = new List<object>();
                var dir = new DirectoryInfo(full);
                foreach (var d in dir.EnumerateDirectories())
                    entries.Add(new { name = d.Name, kind = "dir", size = (long?)null, mtime = d.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
                foreach (var f in dir.EnumerateFiles())
                    entries.Add(new { name = f.Name, kind = "file", size = f.Length, mtime = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
                return Results.Json(new { id, name = rootName, path = path ?? "", entries }, JsonOpts);
            }
            catch (Exception ex)
            {
                return Results.Json(new { message = "读取文件夹失败：" + ex.Message }, statusCode: 500);
            }
        });

        app.MapGet("/dl/{id}", async (string id, string? path, string? disposition, HttpContext ctx) =>
        {
            if (!TryResolveRoot(store, uploadDir, id, out var root, out _))
                return Results.NotFound(new { message = "共享不存在" });

            var full = SafePath.Resolve(root, path);
            if (full is null || !File.Exists(full))
                return Results.NotFound(new { message = "文件不存在" });

            if (!new FileExtensionContentTypeProvider().TryGetContentType(full, out var contentType))
                contentType = "application/octet-stream";

            var fileName = Path.GetFileName(full);
            ctx.Items["LogName"] = fileName;
            ctx.Items["LogPath"] = full;

            // inline 用于浏览器内预览（图片/视频/音频），其余强制下载；
            // 两者都支持 Range（大文件断点续传、视频拖进度条）
            var inline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase);
            await StreamFileAsync(ctx, full, fileName, contentType, inline);
            return Results.Empty;
        });

        app.MapPost("/upload", async (HttpContext ctx) =>
        {
            if (!ctx.Request.HasFormContentType)
                return Results.BadRequest(new { message = "请使用表单方式上传" });

            // 任何异常都转成可读的 JSON 错误（而不是让 Kestrel 直接断开连接，
            // 那样浏览器只会报「网络错误」，用户和排查都看不到原因）
            try
            {
                return await HandleUploadAsync(ctx, uploadDir);
            }
            catch (OperationCanceledException)
            {
                DiagLog.Write("[upload] 客户端取消上传");
                return Results.StatusCode(499);
            }
            catch (Exception ex)
            {
                DiagLog.Write("[upload] 上传失败：" + ex);
                return Results.Json(new { message = "上传失败：" + ex.Message }, statusCode: 500);
            }
        });

        _app = app;
        app.Start(); // 端口被占用等错误在这里抛出
    }

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null) return;
        _app = null;
        try { await app.StopAsync(TimeSpan.FromSeconds(3)); }
        catch { /* 忽略停止时的异常 */ }
        await app.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var app = _app;
        _app = null;
        if (app is null) return;
        try { await app.StopAsync(TimeSpan.FromSeconds(3)); }
        catch { /* 忽略停止时的异常 */ }
        await app.DisposeAsync();
    }

    private static bool TryResolveRoot(SharedStore store, string uploadDir, string id, out string root, out string name)
    {
        if (string.Equals(id, UploadsRootId, StringComparison.OrdinalIgnoreCase))
        {
            root = uploadDir;
            name = "接收的文件（上传）";
            return true;
        }
        if (Guid.TryParse(id, out var gid) && store.Find(gid) is { } item)
        {
            root = item.FullPath;
            name = item.Name;
            return true;
        }
        root = "";
        name = "";
        return false;
    }

    private static string ClientIp(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return "?";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "file" : name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private void ReportProgress(TransferProgress p)
    {
        try { TransferProgressChanged?.Invoke(p); }
        catch { /* 进度回调失败不影响传输 */ }
    }

    /// <summary>处理上传：读表单、逐个落盘、上报进度。异常由调用方转成 JSON 错误。</summary>
    private async Task<IResult> HandleUploadAsync(HttpContext ctx, string uploadDir)
    {
        Directory.CreateDirectory(uploadDir);
        DiagLog.Write($"[upload] 收到上传：{ctx.Request.ContentLength?.ToString() ?? "?"} 字节，来自 {ClientIp(ctx)}");

        IFormCollection form;
        try
        {
            form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"[upload] 读取表单失败：{ex.GetType().Name} {ex.Message}");
            throw;
        }

        long total = 0;
        var saved = new List<string>();
        foreach (var file in form.Files)
        {
            if (file.Length == 0) continue;
            var target = UniquePath(Path.Combine(uploadDir, SanitizeFileName(file.FileName)));
            var name = Path.GetFileName(target);
            var ip = ClientIp(ctx);
            var transferId = Guid.NewGuid();
            var expected = file.Length;
            long done = 0;
            var lastReport = DateTime.UtcNow;
            var lastDone = 0L;

            ReportProgress(new TransferProgress(transferId, "上传", name, ip, expected, 0, false));
            try
            {
                await using var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
                await using var src = file.OpenReadStream();
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await src.ReadAsync(buffer.AsMemory(), ctx.RequestAborted);
                    if (read <= 0) break;
                    await fs.WriteAsync(buffer.AsMemory(0, read), ctx.RequestAborted);
                    done += read;
                    if (DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(200) ||
                        done == expected || done - lastDone >= 4 * 1024 * 1024)
                    {
                        lastReport = DateTime.UtcNow;
                        lastDone = done;
                        ReportProgress(new TransferProgress(transferId, "上传", name, ip, expected, done, false));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiagLog.Write($"[upload] 保存文件失败（{name}）：{ex.GetType().Name} {ex.Message}");
                throw;
            }
            finally
            {
                ReportProgress(new TransferProgress(transferId, "上传", name, ip, expected, Math.Min(done, expected), true));
            }

            total += file.Length;
            saved.Add(name);
            System.Threading.Interlocked.Increment(ref _uploadsCount);
            TransferLogged?.Invoke(new TransferLogEntry(DateTime.Now, ip, "上传", name, file.Length, target));
        }

        if (saved.Count == 0)
            return Results.BadRequest(new { message = "没有收到有效文件" });

        return Results.Json(new { ok = true, count = saved.Count, totalBytes = total, files = saved }, JsonOpts);
    }

    /// <summary>
    /// 手动流式发送文件：支持 Range（206 / 416）、中文文件名编码，并按块上报进度。
    /// （不用 Results.File：Kestrel 的 SendFile 会绕过响应流，统计不到已发送字节。）
    /// </summary>
    private async Task StreamFileAsync(HttpContext ctx, string full, string fileName, string contentType, bool inline)
    {
        var info = new FileInfo(full);
        var total = info.Length;

        long start = 0, end = total - 1;
        var isRange = false;
        var rangeHeader = ctx.Request.Headers.Range.ToString();
        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var spec = rangeHeader[6..].Split(',')[0].Trim(); // 多范围只处理第一段
            var dash = spec.IndexOf('-');
            if (dash >= 0)
            {
                var startText = spec[..dash].Trim();
                var endText = spec[(dash + 1)..].Trim();
                if (startText.Length == 0 && long.TryParse(endText, out var suffix) && suffix > 0)
                {
                    // 后缀范围：最后 N 字节
                    start = Math.Max(0, total - suffix);
                    end = total - 1;
                }
                else if (long.TryParse(startText, out var s2))
                {
                    start = s2;
                    end = long.TryParse(endText, out var e2) && e2 < total ? e2 : total - 1;
                }
                if (start >= 0 && start < total && start <= end) isRange = true;
                else { start = 0; end = total - 1; isRange = false; }
            }
        }

        if (isRange && start >= total)
        {
            ctx.Response.StatusCode = 416;
            ctx.Response.Headers["Content-Range"] = $"bytes */{total}";
            return;
        }

        var length = isRange ? end - start + 1 : total;
        ctx.Response.ContentType = contentType;
        ctx.Response.Headers["Accept-Ranges"] = "bytes";
        if (!inline)
        {
            var cd = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
            cd.SetHttpFileName(fileName);
            ctx.Response.Headers["Content-Disposition"] = cd.ToString();
        }
        if (isRange)
        {
            ctx.Response.StatusCode = 206;
            ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{total}";
        }
        ctx.Response.ContentLength = length;

        var ip = ClientIp(ctx);
        var transferId = Guid.NewGuid();
        ReportProgress(new TransferProgress(transferId, "下载", fileName, ip, length, 0, false));

        var buffer = new byte[81920];
        long sent = 0;
        var lastReport = DateTime.UtcNow;
        var lastSent = 0L;
        try
        {
            await using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            if (start > 0) fs.Seek(start, SeekOrigin.Begin);

            while (sent < length)
            {
                var want = (int)Math.Min(buffer.Length, length - sent);
                var read = await fs.ReadAsync(buffer.AsMemory(0, want), ctx.RequestAborted);
                if (read <= 0) break;
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, read), ctx.RequestAborted);
                sent += read;
                if (DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(200) ||
                    sent == length || sent - lastSent >= 4 * 1024 * 1024)
                {
                    lastReport = DateTime.UtcNow;
                    lastSent = sent;
                    ReportProgress(new TransferProgress(transferId, "下载", fileName, ip, length, sent, false));
                }
            }
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // 客户端取消（断网/关闭）：忽略
        }
        catch (IOException)
        {
            // 连接中断：忽略
        }
        finally
        {
            ReportProgress(new TransferProgress(transferId, "下载", fileName, ip, length, Math.Min(sent, length), true));
        }
    }
}

/// <summary>路径安全：把相对路径解析到共享根目录内部，拦截 ../ 越界访问。</summary>
internal static class SafePath
{
    public static string? Resolve(string root, string? relative)
    {
        try
        {
            var rootFull = Path.GetFullPath(root);
            var full = Path.GetFullPath(Path.Combine(rootFull, relative ?? ""));
            var baseDir = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;
            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                return null;
            return full;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>内嵌网页（编译时打进程序集）。</summary>
internal static class WebUi
{
    public static readonly string IndexHtml = Load();

    private static string Load()
    {
        var asm = typeof(WebUi).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n =>
            n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("缺少内嵌网页 Web/index.html");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
