using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tuck.Mod;

public sealed partial class ModEntry : IMod
{
    // Assigned in Init. The loader calls Init and OnEnable once at launch, and only calls OnDisable if both succeeded.
    private Options _options = null!;
    private AfterhourGround _ground = null!;
    private NightActivitySelection _nightActivitySelection = null!;
    private ModManifest _manifest = null!;
    private Harmony _harmony = null!;
    private bool _optionsPatched;
    private bool _gameplayRegistered;
    private ReleaseNotesWindow? _releaseNotes;

    public void Init(ModManifest manifest)
    {
        _manifest = manifest;
        _harmony = new Harmony("mod." + manifest.ID);
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
        HotkeyGlyphs.Register(_manifest.ID);
        ItemWarnings.Register(_options, _manifest.ID);
        SceneManager.sceneLoaded += OnSceneLoaded;
        Log.Info("Enabled.");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var menu = MainMenuUIController.Instance;
        bool isMenu = menu != null && menu.gameObject.scene == scene;
        if (!isMenu && scene.name != "InventoryScene") return;

        if (!_optionsPatched)
        {
            _harmony.CreateClassProcessor(typeof(OverlappingKeyBindings)).Patch();
            _optionsPatched = true;
        }
        if (isMenu)
        {
            _releaseNotes = ReleaseNotesWindow.Attach(menu!, _manifest, _options);
            return;
        }
        if (_gameplayRegistered) return;
        _gameplayRegistered = true;
        RegisterGameplay();
        GamePatches.Install(_harmony);
        Log.Info("Gameplay patches installed.");
    }

    private void RegisterGameplay()
    {
        Hotkeys.Register(_options, _ground);
        _ground.Register();
        RegisterDebugRecorder();
        ScavengingReturn.Register(_options);
        WorkspaceWarning.Register(_options);
        BellHold.Register(_options);
        VanillaHoldTimeouts.Register(_options);
        HotkeyTooltips.Register(_options);
        ItemWarningPreset.Register(_manifest);
        BackpackSelection.Register(_options);
        BackpackReturn.Register(_options);
        _nightActivitySelection.Register();
        DragAmounts.Register(_options);
        AppraisalSteps.Register(_options);
        AppraisalGuides.Register(_options);
        MagnifierPosition.Register(_options);
        DocumentDiscard.Register(_options);
        ChemFinisherBatch.Register(_options);
        FilterReplacement.Register(_options);
        LabelMemory.Register(_options);
        YeastBioreactor.Register(_options);
    }

    // In reverse order of registration.
    public void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_releaseNotes != null)
        {
            _releaseNotes.enabled = false;
            Object.Destroy(_releaseNotes);
            _releaseNotes = null;
        }
        if (_gameplayRegistered) UnregisterGameplay();
        _gameplayRegistered = false;
        ItemWarnings.Unregister();
        HotkeyGlyphs.Unregister();
        OverlappingKeyBindings.Unregister();
        _options.Unregister();
        _harmony.UnpatchAll(_harmony.Id);
        _optionsPatched = false;
        Log.Info("Disabled.");
    }

    private void UnregisterGameplay()
    {
        YeastBioreactor.Unregister();
        LabelMemory.Unregister();
        FilterReplacement.Unregister();
        ChemFinisherBatch.Unregister();
        DocumentDiscard.Unregister();
        MagnifierPosition.Unregister();
        AppraisalGuides.Unregister();
        AppraisalSteps.Unregister();
        DragAmounts.Unregister();
        _nightActivitySelection.Unregister();
        BackpackReturn.Unregister();
        BackpackSelection.Unregister();
        ItemWarningPreset.Unregister();
        HotkeyTooltips.Unregister();
        VanillaHoldTimeouts.Unregister();
        BellHold.Unregister();
        WorkspaceWarning.Unregister();
        ScavengingReturn.Unregister();
        UnregisterDebugRecorder();
        _ground.Unregister();
        Hotkeys.Unregister();
    }

    // Implemented only in DebugRecording; declarations and calls disappear from other builds.
    partial void RegisterDebugRecorder();
    partial void UnregisterDebugRecorder();
}
