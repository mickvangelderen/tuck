using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

public class ScavengingReturnTests
{
    private sealed class FixtureItem : GameItem
    {
        public FixtureItem(long value, int count = 1) : base("scavenging-loot")
        {
            unitValue = value;
            unitCount = count;
            modifiedState = state.Clone();
        }

        public override void Validate() { }
        public override void Destroy() { }
    }

    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void Field(object obj, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);

    private static GameGridInventory Ground(params GameItem[] items)
    {
        var ground = Raw<GameGridInventory>();
        Field(ground, typeof(GameGridInventory), "items", items.ToList());
        foreach (var item in items) item.parentInventory = ground;
        return ground;
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    [InlineData(1000, true)]
    public void ShowsExistingWarningAtOrAboveMinimum(long value, bool expected)
    {
        Assert.Equal(expected, ScavengingReturn.RequiresConfirmation(Ground(new FixtureItem(value)), 100f));
    }

    [Fact]
    public void MinimumAppliesToCombinedStackAndContainerContents()
    {
        var bag = new FixtureItem(20);
        var contents = Ground(new FixtureItem(10, count: 2));
        var window = Raw<PixelWindow>();
        Field(window, typeof(PixelWindow), "<childElement>k__BackingField", contents);
        Field(bag, typeof(GameItem), "<contentWindow>k__BackingField", window);
        // A second window reference must not count the same contents twice.
        Field(bag, typeof(GameItem), "<examineWindow>k__BackingField", window);
        var ground = Ground(bag, new FixtureItem(20, count: 3));

        Assert.True(ScavengingReturn.RequiresConfirmation(ground, 100f));
        Assert.False(ScavengingReturn.RequiresConfirmation(ground, 101f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ZeroMinimumNeverSkipsTheExistingWarning(long value)
    {
        Assert.True(ScavengingReturn.RequiresConfirmation(Ground(new FixtureItem(value)), 0f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(100000)]
    public void MaximumDisablesWarningRegardlessOfValueLeftBehind(long value)
    {
        Assert.False(ScavengingReturn.RequiresConfirmation(Ground(new FixtureItem(value)), 1000f));
    }
}
