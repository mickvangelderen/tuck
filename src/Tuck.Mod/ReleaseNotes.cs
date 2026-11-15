using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Tuck.Mod;

internal static class ReleaseNotes
{
#if DEBUG
    internal static bool IsDevelopment => true;
#else
    internal static bool IsDevelopment => false;
#endif

    internal static bool ShouldShow(string version, string? acknowledgedVersion, bool development) =>
        development || !string.Equals(version, acknowledgedVersion?.Trim(), StringComparison.Ordinal);

    internal static string ReadChangelog()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Tuck.Changelog")
            ?? throw new InvalidOperationException("The embedded changelog is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // shortcut: supports the changelog's headings, paragraphs, bullets and simple inline Markdown; extend when its format changes.
    internal static string FormatChangelog(string markdown)
    {
        markdown = Regex.Replace(markdown, @"<!--[\s\S]*?-->", "");
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(markdown, @"(?m)^\[([^\]]+)\]:"))
            references.Add(match.Groups[1].Value);

        var body = new StringBuilder();
        bool inReleases = false;
        bool paragraphBreak = false;
        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("## [", StringComparison.Ordinal)) inReleases = true;
            if (!inReleases || Regex.IsMatch(line, @"^\[[^\]]+\]:")) continue;
            string text = line.Trim();
            if (text.Length == 0)
            {
                paragraphBreak = true;
                continue;
            }
            var headingMatch = Regex.Match(text, @"^#{1,6}\s+(.+)$");
            bool bullet = text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal);
            if (body.Length > 0)
            {
                if (paragraphBreak || headingMatch.Success)
                {
                    if (body[body.Length - 1] != '\n') body.Append('\n');
                    if (body.Length < 2 || body[body.Length - 2] != '\n') body.Append('\n');
                }
                else if (body[body.Length - 1] != '\n') body.Append(bullet ? '\n' : ' ');
            }
            paragraphBreak = false;
            if (headingMatch.Success)
                body.Append("<size=120%><b>").Append(FormatInline(headingMatch.Groups[1].Value, references)).Append("</b></size>\n\n");
            else
                body.Append(bullet ? "• " : "").Append(FormatInline(bullet ? text.Substring(2) : text, references));
        }
        return body.ToString().Trim();
    }

    private static string FormatInline(string text, HashSet<string> references)
    {
        var result = new StringBuilder();
        int offset = 0;
        foreach (Match match in Regex.Matches(text,
            @"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|\*(?<italic>[^*]+)\*|\[(?<label>[^\]]+)\](?:\((?<url>[^)]+)\)|\[(?<reference>[^\]]*)\])?"))
        {
            result.Append(Literal(text.Substring(offset, match.Index - offset)));
            if (match.Groups["code"].Success)
                result.Append(Literal(match.Groups["code"].Value));
            else if (match.Groups["bold"].Success)
                result.Append("<b>").Append(Literal(match.Groups["bold"].Value)).Append("</b>");
            else if (match.Groups["italic"].Success)
                result.Append("<i>").Append(Literal(match.Groups["italic"].Value)).Append("</i>");
            else
            {
                string label = match.Groups["label"].Value;
                string reference = match.Groups["reference"].Value;
                bool link = match.Groups["url"].Success
                    || references.Contains(reference.Length == 0 ? label : reference);
                result.Append(Literal(link ? label : match.Value));
            }
            offset = match.Index + match.Length;
        }
        return result.Append(Literal(text.Substring(offset))).ToString();
    }

    private static string Literal(string text) => text.Replace("<", "<noparse><</noparse>");
}
