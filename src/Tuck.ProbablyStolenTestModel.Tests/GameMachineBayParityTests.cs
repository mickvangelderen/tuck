#if HAS_GAME_GEOMETRY
extern alias original;
#endif
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.ProbablyStolenTestModel.Tests;

public class GameMachineBayParityTests
{
#if HAS_GAME_GEOMETRY
    [Theory]
    [InlineData("MachineBay", "machine_bay", 6, 8)]
    [InlineData("MachineBayExt", "machine_bay_ext", 11, 14)]
    public void AdmissionMatchesActualFactoryCallback(string factory, string identifier, int width, int height)
    {
        var method = Assert.Single(typeof(original::ShipSystemDirectory).GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)),
            m => m.Name.StartsWith("<" + factory + ">", StringComparison.Ordinal)
                && m.ReturnType == typeof(bool)
                && m.GetParameters().Select(p => p.ParameterType)
                    .SequenceEqual(new[] { typeof(original::GameItem), typeof(original::GameInventory) }));
        var target = method.IsStatic ? null : RuntimeHelpers.GetUninitializedObject(method.DeclaringType!);
        var admission = (Func<original::GameItem, original::GameInventory, bool>)method.CreateDelegate(
            typeof(Func<original::GameItem, original::GameInventory, bool>), target);

        for (int bits = 0; bits < 256; bits++)
        {
            bool Flag(int bit) => (bits & (1 << bit)) != 0;
            var gameOwner = new OriginalItem(identifier);
            var gameItem = new OriginalItem("incoming");
            var gameInventory = (original::GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(original::GameGridInventory));
            typeof(original::GameInventory).GetField("<parent>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(gameInventory, gameOwner);
            typeof(original::GameGridInventory).GetField("<inventoryShape>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(gameInventory, new original::GridShapeBuilder(width, height).SetDataOutside(1));

            var inventory = new GameGridInventory(width, height);
            var owner = new ModelItem(identifier, new GridShapeBuilder().SetData("1", 1), contents: inventory);
            var item = new ModelItem("incoming", new GridShapeBuilder().SetData("1", 1));
            ModelStorageRules.InitMachineBay(inventory, owner);
            string[] tags = ["IS_OWNED_TAG", "BACKPACK_TAG", "STORAGE", "NESTABLE_DEVICE_TAG", "STORABLE_PACKAGE_TAG"];
            for (int bit = 0; bit < tags.Length; bit++)
            {
                gameItem.state.GetTag(tags[bit]).SetEnabled(Flag(bit));
                item.state.GetTag(tags[bit]).SetEnabled(Flag(bit));
            }
            gameOwner.state.GetTag("IS_OWNED_TAG").SetEnabled(Flag(5));
            owner.state.GetTag("IS_OWNED_TAG").SetEnabled(Flag(5));
            if (Flag(6)) { gameItem.itemTypes.Add("STORAGE"); item.itemTypes.Add("STORAGE"); }
            owner.itemTypes.Clear();
            if (Flag(7)) { gameOwner.itemTypes.Add("STORAGE"); owner.itemTypes.Add("STORAGE"); }
            gameItem.modifiedState = gameItem.state.Clone();
            gameOwner.modifiedState = gameOwner.state.Clone();
            item.modifiedState = item.state.Clone();
            owner.modifiedState = owner.state.Clone();

            Assert.Same(gameOwner, gameInventory.GetParentItem());
            Assert.Same(owner, inventory.GetParentItem());
            bool expected = admission(gameItem, gameInventory);
            bool actual = inventory.mayInventoryAddItemFunc(item, inventory);
            Assert.True(expected == actual, $"{factory}: tag/type/ownership combination {bits} differs.");
        }
    }

    private sealed class OriginalItem(string identifier) : original::GameItem(identifier)
    {
        public override void Validate() { }
        public override void Destroy() { }
    }
#else
    [Fact(Skip = "Fetch game assemblies to compare machine-bay admission with the actual factory callbacks.")]
    public void AdmissionMatchesActualFactoryCallback() { }
#endif
}
