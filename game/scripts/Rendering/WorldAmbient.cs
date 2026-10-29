using Dungeons.Core.Sim;
using Godot;
using System.Text.Json;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    private sealed class AmbientEmitter(int id, AmbientParticleKind kind, float offset)
    { internal readonly int Id = id; internal readonly AmbientParticleKind Kind = kind; internal readonly float Offset = offset; internal AmbientEmissionClock Clock; }
    private readonly List<AmbientEmitter> _ambient = [];
    private readonly HashSet<int> _ambientIds = [];
    /// <summary>Currently bound authored visual emitters; no Core entities are created for them.</summary>
    public int AmbientEmitterCount => _ambient.Count;
    private void AddAmbientEmitter(Entity entity)
    {
        if (_ambientIds.Contains(entity.Id) || !entity.Def.Data.Json.TryGetProperty("render", out JsonElement render)
            || !render.TryGetProperty("particles", out JsonElement effect)) { return; }
        AmbientParticleKind? kind = effect.GetString() switch { "torch_flame" => AmbientParticleKind.TorchFlame, "beacon_stream" => AmbientParticleKind.BeaconStream, _ => null };
        if (kind is null) { return; }
        float height = 1;
        if (render.TryGetProperty("model", out JsonElement model)) { height = _models.StaticMesh(model.GetString()!).GetAabb().End.Y; }
        _ambient.Add(new(entity.Id, kind.Value, kind == AmbientParticleKind.TorchFlame ? height - .12f : .2f)); _ambientIds.Add(entity.Id);
    }
    private void AdvanceAmbient(double delta)
    {
        for (int i = _ambient.Count - 1; i >= 0; i--)
        {
            AmbientEmitter emitter = _ambient[i]; Entity? entity = _session.World.FindEntity(emitter.Id);
            if (entity is not { Alive: true }) { _ambientIds.Remove(emitter.Id); _ambient.RemoveAt(i); continue; }
            Vector3 position = new(entity.Pos.X, entity.Y + emitter.Offset, entity.Pos.Z);
            if (!Camera.IsPositionInFrustum(position) || position.DistanceSquaredTo(_focus) > 32 * 32) { emitter.Clock.Reset(); continue; }
            bool dimmed = emitter.Kind == AmbientParticleKind.BeaconStream && _session.World.Mission.ExitLocked;
            int count = emitter.Clock.Advance(System.Math.Max(0, delta), emitter.Kind == AmbientParticleKind.TorchFlame ? 24 : dimmed ? 4 : 12);
            for (int particle = 0; particle < count; particle++)
            {
                double age = System.Math.Min(delta, .25) * (count - particle - 1) / count;
                Particles.Stream(position, _visualTime - age, emitter.Kind, dimmed);
            }
        }
    }
    private static float BeaconEmission(Entity entity, bool locked)
    {
        JsonElement render = entity.Def.Data.Json.GetProperty("render"); string key = locked ? "lockedEmissive" : "unlockedEmissive";
        return render.TryGetProperty(key, out JsonElement value) ? System.Math.Clamp(value.GetSingle() / 2.5f, 0, 1) : locked ? .08f : 1;
    }
}
