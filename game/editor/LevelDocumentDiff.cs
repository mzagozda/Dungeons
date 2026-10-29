using Dungeons.Core.Level;
using Dungeons.Game.Rendering;

namespace Dungeons.Game.Editor;

/// <summary>Transient comparison of two source documents; no second authored level representation is saved.</summary>
internal static class LevelDocumentDiff
{
    internal static ChunkRect ChangedChunks(LevelDocument before, LevelDocument after)
    {
        if (before.Width != after.Width || before.Height != after.Height || before.Id != after.Id || before.ContentHash != after.ContentHash)
        { return new(0, 0, (after.Width + 15) / 16, (after.Height + 15) / 16); }
        int minX = after.Width, minZ = after.Height, maxX = -1, maxZ = -1;
        for (int z = 0; z < after.Height; z++)
        {
            for (int x = 0; x < after.Width; x++)
            {
                var a = before.GetCell(x, z); var b = after.GetCell(x, z);
                if (a.Height == b.Height && a.Walkable == b.Walkable && a.Solid == b.Solid && a.BlocksProjectiles == b.BlocksProjectiles && a.BlocksVision == b.BlocksVision
                    && a.StairsDir == b.StairsDir && a.MoveCost == b.MoveCost && before.GetTileReference(x, z) == after.GetTileReference(x, z)) { continue; }
                minX = System.Math.Min(minX, x); minZ = System.Math.Min(minZ, z); maxX = System.Math.Max(maxX, x); maxZ = System.Math.Max(maxZ, z);
            }
        }
        return maxX < 0 ? default : ChunkRect.FromCells(new(minX, minZ, maxX - minX + 1, maxZ - minZ + 1), after.Width, after.Height);
    }
}
