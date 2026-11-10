namespace Tuck.Mod;

/// <summary>The mod's logger. <see cref="ModEntry.Init"/> sets it up before anything else runs.</summary>
internal static class Log
{
    private static ModLog _log = null!;

    public static void Init(ModManifest manifest) => _log = new ModLog(manifest);

    public static void Info(string message) => _log.Log(message);

    public static void Warning(string message) => _log.Warning(message);

    public static void Error(string message) => _log.Error(message);
}
