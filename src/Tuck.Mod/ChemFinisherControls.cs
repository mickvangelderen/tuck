using UnityEngine;

namespace Tuck.Mod;

internal sealed class ChemFinisherControls : MonoBehaviour
{
    private GameItem _machine = null!;
    private Options _options = null!;
    private GameSlotInventory _input = null!;
    private GameGridInventory _output = null!;
    private MachineryStack _stack = null!;
    private MachineryButton _original = null!;
    private MachineryText _status = null!;
    private ChemPrintQueue _queue = null!;
    private MachineryStack _batch = null!;
    private MachineryButton[] _buttons = null!;
    private ChemPlacementProbe? _probe;
    private string? _recipe;
    private float _nextRefresh;

    internal static ChemFinisherControls Install(GameItem machine, GameSlotInventory input,
        GameGridInventory output, MachineryStack stack, MachineryButton original, MachineryText status, Options options)
    {
        var controls = machine.contentWindow.handler.gameObject.AddComponent<ChemFinisherControls>();
        controls._machine = machine;
        controls._options = options;
        controls._input = input;
        controls._output = output;
        controls._stack = stack;
        controls._original = original;
        controls._status = status;
        controls._queue = new ChemPrintQueue(() => Time.realtimeSinceStartupAsDouble, controls.TryPrintOne);
        machine.onOneSecondItemFunc += controls.AfterStatusTick;
        controls._buttons = new MachineryButton[3];
        int[] counts = { 1, 5, 20 };
        for (int i = 0; i < counts.Length; i++)
        {
            int count = counts[i];
            controls._buttons[i] = new MachineryButton(count.ToString(), () => controls.Enqueue(count), width: 22);
        }
        controls._batch = MachineryStack.Horizontal(controls._buttons);
        original.Hide();
        stack.Add(controls._batch);
        controls.Refresh();
        return controls;
    }

    private void Update()
    {
        // Refresh a changed recipe before executing any queued work, including after reopening the window.
        if (_recipe != SelectedRecipe() || Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }
        if (!Interactable.isPaused && Time.timeScale > 0f && _queue.Tick()) Refresh();
    }

    // PixelWindow.Hide deactivates this GameObject. Closing discards pending work permanently.
    private void OnDisable() => _queue?.Cancel();
    private void OnEnable() => _nextRefresh = 0f;

    private string? SelectedRecipe() => _machine.IsTag(MachineChemFinisher.SELECTED_CHEM_ID_TAG)
        ? _machine.GetTagReadonly(MachineChemFinisher.SELECTED_CHEM_ID_TAG).GetString() : null;

    private void Refresh()
    {
        string? recipe = SelectedRecipe();
        if (recipe != _recipe)
        {
            _queue.Cancel();
            _recipe = recipe;
            string? sprite = recipe == null ? null : ChemPlacementProbe.SpriteFor(recipe);
            _probe = sprite == null ? null : new ChemPlacementProbe(recipe!,
                GameItem.BuildShapeFromSprite("Items/items_medical", sprite));
        }
        // Keep the manual vanilla action available for recipes from other mods that have no audited probe.
        _original.SetHidden(recipe == null || _probe != null);
        bool visible = _probe != null;
        if (_batch.handler.gameObject.activeSelf != visible)
        {
            _batch.handler.gameObject.SetActive(visible);
            _stack.Validate();
            _machine.contentWindow.Validate();
        }
        bool ready = _probe != null && CanOutput() && _probe.HasSpace(_output);
        foreach (var button in _buttons) button.SetDisabled(!ready);
        ShowStatus();
    }

    private bool CanOutput() => ChemFinisherBatch.IsEnabled && enabled && !_original.isDisabled && _recipe == SelectedRecipe()
        && MachineChemFinisher.CanOutput(_machine, _input);

    private void Enqueue(int count)
    {
        if (!ChemFinisherBatch.IsEnabled || !enabled) return;
        // Recheck the selection and live inventory even if the UI's last refresh said it was ready.
        Refresh();
        if (_probe == null || !CanOutput() || !_probe.HasSpace(_output)) return;
        _queue.Enqueue(count);
        ShowStatus();
    }

    private bool TryPrintOne() => _probe != null
        && ChemFinisherBatch.TryPrintOne(_probe, _output, CanOutput, PrintOne);

    private void AfterStatusTick(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        // Vanilla writes "Ready" every second. Overlay progress after it, without replacing its callback.
        if (isActiveAndEnabled && _queue.State == ChemPrintQueue.PrintState.Printing) ShowStatus();
    }

    private void ShowStatus()
    {
        string value;
        if (_queue.State == ChemPrintQueue.PrintState.Printing)
            value = _options.Text("printing_x_of_y", _queue.Completed, _queue.Total);
        else if (_recipe == null)
            value = LocHelper.GetLocalizedMechanic("mech_chem_finisher_status_no_target");
        else if (MachineChemFinisher.CanOutput(_machine, _input))
            value = LocHelper.GetLocalizedMechanic("mech_turbo_booster_status_ready");
        else if (_input.childItem != null && ChemFinisherRecipe.GetRecipeById(_recipe)?.isRequireDry == true
            && WaterHelper.IsWet(_input.childItem))
            value = LocHelper.GetLocalizedMechanic("mech_chem_finisher_status_wet_ingredient");
        else
            value = LocHelper.GetLocalizedMechanic("mech_chem_finisher_status_not_enough_reagent");

        string text = LocHelper.GetLocalizedMechanic("mech_turbo_booster_status", value);
        if (_status.GetText() != text) _status.SetText(text);
    }

    private bool PrintOne()
    {
        var source = _input.childItem;
        int before = WaterHelper.GetTotalVolume(source);
        // Keep the original callback intact: it owns creation, chemistry, placement, grading and achievements.
        // Select invokes it even though the original button is hidden behind the new quantity controls.
        return _original.Select() && WaterHelper.GetTotalVolume(source) < before;
    }

    internal void Uninstall()
    {
        _queue.Cancel();
        _machine.onOneSecondItemFunc -= AfterStatusTick;
        ShowStatus();
        enabled = false;
        _batch.handler.gameObject.SetActive(false);
        _stack.Detach(_batch);
        Destroy(_batch.handler.gameObject);
        _original.Show();
        _machine.contentWindow.Validate();
        ChemFinisherBatch.Forget(this);
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (_machine != null) _machine.onOneSecondItemFunc -= AfterStatusTick;
        ChemFinisherBatch.Forget(this);
    }
}
