using System.Reflection;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class LocalizationTests
{
    internal static Dictionary<string, string> ReadEnglish()
    {
        // Use the game's value parser, including quoting and escaped newlines.
        var parse = typeof(ModLoader).GetMethod("ParseCsvValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Localization", "en.csv")))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            int separator = line.IndexOf(',');
            Assert.True(separator > 0, $"Missing key or comma: {line}");
            string key = line[..separator].Trim();
            string value = (string)parse.Invoke(null, [line[(separator + 1)..]])!;
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.True(result.TryAdd(key, value), $"Duplicate key: {key}");
        }
        return result;
    }

    [Fact]
    public void SettingsResolveOnlyWhenDisplayedAndFollowTranslationChanges()
    {
        string id = "tuck-localization-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [id], culture: null)!;
        configs.Add(id, config);
        var manifest = new ModManifest { ID = id, Name = "Tuck" };
        Dictionary<string, string>? translations = null;
        try
        {
            var options = new Options(manifest, (key, args) =>
                string.Format((translations ?? throw new InvalidOperationException("Translations have not loaded."))[key], args));
            // Skip EnsureVanilla, which needs Unity. Register every mod row before translations load.
            foreach (var property in typeof(Options).GetProperties())
            {
                switch (property.GetValue(options))
                {
                    case KeyBinding binding: binding.Register(); break;
                    case ScalarSetting scalar: scalar.Register(); break;
                    case ToggleSetting toggle: toggle.Register(); break;
                    case PackingSolverSetting solver: solver.Register(); break;
                }
            }

            var english = ReadEnglish();
            translations = english;
            var labels = config.Entries.ToDictionary(entry => entry.Key, entry => entry.Label());
            var tooltips = config.Entries.Where(entry => entry.Tooltip != null)
                .ToDictionary(entry => entry.Key, entry => entry.Tooltip());
            CheckLabels();
            var duration = GameOptionRegistry.Find(ModHelper.GameOptionId(id, "return_hold_duration"));
            var occupancy = GameOptionRegistry.Find(ModHelper.GameOptionId(id, "workspace_clear_percent"));
            Assert.Equal($"{0.4f:0.0} s", duration.valueText(4));
            Assert.Equal("40%", occupancy.valueText(8));
            Assert.Equal("Disabled", occupancy.valueText(20));
            var solverRow = GameOptionRegistry.Find(ModHelper.GameOptionId(id, "packing_solver"));
            Assert.Equal(new[] { "Container first", "Relaxed A*", "Anytime", "Baseline" }, solverRow.options());

            // Switch the supplied language after registration, including argument order and units.
            translations = english.ToDictionary(pair => pair.Key, pair => "translated: " + pair.Value);
            translations["x_seconds"] = "seconds: {0:0.0}";
            translations["printing_x_of_y"] = "{1} total; {0} done";
            translations["text_with_key_hint"] = "{1}: {0}";
            CheckLabels("translated: ");
            Assert.Equal($"seconds: {0.4f:0.0}", duration.valueText(4));
            Assert.Equal("translated: Disabled", occupancy.valueText(20));
            Assert.Equal(new[] { "translated: Container first", "translated: Relaxed A*", "translated: Anytime", "translated: Baseline" }, solverRow.options());
            Assert.Equal("20 total; 3 done", options.Text("printing_x_of_y", 3, 20));
            Assert.Equal("(G): dialogue", options.Text("text_with_key_hint", "dialogue", "(G)"));

            void CheckLabels(string prefix = "")
            {
                foreach (var entry in config.Entries)
                {
                    string label = prefix + labels[entry.Key];
                    Assert.Equal(label, entry.Label());
                    var row = GameOptionRegistry.Find(ModHelper.GameOptionId(id, entry.Key));
                    Assert.Equal(Options.FormatMenuLabel(manifest, label), row.label());
                    if (entry.Tooltip == null) continue;
                    string tooltip = prefix + tooltips[entry.Key];
                    Assert.Equal(tooltip, entry.Tooltip());
                    Assert.Equal(tooltip, row.tooltip());
                }
            }
        }
        finally
        {
            ModHelper.RemoveGameOptions(id);
            configs.Remove(id);
        }
    }

    [Fact]
    public void EnglishTemplatesPreserveProgressDialogueAndUnits()
    {
        var english = ReadEnglish();
        Assert.Equal("Printing 3/20", string.Format(english["printing_x_of_y"], 3, 20));
        Assert.Equal("50 credits", string.Format(english["x_credits"], 50, " credits"));
        foreach (string key in new[]
        {
            "youre_leaving_that_theres_room_watch",
            "you_bought_all_this_without_checking_the_bag",
            "heavy_things_at_the_bottom_weve_been_over_this",
            "let_me_see_what_fits"
        })
        {
            string line = english[key];
            Assert.StartsWith("Tuck: ", line);
            Assert.Equal(line + " (G)", string.Format(english["text_with_key_hint"], line, "(G)"));
        }
    }
}
