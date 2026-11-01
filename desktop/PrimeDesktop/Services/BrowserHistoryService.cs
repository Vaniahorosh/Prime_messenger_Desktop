using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrimeDesktop.Services;

public class RecentBrowserSite
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Domain { get; set; } = "";
    public string FaviconUrl { get; set; } = "";
    public DateTime LastVisited { get; set; } = DateTime.Now;

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title) && Title != Url)
                return Title;
            return string.IsNullOrWhiteSpace(Domain) ? Url : Domain;
        }
    }

    public string TooltipText => $"{DisplayTitle}\n{Url}";

    public string InitialLetter
    {
        get
        {
            string src = !string.IsNullOrWhiteSpace(Domain) ? Domain : DisplayTitle;
            if (string.IsNullOrWhiteSpace(src)) return "🌐";
            src = src.TrimStart('.', 'w', 'W');
            if (src.Length == 0) return "W";
            return char.ToUpperInvariant(src[0]).ToString();
        }
    }
}

public static class BrowserHistoryService
{
    private static readonly string HistoryFilePath = Path.Combine(StorageService.BaseDirectory, "browser_recent_sites.json");
    private static readonly object _lock = new();

    public static List<RecentBrowserSite> LoadRecentSites()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    string json = File.ReadAllText(HistoryFilePath);
                    var list = JsonSerializer.Deserialize<List<RecentBrowserSite>>(json);
                    if (list != null) return list;
                }
            }
            catch { }
            return new List<RecentBrowserSite>();
        }
    }

    public static void SaveRecentSites(IEnumerable<RecentBrowserSite> sites)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(StorageService.BaseDirectory);
                string json = JsonSerializer.Serialize(sites.Take(12).ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryFilePath, json);
            }
            catch { }
        }
    }

    public static void RecordVisit(string url, string title, string? faviconUrl = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

            string domain = uri.Host;
            var list = LoadRecentSites();

            var existing = list.FirstOrDefault(s =>
                string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Domain, domain, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                list.Remove(existing);
                existing.LastVisited = DateTime.Now;
                existing.Url = url;
                if (!string.IsNullOrWhiteSpace(title) && title != url && title != domain)
                {
                    existing.Title = title;
                }
                if (!string.IsNullOrWhiteSpace(faviconUrl))
                {
                    existing.FaviconUrl = faviconUrl;
                }
                list.Insert(0, existing);
            }
            else
            {
                list.Insert(0, new RecentBrowserSite
                {
                    Url = url,
                    Title = string.IsNullOrWhiteSpace(title) ? domain : title,
                    Domain = domain,
                    FaviconUrl = faviconUrl ?? "",
                    LastVisited = DateTime.Now
                });
            }

            SaveRecentSites(list);
        }
        catch { }
    }

    public static void ClearHistory()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    File.Delete(HistoryFilePath);
                }
            }
            catch { }
        }
    }
}
