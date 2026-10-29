using Dungeons.Core.Sim;
using Dungeons.Core.Util;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class TerrainHashTests
{
    [Fact]
    public void CachedPrefixIsTheExactCanonicalByteStreamAndInvalidatesForEveryTerrainField()
    {
        GameSession session = new ScenarioBuilder(4, 4).Build().Session; NavGrid grid = session.World.Grid;
        ulong original = session.Hash();
        Cell[] variants = [new(height: 1), new(walkable: false), new(solid: true), new(blocksProjectiles: true), new(blocksVision: true), new(stairsDir: StairsDirection.East), new(moveCost: .9f)];
        foreach (Cell variant in variants)
        {
            grid.SetRuntimeCell(2, 2, variant); var expected = new Fnv1a(); expected.Add(grid.Width); expected.Add(grid.Height);
            for (int i = 0; i < grid.Count; i++)
            {
                Cell cell = grid[i]; expected.Add(cell.Height); expected.Add(cell.Walkable); expected.Add(cell.Solid); expected.Add(cell.BlocksProjectiles);
                expected.Add(cell.BlocksVision); expected.Add((byte)cell.StairsDir); expected.Add(BitConverter.SingleToInt32Bits(cell.MoveCost));
            }
            Assert.Equal(expected.Value, grid.HashPrefix.Value); Assert.Equal(expected.Value, grid.HashPrefix.Value);
            Assert.NotEqual(original, session.Hash()); grid.SetRuntimeCell(2, 2, Cell.Floor); Assert.Equal(original, session.Hash());
        }
    }
}
