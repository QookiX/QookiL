using System.Runtime.InteropServices;

namespace QookiL.Core;

/// <summary>
/// 在资源管理器中显示文件/文件夹。
/// 优先用 shell32 的 SHOpenFolderAndSelectItems：它在当前进程内请求「已有的」资源管理器
/// 打开并定位，不新起 explorer.exe —— 受限启动上下文（沙箱/受限令牌）里新起 explorer.exe
/// 会初始化失败，Windows 会弹「应用程序无法正常启动(0xc0000142)」。
/// </summary>
public static class ShellOpen
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    /// <summary>
    /// 打开文件所在文件夹并选中它；传目录则直接打开该目录。
    /// 返回 null 表示成功，否则是错误描述。
    /// </summary>
    public static string? Reveal(string path)
    {
        var pidl = IntPtr.Zero;
        try
        {
            var hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
            if (hr != 0 || pidl == IntPtr.Zero)
                return "无法解析路径（0x" + hr.ToString("X8") + "）";

            var rc = SHOpenFolderAndSelectItems(pidl, 0, null, 0);
            return rc == 0 ? null : "无法打开文件夹（0x" + rc.ToString("X8") + "）";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            if (pidl != IntPtr.Zero) ILFree(pidl);
        }
    }
}
