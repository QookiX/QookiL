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

    public static SharedItemView From(SharedItem item) => new()
    {
        Item = item,
        Title = item.Name,
        Meta = item.IsFolder
            ? "文件夹 · " + item.FullPath
            : FileSizeFormat.Human(item.SizeBytes) + " · " + item.FullPath,
        IconGlyph = item.IsFolder ? "\uE8B7" : "\uE7C3",
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

    public static TransferLogView From(TransferLogEntry e) => new()
    {
        Glyph = e.Kind == "上传" ? "\uE898" : "\uE896",
        BadgeBrush = new SolidColorBrush(
            e.Kind == "上传" ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.SeaGreen),
        Name = e.Name,
        Sub = e.Time.ToString("HH:mm:ss") + "  ·  " + e.Ip,
        Size = e.Bytes > 0 ? FileSizeFormat.Human(e.Bytes) : "",
    };
}
