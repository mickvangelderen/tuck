using System;
using System.IO;

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
}
