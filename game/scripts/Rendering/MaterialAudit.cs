using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Debug startup guard for pixel-art filtering on the actual loaded scene resources.</summary>
public static class MaterialAudit
{
    /// <summary>Throws if a loaded material violates the nearest-family sampling rule.</summary>
    public static int Check(Node root)
    {
        var checkedMaterials = new HashSet<ulong>(); int count = 0; Visit(root); return count;
        void CheckMaterial(Material? material)
        {
            if (material is null || !checkedMaterials.Add(material.GetInstanceId())) { return; }
            count++;
            if (material is BaseMaterial3D standard)
            {
                if (standard.TextureFilter is not (BaseMaterial3D.TextureFilterEnum.Nearest or BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps or BaseMaterial3D.TextureFilterEnum.NearestWithMipmapsAnisotropic))
                { throw new InvalidDataException("Linear-filtered material: " + material.ResourcePath); }
            }
            else if (material is ShaderMaterial shader)
            {
                string code = shader.Shader.Code;
                foreach (string line in code.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("uniform sampler2D", StringComparison.Ordinal) && !line.Contains("filter_nearest", StringComparison.Ordinal))
                    { throw new InvalidDataException("Shader sampler lacks explicit nearest filtering: " + shader.Shader.ResourcePath); }
                }
            }
            else { throw new InvalidDataException("Unaudited material type: " + material.GetClass()); }
        }
        void Visit(Node node)
        {
            if (node is MeshInstance3D instance && instance.Mesh is Mesh mesh)
            { for (int i = 0; i < mesh.GetSurfaceCount(); i++) { CheckMaterial(instance.MaterialOverride ?? instance.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i)); } }
            else if (node is MultiMeshInstance3D multi && multi.Multimesh?.Mesh is Mesh shared)
            { for (int i = 0; i < shared.GetSurfaceCount(); i++) { CheckMaterial(multi.MaterialOverride ?? shared.SurfaceGetMaterial(i)); } }
            foreach (Node child in node.GetChildren()) { Visit(child); }
        }
    }
}
