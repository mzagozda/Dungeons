using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Godot;
using Environment = Godot.Environment;

namespace Dungeons.Game.Editor;

/// <summary>Source-only asset tool. Captures existing game models/meshes and editable marker SVGs.</summary>
public partial class PaletteIconCapture : Node
{
    private readonly ModelLibrary _models = new();
    private Color[] _palette = [];
    private string _root = "", _repository = "";
    public override async void _Ready()
    {
        try
        {
            if (!OS.GetCmdlineUserArgs().Contains("--write-editor-icons")) { throw new ArgumentException("Explicit --write-editor-icons is required to regenerate source assets."); }
            if (DisplayServer.GetName() == "headless") { throw new NotSupportedException("Palette icons require a real renderer."); }
            _root = ProjectSettings.GlobalizePath("res://"); _repository = Path.GetFullPath(Path.Combine(_root, ".."));
            _palette = System.IO.File.ReadLines(Path.Combine(_repository, "tools/palette.gpl"))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length >= 3 && parts.Take(3).All(part => byte.TryParse(part, out _)))
                .Select(parts => Color.Color8(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]))).ToArray();
            if (_palette.Length != 48) { throw new InvalidDataException("The original 48-colour palette is required."); }
            ContentRegistry content = ContentBridge.LoadRegistry(); var records = new List<object>();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var tile in content.Tiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            { records.Add(await Capture(tile.Key, tile.Value.Data, TileSubject(tile.Value, content))); }
            foreach (var entity in content.Entities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                DefinitionData data = entity.Value.Data;
                records.Add(await Capture(entity.Key, data, data.Json.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("model", out JsonElement model)
                    ? Model(model.GetString()!, entity.Value.Kind is EntityKind.Player or EntityKind.Enemy) : null));
            }
            var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string directory in new[] { "game/content", "game/models", "game/textures", "game/shaders", "assets/source/editor-icons" })
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(_repository, directory), "*", SearchOption.AllDirectories))
                { if (Path.GetExtension(file) is ".json" or ".glb" or ".png" or ".gdshader" or ".gdshaderinc" or ".svg") { AddInput(file); } }
            }
            foreach (string file in new[] { "tools/palette.gpl", "game/editor/PaletteIconCapture.cs", "game/scripts/Rendering/ChunkMesher.cs", "game/scripts/Rendering/RenderMaterials.cs", "game/scripts/Rendering/ModelLibrary.cs" })
            { AddInput(Path.Combine(_repository, file)); }
            var manifest = new { formatVersion = 1, contentHash = content.Hash, width = 32, height = 32, paletteColors = 48,
                renderer = RenderingServer.GetCurrentRenderingMethod(), inputs, icons = records };
            System.IO.File.WriteAllText(Path.Combine(_root, "editor/icons/manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            GD.Print($"M9_PALETTE_ICONS=PASS count={records.Count} content={content.Hash}"); GetTree().Quit();
            void AddInput(string file) => inputs[Path.GetRelativePath(_repository, file).Replace('\\', '/')] = Hash(file);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private Node3D? TileSubject(TileDef tile, ContentRegistry content)
    {
        JsonElement render = tile.Data.Json.GetProperty("render"); string kind = render.GetProperty("kind").GetString()!;
        if (render.TryGetProperty("model", out JsonElement model)) { return Model(model.GetString()!); }
        if (kind == "none") { return null; }
        bool stairs = kind == "stairs";
        JsonNode reference = tile.Overlay ? new JsonObject { ["tile"] = "core:grass", ["overlay"] = tile.Id } : JsonValue.Create(tile.Id)!;
        var source = new JsonObject
        {
            ["formatVersion"] = 1, ["id"] = "preview.icon", ["name"] = "Icon fixture", ["author"] = "Block Dungeons", ["version"] = "1.0.0",
            ["size"] = new JsonObject { ["width"] = 8, ["height"] = 8 },
            ["palette"] = new JsonObject { ["G"] = "core:grass", ["V"] = "core:void", ["T"] = reference },
            ["layers"] = new JsonObject { ["floor"] = new JsonArray("GGVVVVVV", stairs ? "VVGVVVVV" : "VVVVVVVV", "VVTVVVVV", "VVVVVVVV", "VVVVVVVV", "VVVVVVVV", "VVVVVVVV", "VVVVVVVV"),
                ["heights"] = new JsonArray("00000000", stairs ? "00100000" : "00000000", "00000000", "00000000", "00000000", "00000000", "00000000", "00000000") },
            ["entities"] = new JsonArray(new JsonObject { ["id"] = "spawn", ["type"] = "core:player_spawn", ["x"] = .5, ["z"] = .5 }, new JsonObject { ["id"] = "exit", ["type"] = "core:exit", ["x"] = 1.5, ["z"] = .5 }),
            ["completion"] = new JsonObject { ["type"] = "exit", ["exit"] = "exit" }
        };
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content);
        if (!loaded.Success) { throw new InvalidDataException(tile.Id + ": " + string.Join('\n', loaded.Diagnostics)); }
        var snapshot = new TerrainSnapshot(loaded.Document!, content, ContentBridge.ReadText("res://content/atlas.json"));
        ChunkGeometry geometry = ChunkMesher.Build(snapshot, 0, 0, snapshot.WaterTexture); var materials = new RenderMaterials(snapshot);
        materials.Foliage.SetShaderParameter("sway_strength", 0); materials.Water.SetShaderParameter("speed", Vector2.Zero);
        var node = new Node3D(); Add(geometry.Terrain, materials.Terrain); Add(geometry.Foliage, materials.Foliage); Add(geometry.Water, materials.Water); return node;
        void Add(MeshBuffer buffer, Material material)
        {
            var vertices = new List<TerrainVertex>(); var indices = new List<int>();
            for (int i = 0; i < buffer.Indices.Length; i += 3)
            {
                TerrainVertex a = buffer.Vertices[buffer.Indices[i]], b = buffer.Vertices[buffer.Indices[i + 1]], c = buffer.Vertices[buffer.Indices[i + 2]];
                if (!Inside(a) || !Inside(b) || !Inside(c)) { continue; }
                indices.Add(vertices.Count); indices.Add(vertices.Count + 1); indices.Add(vertices.Count + 2); vertices.Add(a); vertices.Add(b); vertices.Add(c);
            }
            if (vertices.Count > 0) { node.AddChild(new MeshInstance3D { Mesh = RenderMaterials.Commit(new(vertices.ToArray(), indices.ToArray()), material) }); }
        }
        static bool Inside(TerrainVertex vertex) => vertex.Position.X is >= 2 and <= 3 && vertex.Position.Z is >= 2 and <= 3;
    }

    private Node3D Model(string path, bool front = false)
    {
        Node3D node = _models.Actor(path); if (front) { node.Rotation = new(0, Mathf.Pi, 0); }
        foreach (Node child in Descendants(node))
        {
            if (child is AnimationPlayer animation)
            { animation.Stop(); if (animation.HasAnimation("idle")) { animation.Play("idle"); animation.Advance(0); animation.Pause(); } }
        }
        return node;
    }

    private async Task<object> Capture(string id, DefinitionData data, Node3D? subject)
    {
        string relative = data.EditorIcon;
        if (!relative.StartsWith("editor/icons/", StringComparison.Ordinal) || !relative.EndsWith(".png", StringComparison.Ordinal) || !LevelPackage.IsSafePath(relative) || data.EditorChar is null)
        { subject?.Free(); throw new InvalidDataException("Authored safe icon/character hints required: " + id); }
        string destination = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string? svg = null; Image image;
        if (subject is null)
        {
            svg = "assets/source/editor-icons/" + relative["editor/icons/".Length..^4] + ".svg";
            image = new Image(); Error error = image.LoadSvgFromString(System.IO.File.ReadAllText(Path.Combine(_repository, svg)));
            if (error != Error.Ok) { image.Dispose(); throw new InvalidDataException("Invalid original glyph SVG: " + svg); }
        }
        else
        {
            var viewport = new SubViewport { Size = new(32, 32), OwnWorld3D = true, TransparentBg = true, ProcessMode = ProcessModeEnum.Always, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            GetTree().Root.AddChild(viewport);
            try
            {
                viewport.AddChild(subject);
                viewport.AddChild(new WorldEnvironment { Environment = new Environment { BackgroundMode = Environment.BGMode.ClearColor, AmbientLightSource = Environment.AmbientSource.Color,
                    AmbientLightColor = Colors.White, AmbientLightEnergy = .65f, TonemapMode = Environment.ToneMapper.Linear } });
                viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-50, -45, 0), LightEnergy = .8f, ShadowEnabled = false });
                var camera = new Camera3D { Current = true, KeepAspect = Camera3D.KeepAspectEnum.Height }; viewport.AddChild(camera);
                Aabb? bounds = null;
                foreach (VisualInstance3D visual in Descendants(subject).OfType<VisualInstance3D>())
                {
                    Aabb local = visual.GetAabb();
                    for (int corner = 0; corner < 8; corner++)
                    { Vector3 point = visual.GlobalTransform * local.GetEndpoint(corner); bounds = bounds is Aabb previous ? previous.Expand(point) : new Aabb(point, Vector3.Zero); }
                }
                if (bounds is not Aabb box || box.Size.LengthSquared() == 0) { throw new InvalidDataException("Icon subject has no visible geometry: " + id); }
                Vector3 center = box.GetCenter(); float distance = box.Size.Length() + 4;
                camera.Position = center + new Vector3(1, 1.25f, 1).Normalized() * distance; camera.LookAt(center);
                Basis inverse = camera.GlobalBasis.Inverse(); float extent = .1f;
                for (int corner = 0; corner < 8; corner++)
                { Vector3 point = inverse * (box.GetEndpoint(corner) - center); extent = MathF.Max(extent, MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y))); }
                camera.SetOrthogonal(extent * 2.3f, .05f, distance * 3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                image = viewport.GetTexture().GetImage();
            }
            finally { GetTree().Root.RemoveChild(viewport); viewport.Free(); }
        }
        using (image)
        {
            if (image.GetWidth() != 32 || image.GetHeight() != 32) { throw new InvalidDataException("Icon must be 32×32: " + id); }
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Color pixel = image.GetPixel(x, y), best = Colors.Transparent; float minimum = float.MaxValue;
                    if (pixel.A >= .5f)
                    {
                        foreach (Color candidate in _palette)
                        { float distance = new Vector3(pixel.R - candidate.R, pixel.G - candidate.G, pixel.B - candidate.B).LengthSquared(); if (distance < minimum) { best = candidate; minimum = distance; } }
                    }
                    image.SetPixel(x, y, best);
                }
            }
            if (image.SavePng(destination) != Error.Ok) { throw new IOException("Cannot save " + destination); }
        }
        GD.Print("M9_ICON " + id + " " + relative);
        return new { id, path = relative, sourceSvg = svg, sha256 = Hash(destination) };
    }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(file))).ToLowerInvariant();
    private static IEnumerable<Node> Descendants(Node node)
    { yield return node; foreach (Node child in node.GetChildren()) { foreach (Node descendant in Descendants(child)) { yield return descendant; } } }
}
