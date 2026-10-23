using System.Text.Encodings.Web;
using System.Text.Json;

namespace SwallowIt;

internal sealed class EntryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SwallowIt",
        "entries.json");

    public List<ServiceEntry> Load(out string? warning)
    {
        warning = null;

        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var json = File.ReadAllText(FilePath);
            var entries = JsonSerializer.Deserialize<List<ServiceEntry>>(json, SerializerOptions) ?? [];
            return entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .ToList();
        }
        catch (Exception exception)
        {
            AppLog.Write("读取本地记录失败", exception);
            warning = "读取本地记录失败，列表已重置；原文件已备份。";
            BackupUnreadableFile();
            return [];
        }
    }

    public void Save(IReadOnlyCollection<ServiceEntry> entries)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("无法确定本地记录目录。");
        }

        Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, SerializerOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private void BackupUnreadableFile()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, $"{FilePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
        }
        catch
        {
            // Preserve the original failure; the file may still be recoverable manually.
        }
    }
}
