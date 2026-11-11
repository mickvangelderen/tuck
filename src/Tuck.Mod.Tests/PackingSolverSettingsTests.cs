using System.Reflection;
using global::Tuck.PackSolver.Solvers;
using Tuck.Mod.Packing;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class PackingSolverSettingsTests : IDisposable
{
    private readonly string _id = "tuck-solver-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private readonly ConfigEntry<string> _entry;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    public PackingSolverSettingsTests()
    {
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_id], culture: null)!;
        Configs.Add(_id, _config);
        var english = LocalizationTests.ReadEnglish();
        _options = new Options(new ModManifest { ID = _id, Name = "Tuck" }, (key, args) => string.Format(english[key], args));
        _options.PackingSolver.Register();
        _entry = (ConfigEntry<string>)_config.Get("packing_solver");
        // Exercise the real option/config callbacks without Unity's file-persistence path.
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(_entry, null);
    }

    [Fact]
    public void DropdownChangesTheExistingEngineSolverWithoutRestarting()
    {
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "packing_solver"));
        var engine = new PackEngine(_options);
        Assert.Equal(GameOptionRegistry.Kind.Dropdown, row.kind);
        Assert.False(_entry.RequiresRestart);
        Assert.Equal("Container first", _entry.Default);
        Assert.IsType<ContainerFirstPackSolver>(engine.Solver);
        Type[] types = [typeof(ContainerFirstPackSolver), typeof(RelaxedAStarPackSolver), typeof(AnytimePackSolver), typeof(BaselinePackSolver)];
        string[] names = ["Container first", "Relaxed A*", "Anytime", "Baseline"];
        Assert.Equal(names, row.options());
        Assert.Equal(names, _entry.Choices);
        for (int index = 0; index < types.Length; index++)
        {
            row.setIndex(index);
            Assert.Equal(index, row.getIndex());
            Assert.Equal(types[index], engine.Solver.GetType());
            Assert.Equal(names[index], _entry.Serialize());
        }
        _entry.Reset();
        Assert.Equal(0, row.getIndex());
        Assert.IsType<ContainerFirstPackSolver>(engine.Solver);
    }

    [Fact]
    public void ConfigChangesAndSavedNamesSelectTheSameSolver()
    {
        var engine = new PackEngine(_options);
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "packing_solver"));
        _entry.Set("Relaxed A*");
        Assert.Equal(1, row.getIndex());
        Assert.IsType<RelaxedAStarPackSolver>(engine.Solver);
        string saved = _entry.Serialize();
        _entry.Reset();
        var deserialize = typeof(ConfigEntry<string>).GetMethod("TryDeserialize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)deserialize.Invoke(_entry, [saved])!);
        Assert.Equal(1, row.getIndex());
        Assert.IsType<RelaxedAStarPackSolver>(engine.Solver);
        _entry.Set("unknown solver");
        Assert.Equal(0, row.getIndex());
        Assert.IsType<ContainerFirstPackSolver>(engine.Solver);
    }

    public void Dispose()
    {
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(_entry, _config);
        _options.PackingSolver.Unregister();
        Assert.Null(GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "packing_solver")));
        Configs.Remove(_id);
    }
}
