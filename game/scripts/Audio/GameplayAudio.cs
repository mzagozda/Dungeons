using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Audio;

/// <summary>Read-only event/snapshot adapter. Never resolves expired events through pooled entities.</summary>
public partial class GameplayAudio : Node3D
{
    private readonly Dictionary<string, Dictionary<string, string>> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _surfaceCues = new(StringComparer.Ordinal);
    private sealed record LoopSources(string Sound, Vector3[] Positions, float[] Distances);
    private readonly List<LoopSources> _loops = [];
    private AudioManager _audio = null!;
    private GameSession _session = null!;
    private Camera3D _camera = null!;
    private AudioListener3D _listener = null!;
    private float _loopTimer = 1;
    public void Build(AudioManager audio, GameSession session, LevelDocument level, ContentRegistry content, Camera3D camera)
    {
        _audio = audio; _session = session; _camera = camera;
        var soundBank = new SoundBank(content);
        audio.BeginWorld(this);
        _listener = new(); AddChild(_listener); _listener.MakeCurrent();
        foreach (EntityDef definition in content.Entities.Values)
        {
            if (!definition.Data.Json.TryGetProperty("audio", out JsonElement map)) { continue; }
            _bindings.Add(definition.Id, map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal));
            if (map.TryGetProperty("loop", out var sound) && _loops.All(l => l.Sound != sound.GetString()))
            { int cap = soundBank.Sounds[sound.GetString()!].MaximumInstances; _loops.Add(new(sound.GetString()!, new Vector3[cap], new float[cap])); }
        }
        foreach (TileDef tile in content.Tiles.Values)
        {
            if (tile.Data.Json.TryGetProperty("audio", out var sounds) && sounds.TryGetProperty("footstep", out var step)) { _surfaceCues.Add(tile.Id, step.GetString()!); }
        }
        JsonElement environment = level.Environment.Json;
        if (environment.TryGetProperty("music", out var music) && music.ValueKind == JsonValueKind.String) { audio.ChangeMusic(music.GetString()!); }
        if (environment.TryGetProperty("ambience", out var ambience) && ambience.ValueKind == JsonValueKind.String) { audio.Play(ambience.GetString()!); }
        _Process(0);
    }
    private string Cue(string definition, string cue) => _bindings.TryGetValue(definition, out var map) && map.TryGetValue(cue, out string? id) ? id : "";
    public void Consume(ReadOnlySpan<SimEvent> events)
    {
        foreach (SimEvent ev in events)
        {
            string cue = ev.Kind switch
            {
                SimEventKind.AttackStarted => ev.Value == 1 ? "draw" : "",
                SimEventKind.AttackActive => ev.Amount switch { 1 => "attack1", 2 => "attack2", 3 => "attack3", _ => "attack" },
                SimEventKind.ProjectileSpawned => "release",
                SimEventKind.ProjectileHitTerrain => "hitTerrain",
                SimEventKind.ProjectileHitEntity => "hitEntity",
                SimEventKind.EnemyAlerted => "alert",
                SimEventKind.DamageApplied => ev.Lethal ? "death" : "hit",
                SimEventKind.RollStarted => "roll", SimEventKind.PotionUsed => "potion", SimEventKind.PickupCollected => "collect",
                SimEventKind.Footstep => "footstep",
                SimEventKind.ChestOpened => "open", SimEventKind.CheckpointActivated => "activate",
                SimEventKind.GateStateChanged => ev.Amount == (int)GatePhase.Opening ? "open" : "close", _ => ""
            };
            Vector3 position = new(ev.Position.X, ev.Y, ev.Position.Z);
            string id = cue.Length == 0 ? "" : Cue(ev.Text, cue);
            if (ev.Kind == SimEventKind.Footstep && _surfaceCues.TryGetValue(ev.SurfaceKind, out string? surfaceCue)) { id = surfaceCue; }
            if (id.Length != 0) { _audio.Play(id, position); }
            if (ev.Kind == SimEventKind.DamageApplied && ev.MeleeSwing > 0 && _session.World.Players.Count > 0)
            { _audio.Play(Cue(_session.World.Players[0].Def.Id, "meleeHit"), position); }
            if (ev.Kind == SimEventKind.PlaySound) { _audio.Play(ev.Text, _listener.GlobalPosition); }
            else if (ev.Kind == SimEventKind.MusicChange) { _audio.ChangeMusic(ev.Text); }
            else if (ev.Kind == SimEventKind.MissionComplete) { _audio.Play("core:sfx_mission_complete", _listener.GlobalPosition); }
            else if (ev.Kind == SimEventKind.MissionFailed) { _audio.Play("core:sfx_mission_failed", _listener.GlobalPosition); }
        }
    }
    public override void _Process(double delta)
    {
        if (_session is null || _session.World.Players.Count == 0) { return; }
        Entity hero = _session.World.Players[0];
        _listener.GlobalPosition = new(hero.Pos.X, hero.Y + 1, hero.Pos.Z); _listener.GlobalBasis = _camera.GlobalBasis;
        _audio.SetDucked(!hero.Alive || _session.State is SessionState.Won or SessionState.Failed);
        if (_session.State != SessionState.Running || _audio.BackgroundPaused) { return; }
        _loopTimer += (float)delta;
        if (_loopTimer < .5f) { return; } _loopTimer = 0;
        foreach (LoopSources loop in _loops)
        {
            Array.Fill(loop.Distances, float.PositiveInfinity); int count = 0;
            foreach (Entity entity in _session.World.Entities)
            {
                if (!entity.Alive || Cue(entity.Def.Id, "loop") != loop.Sound) { continue; }
                Vector3 position = new(entity.Pos.X, entity.Y, entity.Pos.Z); float distance = position.DistanceSquaredTo(_listener.GlobalPosition);
                if (distance > 30 * 30) { continue; }
                for (int index = 0; index < loop.Positions.Length; index++)
                {
                    if (distance >= loop.Distances[index]) { continue; }
                    for (int move = loop.Positions.Length - 1; move > index; move--) { loop.Positions[move] = loop.Positions[move - 1]; loop.Distances[move] = loop.Distances[move - 1]; }
                    loop.Positions[index] = position; loop.Distances[index] = distance; count = System.Math.Min(count + 1, loop.Positions.Length); break;
                }
            }
            _audio.UpdateLoopSources(loop.Sound, loop.Positions.AsSpan(0, count));
        }
    }
    public override void _ExitTree() { if (_audio is not null && GodotObject.IsInstanceValid(_audio)) { _audio.EndWorld(this); } }
}
