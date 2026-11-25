using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EveFocus;

internal sealed class ReleaseInfo
{
    public ReleaseInfo(Version version, string tagVersion, string pageUrl, string? exeUrl, string? signatureUrl)
    {
        Version = version;
        TagVersion = tagVersion;
        PageUrl = pageUrl;
        ExeUrl = exeUrl;
        SignatureUrl = signatureUrl;
    }

    public Version Version { get; }
    public string TagVersion { get; }
    public string PageUrl { get; }
    public string? ExeUrl { get; }
    public string? SignatureUrl { get; }
    public string DisplayVersion => AppInfo.Format(Version);
    public bool CanInstall => ExeUrl != null && SignatureUrl != null;
}

internal sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message)
    {
    }
}

internal static class UpdateService
{
    public const string AssetName = "EveFocus.exe";

    private const string LatestReleaseApi = "https://api.github.com/repos/" + AppInfo.Repository + "/releases/latest";
    private const long MaxExeBytes = 20L * 1024 * 1024;
    private const long MaxSignatureBytes = 4096;

    private static readonly DataContractJsonSerializer ReleaseSerializer = new(typeof(GitHubRelease));

    public static ReleaseInfo? FindNewerRelease()
    {
        try
        {
            using var response = CreateRequest(LatestReleaseApi, "application/vnd.github+json").GetResponse();
            using var stream = response.GetResponseStream();
            return Parse(ReleaseSerializer.ReadObject(stream) as GitHubRelease, AppInfo.Version);
        }
        catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public static ReleaseInfo? Parse(GitHubRelease? release, Version current)
    {
        if (release == null || release.Draft || release.Prerelease) return null;
        string tagVersion = (release.TagName ?? "").Trim().TrimStart('v', 'V');
        var version = AppInfo.ParseVersion(tagVersion);
        if (version == null || version <= current) return null;

        var assets = release.Assets ?? new List<GitHubAsset>();
        var exe = assets.FirstOrDefault(a => a != null && a.Name == AssetName);
        var signature = assets.FirstOrDefault(a => a != null && a.Name == AssetName + ".sig");
        string page = release.HtmlUrl != null && release.HtmlUrl.StartsWith("https://github.com/", StringComparison.Ordinal)
            ? release.HtmlUrl
            : AppInfo.ReleasesPage;
        return new ReleaseInfo(version, tagVersion, page, exe?.Url, signature?.Url);
    }

    public static string DownloadVerified(ReleaseInfo release)
    {
        if (!release.CanInstall) throw new UpdateException("The release has no signed " + AssetName + ".");

        string folder = Path.Combine(Path.GetTempPath(), "EveFocus-update-" + release.TagVersion);
        Directory.CreateDirectory(folder);
        string exePath = Path.Combine(folder, AssetName);
        File.WriteAllBytes(exePath, Download(release.ExeUrl!, MaxExeBytes));
        string signature = Encoding.ASCII.GetString(Download(release.SignatureUrl!, MaxSignatureBytes));

        if (!ReleaseSignature.Verify(release.TagVersion, AssetName, ReleaseSignature.Sha256Hex(exePath), signature))
        {
            File.Delete(exePath);
            throw new UpdateException("The downloaded " + AssetName + " isn't signed with the EVE Focus release key, so it wasn't installed.");
        }
        return exePath;
    }

    private static byte[] Download(string url, long maxBytes)
    {
        using var response = CreateRequest(url, "application/octet-stream").GetResponse();
        if (response.ContentLength > maxBytes) throw new UpdateException("The download is larger than expected.");
        using var input = response.GetResponseStream();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > maxBytes) throw new UpdateException("The download is larger than expected.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static HttpWebRequest CreateRequest(string url, string accept)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = "EveFocus/" + AppInfo.DisplayVersion;
        request.Accept = accept;
        request.Timeout = 30000;
        request.ReadWriteTimeout = 30000;
        request.AllowAutoRedirect = true;
        return request;
    }
}

internal static class UpdateInstaller
{
    public static void Replace(string exePath, string newExePath)
    {
        string oldPath = OldPath(exePath);
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(exePath, oldPath);
        try
        {
            File.Copy(newExePath, exePath);
        }
        catch
        {
            File.Move(oldPath, exePath);
            throw;
        }
    }

    public static bool CleanUp(string exePath)
    {
        string oldPath = OldPath(exePath);
        if (!File.Exists(oldPath)) return true;
        try
        {
            File.Delete(oldPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string OldPath(string exePath) => exePath + ".old";
}

[DataContract]
internal sealed class GitHubRelease
{
    [DataMember(Name = "tag_name")] public string? TagName { get; set; }
    [DataMember(Name = "draft")] public bool Draft { get; set; }
    [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
    [DataMember(Name = "html_url")] public string? HtmlUrl { get; set; }
    [DataMember(Name = "assets")] public List<GitHubAsset>? Assets { get; set; }
}

[DataContract]
internal sealed class GitHubAsset
{
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "browser_download_url")] public string? Url { get; set; }
}
