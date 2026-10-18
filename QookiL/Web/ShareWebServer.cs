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

/// <summary>一次传输记录（下载 / 上传）。</summary>
public sealed record TransferLogEntry(DateTime Time, string Ip, string Kind, string Name, long Bytes);

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

    private ShareWebServer(int port) => Port = port;

    public static ShareWebServer Start(int port, SharedStore store, string uploadDir, string? hostName = null)
    {
        var server = new ShareWebServer(port);
        server.StartInternal(store, uploadDir, hostName ?? Environment.MachineName);
        return server;
    }

    private void StartInternal(SharedStore store, string uploadDir, string hostName)
    {
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
                    TransferLogged?.Invoke(new TransferLogEntry(
                        DateTime.Now, ClientIp(ctx), "下载",
                        name?.ToString() ?? "", ctx.Response.ContentLength ?? 0));
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

        app.MapGet("/dl/{id}", (string id, string? path, string? disposition, HttpContext ctx) =>
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

            // inline 用于浏览器内预览（图片/视频/音频），其余强制下载；
            // 两者都启用 Range，大文件可断点续传、视频可拖动进度条
            var inline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase);
            return inline
                ? Results.File(full, contentType, fileDownloadName: null, enableRangeProcessing: true)
                : Results.File(full, contentType, fileName, enableRangeProcessing: true);
        });

        app.MapPost("/upload", async (HttpContext ctx) =>
        {
            if (!ctx.Request.HasFormContentType)
                return Results.BadRequest(new { message = "请使用表单方式上传" });

            Directory.CreateDirectory(uploadDir);
            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);

            long total = 0;
            var saved = new List<string>();
            foreach (var file in form.Files)
            {
                if (file.Length == 0) continue;
                var target = UniquePath(Path.Combine(uploadDir, SanitizeFileName(file.FileName)));
                await using var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20, useAsync: true);
                await file.CopyToAsync(fs, ctx.RequestAborted);
                total += file.Length;
                saved.Add(Path.GetFileName(target));
                System.Threading.Interlocked.Increment(ref _uploadsCount);
                TransferLogged?.Invoke(new TransferLogEntry(DateTime.Now, ClientIp(ctx), "上传", Path.GetFileName(target), file.Length));
            }

            if (saved.Count == 0)
                return Results.BadRequest(new { message = "没有收到有效文件" });

            return Results.Json(new { ok = true, count = saved.Count, totalBytes = total, files = saved }, JsonOpts);
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
