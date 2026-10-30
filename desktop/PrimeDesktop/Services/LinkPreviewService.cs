using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
                    // 2. Generic OpenGraph HTML fetch
                    using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) return null;

                    var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                    if (contentType != null && !contentType.Contains("html") && !contentType.Contains("text"))
                    {
                        return null;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);
                    char[] buffer = new char[80000];
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                    string html = new string(buffer, 0, read);

                    data.Title = ExtractMeta(html, "og:title") ?? ExtractTag(html, "title") ?? data.Domain;
                    data.Description = ExtractMeta(html, "og:description") ?? ExtractMeta(html, "description") ?? string.Empty;
                    data.ImageUrl = ExtractMeta(html, "og:image") ?? ExtractMeta(html, "twitter:image");

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

        private static string? ExtractMeta(string html, string property)
        {
            var match = Regex.Match(html, $@"<meta\s+[^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(property)}[""'][^>]*content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (match.Success) return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());

            var match2 = Regex.Match(html, $@"<meta\s+[^>]*content\s*=\s*[""']([^""']+)[""'][^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(property)}[""']", RegexOptions.IgnoreCase);
            if (match2.Success) return System.Net.WebUtility.HtmlDecode(match2.Groups[1].Value.Trim());

            return null;
        }

        private static string? ExtractTag(string html, string tag)
        {
            var match = Regex.Match(html, $@"<{Regex.Escape(tag)}[^>]*>(.*?)</{Regex.Escape(tag)}>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string val = Regex.Replace(match.Groups[1].Value, @"<[^>]+>", "").Trim();
                return System.Net.WebUtility.HtmlDecode(val);
            }
            return null;
        }
    }
}
