using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QookiL.Core;
using QookiL.Web;

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5189;
var hold = args.Length > 1 && args[1] == "hold";

var baseDir = Path.Combine(Path.GetTempPath(), "qookil-smoke");
var shareDir = Path.Combine(baseDir, "share");
var uploadDir = Path.Combine(baseDir, "upload");
if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
Directory.CreateDirectory(shareDir);
Directory.CreateDirectory(uploadDir);

// ---- 造一组贴近真实使用的共享内容（含真实图片，供缩略图与设计检查） ----
var testFile = Path.Combine(shareDir, "测试 文件.bin");
var testBytes = new byte[1024 * 257]; // 257KB，超过 ASP.NET Core 默认的 128KB multipart 限制
new Random(42).NextBytes(testBytes);
File.WriteAllBytes(testFile, testBytes);

var seedImages = new[] { "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" };
var photoNames = new[] { "照片 城市黄昏.png", "照片 桌面.png", "证件照 底稿.png" };
for (var i = 0; i < seedImages.Length; i++)
    File.WriteAllBytes(Path.Combine(shareDir, photoNames[i]), Convert.FromBase64String(seedImages[i]));

File.WriteAllText(Path.Combine(shareDir, "项目笔记.md"), "# 项目笔记\n\n- 顺手把这份文档共享给手机看看\n- 数据都不出局域网");
File.WriteAllText(Path.Combine(shareDir, "密码本.txt"), "这一页只是演示文件名长度和排版的样子");
File.WriteAllBytes(Path.Combine(shareDir, "安装包.zip"), new byte[48 * 1024]);
File.WriteAllBytes(Path.Combine(shareDir, "演示录像.mp4"), new byte[128 * 1024]);
Directory.CreateDirectory(Path.Combine(shareDir, "子文件夹"));
File.WriteAllText(Path.Combine(shareDir, "子文件夹", "hello.txt"), "你好，世界");
File.WriteAllBytes(Path.Combine(shareDir, "子文件夹", "old-photo.png"), Convert.FromBase64String(seedImages[1]));

var store = new SharedStore();
var root = SharedItem.FromPath(shareDir) ?? throw new InvalidOperationException("无法共享测试目录");
store.Add(root);
// 模拟用户把几张照片直接拖进共享列表（首屏即有真实缩略图）
foreach (var name in photoNames)
{
    var img = SharedItem.FromPath(Path.Combine(shareDir, name));
    if (img is not null) store.Add(img);
}

var logEntries = new System.Collections.Concurrent.ConcurrentQueue<TransferLogEntry>();

await using var server = ShareWebServer.Start(port, store, uploadDir, "SMOKE-TEST");
server.TransferLogged += e =>
{
    logEntries.Enqueue(e);
    Console.WriteLine($"[log] {e.Time:HH:mm:ss} {e.Kind} {e.Name} {e.Bytes}B from {e.Ip} path={e.FullPath}");
};

if (hold)
{
    Console.WriteLine($"HOLD mode: http://127.0.0.1:{port}/  (Ctrl+C 退出)");
    await Task.Delay(Timeout.Infinite);
    return 0;
}

using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
var failures = new List<string>();

async Task Check(string name, Func<Task<bool>> test)
{
    try
    {
        var ok = await test();
        Console.WriteLine(ok ? $"PASS  {name}" : $"FAIL  {name}");
        if (!ok) failures.Add(name);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL  {name} -> {ex.GetType().Name}: {ex.Message}");
        failures.Add(name);
    }
}

var fileUrl = $"/dl/{root.Id}?path={Uri.EscapeDataString("测试 文件.bin")}";

await Check("首页 HTML", async () =>
{
    var html = await http.GetStringAsync("/");
    return html.Contains("QookiL") && html.Contains("/api/roots") && html.Contains("/upload");
});

await Check("获取共享根列表", async () =>
{
    var json = await http.GetFromJsonAsync<JsonElement>("/api/roots");
    var roots = json.GetProperty("roots");
    // 1 个文件夹 + 3 张单独共享的照片
    return roots.GetArrayLength() == 4
        && roots.EnumerateArray().Count(r => r.GetProperty("kind").GetString() == "dir") == 1
        && roots.EnumerateArray().Count(r => r.GetProperty("kind").GetString() == "file") == 3
        && json.GetProperty("uploadsId").GetString() == "uploads";
});

await Check("列出子文件夹内容", async () =>
{
    var json = await http.GetFromJsonAsync<JsonElement>(
        $"/api/items/{root.Id}?path={Uri.EscapeDataString("子文件夹")}");
    var entries = json.GetProperty("entries");
    return entries.GetArrayLength() == 2
        && entries.EnumerateArray().Any(e => e.GetProperty("name").GetString() == "hello.txt")
        && entries.EnumerateArray().Any(e => e.GetProperty("name").GetString() == "old-photo.png");
});

await Check("图片可内联获取（缩略图）", async () =>
{
    var res = await http.GetAsync($"/dl/{root.Id}?path={Uri.EscapeDataString(photoNames[0])}&disposition=inline");
    var bytes = await res.Content.ReadAsByteArrayAsync();
    return res.IsSuccessStatusCode && bytes.Length > 0
        && res.Content.Headers.ContentType?.MediaType == "image/png";
});

await Check("上传根目录可列出", async () =>
{
    var res = await http.GetAsync("/api/items/uploads");
    return res.IsSuccessStatusCode;
});

await Check("下载整个文件（字节一致）", async () =>
{
    var bytes = await http.GetByteArrayAsync(fileUrl);
    return bytes.Length == testBytes.Length && bytes.SequenceEqual(testBytes);
});

await Check("下载响应头（类型/附件/文件名）", async () =>
{
    var res = await http.GetAsync(fileUrl);
    var cd = res.Content.Headers.ContentDisposition;
    return res.Content.Headers.ContentType?.MediaType == "application/octet-stream"
        && cd?.DispositionType == "attachment"
        && cd.FileNameStar == "测试 文件.bin";
});

await Check("断点续传 Range（0-99 返回 206）", async () =>
{
    var req = new HttpRequestMessage(HttpMethod.Get, fileUrl);
    req.Headers.Range = new RangeHeaderValue(0, 99);
    var res = await http.SendAsync(req);
    var bytes = await res.Content.ReadAsByteArrayAsync();
    return res.StatusCode == HttpStatusCode.PartialContent
        && bytes.Length == 100
        && bytes.SequenceEqual(testBytes[..100]);
});

await Check("路径越界 ../.. 被拦截", async () =>
{
    var res = await http.GetAsync($"/api/items/{root.Id}?path={Uri.EscapeDataString("../..")}");
    return res.StatusCode == HttpStatusCode.BadRequest;
});

await Check("未知共享返回 404", async () =>
{
    var res = await http.GetAsync("/dl/00000000-0000-0000-0000-000000000000");
    return res.StatusCode == HttpStatusCode.NotFound;
});

await Check("上传文件到接收文件夹", async () =>
{
    using var form = new MultipartFormDataContent();
    var content = new ByteArrayContent("hello upload 测试"u8.ToArray());
    content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    form.Add(content, "files", "手机上传 测试.txt");

    var res = await http.PostAsync("/upload", form);
    var body = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode) return false;
    var json = JsonDocument.Parse(body).RootElement;
    var ok = json.GetProperty("count").GetInt32() == 1;
    var savedTo = Path.Combine(uploadDir, "手机上传 测试.txt");
    return ok && File.Exists(savedTo) && File.ReadAllText(savedTo) == "hello upload 测试";
});

await Check("上传的文件出现在接收目录列表", async () =>
{
    var json = await http.GetFromJsonAsync<JsonElement>("/api/items/uploads");
    return json.GetProperty("entries").EnumerateArray()
        .Any(e => e.GetProperty("name").GetString() == "手机上传 测试.txt");
});

await Check("实时刷新：rev / uploads 计数", async () =>
{
    var j1 = await http.GetFromJsonAsync<JsonElement>("/api/info");
    var rev1 = j1.GetProperty("rev").GetInt32();
    var up1 = j1.GetProperty("uploads").GetInt32();
    if (rev1 < 1 || up1 != 1) return false;

    // 模拟电脑端又添加 / 移除共享，版本号必须变化（网页端轮询就靠它刷新）
    var extra = SharedItem.FromPath(Path.Combine(shareDir, "项目笔记.md"))!;
    store.Add(extra);
    var j2 = await http.GetFromJsonAsync<JsonElement>("/api/info");
    var rev2 = j2.GetProperty("rev").GetInt32();
    store.Remove(extra.Id);
    var j3 = await http.GetFromJsonAsync<JsonElement>("/api/info");
    var rev3 = j3.GetProperty("rev").GetInt32();
    return rev2 == rev1 + 1 && rev3 == rev2 + 1;
});

await Check("防火墙状态检查（三态读取，含诊断信息）", async () =>
{
    var self = Environment.ProcessPath!;
    var info = await Task.Run(() => FirewallHelper.Check(self));
    Console.WriteLine($"      verdict={info.Verdict}; {info.Details}");
    return !string.IsNullOrWhiteSpace(info.Details);
});

await Check("防火墙撤销 / 动态放行看门狗（辅助路径）", async () =>
{
    var self = Environment.ProcessPath!;
    // 非管理员环境下撤销返回 1（拒绝访问）、管理员环境删除后返回 0——两种都属正常
    var rm = await Task.Run(() => FirewallHelper.RemoveRules(self));
    Console.WriteLine($"      RemoveRules -> {rm}");
    // 父进程不存在 → 加规则（成功与否随权限）后立即走撤销分支
    var flag = Path.Combine(Path.GetTempPath(), "qookil-nonexistent.flag");
    var wd = await Task.Run(() => FirewallHelper.RunWatchdog(self, int.MaxValue - 1, flag));
    Console.WriteLine($"      RunWatchdog -> {wd}");
    return true;
});

await Check("传输记录含完整路径（供「打开所在文件夹」）", () =>
{
    var uploadPath = Path.Combine(uploadDir, "手机上传 测试.txt");
    var dlOk = logEntries.Any(e => e.Kind == "下载" && e.FullPath == testFile);
    var upOk = logEntries.Any(e => e.Kind == "上传" && e.FullPath == uploadPath);
    return Task.FromResult(dlOk && upOk);
});

await Check("网卡地址排序（物理网卡在前，虚拟网卡在后）", async () =>
{
    var addrs = await Task.Run(() => NetworkHelper.GetLanIPv4s());
    foreach (var a in addrs.Take(6))
        Console.WriteLine($"      {a.Ip}  {a.Adapter}{(a.IsVirtual ? "（虚拟）" : "")}");
    var firstVirtual = addrs.FindIndex(a => a.IsVirtual);
    var lastReal = addrs.FindLastIndex(a => !a.IsVirtual);
    return addrs.Count == 0 || firstVirtual < 0 || lastReal < 0 || firstVirtual > lastReal;
});

await Check("系统下载文件夹解析（跟随重定向）", async () =>
{
    var dl = await Task.Run(() => KnownFolders.TryGetDownloads());
    Console.WriteLine($"      resolved downloads = {dl}");
    return !string.IsNullOrEmpty(dl) && Directory.Exists(dl);
});

await server.DisposeAsync();

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("ALL PASSED ✔");
    return 0;
}
Console.WriteLine($"{failures.Count} FAILED: {string.Join(", ", failures)}");
return 1;
