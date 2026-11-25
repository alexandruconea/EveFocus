using System;

namespace EveFocus;

internal static class AppInfo
{
    public const string Repository = "alexandruconea/EveFocus";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

    public static Version Version { get; } = Normalize(typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0));

    public static string DisplayVersion => Format(Version);

    public static Version? ParseVersion(string? text)
    {
        string value = (text ?? "").Trim().TrimStart('v', 'V');
        if (value.Length > 0 && value.IndexOf('.') < 0) value += ".0";
        return Version.TryParse(value, out var version) ? Normalize(version) : null;
    }

    public static string Format(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.Build > 0 ? version.ToString(3) : version.ToString(2);

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
