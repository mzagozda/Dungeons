using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Two preallocated square-sprite batches, with at most 2,000 live particles across both.</summary>
public partial class WorldParticles : Node3D
{
    public const int Capacity = 2000;
    private const int BatchCapacity = Capacity / 2;
    private struct Particle
    { internal Vector3 Origin, Velocity; internal Color Tint; internal float Size, Life, Gravity; internal double Started; }
    private sealed class Batch(MultiMesh mesh)
    { internal readonly MultiMesh Mesh = mesh; internal readonly Particle[] Items = new Particle[BatchCapacity]; internal int Count, Next; }
    private readonly System.Random _random = new(0x56584631);
    private Batch _dust = null!, _sparks = null!;
    public int LiveCount => _dust.Count + _sparks.Count;
    public override void _Ready()
    { _dust = Create("Dust", "particles_dust"); _sparks = Create("Sparks", "particles_spark"); }
    private Batch Create(string name, string shader)
    {
        var mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/" + shader + ".gdshader") } }, InstanceCount = BatchCapacity, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }); return new(mesh);
    }
    public void Burst(Vector3 position, double time, bool dust, int count = 12)
    {
        Batch batch = dust ? _dust : _sparks;
        for (int i = 0; i < System.Math.Clamp(count, 0, BatchCapacity); i++)
        {
            int index;
            if (batch.Count < BatchCapacity) { index = batch.Count++; }
            else { index = batch.Next; batch.Next = (batch.Next + 1) % BatchCapacity; }
            float x = Range(-1, 1), z = Range(-1, 1);
            batch.Items[index] = new() { Origin = position, Velocity = new(x * (dust ? .8f : 2), Range(.4f, dust ? 1 : 2), z * (dust ? .8f : 2)),
                Size = Range(.1f, .25f), Life = dust ? Range(.4f, .6f) : Range(.2f, .35f), Gravity = dust ? .6f : 4, Started = time,
                Tint = (dust ? new Color(.56f, .45f, .31f, .65f) : new Color(1, Range(.65f, 1), .35f)).SrgbToLinear() };
        }
    }
    public void Advance(double time, Camera3D camera)
    { Update(_dust, time, camera.GlobalBasis); Update(_sparks, time, camera.GlobalBasis); }
    /// <summary>Adds one square to the existing bounded additive batch, without a new node or simulation RNG draw.</summary>
    public void Stream(Vector3 position, double time, AmbientParticleKind kind, bool dimmed = false)
    {
        Batch batch = _sparks; int index;
        if (batch.Count < BatchCapacity) { index = batch.Count++; } else { index = batch.Next; batch.Next = (batch.Next + 1) % BatchCapacity; }
        bool beacon = kind == AmbientParticleKind.BeaconStream;
        Color tint = beacon ? new Color(.4f, .85f, 1, dimmed ? .2f : .75f) : new Color(1, Range(.45f, .85f), .12f, .8f);
        batch.Items[index] = new() { Origin = position + new Vector3(Range(-.12f, .12f), 0, Range(-.12f, .12f)),
            Velocity = new(Range(-.08f, .08f), beacon ? Range(1.2f, 2) : Range(.45f, .8f), Range(-.08f, .08f)),
            Size = beacon ? Range(.1f, .2f) : Range(.1f, .18f), Life = beacon ? Range(.8f, 1.4f) : Range(.4f, .6f),
            Gravity = 0, Started = time, Tint = tint.SrgbToLinear() };
    }
    private static void Update(Batch batch, double time, Basis billboard)
    {
        for (int i = 0; i < batch.Count;)
        {
            Particle item = batch.Items[i]; float age = MathF.Max(0, (float)(time - item.Started));
            if (age >= item.Life) { batch.Items[i] = batch.Items[--batch.Count]; continue; }
            float fade = 1 - age / item.Life;
            batch.Mesh.SetInstanceTransform(i, new(billboard.Scaled(Vector3.One * item.Size), item.Origin + item.Velocity * age + Vector3.Down * (.5f * item.Gravity * age * age)));
            Color tint = item.Tint; tint.A *= fade; batch.Mesh.SetInstanceColor(i, tint); i++;
        }
        batch.Mesh.VisibleInstanceCount = batch.Count;
    }
    private float Range(float min, float max) => min + (max - min) * _random.NextSingle();
}
