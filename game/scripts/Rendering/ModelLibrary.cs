using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Cached static composites and rigid actor scenes. Gameplay never lives here.</summary>
public sealed class ModelLibrary
{
    private readonly Dictionary<string, ArrayMesh> _static = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackedScene> _scenes = new(StringComparer.Ordinal);
    /// <summary>Bakes a model's neutral cuboids into material-grouped surfaces for MultiMesh reuse.</summary>
    public ArrayMesh StaticMesh(string path)
    {
        if (_static.TryGetValue(path, out ArrayMesh? cached)) { return cached; }
        Node3D root = Scene(path).Instantiate<Node3D>();
        var groups = new List<(Material original, SurfaceTool tool)>();
        Visit(root, Transform3D.Identity);
        var combined = new ArrayMesh();
        foreach (var group in groups)
        {
            group.tool.SetMaterial(ForStatic(group.original)); group.tool.Commit(combined); group.tool.Dispose();
        }
        root.Free(); _static.Add(path, combined); return combined;
        void Visit(Node node, Transform3D parent)
        {
            Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
            if (node is MeshInstance3D instance && instance.Mesh is Mesh mesh)
            {
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                {
                    Material original = mesh.SurfaceGetMaterial(surface) ?? new StandardMaterial3D();
                    int group = groups.FindIndex(g => g.original == original);
                    if (group < 0) { var tool = new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles); groups.Add((original, tool)); group = groups.Count - 1; }
                    groups[group].tool.AppendFrom(mesh, surface, transform);
                }
            }
            foreach (Node child in node.GetChildren()) { Visit(child, transform); }
        }
    }
    /// <summary>Instantiates a rigid actor/animated prop; pooling is owned by the view layer.</summary>
    public Node3D Actor(string path, bool magnified = false)
    {
        var materials = new Dictionary<ulong, Material>();
        Node3D root = Scene(path).Instantiate<Node3D>(); Configure(root);
        // Core interpolation owns the wrapper; animation tracks own the imported rig.
        var wrapper = new Node3D { Name = "Actor" };
        var pose = new Node3D { Name = "VisualPose" }; wrapper.AddChild(pose); pose.AddChild(root); return wrapper;
        void Configure(Node node)
        {
            if (node is MeshInstance3D instance && instance.Mesh is Mesh mesh)
            {
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                {
                    if (mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D original) { continue; }
                    if (!materials.TryGetValue(original.GetInstanceId(), out Material? material))
                    {
                        if (original.AlbedoTexture is null) { throw new InvalidDataException("Actor model lacks its palette texture."); }
                        ShaderMaterial shader = RenderMaterials.Make(magnified ? "character" : "dither_fade", "albedo_tex", original.AlbedoTexture);
                        if (!magnified) { shader.SetShaderParameter("emission_strength", original.EmissionEnabled ? 2.5f : 0); }
                        material = shader;
                        materials.Add(original.GetInstanceId(), material);
                    }
                    instance.SetSurfaceOverrideMaterial(surface, material);
                }
            }
            if (node is AnimationPlayer player)
            {
                foreach (string name in player.GetAnimationList())
                { if (name is "idle" or "walk") { player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear; } }
            }
            foreach (Node child in node.GetChildren()) { Configure(child); }
        }
    }
    private PackedScene Scene(string path)
    { if (!_scenes.TryGetValue(path, out PackedScene? scene)) { scene = GD.Load<PackedScene>("res://" + path) ?? throw new InvalidDataException("Missing model " + path); _scenes.Add(path, scene); } return scene; }
    private static Material ForStatic(Material original)
    {
        if (original is not BaseMaterial3D standard || standard.AlbedoTexture is null) { throw new InvalidDataException("Static model must have an original palette texture."); }
        ShaderMaterial shader = RenderMaterials.Make("dither_fade", "albedo_tex", standard.AlbedoTexture);
        shader.SetShaderParameter("instance_custom_alpha", true);
        shader.SetShaderParameter("instance_custom_emission", true);
        shader.SetShaderParameter("emission_strength", standard.EmissionEnabled ? 2.5f : 0f); return shader;
    }
}
