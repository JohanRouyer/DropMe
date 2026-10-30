using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace DropMe;

/// <summary>A newer release published on GitHub.</summary>
internal sealed record UpdateInfo(Version Version, string InstallerUrl, string AssetName, string? Sha256, string PageUrl);

/// <summary>Looks for newer releases on GitHub and downloads their installer.</summary>
internal static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/JohanRouyer/DropMe";
    private const string LatestReleaseApi = "https://api.github.com/repos/JohanRouyer/DropMe/releases/latest";

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0))
            : new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DropMe/{CurrentVersion}");
        return client;
    }

    /// <summary>
    /// Returns the latest release when it is newer than this build, or null when up to date.
    /// Throws when GitHub can't be reached or answers something unexpected.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var release = json.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= CurrentVersion)
            return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("DropMe-Setup-", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            // GitHub publishes a "sha256:<hex>" digest for each uploaded asset.
            string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            string? sha256 = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, name, sha256,
                release.GetProperty("html_url").GetString() ?? $"{RepositoryUrl}/releases/latest");
        }
        return null;
    }

    private static readonly string DownloadFolder = Path.Combine(Path.GetTempPath(), "DropMe", "updates");

    /// <summary>Downloads the installer and checks it against the digest GitHub published for it.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update)
    {
        Directory.CreateDirectory(DownloadFolder);
        string path = Path.Combine(DownloadFolder, VirtualFiles.SanitizeFileName(update.AssetName));

        using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file);
        }

        if (update.Sha256 != null)
        {
            string actual;
            await using (var stream = File.OpenRead(path))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException("The downloaded installer doesn't match the checksum published on GitHub.");
            }
        }
        return path;
    }

    /// <summary>Removes installers left over from a finished update (about 50 MB each).</summary>
    public static void DeleteDownloadedInstallers()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
                Directory.Delete(DownloadFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use or locked; it lives in %TEMP% and will be cleaned eventually.
        }
    }
}
