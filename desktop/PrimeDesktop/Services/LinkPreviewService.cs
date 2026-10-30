using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace PrimeDesktop.Services
{
    public class LinkPreviewData
    {
        public string Url { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string? LocalImagePath { get; set; }
        public bool HasImage => !string.IsNullOrEmpty(LocalImagePath) && File.Exists(LocalImagePath);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    }

    public static class LinkPreviewService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly ConcurrentDictionary<string, LinkPreviewData> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string PreviewImagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrimeDesktop", "LinkPreviews");

        static LinkPreviewService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                Directory.CreateDirectory(PreviewImagesDir);
            }
            catch { }
        }

        public static string? ExtractFirstUrl(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, @"https?://[^\s<>""'{}|\\^`]+", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }

        public static async Task<LinkPreviewData?> FetchPreviewAsync(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return null;
            string url = rawUrl.Trim();

            if (_cache.TryGetValue(url, out var cached))
            {
                return cached;
            }

            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();

                LinkPreviewData data = new LinkPreviewData
                {
                    Url = url,
                    Domain = host.StartsWith("www.") ? host.Substring(4) : host
                };

                // 1. YouTube specialized oEmbed check
                if (host.Contains("youtube.com") || host.Contains("youtu.be"))
                {
                    string oembedUrl = $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(url)}&format=json";
                    string json = await _httpClient.GetStringAsync(oembedUrl);
                    var matchTitle = Regex.Match(json, @"""title""\s*:\s*""([^""]+)""");
                    var matchAuthor = Regex.Match(json, @"""author_name""\s*:\s*""([^""]+)""");
                    var matchThumb = Regex.Match(json, @"""thumbnail_url""\s*:\s*""([^""]+)""");

                    data.Title = matchTitle.Success ? System.Net.WebUtility.HtmlDecode(matchTitle.Groups[1].Value) : "YouTube Video";
                    data.Description = matchAuthor.Success ? matchAuthor.Groups[1].Value : "YouTube";
                    data.ImageUrl = matchThumb.Success ? matchThumb.Groups[1].Value.Replace("\\/", "/") : null;
                }
                else
                {
                    // 2. Generic OpenGraph HTML fetch via HtmlAgilityPack
                    using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) return null;

                    var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                    if (contentType != null && !contentType.Contains("html") && !contentType.Contains("text"))
                    {
                        return null;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);
                    char[] buffer = new char[120000];
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                    string html = new string(buffer, 0, read);

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    string? ogTitle = GetMetaContent(doc, "og:title", "twitter:title");
                    string? pageTitle = null;
                    var titleNode = doc.DocumentNode.SelectSingleNode("//head/title") ?? doc.DocumentNode.SelectSingleNode("//title");
                    if (titleNode != null)
                    {
                        pageTitle = System.Net.WebUtility.HtmlDecode(titleNode.InnerText?.Trim());
                    }

                    data.Title = !string.IsNullOrWhiteSpace(ogTitle) ? ogTitle : (!string.IsNullOrWhiteSpace(pageTitle) ? pageTitle : data.Domain);
                    data.Description = GetMetaContent(doc, "og:description", "description", "twitter:description") ?? string.Empty;
                    data.ImageUrl = GetMetaContent(doc, "og:image", "twitter:image", "thumbnail");

                    if (!string.IsNullOrEmpty(data.ImageUrl) && !data.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        data.ImageUrl = new Uri(uri, data.ImageUrl).AbsoluteUri;
                    }
                }

                // 3. Download thumbnail locally if present for fast offline / local display
                if (!string.IsNullOrEmpty(data.ImageUrl))
                {
                    try
                    {
                        string safeFileName = "lp_" + Math.Abs(url.GetHashCode()) + Path.GetExtension(new Uri(data.ImageUrl).AbsolutePath);
                        if (string.IsNullOrWhiteSpace(Path.GetExtension(safeFileName)) || safeFileName.Length > 40)
                        {
                            safeFileName = "lp_" + Math.Abs(url.GetHashCode()) + ".jpg";
                        }
                        string localPath = Path.Combine(PreviewImagesDir, safeFileName);

                        if (!File.Exists(localPath))
                        {
                            byte[] imgBytes = await _httpClient.GetByteArrayAsync(data.ImageUrl);
                            await File.WriteAllBytesAsync(localPath, imgBytes);
                        }
                        data.LocalImagePath = localPath;
                    }
                    catch { }
                }

                _cache[url] = data;
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetMetaContent(HtmlDocument doc, params string[] properties)
        {
            if (doc?.DocumentNode == null) return null;

            foreach (var prop in properties)
            {
                var node = doc.DocumentNode.SelectSingleNode($"//meta[translate(@property, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='{prop.ToLowerInvariant()}'] | //meta[translate(@name, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='{prop.ToLowerInvariant()}']");
                if (node != null)
                {
                    string content = node.GetAttributeValue("content", "");
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return System.Net.WebUtility.HtmlDecode(content.Trim());
                    }
                }
            }
            return null;
        }
    }
}
