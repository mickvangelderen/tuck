using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Repeats the finisher's original single-output action, checking space before each click.</summary>
[HarmonyPatch(typeof(MachineChemFinisher), nameof(MachineChemFinisher.ChemFinisher))]
internal static class ChemFinisherBatch
{
    private static Options? _options;
    private static readonly List<ChemFinisherControls> Controls = new();

    internal static bool IsEnabled => _options?.ChemFinisherBatches.Value == true;

    public static void Register(Options options)
    {
        _options = options;
        options.ChemFinisherBatches.OnChanged += OnChanged;
        OnChanged(options.ChemFinisherBatches.Value);
    }

    public static void Unregister()
    {
        if (_options != null) _options.ChemFinisherBatches.OnChanged -= OnChanged;
        _options = null;
        RemoveControls();
    }

    private static void OnChanged(bool enabled)
    {
        if (!enabled)
        {
            RemoveControls();
            return;
        }
        // Re-enable existing machines as well as newly constructed ones, without a restart.
        if (EmporiumEntry.Instance == null) return;
        foreach (var item in EmporiumEntry.Instance.GetAllItems())
            if (item.identifier == "chem_finisher") Install(item);
    }

    private static void RemoveControls()
    {
        foreach (var controls in Controls.ToArray())
            if (controls != null) controls.Uninstall();
        Controls.Clear();
    }

    internal static void Forget(ChemFinisherControls controls) => Controls.Remove(controls);

    internal static void Postfix(GameItem __result)
    {
        // The loader owns this passive Harmony postfix. Disabled means no UI access or handlers installed.
        if (!IsEnabled) return;
        Install(__result);
    }

    private static void Install(GameItem machine)
    {
        if (machine.contentWindow?.childElement is not MachineryPanelRow panels
            || MachineHelper.GetBodyStorage(machine, 1) is not GameSlotInventory input
            || MachineHelper.GetBodyStorage(machine, 2) is not GameGridInventory output) return;

        foreach (var panel in panels.Panels)
        {
            if (panel.role != "control" || panel.content is not MachineryStack stack
                || stack.elements.Count != 4 || stack.elements[2] is not MachineryText status
                || stack.elements[3] is not MachineryButton original
                || original.isHidden) continue;
            Controls.Add(ChemFinisherControls.Install(machine, input, output, stack, original, status, _options!));
            break;
        }
    }

    internal static bool TryPrintOne(ChemPlacementProbe probe, GameGridInventory output,
        Func<bool> canOutput, Func<bool> printOne)
    {
        // No placement call receives the probe. Vanilla creates and places a fresh real product per click.
        return canOutput() && probe.HasSpace(output) && printOne();
    }
}

/// <summary>A non-rendered geometry probe. Never a product, never inserted into an inventory.</summary>
internal sealed class ChemPlacementProbe : GameItem
{
    // Audited vanilla finisher outputs: fixed sprite shapes, no stacking or item-side placement callbacks.
    // Unknown recipes are deliberately unsupported. Do not instantiate product factories for previews:
    // those factories roll random purity, allocate renderers and (via Item) issue real item IDs.
    internal static string? SpriteFor(string recipe) => recipe switch
    {
        "dream_dust" => "dream_dust",
        "nightmare_dust" => "nightmare_dust",
        "caffeine_pill" => "cafeine_pill",
        "zyanide_pill" => "items_medical_12",
        "birth_control_pill" => "items_medical_15",
        "fanta_pill" => "oxycodone_pill",
        "pink_alt_injector" => "injector_pink_alt",
        _ => null
    };

    internal ChemPlacementProbe(string recipe, GridShape productShape) : base(recipe)
    {
        shape = productShape.Clone();
        modifiedShape = shape.Clone();
        state.EnableTag("IS_OWNED_TAG");
        modifiedState = state.Clone();
    }

    internal bool HasSpace(GameGridInventory output)
    {
        // Only prove direct tray placement. A shape-only probe cannot establish chemistry-dependent
        // acceptance into nested containers or modded stacks. Vanilla finisher products do not stack.
        var candidate = new GridShapeBuilder(modifiedShape);
        for (int flip = 0; flip < 2; flip++)
            for (int rotation = 0; rotation < 4; rotation++)
                for (int y = 0; y < output.inventoryShape.height; y++)
                    for (int x = 0; x < output.inventoryShape.width; x++)
                    {
                        candidate.SetTransform(x, y, flip != 0, rotation);
                        var slot = output.TryInventorySlot(this, 1, candidate);
                        if (slot.numTransfer >= 1) return true;
                    }
        return false;
    }

    public override void Validate() { }
    public override void Destroy() => throw new InvalidOperationException("A placement probe is not a game product.");
}
