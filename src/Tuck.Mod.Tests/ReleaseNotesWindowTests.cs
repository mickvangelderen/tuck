using System.Collections;
using System.Reflection;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class ReleaseNotesWindowTests
{
    [Fact]
    public void ReturningToTheMenuDoesNotScheduleAnotherWindow()
    {
        var attempted = typeof(ReleaseNotesWindow).GetField("_attempted", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = attempted.GetValue(null);
        try
        {
            ReleaseNotesWindow.Register(new ModManifest { ID = "tuck-notes-test" }, null!);
            attempted.SetValue(null, true);
            IEnumerator startup = Array.Empty<object>().GetEnumerator();
            IEnumerator result = startup;
            ReleaseNotesWindow.AfterMenuStart(null!, ref result);
            Assert.Same(startup, result);
        }
        finally
        {
            ReleaseNotesWindow.Unregister();
            attempted.SetValue(null, previous);
        }
    }

    [Fact]
    public void WaitsForVanillaStartupAndOurAssetsAndStopsWhenDisabled()
    {
        var hook = typeof(ModHook).GetField(nameof(ModHook.OnModAssetsLoaded), BindingFlags.Static | BindingFlags.NonPublic)!;
        var ready = typeof(ReleaseNotesWindow).GetField("_assetsLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousHandlers = hook.GetValue(null);
        IEnumerator startup = Array.Empty<object>().GetEnumerator();
        IEnumerator pending = startup;
        try
        {
            ReleaseNotesWindow.Register(new ModManifest { ID = "tuck-notes-test", ModVersion = "1.2.0" }, null!);
            ReleaseNotesWindow.AfterMenuStart(null!, ref pending);
            Assert.NotSame(startup, pending);
            Assert.True(pending.MoveNext());
            Assert.Same(startup, pending.Current);
            Assert.True(pending.MoveNext());
            Assert.Null(pending.Current); // Waiting for assets; no attempt to use the absent Unity UI.

            var handlers = (Action<LoadedMod>)hook.GetValue(null)!;
            var handler = (Action<LoadedMod>)Assert.Single(handlers.GetInvocationList(),
                h => h.Method.DeclaringType == typeof(ReleaseNotesWindow));
            handler(new LoadedMod { Manifest = new ModManifest { ID = "another-mod" } });
            Assert.False((bool)ready.GetValue(null)!);
            handler(new LoadedMod { Manifest = new ModManifest { ID = "tuck-notes-test" } });
            Assert.True((bool)ready.GetValue(null)!);
        }
        finally
        {
            ReleaseNotesWindow.Unregister();
        }
        Assert.False(pending.MoveNext());
        Assert.False((bool)ready.GetValue(null)!);
        Assert.Equal(previousHandlers, hook.GetValue(null));
        pending = startup;
        ReleaseNotesWindow.AfterMenuStart(null!, ref pending);
        Assert.Same(startup, pending);
    }
}
