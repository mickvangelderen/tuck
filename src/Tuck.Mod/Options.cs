using System;
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
    public PackingSolverSetting PackingSolver { get; }
    public KeyBinding Scavenge { get; }
    public KeyBinding Return { get; }
    public KeyBinding Shutters { get; }
    public KeyBinding NightActivities { get; }
    public KeyBinding DragAllMatching { get; }
    public KeyBinding DragEntireInventory { get; }
    public KeyBinding DragPrecisePlacement { get; }
    public ToggleSetting ShowHotkeysInTooltips { get; }
    public ScalarSetting ScavengingLeaveWarningMinimumValue { get; }
    public ToggleSetting CenterBackpackSelection { get; }
    public ToggleSetting OneClickNightActivities { get; }
    public ToggleSetting AppraisalFiftyButtons { get; }
    public ToggleSetting AutoOpenAppraisalGuides { get; }
    public ToggleSetting ChemFinisherBatches { get; }
    public ToggleSetting ReplaceFilters { get; }
    public ToggleSetting DiscardAppraisalSlips { get; }
    public ToggleSetting DiscardEventReceipts { get; }
    public ScalarSetting WorkspaceOccupiedPercent { get; }
    public ScalarSetting ReturnHoldDuration { get; }
    public ScalarSetting BellHoldDuration { get; }

    private readonly Func<string, object[], string> _localize;

    public string Text(string key, params object[] args) => _localize(key, args);

    public Options(ModManifest manifest)
        : this(manifest, (key, args) => ModHelper.GetLocalized(manifest.ID, key, args)) { }

    internal Options(ModManifest manifest, Func<string, object[], string> localize)
    {
        // Localization loads after Init/OnEnable; only resolve inside display callbacks.
        _localize = localize;
        Pack = new KeyBinding(manifest, "pack_key", () => Text("pack"), KeyCode.G);
        PackingSolver = new PackingSolverSetting(manifest, this);
        Scavenge = new KeyBinding(manifest, "scavenge_key", () => Text("scavenge"), KeyCode.Space);
        Return = new KeyBinding(manifest, "return_key", () => Text("return_from_night_activity_hold"), KeyCode.R);
        Shutters = new KeyBinding(manifest, "shutters_key", () => Text("open_close_shutters"), KeyCode.C);
        NightActivities = new KeyBinding(manifest, "night_activities_key", () => Text("open_night_activities"), KeyCode.Q);
        DragAllMatching = new KeyBinding(manifest, "drag_all_matching_key", () => Text("drag_all_matching_items_hold"), KeyCode.LeftShift);
        DragEntireInventory = new KeyBinding(manifest, "drag_entire_inventory_key", () => Text("drag_entire_inventory_hold"), KeyCode.LeftControl);
        DragPrecisePlacement = new KeyBinding(manifest, "drag_precise_placement_key", () => Text("place_dragged_items_in_formation_hold"), KeyCode.LeftAlt);
        ShowHotkeysInTooltips = new ToggleSetting(manifest,
            "show_hotkeys_in_tooltips", () => Text("show_hotkeys_in_tooltips"),
            () => Text("show_current_hotkeys_in_action_tooltips"), defaultValue: true);
        ScavengingLeaveWarningMinimumValue = new ScalarSetting(manifest,
            "scavenging_leave_warning_minimum_value", () => Text("scavenging_warning_threshold"),
            () => Text("warn_before_returning_when_items_left_behind_are_worth"),
            defaultValue: 50f, min: 0f, max: 1000f, step: 10f,
            valueText: value => value >= 1000f ? Text("disabled") : Text("x_credits", value, GeneralHelper.CreditIcon(spaced: true)));
        CenterBackpackSelection = new ToggleSetting(manifest,
            "center_backpack_selection", () => Text("center_backpack_selection_menu"),
            () => Text("center_the_backpack_selection_menu_when_opening_night_activities"), defaultValue: false);
        OneClickNightActivities = new ToggleSetting(manifest,
            "one_click_night_activities", () => Text("one_click_night_activities"),
            () => Text("travel_immediately_when_selecting_a_night_activity"), defaultValue: true);
        AppraisalFiftyButtons = new ToggleSetting(manifest,
            "appraisal_fifty_buttons", () => Text("appraisal_plus_minus_50_buttons"),
            () => Text("arrange_appraisal_buttons_in_two_rows_above_the_estimate_and_send_button"), defaultValue: false);
        AutoOpenAppraisalGuides = new ToggleSetting(manifest,
            "auto_open_appraisal_guides", () => Text("auto_open_appraisal_guides"),
            () => Text("open_owned_reference_documents_when_inspecting_an_item"), defaultValue: false);
        ChemFinisherBatches = new ToggleSetting(manifest,
            "chem_finisher_batches", () => Text("chem_finisher_batch_buttons"),
            () => Text("add_1_5_and_20_batch_buttons_to_the_chem_finisher"), defaultValue: false);
        ReplaceFilters = new ToggleSetting(manifest,
            "replace_filters", () => Text("quick_swap_filters"),
            () => Text("quickly_swap_filters_by_dragging_a_new_filter_onto_the_installed_filter"), defaultValue: true);
        DiscardAppraisalSlips = new ToggleSetting(manifest,
            "discard_appraisal_slips", () => Text("auto_discard_appraisal_slips"),
            () => Text("move_newly_received_appraisal_slips_to_the_trash"), defaultValue: false);
        DiscardEventReceipts = new ToggleSetting(manifest,
            "discard_event_receipts", () => Text("auto_discard_event_receipts"),
            () => Text("move_newly_received_event_receipts_to_the_trash"), defaultValue: false);
        // Keep the existing config key; its value now represents occupied space rather than clear space.
        WorkspaceOccupiedPercent = new ScalarSetting(manifest,
            "workspace_clear_percent", () => Text("workspace_warning_threshold"),
            () => Text("warn_before_going_outside_when_more_than_this_percentage_of_the_workspace_is_occupied"),
            defaultValue: 40f, min: 0f, max: 100f, step: 5f,
            valueText: value => value >= 100f ? Text("disabled") : Text("x_percent", value));
        ReturnHoldDuration = new ScalarSetting(manifest,
            "return_hold_duration", () => Text("return_hold_duration"),
            () => Text("how_long_to_hold_the_return_key"),
            defaultValue: 0.4f, min: 0f, max: 1f, step: 0.1f, valueText: value => Text("x_seconds", value));
        BellHoldDuration = new ScalarSetting(manifest,
            "bell_hold_duration", () => Text("bell_hold_duration"),
            () => Text("how_long_to_hold_the_bell_button_or_key"),
            defaultValue: 0.4f, min: 0f, max: 1f, step: 0.1f, valueText: value => Text("x_seconds", value));
    }

    public void Register()
    {
        // Without this, entries added before the options window first opens are placed above the vanilla ones.
        GameOptionRegistry.EnsureVanilla();
        Pack.Register();
        PackingSolver.Register();
        Scavenge.Register();
        Return.Register();
        Shutters.Register();
        NightActivities.Register();
        DragAllMatching.Register();
        DragEntireInventory.Register();
        DragPrecisePlacement.Register();
        ShowHotkeysInTooltips.Register();
        ScavengingLeaveWarningMinimumValue.Register();
        CenterBackpackSelection.Register();
        OneClickNightActivities.Register();
        AppraisalFiftyButtons.Register();
        AutoOpenAppraisalGuides.Register();
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
        AutoOpenAppraisalGuides.Unregister();
        AppraisalFiftyButtons.Unregister();
        OneClickNightActivities.Unregister();
        CenterBackpackSelection.Unregister();
        ScavengingLeaveWarningMinimumValue.Unregister();
        ShowHotkeysInTooltips.Unregister();
        DragPrecisePlacement.Unregister();
        DragEntireInventory.Unregister();
        DragAllMatching.Unregister();
        NightActivities.Unregister();
        Shutters.Unregister();
        Return.Unregister();
        Scavenge.Unregister();
        PackingSolver.Unregister();
        Pack.Unregister();
    }
}
