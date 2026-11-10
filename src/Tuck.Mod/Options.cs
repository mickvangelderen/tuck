using UnityEngine;

namespace Tuck.Mod;

/// <summary>All of the mod's settings, and their rows in the game's Options window.</summary>
/// <remarks>
/// Pass the whole object to whatever needs settings, rather than individual settings such as <see cref="Pack"/>, so
/// any class can start using another setting without changing its callers.
/// </remarks>
internal sealed class Options
{
    // Match vanilla's mod-source tooltip color while keeping a compact prefix for settings rows.
    internal static string FormatMenuLabel(ModManifest manifest, string label) =>
        $"{RenderHandler.ApplyRichTextColorTag($"[{manifest.Name}]", RenderHandler.ColorPalette.LightBlue)} {label}";

    public KeyBinding Pack { get; }
    public KeyBinding Scavenge { get; }
    public KeyBinding Return { get; }
    public KeyBinding Shutters { get; }
    public KeyBinding NightActivities { get; }
    public KeyBinding DragAllMatching { get; }
    public KeyBinding DragEntireInventory { get; }
    public KeyBinding DragPrecisePlacement { get; }
    public ScalarSetting ScavengingLeaveWarningMinimumValue { get; }
    public ToggleSetting CenterBackpackSelection { get; }
    public ToggleSetting AppraisalFiftyButtons { get; }
    public ToggleSetting ChemFinisherBatches { get; }
    public ToggleSetting ReplaceFilters { get; }
    public ToggleSetting DiscardAppraisalSlips { get; }
    public ToggleSetting DiscardEventReceipts { get; }
    public ScalarSetting WorkspaceOccupiedPercent { get; }
    public ScalarSetting ReturnHoldDuration { get; }
    public ScalarSetting BellHoldDuration { get; }

    public Options(ModManifest manifest)
    {
        Pack = new KeyBinding(manifest, "pack_key", "Pack", KeyCode.G);
        Scavenge = new KeyBinding(manifest, "scavenge_key", "Scavenge", KeyCode.Space);
        Return = new KeyBinding(manifest, "return_key", "Return from night activity (hold)", KeyCode.R);
        Shutters = new KeyBinding(manifest, "shutters_key", "Open/close shutters", KeyCode.C);
        NightActivities = new KeyBinding(manifest, "night_activities_key", "Open night activities", KeyCode.Q);
        DragAllMatching = new KeyBinding(manifest, "drag_all_matching_key", "Drag all matching items (hold)", KeyCode.LeftShift);
        DragEntireInventory = new KeyBinding(manifest, "drag_entire_inventory_key", "Drag entire inventory (hold)", KeyCode.LeftControl);
        DragPrecisePlacement = new KeyBinding(manifest, "drag_precise_placement_key", "Place dragged items in formation (hold)", KeyCode.LeftAlt);
        ScavengingLeaveWarningMinimumValue = new ScalarSetting(manifest,
            "scavenging_leave_warning_minimum_value", "Scavenging warning threshold",
            "Warn before returning from scavenging when items left behind are worth at least this many credits. Vanilla: 0. Disable: 1000.",
            defaultValue: 50f, min: 0f, max: 1000f, step: 10f,
            valueText: value => value >= 1000f ? "Disabled" : $"{value:0}{GeneralHelper.CreditIcon(spaced: true)}");
        CenterBackpackSelection = new ToggleSetting(manifest,
            "center_backpack_selection", "Center backpack selection menu",
            "Center the backpack selection menu when opening night activities.", defaultValue: false);
        AppraisalFiftyButtons = new ToggleSetting(manifest,
            "appraisal_fifty_buttons", "Appraisal ±50 buttons",
            "Arrange +/-5, 10, 50 and 100 appraisal buttons in two rows above the estimate and send button.", defaultValue: false);
        ChemFinisherBatches = new ToggleSetting(manifest,
            "chem_finisher_batches", "Chem finisher batch buttons",
            "Add 1, 5 and 20 batch buttons to the chem finisher. Turn off to restore its original controls.", defaultValue: false);
        ReplaceFilters = new ToggleSetting(manifest,
            "replace_filters", "Quick swap filters",
            "Quickly swap filters by dragging a new filter onto the installed filter in an open filter slot. The removed filter must fit in the dragged filter's original space.", defaultValue: true);
        DiscardAppraisalSlips = new ToggleSetting(manifest,
            "discard_appraisal_slips", "Auto-discard appraisal slips",
            "Move newly received appraisal slips to the trash.", defaultValue: false);
        DiscardEventReceipts = new ToggleSetting(manifest,
            "discard_event_receipts", "Auto-discard event receipts",
            "Move newly received event receipts to the trash.", defaultValue: false);
        // Keep the existing config key; its value now represents occupied space rather than clear space.
        WorkspaceOccupiedPercent = new ScalarSetting(manifest,
            "workspace_clear_percent", "Workspace warning threshold",
            "Warn before going outside when more than this percentage of the workspace is occupied. Vanilla: 0%. Disable: 100%.",
            defaultValue: 40f, min: 0f, max: 100f, step: 5f,
            valueText: value => value >= 100f ? "Disabled" : $"{value:0}%");
        ReturnHoldDuration = new ScalarSetting(manifest,
            "return_hold_duration", "Return hold duration",
            "How long to hold the return key before releasing it to leave a night activity. Set to 0.0 s to return on key press.",
            defaultValue: 0.4f, min: 0f, max: 1f, step: 0.1f, valueText: value => $"{value:0.0} s");
        BellHoldDuration = new ScalarSetting(manifest,
            "bell_hold_duration", "Bell hold duration",
            "How long to hold the bell button or key when the game's instant next customer setting is off and a customer is present. Set to 0.0 s for no hold.",
            defaultValue: 0.4f, min: 0f, max: 1f, step: 0.1f, valueText: value => $"{value:0.0} s");
    }

    public void Register()
    {
        // Without this, entries added before the options window first opens are placed above the vanilla ones.
        GameOptionRegistry.EnsureVanilla();
        Pack.Register();
        Scavenge.Register();
        Return.Register();
        Shutters.Register();
        NightActivities.Register();
        DragAllMatching.Register();
        DragEntireInventory.Register();
        DragPrecisePlacement.Register();
        ScavengingLeaveWarningMinimumValue.Register();
        CenterBackpackSelection.Register();
        AppraisalFiftyButtons.Register();
        ChemFinisherBatches.Register();
        ReplaceFilters.Register();
        DiscardAppraisalSlips.Register();
        DiscardEventReceipts.Register();
        WorkspaceOccupiedPercent.Register();
        ReturnHoldDuration.Register();
        BellHoldDuration.Register();
    }

    public void Unregister()
    {
        BellHoldDuration.Unregister();
        ReturnHoldDuration.Unregister();
        WorkspaceOccupiedPercent.Unregister();
        DiscardEventReceipts.Unregister();
        DiscardAppraisalSlips.Unregister();
        ReplaceFilters.Unregister();
        ChemFinisherBatches.Unregister();
        AppraisalFiftyButtons.Unregister();
        CenterBackpackSelection.Unregister();
        ScavengingLeaveWarningMinimumValue.Unregister();
        DragPrecisePlacement.Unregister();
        DragEntireInventory.Unregister();
        DragAllMatching.Unregister();
        NightActivities.Unregister();
        Shutters.Unregister();
        Return.Unregister();
        Scavenge.Unregister();
        Pack.Unregister();
    }
}
