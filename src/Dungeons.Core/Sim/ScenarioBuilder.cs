using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Code-authored tiny levels for deterministic tests/tools; never contains mission-specific rules.</summary>
public sealed class ScenarioBuilder
{
    /// <summary>Creates a flat document; add fixtures explicitly through the same document API.</summary>
    public ScenarioBuilder(int width, int height, string id = "scenario") { Document = new LevelDocument(width, height, id); }
    /// <summary>Editable source document; sessions snapshot it.</summary>
    public LevelDocument Document { get; }
    /// <summary>Paints one cell.</summary>
    public ScenarioBuilder Cell(int x, int z, Cell cell) { Document.SetCell(x, z, cell); return this; }
    /// <summary>Places a definition at a world-space point.</summary>
    public ScenarioBuilder Entity(EntityDef def, Vec2 position, string? id = null) { Document.AddEntity(new EntityPlacement(def, position, id)); return this; }
    /// <summary>Creates a running scenario without a GPU or filesystem.</summary>
    public Scenario Build(ulong seed = 1, bool record = false, int replayCapacity = GameConstants.DefaultReplayTicks, ILogger? log = null)
        => new(new GameSession(Document, seed, log, record, replayCapacity));
    /// <summary>Parses small fixture maps: . floor, # wall, ~ water, digits height, ^&gt;v&lt; stairs.</summary>
    public static ScenarioBuilder FromAscii(string ascii, string id = "scenario")
    {
        if (ascii is null) { throw new ArgumentNullException(nameof(ascii)); }
        string[] rows = ascii.Replace("\r", "").Trim('\n').Split('\n');
        var builder = new ScenarioBuilder(rows[0].Length, rows.Length, id);
        for (int z = 0; z < rows.Length; z++)
        {
            if (rows[z].Length != builder.Document.Width) { throw new ArgumentException("ASCII rows must have equal width.", nameof(ascii)); }
            for (int x = 0; x < rows[z].Length; x++)
            {
                char symbol = rows[z][x];
                Cell cell = symbol switch
                {
                    '.' => Sim.Cell.Floor, '#' => Sim.Cell.Wall, '~' => new Cell(walkable: false),
                    >= '0' and <= '9' => new Cell(height: (byte)(symbol - '0')),
                    '^' => new Cell(stairsDir: StairsDirection.North), '>' => new Cell(stairsDir: StairsDirection.East),
                    'v' => new Cell(stairsDir: StairsDirection.South), '<' => new Cell(stairsDir: StairsDirection.West),
                    _ => throw new ArgumentException("Unknown ASCII fixture cell.", nameof(ascii)),
                };
                builder.Document.SetCell(x, z, cell);
            }
        }
        return builder;
    }
}

/// <summary>Deterministic scenario driver using the production session/pathfinder.</summary>
public sealed class Scenario
{
    internal Scenario(GameSession session) { Session = session; }
    /// <summary>Session under test.</summary>
    public GameSession Session { get; }
    /// <summary>Runs a fixed count with a tick-indexed command source.</summary>
    public void Run(int ticks, Func<int, PlayerInput> input)
    {
        if (ticks < 0) { throw new ArgumentOutOfRangeException(nameof(ticks)); }
        if (input is null) { throw new ArgumentNullException(nameof(input)); }
        for (int i = 0; i < ticks; i++) { Session.Step(input(i)); }
    }
    /// <summary>Advances idle single-player time, converting seconds with the same load rule.</summary>
    public void Advance(float seconds) { for (int i = 0, count = MathUtil.SecondsToTicks(seconds); i < count; i++) { Session.Step(default(PlayerInput)); } }
    /// <summary>Issues a single latched button press.</summary>
    public void Press(InputButtons button) { Session.Step(new PlayerInput(default, pressed: button, held: button)); }
    /// <summary>Drives player slot zero via the same A* as AI, with a bounded duration.</summary>
    public bool MoveToward(Vec2 target, float maxSeconds)
    {
        if (Session.World.Players.Count == 0) { return false; }
        Entity player = Session.World.Players[0];
        var pathfinder = new AStar(Session.World.Grid);
        var path = new IntVec2[GameConstants.MaxPathWaypoints];
        int budget = MathUtil.SecondsToTicks(maxSeconds), waypoint = 0;
        PathResult route = pathfinder.FindPath(IntVec2.FromPosition(player.Pos), IntVec2.FromPosition(target), path);
        for (int i = 0; i < budget; i++)
        {
            Vec2 aim = waypoint < route.Count ? path[waypoint].Center : target;
            if ((aim - player.Pos).Length <= player.Stats.Speed * GameConstants.Dt)
            {
                if (waypoint < route.Count) { waypoint++; continue; }
                return route.ReachedGoal;
            }
            Session.Step(new PlayerInput((aim - player.Pos).Normalized));
        }
        return false;
    }
}
