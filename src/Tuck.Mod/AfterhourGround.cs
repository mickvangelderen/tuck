namespace Tuck.Mod;

/// <summary>Owns Tuck and packing controls at supported locations.</summary>
internal sealed class AfterhourGround
{
    private readonly Options _options;
    private readonly string _modId;
    // Compared with Unity's null: Tuck is also destroyed if the game destroys the scene.
    private TuckMascot? _mascot;

    internal AfterhourGroundControls? Controls => _mascot != null ? _mascot.Controls : null;

    public AfterhourGround(Options options, string modId)
    {
        _options = options;
        _modId = modId;
    }

    public void Register()
    {
        ModHook.OnAfterhourArriveLate += OnAfterhourArriveLate;
        ModHook.OnAfterhourLeaveEarly += OnAfterhourLeaveEarly;
    }

    public void Unregister()
    {
        ModHook.OnAfterhourArriveLate -= OnAfterhourArriveLate;
        ModHook.OnAfterhourLeaveEarly -= OnAfterhourLeaveEarly;
        Uninstall();
    }

    // Late: Current, the stage and the vanilla window layout are now ready.
    private void OnAfterhourArriveLate(string locationId)
    {
        if (Install())
        {
            Log.Info($"Added Tuck at '{locationId}'.");
        }
    }

    // Stop input before the game transfers inventory on the way out.
    private void OnAfterhourLeaveEarly(string locationId)
    {
        if (Uninstall())
        {
            Log.Info($"Removed Tuck on leaving '{locationId}'.");
        }
    }

    private bool Install()
    {
        if (_mascot != null)
        {
            return false;
        }
        var window = EmporiumEntry.Instance != null ? EmporiumEntry.Instance.afterhourWindow : null;
        if (window == null || window.handler == null)
        {
            return false;
        }
        var location = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
        if (location == null) return false;
        _mascot = TuckMascot.Install(location, window, _options, _modId);
        return _mascot != null;
    }

    private bool Uninstall()
    {
        var mascot = _mascot;
        _mascot = null;
        if (mascot == null)
        {
            return false;
        }
        mascot.Uninstall();
        return true;
    }
}
