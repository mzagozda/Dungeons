using Dungeons.Core.Level;
using Godot;

namespace Dungeons.Game;

/// <summary>Bounded virtual/user-directory IO for the reserved package asset source.</summary>
public static class LevelAssetBridge
{
    public static IAssetSource Read(string directory, int byteBudget = LevelPackage.MaxBytes)
    {
        if (byteBudget is < 0 or > LevelPackage.MaxBytes) { throw new ArgumentOutOfRangeException(nameof(byteBudget)); }
        using DirAccess? parent = DirAccess.Open(directory);
        if (parent is null) { throw new IOException("Cannot open level directory: " + directory); }
        if (!parent.DirExists("assets")) { return MemoryAssetSource.Empty; }
        if (parent.IsLink("assets")) { throw new InvalidDataException("Linked asset directories are unsupported."); }
        var files = new List<(string path, ReadOnlyMemory<byte> bytes)>(); var pending = new Stack<string>(); pending.Push("assets");
        long total = 0; int entries = 0;
        while (pending.TryPop(out string? relative))
        {
            if (++entries > LevelPackage.MaxEntries || !LevelPackage.IsSafePath(relative)) { throw new InvalidDataException("Asset directory exceeds path/count limits."); }
            using DirAccess? folder = DirAccess.Open(directory.PathJoin(relative));
            if (folder is null) { throw new IOException("Cannot open asset directory."); }
            folder.IncludeHidden = true;
            foreach (string child in folder.GetDirectories())
            { if (folder.IsLink(child)) { throw new InvalidDataException("Linked asset directories are unsupported."); } pending.Push(relative + "/" + child); }
            foreach (string name in folder.GetFiles())
            {
                string path = relative + "/" + name;
                if (++entries > LevelPackage.MaxEntries || !LevelPackage.IsSafePath(path) || folder.IsLink(name)) { throw new InvalidDataException("Unsafe or excess asset file."); }
                using Godot.FileAccess? file = Godot.FileAccess.Open(directory.PathJoin(path), Godot.FileAccess.ModeFlags.Read);
                if (file is null) { throw new IOException("Cannot read asset file: " + path); }
                ulong length = file.GetLength(); if (length > (ulong)(byteBudget - total)) { throw new InvalidDataException("Level and assets exceed 64 MiB."); }
                byte[] bytes = file.GetBuffer((long)length); if ((ulong)bytes.LongLength != length) { throw new IOException("Asset file read was incomplete."); }
                total += bytes.Length; files.Add((path, bytes));
            }
        }
        return new MemoryAssetSource(files);
    }
}
