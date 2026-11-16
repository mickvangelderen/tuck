using Tuck.ProbablyStolenTestModel;

namespace Tuck.ProbablyStolenTestModel.Tests;

public class GunGeometryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void InstalledStockExpandsGunAndPreservesBasePosition(int rotation, bool flipped)
    {
        var socket = new GameSlotInventory();
        var gun = new ModelItem("smg", new GridShapeBuilder().SetDataFill(7, 2, 1), contents: socket);
        var stock = new ModelItem("stock", new GridShapeBuilder().SetDataFill(2, 1, 1));
        ModelGunGeometry.InitAttachment(stock);
        Assert.True(socket.UncheckedAccept(stock));
        gun.modifiedShape = new GridShapeBuilder(gun.shape).SetTransform(4, 5, flipped, rotation);
        var before = gun.modifiedShape.ForwardTransform(0, 0);
        ModelGunGeometry.Resolve(gun);
        Assert.Equal((9, 2), (gun.modifiedShape.width, gun.modifiedShape.height));
        Assert.Equal((2, 0), (gun.modifiedXOrigin, gun.modifiedYOrigin));
        Assert.Equal(before, gun.modifiedShape.ForwardTransform(gun.modifiedXOrigin, gun.modifiedYOrigin));
        ModelGunGeometry.Resolve(gun);
        Assert.Equal(before, gun.modifiedShape.ForwardTransform(gun.modifiedXOrigin, gun.modifiedYOrigin));
        Assert.Equal(1, TransferScenario.Move(gun, new GameGridInventory(7, 2)));
        Assert.Equal(0, TransferScenario.Move(gun, new GameGridInventory(9, 2)));
    }

    [Fact]
    public void KnownIdentifierDoesNotAuthorizeAnArbitraryShapeCallback()
    {
        var stock = new ModelItem("stock", new GridShapeBuilder().SetData("1", 1));
        stock.parentItemShapeModifierForItemFunc = (_, _, _, _, _, _) => new GridShapeBuilder(1, 1);
        Assert.False(ModelGunGeometry.IsSupported(stock));
        Assert.Throws<NotSupportedException>(() => TransferScenario.Move(stock, new GameSlotInventory()));
    }
}
