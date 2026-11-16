using Tuck.Build;

namespace Tuck.Mod.Tests;

public class ReleaseNotesTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData("", false, true)]
    [InlineData("1.2.0", false, false)]
    [InlineData("1.2.0\r\n", false, false)]
    [InlineData("1.1.0", false, true)]
    [InlineData("1.2.0-dev", false, true)]
    [InlineData(null, true, true)]
    [InlineData("1.2.0", true, true)]
    public void ShowsOnVersionChangeOrEveryDevelopmentLaunch(string? acknowledged, bool development, bool expected)
    {
        Assert.Equal(expected, ReleaseNotes.ShouldShow("1.2.0", acknowledged, development));
    }

    [Fact]
    public void DisplaysEveryVersionWithoutPreambleCommentsOrUrls()
    {
        const string changelog = """
            # Changelog
            All notable changes to Tuck will be documented in this file.
            The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
            <!--
            ## [1.2.0] - 2000-01-01
            Instructions, not release notes.
            -->
            ## [Unreleased]
            - Future work.
            ## [1.2.0] - 2026-11-12
            - First change.
              Its continuation.
            - Second change. <!-- hidden -->
            ## [1.1.0] - 2026-11-11
            Older change.

            [Unreleased]: https://example.com/upcoming
            [1.2.0]: https://example.com/current
            [1.1.0]: https://example.com/old
            """;

        Assert.Equal("""
            <size=120%><b>Unreleased</b></size>

            • Future work.

            <size=120%><b>1.2.0 - 2026-11-12</b></size>

            • First change. Its continuation.
            • Second change.

            <size=120%><b>1.1.0 - 2026-11-11</b></size>

            Older change.
            """.Replace("\r\n", "\n"), ChangelogRenderer.Render(changelog));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EmptyUnreleasedStillDisplaysTheReleasedHistory(string newline)
    {
        string changelog = string.Join(newline, "## [Unreleased]", "", "## [1.2.0] - 2026-11-12", "Released.");
        Assert.Equal("<size=120%><b>[Unreleased]</b></size>\n\n<size=120%><b>[1.2.0] - 2026-11-12</b></size>\n\nReleased.",
            ChangelogRenderer.Render(changelog));
    }

    [Fact]
    public void FormatsHeadingsAndEmphasisAndKeepsOnlyLinkLabels()
    {
        const string changelog = """
            ## [Unreleased]
            ### Improvements
            - **Bold**, *italic*, and `code`.
            - [Details](https://example.com/details).
            - [Release][release], [release] and [release][].
            - [Unknown] stays readable.
            - __Bold with *nested italic*__ and [**formatted** label](https://example.com/a_(b)).

            [release]: https://example.com/release
            """;
        Assert.Equal("""
            <size=120%><b>[Unreleased]</b></size>

            <size=120%><b>Improvements</b></size>

            • <b>Bold</b>, <i>italic</i>, and code.
            • Details.
            • Release, release and release.
            • [Unknown] stays readable.
            • <b>Bold with <i>nested italic</i></b> and <b>formatted</b> label.
            """.Replace("\r\n", "\n"), ChangelogRenderer.Render(changelog));
    }

    [Fact]
    public void DisplaysLiteralMarkupWithoutInterpretingItAsTmpTags()
    {
        Assert.Equal("""
            <size=120%><b>[Unreleased]</b></size>

            • <noparse><</noparse>sprite name=secret> and <noparse><</noparse>/noparse>.
            • <noparse><</noparse>b>raw<noparse><</noparse>/b> and <noparse><</noparse>sprite>.

            <noparse><</noparse>/noparse><noparse><</noparse>sprite name=secret>
            """.Replace("\r\n", "\n"), ChangelogRenderer.Render("""
            ## [Unreleased]
            - `<sprite name=secret>` and </noparse>.
            - <b>raw</b> and &lt;sprite&gt;. <!-- hidden -->

            ```
            </noparse><sprite name=secret>
            ```
            """));
    }

    [Fact]
    public void SeparatesParagraphsAndHeadersWithoutSpreadingOutListItems()
    {
        const string changelog = """
            ## [Unreleased]
            First paragraph
            continued on another source line.


            Second paragraph.

            - First item
              continued here.
            - Second item.
            ### More changes
            Another paragraph.
            """;
        Assert.Equal("""
            <size=120%><b>[Unreleased]</b></size>

            First paragraph continued on another source line.

            Second paragraph.

            • First item continued here.
            • Second item.

            <size=120%><b>More changes</b></size>

            Another paragraph.
            """.Replace("\r\n", "\n"), ChangelogRenderer.Render(changelog));
    }

    [Fact]
    public void EmbedsRenderedChangelogIncludingItsReleaseHistory()
    {
        string rendered = ReleaseNotes.ReadChangelog();
        Assert.StartsWith("<size=120%><b>Unreleased</b></size>", rendered);
        Assert.Contains("Initial public release.", rendered);
        Assert.DoesNotContain("All notable changes", rendered);
        Assert.DoesNotContain("Keep a Changelog", rendered);
        Assert.DoesNotContain("https://", rendered);
        Assert.DoesNotContain(typeof(ReleaseNotes).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "Markdig" or "RenderChangelog");
#if DEBUG
        Assert.True(ReleaseNotes.IsDevelopment);
#else
        Assert.False(ReleaseNotes.IsDevelopment);
#endif
    }
}
