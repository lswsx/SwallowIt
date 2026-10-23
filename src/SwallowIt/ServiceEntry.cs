using System.Drawing;
using System.Text.Json.Serialization;

namespace SwallowIt;

public enum EntryState
{
    Collected,
    Shown,
    Gone
}

public sealed class ServiceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public long WindowHandle { get; set; }

    public int ProcessId { get; set; }

    public long ProcessStartTimeTicks { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string WindowClass { get; set; } = string.Empty;

    public string OriginalTitle { get; set; } = string.Empty;

    public bool HiddenByUs { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public IntPtr Handle => new(WindowHandle);

    [JsonIgnore]
    public EntryState State { get; set; } = EntryState.Shown;

    [JsonIgnore]
    public string CurrentTitle { get; set; } = string.Empty;

    [JsonIgnore]
    public Image? Icon { get; set; }

    [JsonIgnore]
    public string StateText => State switch
    {
        EntryState.Collected => "已收纳",
        EntryState.Shown => "已显示",
        _ => "窗口已消失"
    };
}
