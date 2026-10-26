using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TaskNinja.Views
{
    /// <summary>
    /// Shared helpers for turning plain task text into clickable links —
    /// so a URL pasted into a task's notes becomes a real hyperlink
    /// wherever the notes are shown. Mirrors how ClipNinja recognizes and
    /// opens URLs. Centralized here so the hover preview and the detail
    /// editor behave identically.
    /// </summary>
    public static class TextLinkify
    {
        // http(s):// URLs, and bare www.* domains we prefix on open.
        private static readonly Regex UrlRegex = new(
            @"(https?://[^\s<>""']+)|((?<!http://|https://)\bwww\.[A-Za-z0-9][^\s<>""']+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Normalize a URL for the OS to open — prepends http://
        /// to a bare www.* link.</summary>
        public static string NormalizeUrl(string url)
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return url;
            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                return "http://" + url;
            return url;
        }

        /// <summary>Open a URL in the default browser (best-effort).</summary>
        public static void Open(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(NormalizeUrl(url)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Services.Trace.Log("linkify", $"open url failed: {ex.Message}");
            }
        }

        /// <summary>Distinct URLs found in the text, in order of first
        /// appearance.</summary>
        public static List<string> Extract(string? text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match m in UrlRegex.Matches(text))
            {
                var url = TrimTrailingPunctuation(m.Value);
                if (!list.Contains(url)) list.Add(url);
            }
            return list;
        }

        /// <summary>Build a wrapping TextBlock whose URL spans are
        /// clickable Hyperlinks and everything else is plain text. Use
        /// this anywhere the notes are DISPLAYED (not edited).</summary>
        public static TextBlock BuildBlock(string? text, double fontSize, Brush textBrush, Brush linkBrush)
        {
            var tb = new TextBlock
            {
                FontSize = fontSize,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            AppendInlines(tb.Inlines, text ?? "", linkBrush);
            return tb;
        }

        /// <summary>Append text to an inline collection, converting URL
        /// spans into clickable Hyperlink runs.</summary>
        private static void AppendInlines(InlineCollection inlines, string text, Brush linkBrush)
        {
            int pos = 0;
            foreach (Match m in UrlRegex.Matches(text))
            {
                // Plain text before this match.
                if (m.Index > pos)
                    inlines.Add(new Run(text.Substring(pos, m.Index - pos)));

                // The URL may end with punctuation that isn't part of it
                // ("see https://x.com." — the period is a sentence end).
                string raw = m.Value;
                string url = TrimTrailingPunctuation(raw);
                string trailing = raw.Substring(url.Length);

                var link = new Hyperlink(new Run(url))
                {
                    Foreground = linkBrush,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = NormalizeUrl(url),
                };
                var captured = url;
                link.Click += (_, _) => Open(captured);
                inlines.Add(link);

                if (trailing.Length > 0) inlines.Add(new Run(trailing));
                pos = m.Index + raw.Length;
            }
            // Remaining plain text after the last match.
            if (pos < text.Length)
                inlines.Add(new Run(text.Substring(pos)));
        }

        /// <summary>Strip trailing sentence punctuation / closing brackets
        /// that a regex greedily grabs but that aren't part of the URL.</summary>
        private static string TrimTrailingPunctuation(string url)
        {
            // Common trailing chars to drop; keep a ')' only if the URL has
            // a matching '(' (e.g. Wikipedia URLs).
            while (url.Length > 0)
            {
                char c = url[^1];
                if (c is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'' or '>' or ']' or '}')
                {
                    url = url[..^1];
                    continue;
                }
                if (c == ')' && url.Count(ch => ch == '(') < url.Count(ch => ch == ')'))
                {
                    url = url[..^1];
                    continue;
                }
                break;
            }
            return url;
        }
    }
}
