namespace SwallowIt;

internal static class AppLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SwallowIt",
        "error.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}" +
                $"{new string('-', 72)}{Environment.NewLine}";

            File.AppendAllText(FilePath, entry);
        }
        catch
        {
            // 日志失败不能影响程序本身。
        }
    }
}
