#:package Markdig@0.41.3
#:property PublishAot=false

using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

// Run with: dotnet run --file tools/WorkshopDescription.cs -- README.md output.txt
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: WorkshopDescription.cs <markdown-file> <output-file>");
    return 1;
}

try
{
    var pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();
    var document = Markdown.Parse(File.ReadAllText(args[0]), pipeline);
    var description = RenderBlocks(document);
    if (string.IsNullOrWhiteSpace(description))
        throw new InvalidOperationException("The Workshop description is empty.");

    File.WriteAllText(args[1], description + "\n", new UTF8Encoding(false));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Workshop description: {error.Message}");
    return 1;
}

static string RenderBlocks(ContainerBlock blocks, string separator = "\n\n") =>
    string.Join(separator, blocks.Select(RenderBlock).Where(text => text.Length != 0));

static string RenderBlock(Block block) => block switch
{
    HeadingBlock heading => Wrap($"h{Math.Min(heading.Level, 3)}", RenderInlines(heading.Inline)),
    ParagraphBlock paragraph => RenderInlines(paragraph.Inline).Trim(),
    ListBlock list => Wrap(list.IsOrdered ? "olist" : "list",
        "\n" + string.Join("\n", list.Cast<ListItemBlock>()
            .Select(item => "[*]" + RenderBlocks(item, "\n"))) + "\n"),
    QuoteBlock quote => Wrap("quote", RenderBlocks(quote)),
    CodeBlock code => Wrap("code", code.Lines.ToString()),
    ThematicBreakBlock => "[hr][/hr]",
    LinkReferenceDefinitionGroup => "",
    // Fail visibly if the README adopts syntax this renderer cannot preserve.
    _ => throw new NotSupportedException($"Unsupported Markdown block {block.GetType().Name} on line {block.Line + 1}.")
};

static string RenderInlines(ContainerInline? container)
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
            // Image hosting is separate from publishing; omit images for now.
            LinkInline { IsImage: true } => "",
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
