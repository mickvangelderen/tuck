using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tuck.Build;

internal static class ChangelogRenderer
{
    internal static string Render(string markdown) => Blocks(Markdown.Parse(markdown)
        .SkipWhile(block => block is not HeadingBlock { Level: 2 }));

    private static string Blocks(IEnumerable<Block> blocks) =>
        string.Join("\n\n", blocks.Select(Block).Where(text => text.Length != 0));

    private static string Block(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return "<size=120%><b>" + Inlines(heading.Inline!) + "</b></size>";
            case ParagraphBlock paragraph:
                return Inlines(paragraph.Inline!).TrimEnd();
            case ListBlock list:
                int number = list.IsOrdered ? int.Parse(list.OrderedStart!) : 0;
                return string.Join("\n", list.Select(item =>
                    (list.IsOrdered ? $"{number++}. " : "• ") + Block(item).Replace("\n", "\n  ")));
            case HtmlBlock { Type: HtmlBlockType.Comment }:
            case LinkReferenceDefinitionGroup:
            case LinkReferenceDefinition:
                return "";
            case CodeBlock code:
                return Literal(code.Lines.ToString());
            case HtmlBlock html:
                return Literal(html.Lines.ToString());
            case ThematicBreakBlock:
                return "—";
            case ContainerBlock container:
                return Blocks(container);
            default:
                throw new NotSupportedException($"Unsupported changelog block: {block.GetType().Name}");
        }
    }

    private static string Inlines(ContainerInline container) => string.Concat(container.Select(Inline));

    private static string Inline(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return Literal(literal.Content.ToString());
            case CodeInline code:
                return Literal(code.Content);
            case HtmlEntityInline entity:
                return Literal(entity.Transcoded.ToString());
            case HtmlInline html:
                return html.Tag.StartsWith("<!--", StringComparison.Ordinal) ? "" : Literal(html.Tag);
            case AutolinkInline link:
                return Literal(link.Url);
            case LineBreakInline lineBreak:
                return lineBreak.IsHard ? "\n" : " ";
            case EmphasisInline emphasis:
                string tag = emphasis.DelimiterCount == 2 ? "b" : "i";
                return $"<{tag}>" + Inlines(emphasis) + $"</{tag}>";
            // Links (and images) keep their labels, without URLs or TMP attributes.
            case ContainerInline container:
                return Inlines(container);
            default:
                throw new NotSupportedException($"Unsupported changelog inline: {inline.GetType().Name}");
        }
    }

    private static string Literal(string text) => text.Replace("<", "<noparse><</noparse>");
}
