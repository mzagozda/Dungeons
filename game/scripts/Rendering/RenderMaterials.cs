using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Main-thread material/mesh commits for the worker-produced geometry.</summary>
public sealed class RenderMaterials
{
    /// <summary>Creates nearest-filtered terrain, water and foliage shader resources.</summary>
    public RenderMaterials(TerrainSnapshot source)
    {
        Texture2D atlas = GD.Load<Texture2D>("res://textures/terrain_atlas.png");
        Terrain = Make("terrain", "albedo_atlas", atlas); Foliage = Make("foliage", "albedo_atlas", atlas); Water = Make("water", "albedo_atlas", atlas);
        AtlasRect rect = source.WaterTexture; Water.SetShaderParameter("atlas_rect", new Vector4(rect.X, rect.Y, rect.Width, rect.Height));
    }
    /// <summary>Shared terrain material.</summary>
    public ShaderMaterial Terrain { get; }
    /// <summary>Shared water material.</summary>
    public ShaderMaterial Water { get; }
    /// <summary>Shared alpha-scissored foliage material.</summary>
    public ShaderMaterial Foliage { get; }
    /// <summary>Copies POD geometry into a GPU resource. Must run on the scene thread.</summary>
    public static ArrayMesh Commit(MeshBuffer data, Material material)
    {
        var mesh = new ArrayMesh(); if (data.Vertices.Length == 0) { return mesh; }
        var positions = new Vector3[data.Vertices.Length]; var normals = new Vector3[data.Vertices.Length]; var uv = new Vector2[data.Vertices.Length]; var colors = new Color[data.Vertices.Length];
        for (int i = 0; i < data.Vertices.Length; i++)
        {
            TerrainVertex v = data.Vertices[i]; positions[i] = new(v.Position.X, v.Position.Y, v.Position.Z); normals[i] = new(v.Normal.X, v.Normal.Y, v.Normal.Z);
            uv[i] = new(v.U, v.V); colors[i] = new(v.R, v.G, v.B, v.A);
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions; arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv; arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); mesh.SurfaceSetMaterial(0, material); return mesh;
    }
    /// <summary>Creates one shader material with an explicit texture sampler.</summary>
    public static ShaderMaterial Make(string shader, string textureName, Texture2D texture)
    { var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/" + shader + ".gdshader") }; material.SetShaderParameter(textureName, texture); return material; }
}
