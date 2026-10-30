using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DropMe;

/// <summary>Turns an image dragged from a web page into a real image file.</summary>
internal static class ImageDownloader
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".avif", ".ico", ".tif", ".tiff" };

    private static readonly Regex ImgSrc = new(@"<img\b[^>]*?\bsrc\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]*>", RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        // Images only: anything over 100 MB is refused rather than loaded into memory.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 100 * 1024 * 1024 };
        // Some sites refuse requests that don't look like they come from a browser.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/png,image/*;q=0.8,*/*;q=0.5");
        return client;
    }

    /// <summary>
    /// Finds the image being dragged: an &lt;img&gt; that is the whole HTML fragment (an image,
    /// or a link around an image), or a plain URL that points to an image file.
    /// A text selection that merely contains an image is left alone.
    /// </summary>
    public static string? FindImageUrl(string? html, string? plainText)
    {
        if (!string.IsNullOrEmpty(html))
        {
            string fragment = ExtractFragment(html);
            var match = ImgSrc.Match(fragment);
            string visibleText = WebUtility.HtmlDecode(Tags.Replace(fragment, "")).Trim();
            if (match.Success && visibleText.Length == 0)
            {
                string src = WebUtility.HtmlDecode(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
                return Resolve(src, SourceUrl(html));
            }
        }

        if (plainText != null
            && Uri.TryCreate(plainText.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && ImageExtensions.Contains(Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()))
            return uri.ToString();

        return null;
    }

    public static async Task<string?> TryDownloadAsync(string url, string folder, Func<string, string> uniquePath)
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");

            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                // data:image/png;base64,....
                int comma = url.IndexOf(',');
                if (comma < 0)
                    return null;
                string header = url[5..comma];
                if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                    return null;
                string ext = ExtensionForMime(header.Split(';')[0]) ?? ".png";
                string path = uniquePath(Path.Combine(folder, $"Image {stamp}{ext}"));
                await File.WriteAllBytesAsync(path, Convert.FromBase64String(url[(comma + 1)..]));
                return path;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return null;

            using var response = await Http.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
                return null;
            string? mime = response.Content.Headers.ContentType?.MediaType;
            string urlExt = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (mime != null && !mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !ImageExtensions.Contains(urlExt))
                return null;

            string extension = ExtensionForMime(mime) ?? (ImageExtensions.Contains(urlExt) ? urlExt : ".jpg");
            string baseName = VirtualFiles.SanitizeFileName(Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(uri.AbsolutePath)));
            if (baseName == "File" || baseName.Length < 2)
                baseName = $"Image {stamp}";
            if (baseName.Length > 80)
                baseName = baseName[..80];

            string target = uniquePath(Path.Combine(folder, baseName + extension));
            await File.WriteAllBytesAsync(target, await response.Content.ReadAsByteArrayAsync());
            return target;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DropMe: could not download image {url}: {ex}");
            return null;
        }
    }

    // CF_HTML wraps the dragged markup between <!--StartFragment--> and <!--EndFragment-->.
    private static string ExtractFragment(string html)
    {
        int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            return html[(start + "<!--StartFragment-->".Length)..end];
        return html;
    }

    private static Uri? SourceUrl(string html)
    {
        var match = Regex.Match(html, @"^SourceURL:(\S+)", RegexOptions.Multiline);
        return match.Success && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static string? Resolve(string src, Uri? baseUri)
    {
        // Uri can't hold large data: URIs, and they need no resolving anyway.
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return src;
        if (Uri.TryCreate(src, UriKind.Absolute, out var absolute))
            return absolute.ToString();
        if (baseUri != null && Uri.TryCreate(baseUri, src, out var resolved))
            return resolved.ToString();
        return null;
    }

    private static string? ExtensionForMime(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "image/avif" => ".avif",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        "image/tiff" => ".tiff",
        _ => null,
    };
}
