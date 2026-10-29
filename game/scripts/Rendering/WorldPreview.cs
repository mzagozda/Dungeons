using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    private sealed class PreviewBinding(string definition, string model, Node3D node, ActorBinding? actor, int lightId)
    {
        internal readonly string Definition = definition, Model = model;
        internal readonly Node3D Node = node;
        internal readonly ActorBinding? Actor = actor;
        internal readonly int LightId = lightId;
        internal string Properties = "";
    }
    private readonly Dictionary<string, PreviewBinding> _previewEntities = new(StringComparer.Ordinal);
    private int _nextPreviewId = -1;
    /// <summary>Visual for an authored placement id; player_spawn selects its idle hero preview.</summary>
    public Node3D? PreviewEntity(string authoredId)
        => _previewEntities.TryGetValue("id:" + authoredId, out PreviewBinding? binding) ? binding.Node : null;
    /// <summary>Builds an authored idle preview from its bound registry, without a GameSession or runtime entities.</summary>
    public void Build(LevelDocument document)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        Build(document, document.Registry ?? throw new ArgumentException("An authored bound document is required.", nameof(document)));
    }
    /// <summary>Builds a session-free preview with an explicit matching registry.</summary>
    public void Build(LevelDocument document, ContentRegistry content)
    {
        PrepareView(document, content, null); SynchronizePreviewEntities();
        _renderFocus = _focus = new(document.Width / 2f, 0, document.Height / 2f); UpdateCamera(0); CullLights();
    }
    /// <summary>Moves the preview's camera focus; normal authored bounds still apply.</summary>
    public void FocusPreview(Vector3 focus)
    {
        if (!IsPreview) { throw new InvalidOperationException("Camera preview focus requires a session-free view."); }
        if (!focus.IsFinite()) { throw new ArgumentOutOfRangeException(nameof(focus)); }
        _renderFocus = focus; UpdateCamera(0); CullLights();
    }
    private void SynchronizePreviewEntities()
    {
        NavGrid grid = _document.BakeGrid(); var seen = new HashSet<string>(StringComparer.Ordinal);
        bool hasPlayer = _document.Entities.Any(placement => placement.Definition.Kind == EntityKind.Player);
        for (int i = 0; i < _document.Entities.Count; i++)
        {
            EntityPlacement placement = _document.Entities[i]; string key = placement.Id is { } id ? "id:" + id : "index:" + i;
            Bind(key, placement.Definition, placement.Position, placement.Facing, placement.Properties);
            if (!hasPlayer && placement.Definition.Id == "core:player_spawn" && _content.Entities.TryGetValue("core:hero", out EntityDef? hero))
            { Bind(key, hero, placement.Position, placement.Facing, DefinitionData.Empty.Json); hasPlayer = true; }
        }
        foreach (string key in _previewEntities.Keys.Where(key => !seen.Contains(key)).ToArray()) { RemovePreviewEntity(key); }
        CullLights();
        void Bind(string key, EntityDef definition, Dungeons.Core.Math.Vec2 position, Dungeons.Core.Math.Vec2 facing, JsonElement suppliedProperties)
        {
            bool rendered = definition.Data.Json.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("model", out _);
            if (!rendered && definition.Kind != EntityKind.Light) { return; }
            string path = rendered ? render.GetProperty("model").GetString()! : ""; seen.Add(key);
            if (_previewEntities.TryGetValue(key, out PreviewBinding? binding) && (binding.Definition != definition.Id || binding.Model != path))
            { RemovePreviewEntity(key); binding = null; }
            if (binding is null)
            {
                ActorBinding? actor = rendered ? CreateActor(definition, path) : null; Node3D spatial = actor?.Node ?? new Node3D(); int id = _nextPreviewId--;
                if (actor is null) { AddChild(spatial); } else { actor.Id = id; actor.Motion?.Deactivate(); actor.Animation?.Stop(); }
                spatial.Name = "PreviewEntity_" + -id;
                if (actor?.Animation is { } animation && animation.HasAnimation("idle")) { animation.Play("idle"); animation.Advance(0); animation.Pause(); }
                binding = new(definition.Id, path, spatial, actor, id); _previewEntities.Add(key, binding);
            }
            Node3D node = binding.Node; node.Position = new(position.X, grid.HeightAt(position), position.Z);
            node.Rotation = new(0, Mathf.Atan2(-facing.X, -facing.Z), 0);
            node.Scale = Vector3.One * (render.ValueKind == JsonValueKind.Object && render.TryGetProperty("scale", out JsonElement scale) ? scale.GetSingle() : 1);
            string properties = suppliedProperties.GetRawText();
            if (binding.Properties != properties)
            {
                binding.Properties = properties;
                if (definition.Kind is EntityKind.Gate or EntityKind.Chest && binding.Actor?.Animation is { } animation)
                {
                    bool open = suppliedProperties.TryGetProperty("state", out JsonElement state) && state.GetString() == "open";
                    string clip = open ? "open" : "close";
                    if (animation.HasAnimation(clip)) { animation.Play(clip); animation.Seek(animation.GetAnimation(clip).Length, true); animation.Pause(); }
                }
                RemovePreviewLight(binding.LightId); AddEntityLight(binding.LightId, definition.Kind, node.Position, suppliedProperties);
            }
            else
            {
                for (int i = 0; i < _lights.Count; i++) { if (_lights[i].entityId == binding.LightId) { _lights[i].light.Position = node.Position + Vector3.Up * 1.4f; break; } }
            }
        }
    }
    private void RemovePreviewEntity(string key)
    {
        PreviewBinding binding = _previewEntities[key]; RemovePreviewLight(binding.LightId); binding.Actor?.Motion?.Deactivate();
        RemoveChild(binding.Node); binding.Node.Free(); _previewEntities.Remove(key);
    }
    private void RemovePreviewLight(int id)
    {
        for (int i = _lights.Count - 1; i >= 0; i--)
        {
            if (_lights[i].entityId != id) { continue; }
            RemoveChild(_lights[i].light); _lights[i].light.Free(); _lights.RemoveAt(i); _lightIds.Remove(id);
        }
    }
}
