namespace Tuck.Mod;

public sealed partial class ModEntry : IMod
{
    // Assigned in Init. The loader calls Init and OnEnable once at launch, and only calls OnDisable if both succeeded.
    private Options _options = null!;
    private AfterhourGround _ground = null!;
    private NightActivitySelection _nightActivitySelection = null!;
    private ModManifest _manifest = null!;

    public void Init(ModManifest manifest)
    {
        _manifest = manifest;
        Log.Init(manifest);
        _options = new Options(manifest);
        _ground = new AfterhourGround(_options, manifest.ID);
        _nightActivitySelection = new NightActivitySelection(_options);
        Log.Info($"Initialized v{manifest.ModVersion}.");
    }

    public void OnEnable()
    {
        _options.Register();
        OverlappingKeyBindings.Register(_options);
        Hotkeys.Register(_options, _ground);
        _ground.Register();
        RegisterDebugRecorder();
        ScavengingReturn.Register(_options);
        WorkspaceWarning.Register(_options);
        BellHold.Register(_options);
        VanillaHoldTimeouts.Register(_options);
        HotkeyGlyphs.Register(_manifest.ID);
        HotkeyTooltips.Register(_options);
        ItemWarnings.Register(_options, _manifest.ID);
        ItemWarningPreset.Register(_manifest);
        BackpackSelection.Register(_options);
        _nightActivitySelection.Register();
        DragAmounts.Register(_options);
        AppraisalSteps.Register(_options);
        AppraisalGuides.Register(_options);
        DocumentDiscard.Register(_options);
        ChemFinisherBatch.Register(_options);
        FilterReplacement.Register(_options);
        LabelMemory.Register(_options);
        YeastBioreactor.Register(_options);
        ReleaseNotesWindow.Register(_manifest, _options);
        Log.Info("Enabled.");
    }

    // In reverse order of registration.
    public void OnDisable()
    {
        ReleaseNotesWindow.Unregister();
        YeastBioreactor.Unregister();
        LabelMemory.Unregister();
        FilterReplacement.Unregister();
        ChemFinisherBatch.Unregister();
        DocumentDiscard.Unregister();
        AppraisalGuides.Unregister();
        AppraisalSteps.Unregister();
        DragAmounts.Unregister();
        _nightActivitySelection.Unregister();
        BackpackSelection.Unregister();
        ItemWarningPreset.Unregister();
        ItemWarnings.Unregister();
        HotkeyTooltips.Unregister();
        HotkeyGlyphs.Unregister();
        VanillaHoldTimeouts.Unregister();
        BellHold.Unregister();
        WorkspaceWarning.Unregister();
        ScavengingReturn.Unregister();
        UnregisterDebugRecorder();
        _ground.Unregister();
        Hotkeys.Unregister();
        OverlappingKeyBindings.Unregister();
        _options.Unregister();
        Log.Info("Disabled.");
    }

    // Implemented only in DebugRecording; declarations and calls disappear from other builds.
    partial void RegisterDebugRecorder();
    partial void UnregisterDebugRecorder();
}
