namespace Tuck.Mod;

public sealed partial class ModEntry : IMod
{
    // Assigned in Init. The loader calls Init and OnEnable once at launch, and only calls OnDisable if both succeeded.
    private Options _options = null!;
    private AfterhourGround _ground = null!;
    private ModManifest _manifest = null!;

    public void Init(ModManifest manifest)
    {
        _manifest = manifest;
        Log.Init(manifest);
        _options = new Options(manifest);
        _ground = new AfterhourGround(_options, manifest.ID);
        Log.Info($"Initialized v{manifest.ModVersion}.");
    }

    public void OnEnable()
    {
        _options.Register();
        _ground.Register();
        RegisterDebugRecorder();
        ScavengingReturn.Register(_options);
        WorkspaceWarning.Register(_options);
        BellHold.Register(_options);
        StoreHotkeys.Register(_options);
        HotkeyTooltips.Register(_options);
        BackpackSelection.Register(_options);
        DragAmounts.Register(_options);
        AppraisalSteps.Register(_options);
        DocumentDiscard.Register(_options);
        ChemFinisherBatch.Register(_options);
        FilterReplacement.Register(_options);
        Log.Info("Enabled.");
    }

    // In reverse order of registration.
    public void OnDisable()
    {
        FilterReplacement.Unregister();
        ChemFinisherBatch.Unregister();
        DocumentDiscard.Unregister();
        AppraisalSteps.Unregister();
        DragAmounts.Unregister();
        BackpackSelection.Unregister();
        HotkeyTooltips.Unregister();
        StoreHotkeys.Unregister();
        BellHold.Unregister();
        WorkspaceWarning.Unregister();
        ScavengingReturn.Unregister();
        UnregisterDebugRecorder();
        _ground.Unregister();
        _options.Unregister();
        Log.Info("Disabled.");
    }

    // Implemented only in DebugRecording; declarations and calls disappear from other builds.
    partial void RegisterDebugRecorder();
    partial void UnregisterDebugRecorder();
}
