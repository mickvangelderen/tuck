using System.Text.RegularExpressions;
using System.Xml.Linq;
using Markdig;
using Markdig.Syntax;

internal static class WorkshopReleaseNotes
{
    public static string Load(string changelogPath, string manifestPath)
    {
        string? version = XDocument.Load(manifestPath).Root?.Element("ModVersion")?.Value.Trim();
        if (string.IsNullOrEmpty(version))
            throw new InvalidDataException("The staged mod manifest must specify a ModVersion.");

        string markdown = File.ReadAllText(changelogPath);
        var document = Markdown.Parse(markdown);
        var sections = document.OfType<HeadingBlock>().Where(heading => heading.Level == 2).ToArray();
        string SectionName(HeadingBlock heading) => Regex.Match(
            markdown.Substring(heading.Span.Start, heading.Span.Length), @"^##\s+\[([^\]]+)\](?:\s|$)").Groups[1].Value;

        if (sections.Length == 0 || SectionName(sections[0]) != "Unreleased")
            throw new InvalidDataException("CHANGELOG.md must start its release sections with ## [Unreleased].");
        if (sections.Length < 2)
            throw new InvalidDataException("CHANGELOG.md must have a versioned release section immediately after Unreleased.");

        int unreleased = document.IndexOf(sections[0]);
        int release = document.IndexOf(sections[1]);
        if (document.Skip(unreleased + 1).Take(release - unreleased - 1)
            .Any(block => block is not LinkReferenceDefinitionGroup))
            throw new InvalidDataException("The Unreleased section must be empty before publishing. Move its entries into a versioned release section.");
        string releaseVersion = SectionName(sections[1]);
        if (releaseVersion != version)
            throw new InvalidDataException($"The first release section version '{releaseVersion}' does not match manifest ModVersion '{version}'.");

        int end = sections.Length > 2 ? document.IndexOf(sections[2]) : document.Count;
        for (int index = document.Count - 1; index >= 0; index--)
            if (index < release || index >= end) document.RemoveAt(index);

        string notes = new WorkshopDescription(changelogPath, document).Render();
        WorkshopLimits.Validate("Change note", notes, WorkshopLimits.ChangeNoteMaxBytes);
        return notes;
    }
}
