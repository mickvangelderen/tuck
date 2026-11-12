using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class AppraisalGuidesTests
{
    [Fact]
    public void ClosesGuidesFromRepeatedInspectionsOnceAndClearsBeforeCloseCallbacks()
    {
        var opened = (HashSet<string>)typeof(AppraisalGuides)
            .GetField("OpenedGuides", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            opened.Add("cigarette_guide");
            opened.Add("cigarette_color");
            opened.Add("cigarette_guide");
            opened.Add("mod_reference");
            var closed = new List<string>();
            AppraisalGuides.CloseGuides(id =>
            {
                closed.Add(id);
                // A native close callback can itself close the inspection window.
                AppraisalGuides.CloseGuides(_ => Assert.Fail("Guide was closed recursively."));
            });
            Assert.Equal(new[] { "cigarette_color", "cigarette_guide", "mod_reference" }, closed.Order());
            AppraisalGuides.CloseGuides(_ => Assert.Fail("Guide was closed twice."));
        }
        finally { opened.Clear(); }
    }

    [Theory]
    [InlineData("HAND_TAG", "hand_guide")]
    [InlineData("RIBWICH_TAG", "ribwich_guide")]
    [InlineData("PLASMA_CELL_TAG", "fuel_guide")]
    [InlineData("CIGARETTE_TAG", "cigarette_guide", "cigarette_color")]
    [InlineData("INS_INJECTOR_TAG", "injector_guide")]
    [InlineData("FOOD_STAMP_TAG", "stamp_guide", "logo_checker")]
    [InlineData("BERRY_TAG")]
    [InlineData("APPRAISAL_ITEM")]
    public void UsesInspectionTagsWithoutRequiringAnAppraisalServiceJob(string tag, params string[] guides)
    {
        var item = new FixtureItem("variant_from_a_mod");
        item.modifiedState.EnableTag(tag);
        Assert.Equal(guides, AppraisalGuides.GetGuideIds(item));
    }

    [Fact]
    public void RegistryAdditionsAndReplacementsTakePrecedenceIncludingAnAbsentGuide()
    {
        var original = AppraisalItemRegistry.Get("hand");
        var replacement = new AppraisalItemType
        {
            id = original.id,
            itemId = "test_hand",
            guideItemId = "mod_hand_guide",
            createRandom = () => new FixtureItem("test_hand"),
            getCorrectValue = _ => 1,
            source = "tuck-guide-tests"
        };
        var custom = new AppraisalItemType
        {
            id = "tuck_test_cigarette",
            isType = item => item.IsTag("CIGARETTE_TAG"),
            guideItemId = "mod_cigarette_guide",
            createRandom = () => new FixtureItem("test_cigarette"),
            getCorrectValue = _ => 1,
            source = replacement.source
        };
        try
        {
            AppraisalItemRegistry.Register(replacement);
            AppraisalItemRegistry.Register(custom);
            Assert.Equal(["mod_hand_guide"], AppraisalGuides.GetGuideIds(new FixtureItem("test_hand")));
            var cigarette = new FixtureItem("test_cigarette");
            cigarette.modifiedState.EnableTag("CIGARETTE_TAG");
            Assert.Equal(["mod_cigarette_guide"], AppraisalGuides.GetGuideIds(cigarette));
            custom.guideItemId = null;
            Assert.Empty(AppraisalGuides.GetGuideIds(cigarette));
        }
        finally
        {
            AppraisalItemRegistry.RemoveAll(replacement.source);
            AppraisalItemRegistry.Register(original);
        }
    }

    [Fact]
    public void OnlySelectsOwnedGuidesAllowedByTheNormalActivationChecks()
    {
        var guide = new FixtureItem("hand_guide");
        var slot = (GameSlotInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameSlotInventory));
        typeof(GameSlotInventory).GetField("currentItem", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(slot, guide);
        guide.parentInventory = slot;
        guide.mayActivateSlotItemFunc = (_, _, _) => true;
        guide.canActivateSlotItemFunc = (_, _, _) => true;
        Assert.Same(guide, AppraisalGuides.FindReadableGuide([guide], "hand_guide"));
        Assert.Null(AppraisalGuides.FindReadableGuide([guide], "fuel_guide"));
        Assert.Null(AppraisalGuides.FindReadableGuide([], "hand_guide"));

        guide.modifiedState.DisableTag("IS_OWNED_TAG");
        Assert.Null(AppraisalGuides.FindReadableGuide([guide], "hand_guide"));
        guide.modifiedState.EnableTag("IS_OWNED_TAG");
        guide.forceDisableActivate = true;
        Assert.Null(AppraisalGuides.FindReadableGuide([guide], "hand_guide"));
        guide.forceDisableActivate = false;
        guide.mayActivateSlotItemFunc = (_, _, _) => false;
        Assert.Null(AppraisalGuides.FindReadableGuide([guide], "hand_guide"));
        guide.mayActivateSlotItemFunc = (_, _, _) => true;
        guide.canActivateSlotItemFunc = (_, _, _) => false;
        Assert.Null(AppraisalGuides.FindReadableGuide([guide], "hand_guide"));
        guide.canActivateSlotItemFunc = (_, _, _) => true;
        Assert.Same(guide, AppraisalGuides.FindReadableGuide([new FixtureItem("hand_guide"), guide], "hand_guide"));
        guide.parentInventory = null;
        Assert.Null(AppraisalGuides.FindReadableGuide([guide], "hand_guide"));
    }

    [Fact]
    public void DisabledByDefaultAndStopsImmediatelyWhenDisabledOrUnregistered()
    {
        string modId = "tuck-guides-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [modId], null)!;
        configs.Add(modId, config);
        try
        {
            AppraisalGuides.Postfix(null!, null!);
            AppraisalGuides.OnPanelDisabled(null!);
            var options = new Options(new ModManifest { ID = modId, Name = "Tuck" });
            var entry = (ConfigEntry<bool>)config.Get("auto_open_appraisal_guides");
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
            Assert.False(options.AutoOpenAppraisalGuides.Value);
            AppraisalGuides.Register(options);
            AppraisalGuides.Postfix(null!, null!);
            AppraisalGuides.OnPanelDisabled(null!);
            entry.Set(true);
            // Unrelated inspections must not access the document UI, even while enabled.
            AppraisalGuides.Postfix(null!, new FixtureItem("unrelated"));
            AppraisalGuides.OnPanelDisabled(null!);
            entry.Set(false);
            AppraisalGuides.Postfix(null!, null!);
            AppraisalGuides.OnPanelDisabled(null!);
            entry.Set(true);
            AppraisalGuides.Unregister();
            AppraisalGuides.Postfix(null!, null!);
            AppraisalGuides.OnPanelDisabled(null!);
        }
        finally
        {
            AppraisalGuides.Unregister();
            configs.Remove(modId);
        }
    }

    private sealed class FixtureItem : GameItem
    {
        public FixtureItem(string id) : base(id)
        {
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
        }

        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Guides must not be destroyed.");
    }
}
