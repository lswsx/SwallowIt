namespace SwallowIt;

internal static class AppIcon
{
    private const string ResourceName = "SwallowIt.app.ico";

    public static Icon Create()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("缺少应用图标资源。");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
