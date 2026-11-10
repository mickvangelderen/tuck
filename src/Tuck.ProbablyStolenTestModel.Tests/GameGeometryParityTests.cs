#if HAS_GAME_GEOMETRY
extern alias original;
#endif

namespace Tuck.ProbablyStolenTestModel.Tests;

public class GameGeometryParityTests
{
#if HAS_GAME_GEOMETRY
    [Fact]
    public void GeometryMatchesOriginalAssemblyAcrossMasksAndTransforms()
    {
        var random = new Random(32931);
        for (int example = 0; example < 100; example++)
        {
            int width = random.Next(1, 6), height = random.Next(1, 6);
            var cells = Enumerable.Range(0, width * height).Select(_ => (byte)random.Next(0, 4)).ToArray();
            byte outside = (byte)random.Next(0, 4);
            for (int rotation = 0; rotation < 4; rotation++)
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    int x = random.Next(-5, 6), y = random.Next(-5, 6);
                    original::GridShape game = new original::GridShapeBuilder(width, height).SetData(cells, width)
                        .SetDataOutside(outside).SetTransform(x, y, mirror != 0, rotation).Build();
                    GridShape copy = new GridShapeBuilder(width, height).SetData(cells, width)
                        .SetDataOutside(outside).SetTransform(x, y, mirror != 0, rotation).Build();
                    for (int gy = y - 2; gy <= y + game.globalHeight + 2; gy++)
                        for (int gx = x - 2; gx <= x + game.globalWidth + 2; gx++)
                        {
                            Assert.Equal(game.Get(gx, gy), copy.Get(gx, gy));
                            Assert.Equal(game.InverseTransform(gx, gy), copy.InverseTransform(gx, gy));
                            Assert.Equal(game.Intersects(gx, gy), copy.Intersects(gx, gy));
                        }
                    for (int ly = 0; ly < height; ly++)
                        for (int lx = 0; lx < width; lx++)
                            Assert.Equal(game.ForwardTransform(lx, ly), copy.ForwardTransform(lx, ly));
                    original::GridShape other = new original::GridShapeBuilder(3, 4).SetDataFill(1).SetPosition(x + 1, y - 1);
                    GridShape otherCopy = new GridShapeBuilder(3, 4).SetDataFill(1).SetPosition(x + 1, y - 1);
                    Assert.Equal(game.Intersects(other), copy.Intersects(otherCopy));
                    Assert.Equal(game.FullyIntersects(other), copy.FullyIntersects(otherCopy));
                }
        }
    }
#else
    [Fact(Skip = "Fetch the game assemblies to compare model geometry with the original implementation.")]
    public void GeometryMatchesOriginalAssemblyAcrossMasksAndTransforms() { }
#endif
}
