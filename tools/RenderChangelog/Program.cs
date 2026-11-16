using System.Text;
using Tuck.Build;

if (args.Length != 2)
    throw new ArgumentException("Usage: RenderChangelog <CHANGELOG.md> <output.txt>");

File.WriteAllText(args[1], ChangelogRenderer.Render(File.ReadAllText(args[0])), new UTF8Encoding(false));
