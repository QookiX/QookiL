namespace QookiL.Core;

public static class FileSizeFormat
{
    public static string Human(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return v >= 100 || i == 0 ? $"{(long)Math.Round(v)} {units[i]}" : $"{v:0.#} {units[i]}";
    }
}
