using System.Runtime.InteropServices;

namespace QookiL.Core;

/// <summary>系统已知文件夹解析（跟随用户重定向，例如「下载」被移动到其他盘）。</summary>
public static class KnownFolders
{
    // FOLDERID_Downloads
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr path);

    /// <summary>系统「下载」文件夹的真实路径（含重定向）；解析失败返回 null。</summary>
    public static string? TryGetDownloads()
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            var id = DownloadsId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out ptr) != 0) return null;
            return Marshal.PtrToStringUni(ptr);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
    }
}
