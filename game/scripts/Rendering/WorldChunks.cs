using System.Diagnostics;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    private sealed class ChunkSlot(int x, int z, TerrainSnapshot latest)
    {
        internal readonly int X = x, Z = z;
        internal TerrainSnapshot? Latest = latest;
        internal Task<ChunkResult>? Task;
        internal long Requested = 1, Running, Committed;
        internal Node3D? Root;
        internal bool Water;
        internal double BuildMilliseconds, CommitMilliseconds;
    }
    private sealed record ChunkResult(ChunkGeometry Geometry, double Milliseconds);
    private ChunkSlot[] _chunks = [];
    private TerrainSnapshot _snapshot = null!;
    private ContentRegistry _content = null!;
    private int _columns, _rows, _pendingChunks, _commitCursor;
    private string _environmentJson = "";
    /// <summary>True when this view has authored idle placements and no GameSession.</summary>
    public bool IsPreview => _document is not null && _session is null;
    /// <summary>Chunks awaiting a current result; each has at most one worker task.</summary>
    public int PendingChunkCount => _pendingChunks;
    /// <summary>Worker results discarded because newer edits superseded them.</summary>
    public int DiscardedChunkBuilds { get; private set; }
    /// <summary>Largest observed pure mesh build time, excluding thread-pool queue delay.</summary>
    public double MaxChunkBuildMilliseconds { get; private set; }
    /// <summary>Largest scene-thread chunk replacement time.</summary>
    public double MaxChunkCommitMilliseconds { get; private set; }
    /// <summary>Committed version at a chunk; unchanged chunks retain their version and node.</summary>
    public long ChunkRevision(int x, int z) => _chunks[ChunkIndex(x, z)].Committed;
    /// <summary>Last committed CPU meshing and scene replacement timings for a chunk, in milliseconds.</summary>
    public (double Build, double Commit) ChunkTimings(int x, int z)
    { ChunkSlot slot = _chunks[ChunkIndex(x, z)]; return (slot.BuildMilliseconds, slot.CommitMilliseconds); }
    /// <summary>Rebuilds only the supplied affected chunks. Use ChunkRect.FromCells for cliff/AO borders.
    /// Runtime views need a matching reloaded session for gameplay changes; preview views read authored placements.</summary>
    public void ApplyChange(ChunkRect affected)
    {
        if (_document is null) { throw new InvalidOperationException("Build a view first."); }
        if (_document.HasErrors) { throw new ArgumentException("Cannot render a document with validation errors."); }
        if (_document.Width != _snapshot.Width || _document.Height != _snapshot.Height || _document.Id != _snapshot.LevelId || _document.ContentHash != _content.Hash)
        { throw new InvalidOperationException("Rebuild the view after size, identity or registry changes."); }
        if (_session is not null && _session.World.SourceHash != _document.SourceHash) { throw new InvalidOperationException("Reload the gameplay session before presenting its changed document."); }
        if (_environmentJson != _document.Environment.Json.GetRawText()) { ApplyEnvironment(_document.Environment); UpdateCamera(0); }
        if (IsPreview) { SynchronizePreviewEntities(); }
        ChunkRect clipped = affected.Clip(_columns, _rows); if (clipped.IsEmpty) { return; }
        _snapshot = _snapshot.WithChanges(_document, new(clipped.X * 16, clipped.Z * 16, clipped.Width * 16, clipped.Height * 16));
        for (int z = clipped.Z; z < clipped.Z + clipped.Height; z++)
        {
            for (int x = clipped.X; x < clipped.X + clipped.Width; x++)
            {
                ChunkSlot slot = _chunks[z * _columns + x];
                if (slot.Requested == slot.Committed) { _pendingChunks++; }
                slot.Requested++; slot.Latest = _snapshot; if (slot.Task is null) { Schedule(slot); }
            }
        }
    }
    private void PrepareView(LevelDocument document, ContentRegistry content, GameSession? session)
    {
        // Bake/validate before replacing the current scene, so a rejected build leaves it intact.
        var snapshot = new TerrainSnapshot(document, content, ContentBridge.ReadText("res://content/atlas.json"));
        if (session is not null && (session.World.SourceHash != document.SourceHash || session.World.ContentHash != content.Hash || session.World.LevelId != document.Id))
        { throw new ArgumentException("The session and presentation document/registry identities differ.", nameof(session)); }
        ResetView(); _document = document; _content = content; _session = session!; _snapshot = snapshot;
        _materials = new(snapshot); ApplyEnvironment(document.Environment);
        _columns = (document.Width + 15) / 16; _rows = (document.Height + 15) / 16;
        _chunks = new ChunkSlot[_columns * _rows]; _pendingChunks = _chunks.Length;
        for (int z = 0; z < _rows; z++)
        { for (int x = 0; x < _columns; x++) { var slot = new ChunkSlot(x, z, snapshot); _chunks[z * _columns + x] = slot; Schedule(slot); } }
    }
    private void ResetView()
    {
        GraphicsRuntime.Instance?.Detach(this);
        foreach (Node child in GetChildren()) { child.Free(); }
        _actors.Clear(); _actorById.Clear(); _actorPool.Clear(); _animationProfiles.Clear(); _stuckArrows.Clear();
        _lights.Clear(); _lightIds.Clear(); _batchedEntityIds.Clear(); _fades.Clear(); _staticInstances.Clear(); _actorFades.Clear(); _previewEntities.Clear();
        _ambient.Clear(); _ambientIds.Clear();
        _chunks = []; _pendingChunks = _commitCursor = _frames = _nextStuckArrow = 0; _nextPreviewId = -1;
        _visualTime = 0; _shake.Clear(); ChunkCount = WaterMeshCount = EnabledLightCount = DiscardedChunkBuilds = 0;
        MaxChunkBuildMilliseconds = MaxChunkCommitMilliseconds = 0; _focus = _renderFocus = Vector3.Zero;
        CombatOverlay = null!; Particles = null!;
    }
    private int ChunkIndex(int x, int z)
    {
        if ((uint)x >= (uint)_columns || (uint)z >= (uint)_rows) { throw new ArgumentOutOfRangeException(nameof(x)); }
        return z * _columns + x;
    }
    private static void Schedule(ChunkSlot slot)
    {
        TerrainSnapshot snapshot = slot.Latest ?? throw new InvalidOperationException("A queued chunk needs a snapshot."); slot.Running = slot.Requested;
        slot.Task = Task.Run(() =>
        {
            long start = Stopwatch.GetTimestamp(); ChunkGeometry geometry = ChunkMesher.Build(snapshot, slot.X, slot.Z, snapshot.WaterTexture);
            return new ChunkResult(geometry, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        });
    }
    private int CommitReadyChunks()
    {
        int committed = 0;
        for (int scanned = 0; scanned < _chunks.Length && committed < 4; scanned++)
        {
            int index = _commitCursor++ % _chunks.Length; if (_commitCursor == int.MaxValue) { _commitCursor = 0; }
            ChunkSlot slot = _chunks[index]; if (slot.Task is not { IsCompleted: true } task) { continue; }
            ChunkResult result = task.GetAwaiter().GetResult(); slot.Task = null;
            MaxChunkBuildMilliseconds = System.Math.Max(MaxChunkBuildMilliseconds, result.Milliseconds);
            if (slot.Running != slot.Requested) { DiscardedChunkBuilds++; Schedule(slot); continue; }
            long start = Stopwatch.GetTimestamp(); ReplaceChunk(slot, result.Geometry, index);
            slot.BuildMilliseconds = result.Milliseconds; slot.CommitMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            MaxChunkCommitMilliseconds = System.Math.Max(MaxChunkCommitMilliseconds, slot.CommitMilliseconds);
            slot.Committed = slot.Running; slot.Latest = null; _pendingChunks--; committed++;
        }
        return committed;
    }
    private void ReplaceChunk(ChunkSlot slot, ChunkGeometry geometry, int index)
    {
        var root = new Node3D { Name = "Chunk_" + slot.X + "_" + slot.Z };
        AddMesh(root, "Terrain", geometry.Terrain, _materials.Terrain);
        bool water = geometry.Water.Indices.Length > 0;
        if (water) { AddMesh(root, "Water", geometry.Water, _materials.Water); }
        if (geometry.Foliage.Indices.Length > 0) { AddMesh(root, "Foliage", geometry.Foliage, _materials.Foliage); }
        // Discard dither references before releasing the old MultiMeshes.
        _fades.RemoveAll(fade => fade.Chunk == index);
        BuildChunkModels(root, slot.Latest!, slot.X, slot.Z, index);
        if (slot.Root is { } previous) { RemoveChild(previous); previous.Free(); } else { ChunkCount++; }
        if (slot.Water) { WaterMeshCount--; } if (water) { WaterMeshCount++; }
        AddChild(root); slot.Root = root; slot.Water = water;
    }
    private static void AddMesh(Node parent, string name, MeshBuffer data, Material material)
        => parent.AddChild(new MeshInstance3D { Name = name, Mesh = RenderMaterials.Commit(data, material) });
    private void BuildChunkModels(Node3D root, TerrainSnapshot snapshot, int chunkX, int chunkZ, int index)
    {
        var batches = new Dictionary<string, List<(Vector3 Position, float Rotation)>>(StringComparer.Ordinal);
        for (int z = chunkZ * 16; z < System.Math.Min(snapshot.Height, (chunkZ + 1) * 16); z++)
        {
            for (int x = chunkX * 16; x < System.Math.Min(snapshot.Width, (chunkX + 1) * 16); x++)
            {
                TerrainCell cell = snapshot[x, z]; if (cell.Model is not { } path) { continue; }
                if (!batches.TryGetValue(path, out var positions)) { positions = []; batches.Add(path, positions); }
                positions.Add((new(x + .5f, cell.Height, z + .5f), cell.Rotation));
            }
        }
        int number = 0;
        foreach (var pair in batches)
        {
            ArrayMesh mesh = _models.StaticMesh(pair.Key);
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = pair.Value.Count };
            for (int i = 0; i < pair.Value.Count; i++)
            {
                var placement = pair.Value[i]; multimesh.SetInstanceTransform(i, new(new Basis(Vector3.Up, placement.Rotation), placement.Position));
                multimesh.SetInstanceColor(i, Colors.White); multimesh.SetInstanceCustomData(i, Colors.White);
                if (mesh.GetAabb().Size.Y > 1.2f) { _fades.Add(new(multimesh, i, (int)placement.Position.X, (int)placement.Position.Z, index)); }
            }
            root.AddChild(new MultiMeshInstance3D { Name = "Models_" + number++, Multimesh = multimesh });
        }
    }
}
