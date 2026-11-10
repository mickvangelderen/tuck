using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

internal sealed class WorkshopDescription
{
    private readonly MarkdownDocument _document;
    private readonly string _directory;
    private IReadOnlyDictionary<string, string> _imageUrls = new Dictionary<string, string>();

    public WorkshopDescription(string path)
    {
        _directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UsePipeTables()
            .Build();
        _document = Markdown.Parse(File.ReadAllText(path), pipeline);
    }

    // Markdown parsing handles image references, links, escaped paths and code blocks.
    public string[] Images => _document.Descendants<LinkInline>().Where(link => link.IsImage)
        .Select(link => ImagePath(link.Url)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private string ImagePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new InvalidOperationException($"README images must be local files: {url}");
        return Path.GetFullPath(Uri.UnescapeDataString(url), _directory);
    }

    public string Render(IReadOnlyDictionary<string, string>? imageUrls = null)
    {
        _imageUrls = imageUrls ?? new Dictionary<string, string>();
        var description = RenderBlocks(_document);
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("The Workshop description is empty.");
        return description + "\n";
    }

    private string RenderBlocks(ContainerBlock blocks, string separator = "\n\n") =>
        string.Join(separator, blocks.Select(RenderBlock).Where(text => text.Length != 0));

    private string RenderBlock(Block block) => block switch
    {
        HeadingBlock heading => Wrap($"h{Math.Min(heading.Level, 3)}", RenderInlines(heading.Inline)),
        ParagraphBlock paragraph => RenderInlines(paragraph.Inline).Trim(),
        ListBlock list => Wrap(list.IsOrdered ? "olist" : "list",
            "\n" + string.Join("\n", list.Cast<ListItemBlock>()
                .Select(item => "[*]" + RenderBlocks(item, "\n"))) + "\n"),
        QuoteBlock quote => Wrap("quote", RenderBlocks(quote)),
        // Keep overview tables readable on Steam as labeled lists.
        Table table => Wrap("list", "\n" + string.Join("\n", table.Cast<TableRow>()
            .Where(row => !row.IsHeader)
            .Select(row => "[*]" + string.Join(": ", row.Cast<TableCell>()
                .Select((cell, index) => index == 0 ? Wrap("b", RenderBlocks(cell)) : RenderBlocks(cell))))) + "\n"),
        CodeBlock code => Wrap("code", code.Lines.ToString()),
        ThematicBreakBlock => "[hr][/hr]",
        LinkReferenceDefinitionGroup => "",
        // Fail visibly if the README adopts syntax this renderer cannot preserve.
        _ => throw new NotSupportedException($"Unsupported Markdown block {block.GetType().Name} on line {block.Line + 1}.")
    };

    private string RenderInlines(ContainerInline? container)
    {
        var text = new StringBuilder();
        for (var inline = container?.FirstChild; inline != null; inline = inline.NextSibling)
        {
            text.Append(inline switch
            {
                LiteralInline literal => literal.Content.ToString(),
                EmphasisInline emphasis => Wrap(
                    emphasis.DelimiterChar == '~' ? "strike" : emphasis.DelimiterCount == 2 ? "b" : "i",
                    RenderInlines(emphasis)),
                // Steam has no inline monospace tag. Preserve code as literal inline text.
                CodeInline code => Wrap("noparse", code.Content),
                LineBreakInline lineBreak => lineBreak.IsHard ? "\n" : " ",
                // An offline preparation has no URLs; publishing resolves every image first.
                LinkInline { IsImage: true } link => _imageUrls.Count == 0 ? ""
                    : Wrap("img", _imageUrls[ImagePath(link.Url)]),
                LinkInline link => RenderLink(link.Url, RenderInlines(link)),
                AutolinkInline link => RenderLink(link.Url, link.Url),
                HtmlEntityInline entity => entity.Transcoded.ToString(),
                _ => throw new NotSupportedException($"Unsupported Markdown inline {inline.GetType().Name}.")
            });
        }
        return text.ToString();
    }

    static string RenderLink(string? url, string label)
    {
        // Relative paths, anchors and file URLs cannot be followed from Steam.
        // Retain their labels as plain text rather than losing part of a sentence.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
            return label;

        return $"[url={url!.Replace("[", "%5B").Replace("]", "%5D")}]{label}[/url]";
    }

    static string Wrap(string tag, string text) => $"[{tag}]{text}[/{tag}]";
}
