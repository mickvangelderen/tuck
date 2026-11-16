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
    public KeyBinding Sleep { get; }
    public KeyBinding Tablet { get; }
    public KeyBinding Accept { get; }
    public KeyBinding Decline { get; }
    public KeyBinding DragAllMatching { get; }
    public KeyBinding DragEntireInventory { get; }
    public KeyBinding DragPrecisePlacement { get; }
    public ToggleSetting AllowOverlappingKeyBindings { get; }
    public ToggleSetting ShowHotkeysInTooltips { get; }
    public StolenWarningSetting StolenWarnings { get; }
    public ToggleSetting LowBatteryWarnings { get; }
    public ToggleSetting MissingIngredientsWarnings { get; }
    public ToggleSetting MissingWaterWarnings { get; }
    public ToggleSetting SpillWarnings { get; }
    public ScalarSetting OutputOccupiedPercent { get; }
    public ToggleSetting FilterDurabilityWarnings { get; }
    public ScalarSetting ScavengingLeaveWarningMinimumValue { get; }
    public ToggleSetting CenterBackpackSelection { get; }
    public ToggleSetting ReturnBackpack { get; }
    public ToggleSetting OneClickNightActivities { get; }
    public ToggleSetting AppraisalFiftyButtons { get; }
    public ToggleSetting AutoOpenAppraisalGuides { get; }
    public ToggleSetting ChemFinisherBatches { get; }
    public ToggleSetting ReplaceFilters { get; }
    public ToggleSetting RememberLabels { get; }
    public ToggleSetting YeastBioreactorTool { get; }
    public ToggleSetting DiscardAppraisalSlips { get; }
    public ToggleSetting DiscardEventReceipts { get; }
    public ScalarSetting WorkspaceOccupiedPercent { get; }
    public ScalarSetting ReturnHoldDuration { get; }
    public ScalarSetting BellHoldDuration { get; }
    public ScalarSetting AcceptHoldDuration { get; }
    public ScalarSetting DeclineHoldDuration { get; }
    public ScalarSetting HoldTimeoutDuration { get; }

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
        Sleep = new KeyBinding(manifest, "sleep_key", () => Text("sleep"), KeyCode.E);
        Tablet = new KeyBinding(manifest, "tablet_key", () => Text("open_close_shop_tablet"), KeyCode.T);
        Accept = new KeyBinding(manifest, "accept_key", () => Text("accept"), KeyCode.A);
        Decline = new KeyBinding(manifest, "decline_key", () => Text("decline"), KeyCode.D);
        DragAllMatching = new KeyBinding(manifest, "drag_all_matching_key", () => Text("drag_all_matching_items_hold"), KeyCode.LeftAlt);
        DragEntireInventory = new KeyBinding(manifest, "drag_entire_inventory_key", () => Text("drag_entire_inventory_hold"), KeyCode.LeftControl);
        DragPrecisePlacement = new KeyBinding(manifest, "drag_precise_placement_key", () => Text("place_dragged_items_in_formation_hold"), KeyCode.LeftShift,
            () => Text("key_binding_for_the_base_games_formation_placement"));
        AllowOverlappingKeyBindings = new ToggleSetting(manifest,
            "allow_overlapping_keybindings", () => Text("allow_overlapping_keybindings"),
            () => Text("allow_multiple_actions_to_share_the_same_key"), defaultValue: true);
        ShowHotkeysInTooltips = new ToggleSetting(manifest,
            "show_hotkeys_in_tooltips", () => Text("show_hotkeys_in_tooltips"),
            () => Text("show_current_hotkeys_in_action_tooltips"), defaultValue: true);
        StolenWarnings = new StolenWarningSetting(manifest, this);
        LowBatteryWarnings = new ToggleSetting(manifest,
            "low_battery_warnings", () => Text("low_battery_warnings"),
            () => Text("warn_when_a_machine_cannot_power_the_next_night"), defaultValue: true);
        MissingIngredientsWarnings = new ToggleSetting(manifest,
            "missing_ingredients_warnings", () => Text("missing_ingredients_warnings"),
            () => Text("warn_when_a_machine_is_missing_production_inputs"), defaultValue: true);
        MissingWaterWarnings = new ToggleSetting(manifest,
            "missing_water_warnings", () => Text("missing_water_warnings"),
            () => Text("warn_when_a_machine_has_insufficient_water"), defaultValue: true);
        SpillWarnings = new ToggleSetting(manifest,
            "spill_warnings", () => Text("overflow_warning"),
            () => Text("warn_when_machine_outputs_reach_the_occupied_threshold"), defaultValue: true);
        OutputOccupiedPercent = new ScalarSetting(manifest,
            "output_occupied_percent", () => Text("overflow_warning_threshold"),
            () => Text("output_fullness_threshold_zero_means_any_contents"),
            defaultValue: 50f, min: 0f, max: 100f, step: 5f, valueText: value => Text("x_percent", value));
        FilterDurabilityWarnings = new ToggleSetting(manifest,
            "filter_durability_warnings", () => Text("filter_durability_warnings"),
            () => Text("warn_when_all_installed_filters_are_depleted"), defaultValue: true);
        ScavengingLeaveWarningMinimumValue = new ScalarSetting(manifest,
            "scavenging_leave_warning_minimum_value", () => Text("scavenging_warning_threshold"),
            () => Text("warn_before_returning_when_items_left_behind_are_worth"),
            defaultValue: 50f, min: 0f, max: 1000f, step: 10f,
            valueText: value => value >= 1000f ? Text("disabled") : Text("x_credits", value, GeneralHelper.CreditIcon(spaced: true)));
        CenterBackpackSelection = new ToggleSetting(manifest,
            "center_backpack_selection", () => Text("center_backpack_selection_menu"),
            () => Text("center_the_backpack_selection_menu_when_opening_night_activities"), defaultValue: true);
        ReturnBackpack = new ToggleSetting(manifest,
            "return_backpack", () => Text("return_backpack_to_its_previous_place"),
            () => Text("restore_backpack_position_and_rotation_after_night_activities_when_space_is_available"), defaultValue: true);
        OneClickNightActivities = new ToggleSetting(manifest,
            "one_click_night_activities", () => Text("one_click_night_activities"),
            () => Text("travel_immediately_when_selecting_a_night_activity"), defaultValue: true);
        AppraisalFiftyButtons = new ToggleSetting(manifest,
            "appraisal_fifty_buttons", () => Text("appraisal_plus_minus_50_buttons"),
            () => Text("arrange_appraisal_buttons_in_two_rows_above_the_estimate_and_send_button"), defaultValue: true);
        AutoOpenAppraisalGuides = new ToggleSetting(manifest,
            "auto_open_appraisal_guides", () => Text("auto_open_appraisal_guides"),
            () => Text("open_owned_reference_documents_when_inspecting_an_item"), defaultValue: true);
        ChemFinisherBatches = new ToggleSetting(manifest,
            "chem_finisher_batches", () => Text("chem_finisher_batch_buttons"),
            () => Text("add_1_5_and_20_batch_buttons_to_the_chem_finisher"), defaultValue: true);
        ReplaceFilters = new ToggleSetting(manifest,
            "replace_filters", () => Text("quick_swap_filters"),
            () => Text("quickly_swap_filters_by_dragging_a_new_filter_onto_the_installed_filter"), defaultValue: true);
        RememberLabels = new ToggleSetting(manifest,
            "remember_labels", () => Text("remember_labeler_edits"),
            () => Text("remember_last_label_for_reuse"), defaultValue: true);
        YeastBioreactorTool = new ToggleSetting(manifest,
            "yeast_bioreactor_tool", () => Text("yeast_bioreactor_tool"),
            () => Text("use_the_equipped_yeast_bioreactor_to_start_fermentation"), defaultValue: true);
        DiscardAppraisalSlips = new ToggleSetting(manifest,
            "discard_appraisal_slips", () => Text("auto_discard_appraisal_slips"),
            () => Text("move_newly_received_appraisal_slips_to_the_trash"), defaultValue: true);
        DiscardEventReceipts = new ToggleSetting(manifest,
            "discard_event_receipts", () => Text("auto_discard_event_receipts"),
            () => Text("move_newly_received_event_receipts_to_the_trash"), defaultValue: true);
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
        AcceptHoldDuration = new ScalarSetting(manifest,
            "accept_hold_duration", () => Text("accept_hold_duration"),
            () => Text("how_long_to_hold_accept"),
            defaultValue: 0.4f, min: 0f, max: 1f, step: 0.1f, valueText: value => Text("x_seconds", value));
        DeclineHoldDuration = new ScalarSetting(manifest,
            "decline_hold_duration", () => Text("decline_hold_duration"),
            () => Text("how_long_to_hold_decline"),
            defaultValue: 0.4f, min: 0f, max: 1f, step: 0.1f, valueText: value => Text("x_seconds", value));
        HoldTimeoutDuration = new ScalarSetting(manifest,
            "hold_timeout_duration", () => Text("hold_timeout"),
            () => Text("holds_are_cancelled_after_becoming_ready"),
            defaultValue: 1.5f, min: 0.5f, max: HoldTimeout.DisabledAt, step: 0.1f,
            valueText: value => HoldTimeout.GetSeconds(value).HasValue ? Text("x_seconds", value) : Text("disabled"));
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
        Sleep.Register();
        Tablet.Register();
        Accept.Register();
        Decline.Register();
        DragAllMatching.Register();
        DragEntireInventory.Register();
        DragPrecisePlacement.Register();
        AllowOverlappingKeyBindings.Register();
        ShowHotkeysInTooltips.Register();
        StolenWarnings.Register();
        LowBatteryWarnings.Register();
        MissingIngredientsWarnings.Register();
        MissingWaterWarnings.Register();
        SpillWarnings.Register();
        OutputOccupiedPercent.Register();
        FilterDurabilityWarnings.Register();
        ScavengingLeaveWarningMinimumValue.Register();
        CenterBackpackSelection.Register();
        ReturnBackpack.Register();
        OneClickNightActivities.Register();
        AppraisalFiftyButtons.Register();
        AutoOpenAppraisalGuides.Register();
        ChemFinisherBatches.Register();
        ReplaceFilters.Register();
        RememberLabels.Register();
        YeastBioreactorTool.Register();
        DiscardAppraisalSlips.Register();
        DiscardEventReceipts.Register();
        WorkspaceOccupiedPercent.Register();
        ReturnHoldDuration.Register();
        BellHoldDuration.Register();
        AcceptHoldDuration.Register();
        DeclineHoldDuration.Register();
        HoldTimeoutDuration.Register();
    }

    public void Unregister()
    {
        HoldTimeoutDuration.Unregister();
        DeclineHoldDuration.Unregister();
        AcceptHoldDuration.Unregister();
        BellHoldDuration.Unregister();
        ReturnHoldDuration.Unregister();
        WorkspaceOccupiedPercent.Unregister();
        DiscardEventReceipts.Unregister();
        DiscardAppraisalSlips.Unregister();
        YeastBioreactorTool.Unregister();
        RememberLabels.Unregister();
        ReplaceFilters.Unregister();
        ChemFinisherBatches.Unregister();
        AutoOpenAppraisalGuides.Unregister();
        AppraisalFiftyButtons.Unregister();
        OneClickNightActivities.Unregister();
        ReturnBackpack.Unregister();
        CenterBackpackSelection.Unregister();
        ScavengingLeaveWarningMinimumValue.Unregister();
        FilterDurabilityWarnings.Unregister();
        OutputOccupiedPercent.Unregister();
        SpillWarnings.Unregister();
        MissingWaterWarnings.Unregister();
        MissingIngredientsWarnings.Unregister();
        LowBatteryWarnings.Unregister();
        StolenWarnings.Unregister();
        ShowHotkeysInTooltips.Unregister();
        AllowOverlappingKeyBindings.Unregister();
        DragPrecisePlacement.Unregister();
        DragEntireInventory.Unregister();
        DragAllMatching.Unregister();
        Decline.Unregister();
        Accept.Unregister();
        Tablet.Unregister();
        Sleep.Unregister();
        NightActivities.Unregister();
        Shutters.Unregister();
        Return.Unregister();
        Scavenge.Unregister();
        PackingSolver.Unregister();
        Pack.Unregister();
    }
}
