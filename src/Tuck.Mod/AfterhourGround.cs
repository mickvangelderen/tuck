namespace Tuck.Mod;

/// <summary>Owns night return controls, and Tuck and packing controls at supported locations.</summary>
internal sealed class AfterhourGround
{
    private readonly Options _options;
    private readonly string _modId;
    // Compared with Unity's null: Tuck is also destroyed if the game destroys the scene.
    private TuckMascot? _mascot;
    private AfterhourReturnControls? _returnControls;

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
        // Returning also works at locations without a Tuck sprite or a visible Ground window.
        var map = MapUIManager.Instance;
        if (_returnControls == null && map != null)
            _returnControls = AfterhourReturnControls.Install(map, _options);
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
        if (_returnControls != null) _returnControls.Uninstall();
        _returnControls = null;
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
