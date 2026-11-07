using System;
using System.IO;

namespace PrimeDesktop.Models;

public class SharedMediaItem
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsVideo { get; set; }
    public string DurationText { get; set; } = string.Empty;
    public string FormattedTime { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public ChatMessage? SourceMessage { get; set; }
}

public class SharedFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ExtensionBadge { get; set; } = "FILE";
    public string BadgeBrushHex { get; set; } = "#3B82F6";
    public string FormattedSize { get; set; } = string.Empty;
    public string FormattedTime { get; set; } = string.Empty;
    public ChatMessage? SourceMessage { get; set; }

    public static (string badge, string colorHex) GetExtensionStyle(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return ("FILE", "#6B7280");
        string ext = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "FILE";

        string color = ext switch
        {
            "PDF" => "#EF4444",                                     // Red
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => "#F59E0B",   // Amber / Orange
            "DOC" or "DOCX" => "#2563EB",                            // Blue
            "XLS" or "XLSX" or "CSV" => "#10B981",                   // Emerald Green
            "PPT" or "PPTX" => "#EA580C",                            // Deep Orange
            "TXT" or "MD" or "LOG" => "#64748B",                     // Slate
            "EXE" or "MSI" or "BAT" or "CMD" => "#8B5CF6",           // Purple
            "APK" => "#22C55E",                                     // Android Green
            "ISO" or "IMG" or "BIN" => "#D97706",                   // Bronze
            _ => "#0284C7"                                          // Light Blue
        };

        if (ext.Length > 4) ext = ext.Substring(0, 4);
        return (ext, color);
    }
}
