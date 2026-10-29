using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Tools;

/// <summary>Seeded, engine-free simulation fuzzing. No expected hash is approved or replaced.</summary>
internal static class SimulationFuzz
{
    internal const int Version = 2;
    internal const ulong DefaultSeed = 0x53494d46555a5aUL;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal sealed record InputRun(int Count, float MoveX, float MoveZ, float AimX, float AimZ, bool AimValid, InputButtons Pressed, InputButtons Held);
    internal sealed record Epoch(int Step, int Number, ulong Seed, string InitialHash);
    internal sealed record CaseResult(int Index, string Kind, ulong Seed, int Players, int ActualTicksPerRun, int Epochs, string FinalHash, long EntityChecks, int GateClosures);
    internal sealed record Violation(int EntityId, string Rule, string Detail);

    internal static int Run(LevelDocument authored, ContentRegistry content, string output, int cases = 32, int ticks = 10000, ulong seed = DefaultSeed, string? sourcePath = null)
    {
        if (cases is < 2 or > 128 || ticks is < 1 or > 100000) { throw new ArgumentOutOfRangeException(nameof(cases), "Use 2..128 cases and 1..100000 ticks per case."); }
        if (Directory.Exists(output) || File.Exists(output)) { throw new IOException("Simulation fuzz evidence directory must be new."); }
        Directory.CreateDirectory(output);
        if (sourcePath is not null) { File.Copy(sourcePath, Path.Combine(output, "source" + Path.GetExtension(sourcePath)), false); }
        var timer = Stopwatch.StartNew(); var results = new List<CaseResult>();
        Write(Path.Combine(output, "identity.json"), new
        {
            generatorVersion = Version, suiteSeed = seed, cases, ticksPerRun = ticks, authored.Id, authored.SourceHash,
            contentHash = content.Hash, coreSha256 = Sha(typeof(GameSession).Assembly.Location), toolsSha256 = Sha(typeof(SimulationFuzz).Assembly.Location),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            physicalAcceptance = false, soakAcceptance = false,
        });
        try
        {
            for (int index = 0; index < cases; index++)
            {
                ulong caseSeed = CaseSeed(seed, index); bool generated = index % 2 != 0;
                bool gates = generated && index % 10 == 9;
                LevelDocument level = gates ? GenerateGates(authored, content, caseSeed, index / 2) : generated ? Generate(content, caseSeed, index / 2) : authored;
                string folder = Path.Combine(output, $"case-{index:D3}"); Directory.CreateDirectory(folder);
                CaseResult result = RunCase(level, content, caseSeed, ticks, index, gates ? "generated-gates" : generated ? "generated" : "mossy-hollow", folder);
                results.Add(result); Console.WriteLine($"SIM-FUZZ case={index} kind={result.Kind} seed={caseSeed} ticks={ticks} epochs={result.Epochs} gateClosures={result.GateClosures} hash={result.FinalHash}");
            }
            Write(Path.Combine(output, "result.json"), new
            {
                passed = true, simulationFuzzAcceptance = ticks >= 10000 && results.Any(result => result.Kind == "generated-gates" && result.GateClosures > 0), cases = results,
                actualTicksAcrossBothRuns = (long)cases * ticks * 2, elapsedSeconds = timer.Elapsed.TotalSeconds,
                physicalAcceptance = false, soakAcceptance = false,
            });
            return 0;
        }
        catch (Exception exception)
        {
            Write(Path.Combine(output, "result.json"), new { passed = false, casesCompleted = results, error = exception.ToString(), elapsedSeconds = timer.Elapsed.TotalSeconds });
            throw;
        }
    }

    internal static CaseResult RunCase(LevelDocument level, ContentRegistry content, ulong seed, int ticks, int index, string kind, string folder)
    {
        var initial = GameSession.Create(level, content, seed);
        int players = initial.World.Players.Count;
        if (players is < 1 or > GameConstants.MaxPlayers) { throw new InvalidDataException("Fuzz map must contain controllable players."); }
        PlayerInput[][] inputs = Inputs(seed, ticks, players);
        if (kind == "generated-gates")
        {
            string source = LevelSerializer.Write(level);
            if (Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant() != level.SourceHash) { throw new InvalidDataException("Generated gate source is not the exact loaded identity."); }
            using var writer = new StreamWriter(new FileStream(Path.Combine(folder, "level.json"), FileMode.CreateNew)); writer.Write(source);
        }
        var cells = new List<object>();
        for (int z = 0; z < level.Height; z++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                Cell cell = level.GetCell(x, z);
                cells.Add(new { x, z, cell.Height, cell.Walkable, cell.Solid, cell.BlocksProjectiles, cell.BlocksVision, cell.StairsDir, cell.MoveCost });
            }
        }
        // Save the whole sanitized stream before stepping. Together with the archived
        // authored source/content identity this preserves even an exception's prefix.
        Write(Path.Combine(folder, "plan.json"), new
        {
            generatorVersion = Version, index, kind, seed, ticks, level.Id, level.SourceHash, contentHash = content.Hash,
            level.Width, level.Height, cells,
            placements = level.Entities.Select(e => new { type = e.Definition.Id, e.Id, x = e.Position.X, z = e.Position.Z, facingX = e.Facing.X, facingZ = e.Facing.Z, e.Tags, e.Properties }),
            inputs = inputs.Select(Compress).ToArray(),
        });
        ulong[] hashes = new ulong[ticks + 1]; SessionState[] states = new SessionState[ticks + 1];
        var epochs = new List<Epoch>(); long entityChecks = 0; int step = 0, run = 0, epoch = 0, gateClosures = 0;
        GameSession session = initial; var frame = new PlayerInput[players];
        try
        {
            for (run = 0; run < 2; run++)
            {
                session = run == 0 ? initial : GameSession.Create(level, content, seed); epoch = 0;
                AssertWorld(session.World); ulong initialHash = session.Hash();
                if (run == 0) { hashes[0] = initialHash; states[0] = session.State; }
                else if (hashes[0] != initialHash || states[0] != session.State) { throw new InvalidDataException("Initial simulation state differs between runs."); }
                if (run == 0) { epochs.Add(new(0, 0, seed, initialHash.ToString("x16"))); }
                for (step = 0; step < ticks; step++)
                {
                    if (session.State is SessionState.Won or SessionState.Failed)
                    {
                        // Terminal calls are not simulation ticks. Start a fresh, recorded
                        // epoch so every requested step actually exercises the systems.
                        epoch++; ulong epochSeed = CaseSeed(seed, epoch);
                        session = GameSession.Create(level, content, epochSeed); AssertWorld(session.World);
                        if (run == 0) { epochs.Add(new(step, epoch, epochSeed, session.Hash().ToString("x16"))); }
                    }
                    for (int player = 0; player < players; player++) { frame[player] = inputs[player][step]; }
                    int before = session.Tick; session.Step(frame);
                    if (session.Tick != before + 1) { throw new InvalidDataException("Fuzz step did not advance exactly one simulation tick."); }
                    ReadOnlySpan<SimEvent> events = session.DrainEvents();
                    if (run == 0) { foreach (SimEvent e in events) { if (e.Kind == SimEventKind.GateStateChanged && e.Amount == (int)GatePhase.Closing) { gateClosures++; } } }
                    AssertWorld(session.World); entityChecks += session.World.Entities.Count;
                    ulong hash = session.Hash();
                    if (run == 0) { hashes[step + 1] = hash; states[step + 1] = session.State; }
                    else if (hashes[step + 1] != hash || states[step + 1] != session.State)
                    { throw new InvalidDataException($"Determinism mismatch: expected {hashes[step + 1]:x16}/{states[step + 1]}, actual {hash:x16}/{session.State}."); }
                }
            }
            if (ticks >= 10000 && kind == "generated-gates" && gateClosures == 0) { throw new InvalidDataException("Generated gate case did not exercise a closing transition."); }
            var result = new CaseResult(index, kind, seed, players, ticks, epochs.Count, hashes[^1].ToString("x16"), entityChecks, gateClosures);
            Write(Path.Combine(folder, "epochs.json"), epochs); Write(Path.Combine(folder, "result.json"), result);
            return result;
        }
        catch (Exception exception)
        {
            Write(Path.Combine(folder, "epochs.json"), epochs);
            Write(Path.Combine(folder, "failure.json"), new
            {
                run, step, epoch, tick = session.Tick, state = session.State, expectedHash = hashes[System.Math.Min(step + 1, ticks)].ToString("x16"),
                actualHash = SafeHash(session), violation = Inspect(session.World), error = exception.ToString(),
                entities = session.World.Entities.Select(e => new { e.Id, type = e.Def.Id, e.Kind, x = Bits(e.Pos.X), z = Bits(e.Pos.Z), y = Bits(e.Y), e.Alive, hp = e.Health.Current }),
            });
            throw;
        }
    }

    internal static Violation? Inspect(World world)
    {
        foreach (Entity entity in world.Entities)
        {
            if (!entity.Pos.IsFinite || !entity.PrevPos.IsFinite || !float.IsFinite(entity.Y) || !float.IsFinite(entity.PrevY) || !entity.Vel.IsFinite || !entity.Knockback.IsFinite || !entity.Facing.IsFinite || !entity.Projectile.Velocity.IsFinite)
            { return new(entity.Id, "finite", "Position, previous transform, facing or velocity is non-finite."); }
            if (entity.Pos.X < 0 || entity.Pos.Z < 0 || entity.Pos.X >= world.Grid.Width || entity.Pos.Z >= world.Grid.Height)
            { return new(entity.Id, "bounds", $"Centre {entity.Pos} outside {world.Grid.Width}x{world.Grid.Height}."); }
            if (entity.Alive && entity.Health.Current < 0) { return new(entity.Id, "health", "Living entity has negative HP."); }
            // Static props/markers own terrain footprints (a closed gate is solid by
            // definition). Corpses no longer collide. Dynamic entities must not be
            // embedded in solid terrain; airborne projectiles use their height.
            if (!entity.Alive || entity.Kind is not (EntityKind.Player or EntityKind.Enemy or EntityKind.Pickup or EntityKind.Projectile)) { continue; }
            for (int z = (int)MathF.Floor(entity.Pos.Z - entity.Radius); z <= (int)MathF.Floor(entity.Pos.Z + entity.Radius); z++)
            {
                for (int x = (int)MathF.Floor(entity.Pos.X - entity.Radius); x <= (int)MathF.Floor(entity.Pos.X + entity.Radius); x++)
                {
                    bool inside = world.Grid.Contains(x, z); Cell cell = world.Grid[x, z];
                    bool blocked = !inside || cell.Solid;
                    if (entity.Kind == EntityKind.Projectile) { blocked = !inside || cell.BlocksProjectiles || cell.Height >= entity.Y; }
                    if (!blocked) { continue; }
                    float dx = entity.Pos.X - System.Math.Clamp(entity.Pos.X, x, x + 1), dz = entity.Pos.Z - System.Math.Clamp(entity.Pos.Z, z, z + 1);
                    float penetration = entity.Radius - MathF.Sqrt(dx * dx + dz * dz);
                    if (penetration > .001f) { return new(entity.Id, "solid", $"{entity.Def.Id} overlaps cell ({x},{z}) by {penetration:R}."); }
                }
            }
        }
        return null;
    }

    private static void AssertWorld(World world)
    {
        if (Inspect(world) is Violation failure) { throw new InvalidDataException($"Entity {failure.EntityId}, {failure.Rule}: {failure.Detail}"); }
    }

    internal static LevelDocument Generate(ContentRegistry content, ulong seed, int variant)
    {
        var random = new Rng(seed ^ 0x4d4150UL); int size = new[] { 8, 16, 32, 64 }[variant % 4];
        var level = new LevelDocument(size, size, $"fuzz.map-{seed:x16}");
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                int choice = random.NextInt(0, 100);
                Cell cell = choice < 15 ? Cell.Wall : choice < 22 ? new(walkable: false, moveCost: 1) : choice < 30 ? new(height: 1, moveCost: 1) : new(moveCost: choice % 3 + 1);
                // Two continuous corridors keep the random fields connected to a useful
                // combat arena. Open map edges exercise the outside-world boundary too.
                if (x is 1 or 2 || z is 1 or 2) { cell = Cell.Floor; }
                level.SetCell(x, z, cell);
            }
        }
        // A consistent rise/landing pair among the random islands exercises stairs.
        level.SetCell(size - 3, size - 3, new(stairsDir: StairsDirection.East, moveCost: 1));
        level.SetCell(size - 2, size - 3, new(height: 1, moveCost: 1));
        int players = 1 + variant % GameConstants.MaxPlayers;
        for (int player = 0; player < players; player++) { level.AddEntity(new(content.Entities["core:hero"], new(1.5f + player, 1.5f), $"hero-{player}")); level.SetCell(1 + player, 1, Cell.Floor); }
        int enemies = System.Math.Min(32, size / 2);
        for (int enemy = 0; enemy < enemies; enemy++)
        {
            int x = random.NextInt(1, size - 1), z = random.NextInt(3, size - 1); level.SetCell(x, z, Cell.Floor);
            level.AddEntity(new(content.Entities[enemy % 2 == 0 ? "core:zombie" : "core:skeleton"], new(x + .5f, z + .5f), $"enemy-{enemy}"));
        }
        string[] pickups = ["core:emerald_pile", "core:food", "core:arrow_bundle"];
        for (int item = 0; item < 3; item++) { level.AddEntity(new(content.Entities[pickups[item]], new(1.5f, 2.5f + item), $"pickup-{item}")); level.SetCell(1, 2 + item, Cell.Floor); }
        return level;
    }

    internal static LevelDocument GenerateGates(LevelDocument template, ContentRegistry content, ulong seed, int variant)
    {
        int size = new[] { 8, 16, 32, 64 }[variant % 4]; var random = new Rng(seed ^ 0x47415445UL);
        JsonObject source = template.ToJsonObject(); source["id"] = "fuzz.gates_" + seed.ToString("x16"); source["name"] = "Generated gate fuzz map";
        source["size"] = new JsonObject { ["width"] = size, ["height"] = size };
        source["palette"] = new JsonObject { ["."] = "core:grass", ["#"] = "core:stone_wall", ["~"] = "core:water" };
        var floor = new JsonArray(); var heights = new JsonArray();
        for (int z = 0; z < size; z++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++)
            { int roll = random.NextInt(0, 100); row[x] = z <= 2 || x is 1 or 2 || x == size - 2 ? '.' : roll < 15 ? '#' : roll < 25 ? '~' : '.'; }
            floor.Add(new string(row)); heights.Add(new string('0', size));
        }
        source["layers"] = new JsonObject { ["floor"] = floor, ["heights"] = heights };
        source["entities"] = new JsonArray(
            new JsonObject { ["id"] = "spawn", ["type"] = "core:player_spawn", ["x"] = 1.5, ["z"] = 1.5 },
            new JsonObject { ["id"] = "gate", ["type"] = "core:gate", ["x"] = 2.5, ["z"] = 1.5, ["props"] = new JsonObject { ["state"] = "open", ["width"] = 1, ["axis"] = "x", ["interactable"] = true } },
            // Full health leaves food uncollected, exercising a live pickup when
            // random Interact edges close the gate around its collision circle.
            new JsonObject { ["type"] = "core:food", ["x"] = 2.5, ["z"] = 1.5 },
            new JsonObject { ["id"] = "exit", ["type"] = "core:exit", ["x"] = size - 1.5, ["z"] = size - 1.5 });
        source["triggers"] = new JsonArray(); source["objectives"] = new JsonArray();
        source["completion"] = new JsonObject { ["type"] = "exit", ["exit"] = "exit", ["requires"] = new JsonArray() }; source.Remove("editorHints");
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content);
        if (!loaded.Success) { throw new InvalidDataException("Generated gate map failed validation: " + string.Join('\n', loaded.Diagnostics)); }
        // Archive exact canonical bytes with a matching source identity, not only
        // baked cells: these cases also contain normal authored interaction rules.
        return LevelLoader.Load(LevelSerializer.Write(loaded.Document!), content).Document ?? throw new InvalidDataException("Canonical gate map did not reload.");
    }

    internal static PlayerInput[][] Inputs(ulong seed, int ticks, int players)
    {
        var random = new Rng(seed ^ 0x494e505554UL); var all = new PlayerInput[players][];
        for (int player = 0; player < players; player++)
        {
            all[player] = new PlayerInput[ticks]; InputButtons previous = InputButtons.None;
            for (int step = 0; step < ticks;)
            {
                Vec2 move = new(random.Range(-1.5f, 1.5f), random.Range(-1.5f, 1.5f)); Vec2 aim = new(random.Range(-1, 1), random.Range(-1, 1));
                // Host sanitization is part of the public input boundary as well.
                int special = random.NextInt(0, 32);
                if (special == 0) { move = new(float.NaN, float.PositiveInfinity); }
                if (special == 1) { aim = new(float.NegativeInfinity, float.NaN); }
                if (special is 2 or 3) { move = Vec2.Zero; }
                InputButtons held = (InputButtons)random.NextInt(0, 32); bool aimValid = random.NextInt(0, 3) != 0;
                int end = System.Math.Min(ticks, step + random.NextInt(1, 25));
                for (; step < end; step++) { all[player][step] = new(move, aim, aimValid, held & ~previous, held); previous = held; }
            }
        }
        return all;
    }

    private static InputRun[] Compress(PlayerInput[] inputs)
    {
        var runs = new List<InputRun>();
        for (int start = 0; start < inputs.Length;)
        {
            PlayerInput input = inputs[start]; int end = start + 1;
            while (end < inputs.Length && inputs[end].Equals(input)) { end++; }
            runs.Add(new(end - start, input.Move.X, input.Move.Z, input.Aim.X, input.Aim.Z, input.AimValid, input.Pressed, input.Held)); start = end;
        }
        return runs.ToArray();
    }
    internal static ulong CaseSeed(ulong seed, int index) { ulong state = unchecked(seed + (ulong)index * 0x9e3779b97f4a7c15UL); return Rng.SplitMix64(ref state); }
    private static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("x8");
    private static string SafeHash(GameSession session) { try { return session.Hash().ToString("x16"); } catch (Exception exception) { return "unavailable: " + exception.GetType().Name; } }
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Write<T>(string path, T value) { using Stream output = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(output, value, Json); }
}
