using Tuck.Mod.DebugRecording;

namespace Tuck.Mod;

public sealed partial class ModEntry
{
    partial void RegisterDebugRecorder() => PackRecorder.Register(_manifest.ID);
    partial void UnregisterDebugRecorder() => PackRecorder.Unregister();
}
