using System;
using System.Linq;
using global::Tuck.PackSolver;
using global::Tuck.PackSolver.Solvers;

namespace Tuck.Mod;

/// <summary>A persisted solver choice with a localized dropdown in Options > Gameplay.</summary>
internal sealed class PackingSolverSetting
{
    private static readonly (string Id, string LabelKey, IPackSolver Solver)[] Choices =
    {
        ("Container first", "container_first", new ContainerFirstPackSolver()),
        ("Relaxed A*", "relaxed_a_star", new RelaxedAStarPackSolver()),
        ("Anytime", "anytime", new AnytimePackSolver()),
        ("Baseline", "baseline", new BaselinePackSolver())
    };

    private readonly ConfigEntry<string> _setting;
    private readonly Func<string> _menuLabel;
    private readonly Options _options;
    private int Index => Math.Max(0, Array.FindIndex(Choices, choice => choice.Id == _setting.Value));
    public IPackSolver Value => Choices[Index].Solver;

    public PackingSolverSetting(ModManifest manifest, Options options)
    {
        _options = options;
        _setting = ModHelper.GetConfig(manifest.ID).Define("packing_solver", Choices[0].Id, "Gameplay",
            () => options.Text("packing_solver"), Choices.Select(choice => choice.Id).ToArray(),
            () => options.Text("choose_the_solver_for_the_next_pack"));
        _menuLabel = () => Options.FormatMenuLabel(manifest, _setting.Label());
    }

    public void Register() => ModHelper.AddGameOptionDropdown(
        _setting.Config.ModId, ModHelper.OPTION_SECTION_GAMEPLAY, _setting.Key, _menuLabel,
        () => Choices.Select(choice => _options.Text(choice.LabelKey)).ToArray(), () => Index,
        index => _setting.Set(Choices[index >= 0 && index < Choices.Length ? index : 0].Id), _setting.Tooltip);

    public void Unregister() => ModHelper.RemoveGameOption(_setting.Config.ModId, _setting.Key);
}
