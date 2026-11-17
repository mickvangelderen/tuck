using System.Reflection;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class ReleaseNotesWindowTests
{
    [Fact]
    public void ReturningToTheMenuDoesNotAttachAnotherWindow()
    {
        var attempted = typeof(ReleaseNotesWindow).GetField("_attempted", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = attempted.GetValue(null);
        try
        {
            attempted.SetValue(null, true);
            Assert.Null(ReleaseNotesWindow.Attach(null!, new ModManifest { ID = "tuck-notes-test" }, null!));
        }
        finally
        {
            attempted.SetValue(null, previous);
        }
    }

}
