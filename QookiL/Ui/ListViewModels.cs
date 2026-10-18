using QookiL.Core;
using QookiL.Web;
using Microsoft.UI.Xaml.Media;

namespace QookiL;

/// <summary>共享列表的展示模型。</summary>
public sealed class SharedItemView
{
    public required SharedItem Item { get; init; }
    public required string Title { get; init; }
    public required string Meta { get; init; }
    public required string IconGlyph { get; init; }
    public bool Received { get; init; }

    public static SharedItemView From(SharedItem item, bool received = false) => new()
    {
        Item = item,
        Title = item.Name,
        Meta = received
            ? "手机上传 · " + FileSizeFormat.Human(item.SizeBytes) + " · " + item.FullPath
            : item.IsFolder
                ? "文件夹 · " + item.FullPath
                : FileSizeFormat.Human(item.SizeBytes) + " · " + item.FullPath,
        IconGlyph = item.IsFolder ? "\uE8B7" : "\uE7C3",
        Received = received,
    };
}

/// <summary>传输记录的展示模型。</summary>
public sealed class TransferLogView
{
    public required string Glyph { get; init; }
    public required SolidColorBrush BadgeBrush { get; init; }
    public required string Name { get; init; }
    public required string Sub { get; init; }
    public required string Size { get; init; }
    public required string FullPath { get; init; }

    public static TransferLogView From(TransferLogEntry e) => new()
    {
        // E896 上箭头（上传）、E898 下箭头（下载）
        Glyph = e.Kind == "上传" ? "\uE896" : "\uE898",
        BadgeBrush = new SolidColorBrush(
            e.Kind == "上传" ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.SeaGreen),
        Name = e.Name,
        Sub = e.Time.ToString("HH:mm:ss") + "  ·  " + e.Ip,
        Size = e.Bytes > 0 ? FileSizeFormat.Human(e.Bytes) : "",
        FullPath = e.FullPath,
    };
}

/// <summary>进行中传输的展示模型（进度行）。</summary>
public sealed class TransferProgressView
{
    public required Guid Id { get; init; }
    public required string Glyph { get; init; }
    public required SolidColorBrush BadgeBrush { get; init; }
    public required string Name { get; init; }
    public required double Percent { get; init; }
    public required string PctText { get; init; }
    public required string SizeText { get; init; }

    public static TransferProgressView From(TransferProgress p) => new()
    {
        Id = p.Id,
        Glyph = p.Kind == "上传" ? "\uE896" : "\uE898",
        BadgeBrush = new SolidColorBrush(
            p.Kind == "上传" ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.SeaGreen),
        Name = p.Name,
        Percent = p.Percent,
        PctText = p.Total > 0 ? $"{p.Percent:0}%" : "",
        SizeText = p.Total > 0
            ? $"{FileSizeFormat.Human(p.Done)} / {FileSizeFormat.Human(p.Total)}"
            : FileSizeFormat.Human(p.Done),
    };
}
