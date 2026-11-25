using System.Drawing;

namespace EveFocus;

internal static class AppIcon
{
    private const string ResourceName = "EveFocus.ico";

    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
        return stream != null ? new Icon(stream) : (Icon)SystemIcons.Application.Clone();
    }

    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
        return stream != null ? new Icon(stream, size) : (Icon)SystemIcons.Application.Clone();
    }
}
