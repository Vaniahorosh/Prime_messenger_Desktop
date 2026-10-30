using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimeDesktop.Controls
{
    public class FormattedMessageTextBlock : TextBlock
    {
        public static readonly DependencyProperty RawTextProperty =
            DependencyProperty.Register(nameof(RawText), typeof(string), typeof(FormattedMessageTextBlock),
                new PropertyMetadata(string.Empty, OnRawTextChanged));

        public string RawText
        {
            get => (string)GetValue(RawTextProperty);
            set => SetValue(RawTextProperty, value);
        }

        public FormattedMessageTextBlock()
        {
            TextWrapping = TextWrapping.Wrap;
        }

        private static void OnRawTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FormattedMessageTextBlock block)
            {
                block.UpdateFormattedInlines(e.NewValue as string);
            }
        }

        public void UpdateFormattedInlines(string? raw)
        {
            Inlines.Clear();
            if (string.IsNullOrEmpty(raw)) return;

            string content = raw;
            if (content.StartsWith("[RICH]", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Length > 6 ? content.Substring(6) : string.Empty;
            }

            try
            {
                // Normalize breaks
                content = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"</p>", "\n", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"<p[^>]*>", "", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"<tr>", "\n", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"</(td|th)>", "   ", RegexOptions.IgnoreCase);

                // Tokenize by HTML tags: <b>, <i>, <code>, <h3>, <blockquote>, <a href="...">
                string pattern = @"(</?[a-zA-Z0-9]+(?:\s+[^>]*?)?>)";
                string[] parts = Regex.Split(content, pattern);

                bool isBold = false;
                bool isItalic = false;
                bool isCode = false;
                bool isHeader = false;
                bool isQuote = false;
                string? linkUrl = null;

                Brush accentBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DeepSkyBlue;
                Brush codeBgBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                FontFamily codeFont = new FontFamily("Consolas, Courier New");

                foreach (string part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    string lower = part.ToLowerInvariant().Trim();

                    if (lower == "<b>" || lower == "<strong>") { isBold = true; continue; }
                    if (lower == "</b>" || lower == "</strong>") { isBold = false; continue; }
                    if (lower == "<i>" || lower == "<em>") { isItalic = true; continue; }
                    if (lower == "</i>" || lower == "</em>") { isItalic = false; continue; }
                    if (lower == "<code>" || lower == "<pre>") { isCode = true; continue; }
                    if (lower == "</code>" || lower == "</pre>") { isCode = false; continue; }
                    if (lower.StartsWith("<h1") || lower.StartsWith("<h2") || lower.StartsWith("<h3") || lower.StartsWith("<h4")) { isHeader = true; continue; }
                    if (lower.StartsWith("</h")) { isHeader = false; continue; }
                    if (lower.StartsWith("<blockquote")) { isQuote = true; continue; }
                    if (lower == "</blockquote>") { isQuote = false; continue; }

                    if (lower.StartsWith("<a ") && lower.Contains("href="))
                    {
                        var m = Regex.Match(part, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                        if (m.Success) linkUrl = m.Groups[1].Value;
                        continue;
                    }
                    if (lower == "</a>")
                    {
                        linkUrl = null;
                        continue;
                    }

                    if (part.StartsWith("<") && part.EndsWith(">"))
                    {
                        // Other unsupported html tag, skip
                        continue;
                    }

                    // Decode HTML entities
                    string textSnippet = System.Net.WebUtility.HtmlDecode(part);

                    // Add content with formatting
                    AddTextSnippetWithLinks(textSnippet, isBold, isItalic, isCode, isHeader, isQuote, linkUrl, accentBrush, codeBgBrush, codeFont);
                }
            }
            catch
            {
                // Fallback to simple decoded text
                Inlines.Clear();
                AddTextSnippetWithLinks(System.Net.WebUtility.HtmlDecode(content), false, false, false, false, false, null, Brushes.DeepSkyBlue, Brushes.Transparent, FontFamily);
            }
        }

        private void AddTextSnippetWithLinks(string text, bool isBold, bool isItalic, bool isCode, bool isHeader, bool isQuote, string? forcedLinkUrl, Brush accentBrush, Brush codeBgBrush, FontFamily codeFont)
        {
            if (string.IsNullOrEmpty(text)) return;

            // If inside <a> tag
            if (!string.IsNullOrEmpty(forcedLinkUrl))
            {
                var hl = CreateHyperlink(text, forcedLinkUrl, accentBrush);
                Inlines.Add(hl);
                return;
            }

            // Detect plain text URLs
            var urlMatches = Regex.Matches(text, @"https?://[^\s<>""'{}|\\^`]+", RegexOptions.IgnoreCase);
            if (urlMatches.Count == 0)
            {
                Inlines.Add(CreateInline(text, isBold, isItalic, isCode, isHeader, isQuote, accentBrush, codeBgBrush, codeFont));
            }
            else
            {
                int lastIdx = 0;
                foreach (Match m in urlMatches)
                {
                    if (m.Index > lastIdx)
                    {
                        string sub = text.Substring(lastIdx, m.Index - lastIdx);
                        Inlines.Add(CreateInline(sub, isBold, isItalic, isCode, isHeader, isQuote, accentBrush, codeBgBrush, codeFont));
                    }

                    string url = m.Value;
                    Inlines.Add(CreateHyperlink(url, url, accentBrush));
                    lastIdx = m.Index + m.Length;
                }

                if (lastIdx < text.Length)
                {
                    string sub = text.Substring(lastIdx);
                    Inlines.Add(CreateInline(sub, isBold, isItalic, isCode, isHeader, isQuote, accentBrush, codeBgBrush, codeFont));
                }
            }
        }

        private Inline CreateInline(string text, bool isBold, bool isItalic, bool isCode, bool isHeader, bool isQuote, Brush accentBrush, Brush codeBgBrush, FontFamily codeFont)
        {
            string displayText = isQuote ? "▍ " + text : text;
            Run run = new Run(displayText);

            if (isHeader)
            {
                run.FontWeight = FontWeights.Bold;
                run.FontSize = FontSize + 2.5;
                run.Foreground = accentBrush;
            }
            else if (isBold)
            {
                run.FontWeight = FontWeights.Bold;
            }

            if (isItalic || isQuote)
            {
                run.FontStyle = FontStyles.Italic;
            }

            if (isCode)
            {
                run.FontFamily = codeFont;
                run.Background = codeBgBrush;
                run.Foreground = accentBrush;
            }

            return run;
        }

        private static Hyperlink CreateHyperlink(string displayText, string url, Brush accentBrush)
        {
            Hyperlink link = new Hyperlink(new Run(displayText))
            {
                Foreground = accentBrush,
                Cursor = Cursors.Hand,
                TextDecorations = System.Windows.TextDecorations.Underline
            };

            link.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            };

            return link;
        }
    }
}
