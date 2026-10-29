using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    /// <summary>Rebinds a successfully reloaded session, retaining unaffected terrain chunks when size,
    /// id and content match. Runtime actors/effects reset for the new simulation epoch.</summary>
    public void Reload(LevelDocument document, GameSession session, ChunkRect changedChunks)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(session);
        if (document.HasErrors || session.World.SourceHash != document.SourceHash || session.World.LevelId != document.Id || session.World.ContentHash != document.ContentHash)
        { throw new ArgumentException("Reload requires a valid document and matching reloaded session."); }
        var content = document.Registry ?? throw new ArgumentException("Reload requires an authored registry-bound document.", nameof(document));
        if (_document is null || IsPreview || document.Width != _snapshot.Width || document.Height != _snapshot.Height || document.Id != _snapshot.LevelId || content.Hash != _content.Hash)
        { Build(document, content, session); return; }
        var keep = new HashSet<Node> { Camera, _sun };
        foreach (WorldEnvironment environment in GetChildren().OfType<WorldEnvironment>()) { keep.Add(environment); }
        foreach (ChunkSlot chunk in _chunks) { if (chunk.Root is not null) { keep.Add(chunk.Root); } }
        foreach (Node child in GetChildren()) { if (!keep.Contains(child)) { child.Free(); } }
        _actors.Clear(); _actorById.Clear(); _actorPool.Clear(); _animationProfiles.Clear(); _stuckArrows.Clear();
        _lights.Clear(); _lightIds.Clear(); _batchedEntityIds.Clear(); _staticInstances.Clear(); _actorFades.Clear();
        _ambient.Clear(); _ambientIds.Clear();
        _fades.RemoveAll(fade => fade.Chunk < 0); _nextStuckArrow = 0; _visualTime = 0; _shake.Clear();
        _document = document; _content = content; _session = session;
        BuildRuntime(content, session); ApplyChange(changedChunks);
    }
}
