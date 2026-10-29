using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;

namespace Dungeons.Tools;

internal static class ToolCli
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("dungeons-tool M2, level format 1"); return 0; }
            if (args.Length == 0) { return Usage(); }
            if (args[0] == "schema" && !(args.Length >= 2 && args[1] == "--editor"))
            {
                if (args.Length == 1) { Console.WriteLine(LevelSchema.Json); return 0; }
                if (args.Length == 2 && args[1] == "--triggers")
                { Console.Write(TriggerSchema.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n"); return 0; }
                return Usage();
            }
            if (args[0] == "diagnostics") { PrintDiagnosticCatalogue(); return 0; }
            var arguments = args.ToList(); int index = arguments.IndexOf("--content"); string contentPath = FindContent();
            if (index >= 0) { if (index + 1 >= arguments.Count) { return Usage(); } contentPath = arguments[index + 1]; arguments.RemoveRange(index, 2); }
            ContentRegistry content = LoadContent(contentPath); Print(content.Diagnostics); if (!content.IsValid) { return 1; }
            return arguments[0] switch
            {
                "schema" when arguments.Count == 2 && arguments[1] == "--editor" => DumpEditorSchema(content),
                "validate" when arguments.Count >= 2 => Validate(arguments.Skip(1), content),
                "preview" when arguments.Count == 2 => Preview(arguments[1], content),
                "preview" when arguments.Count == 4 && arguments[2] == "--png" => PreviewPng(arguments[1], arguments[3], contentPath, content),
                "preview" when arguments.Count == 4 && arguments[1] == "--png" => PreviewPng(arguments[2], arguments[3], contentPath, content),
                "pack" when arguments.Count == 3 => Pack(arguments[1], arguments[2], content),
                "unpack" when arguments.Count == 3 => Unpack(arguments[1], arguments[2], content),
                "migrate" when arguments.Count == 3 => Migrate(arguments[1], arguments[2], content),
                "replay" when arguments.Count == 3 => Replay(arguments[1], arguments[2], null, content),
                "replay" when arguments.Count == 5 && arguments[1] == "--rerecord" => Replay(arguments[2], arguments[3], arguments[4], content),
                "replay" when arguments.Count == 5 && arguments[1] == "--record-idle" => RecordIdle(arguments[2], int.Parse(arguments[3], System.Globalization.CultureInfo.InvariantCulture), arguments[4], content),
                "fuzz" when arguments.Count is 2 or 3 => Fuzz(arguments[1], arguments.Count == 3 ? int.Parse(arguments[2], System.Globalization.CultureInfo.InvariantCulture) : 600, content),
                "sim-fuzz" when arguments.Count is >= 3 and <= 6 => FuzzSimulation(arguments, content),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or FormatException or OverflowException)
        { Console.Error.WriteLine("ERROR: " + ex.Message); return 1; }
    }
    private static int FuzzSimulation(List<string> args, ContentRegistry content)
    {
        LevelLoadResult loaded = Load(args[1], content); Print(loaded.Diagnostics);
        if (!loaded.Success) { return 1; }
        int cases = args.Count > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 32;
        int ticks = args.Count > 4 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 10000;
        ulong seed = args.Count > 5 ? ulong.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : SimulationFuzz.DefaultSeed;
        return SimulationFuzz.Run(loaded.Document!, content, args[2], cases, ticks, seed, args[1]);
    }
    private static int Usage()
    { Console.Error.WriteLine("dungeons-tool validate <file|dir>... | preview <level.json|dlvl> [--png <new-output.png>] | pack <dir> <out.dlvl> | unpack <in.dlvl> <new-dir> | migrate <in> <new-out.json> | replay <level> <dreplay> | replay --rerecord <level> <dreplay> <new-out.dreplay> | schema [--triggers|--editor] | diagnostics | fuzz <level.json> [seconds=600] | sim-fuzz <level> <new-evidence-dir> [cases=32] [ticks=10000] [seed]\nOptional: --content <content-dir>. Outputs must not already exist."); return 2; }
    private static int DumpEditorSchema(ContentRegistry content)
    { Console.Write(ContentEditorCatalog.ToJson(content).ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n"); return 0; }
    private static string FindContent()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null) { string path = Path.Combine(directory.FullName, "game/content"); if (File.Exists(Path.Combine(path, "index.json"))) { return path; } directory = directory.Parent; }
        }
        return "content";
    }
    private static ContentRegistry LoadContent(string directory)
    {
        string[] manifest = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(directory, "index.json"))) ?? throw new InvalidDataException("Invalid content manifest.");
        return ContentRegistry.Load(manifest.Select(path => { if (!LevelPackage.IsSafePath(path)) { throw new InvalidDataException("Unsafe manifest path."); } return (path, File.ReadAllText(Path.Combine(directory, path))); }));
    }
    private static LevelLoadResult Load(string path, ContentRegistry content)
    {
        using Stream input = File.OpenRead(path);
        if (!path.EndsWith(".dlvl", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length > LevelPackage.MaxBytes) { return LevelLoader.Load(input, content); }
            return LevelLoader.Load(input, content, assets: ReadAssets(Path.GetDirectoryName(Path.GetFullPath(path))!, LevelPackage.MaxBytes - (int)input.Length));
        }
        PackageLoadResult package = LevelPackage.Read(input); Print(package.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return package.Package is null ? LevelLoader.Load("null", content) : LevelLoader.LoadPackage(package.Package, content);
    }
    private static IAssetSource ReadAssets(string directory, int byteBudget)
    {
        var root = new DirectoryInfo(Path.Combine(directory, "assets")); if (!root.Exists) { return MemoryAssetSource.Empty; }
        var files = new List<(string path, ReadOnlyMemory<byte> bytes)>(); var pending = new Stack<DirectoryInfo>(); pending.Push(root); long total = 0; int entries = 0;
        while (pending.TryPop(out DirectoryInfo? folder))
        {
            if (++entries > LevelPackage.MaxEntries || (folder.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Linked or excess asset directories."); }
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Linked asset files/directories."); }
                if (entry is DirectoryInfo child) { pending.Push(child); continue; }
                var file = (FileInfo)entry; if (++entries > LevelPackage.MaxEntries || (total += file.Length) > byteBudget) { throw new InvalidDataException("Asset source exceeds package limits."); }
                string path = Path.GetRelativePath(directory, file.FullName).Replace('\\', '/'); if (!LevelPackage.IsSafePath(path)) { throw new InvalidDataException("Unsafe asset path."); }
                byte[] bytes = File.ReadAllBytes(file.FullName); if (bytes.LongLength != file.Length) { throw new IOException("Asset file changed during read."); } files.Add((path, bytes));
            }
        }
        return new MemoryAssetSource(files);
    }
    private static int Validate(IEnumerable<string> paths, ContentRegistry content)
    {
        bool failed = false; int count = 0;
        foreach (string path in paths.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "level.json", SearchOption.AllDirectories) : new[] { path }).OrderBy(p => p, StringComparer.Ordinal))
        { LevelLoadResult result = Load(path, content); Print(result.Diagnostics); count++; Console.WriteLine((result.Success ? "VALID " : "INVALID ") + path); failed |= !result.Success; }
        if (count == 0) { Console.Error.WriteLine("No levels found."); return 1; } return failed ? 1 : 0;
    }
    private static int Preview(string path, ContentRegistry content)
    {
        LevelLoadResult result = Load(path, content); Print(result.Diagnostics); if (result.Document is not LevelDocument document) { return 1; }
        JsonArray rows = document.ToJsonObject()["layers"]!["floor"]!.AsArray(); char[][] cells = rows.Select(r => r!.GetValue<string>().ToCharArray()).ToArray();
        for (int z = 0; z < document.Height; z++)
        {
            for (int x = 0; x < document.Width; x++)
            {
                var reference = document.GetTileReference(x, z);
                string visible = reference.Overlay ?? reference.Tile;
                cells[z][x] = content.Tile(visible).Data.EditorChar ?? cells[z][x];
            }
        }
        foreach (EntityPlacement placement in document.Entities)
        {
            cells[(int)placement.Position.Z][(int)placement.Position.X] = placement.Definition.Data.EditorChar ?? '?';
        }
        Console.WriteLine(document.Name); foreach (char[] row in cells) { Console.WriteLine(row); } return 0;
    }
    private static int PreviewPng(string path, string output, string contentPath, ContentRegistry content)
    {
        if (File.Exists(output) || Directory.Exists(output)) { throw new IOException("Thumbnail destination already exists."); }
        LevelLoadResult result = Load(path, content); Print(result.Diagnostics); if (!result.Success) { return 1; }
        ThumbnailCli.Capture(Path.GetFullPath(path), Path.GetFullPath(output), Path.GetFullPath(contentPath), result.Document!);
        Console.WriteLine("PNG " + output); return 0;
    }
    private static int Pack(string directory, string output, ContentRegistry content)
    {
        LevelLoadResult level = Load(Path.Combine(directory, "level.json"), content); Print(level.Diagnostics); if (!level.Success) { return 1; }
        string root = Path.GetFullPath(directory); var files = new List<(string path, ReadOnlyMemory<byte> bytes)>(); long total = 0;
        var pending = new Stack<DirectoryInfo>(); pending.Push(new(root));
        while (pending.TryPop(out DirectoryInfo? folder))
        {
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Linked directories cannot be packed."); }
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Linked files cannot be packed."); }
                if (entry is DirectoryInfo child) { pending.Push(child); continue; }
                var file = (FileInfo)entry; if (files.Count == LevelPackage.MaxEntries || (total += file.Length) > LevelPackage.MaxBytes) { throw new InvalidDataException("Package exceeds limits."); }
                files.Add((Path.GetRelativePath(root, file.FullName).Replace('\\', '/'), File.ReadAllBytes(file.FullName)));
            }
        }
        PackageLoadResult package = LevelPackage.Create(files); Print(package.Diagnostics.Where(diagnostic => !level.Diagnostics.Contains(diagnostic))); if (package.Package is null) { return 1; }
        using Stream destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write); package.Package.Write(destination); return 0;
    }
    private static int Unpack(string input, string output, ContentRegistry content)
    {
        using Stream source = File.OpenRead(input); PackageLoadResult package = LevelPackage.Read(source); Print(package.Diagnostics); if (package.Package is null) { return 1; }
        LevelLoadResult level = LevelLoader.LoadPackage(package.Package, content); Print(level.Diagnostics.Where(diagnostic => !package.Diagnostics.Contains(diagnostic))); if (!level.Success) { return 1; }
        string root = Path.GetFullPath(output); if (Directory.Exists(root) || File.Exists(root)) { throw new IOException("Unpack destination already exists."); }
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinations = package.Package.Entries.Select(e => (path: Path.GetFullPath(Path.Combine(root, e.Key)), data: e.Value)).ToArray();
        if (destinations.Any(e => !e.path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) { throw new InvalidDataException("Archive path escaped the destination."); }
        Directory.CreateDirectory(root);
        foreach (var entry in destinations) { Directory.CreateDirectory(Path.GetDirectoryName(entry.path)!); using Stream file = new FileStream(entry.path, FileMode.CreateNew, FileAccess.Write); file.Write(entry.data.Span); }
        return 0;
    }
    private static int Migrate(string input, string output, ContentRegistry content)
    {
        LevelLoadResult level = Load(input, content); Print(level.Diagnostics); if (level.Document is null) { return 1; }
        using var file = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)); file.WriteLine(level.Document.ToJson()); return 0;
    }
    private static int Fuzz(string path, int seconds, ContentRegistry content)
    {
        if (seconds <= 0 || seconds > 3600) { throw new ArgumentOutOfRangeException(nameof(seconds)); }
        string source = File.ReadAllText(path); JsonObject original = JsonNode.Parse(source)!.AsObject(); var rng = new Random(87231);
        var timer = Stopwatch.StartNew(); int cases = 0, errors = 0; double longest = 0;
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            string mutant;
            switch (rng.Next(12))
            {
                case 0: mutant = source[..rng.Next(source.Length)]; break;
                case 1: mutant = source.Insert(rng.Next(source.Length), "\ud83d\ude08\u0001"); break;
                case 2: mutant = source.Replace("36.5", "1e9999"); break;
                case 3: mutant = source.Replace("\"formatVersion\": 1", "\"formatVersion\": null"); break;
                case 4:
                    JsonObject manyRows = original.DeepClone().AsObject(); manyRows["layers"]!["floor"] = new JsonArray(Enumerable.Range(0, 10000).Select(_ => (JsonNode?)JsonValue.Create("........")).ToArray()); mutant = manyRows.ToJsonString(); break;
                case 5: mutant = new string('[', 40) + "0" + new string(']', 40); break;
                case 6:
                    JsonObject entityCase = original.DeepClone().AsObject(); JsonArray entities = entityCase["entities"]!.AsArray();
                    if (entities.Count > 0) { entities[rng.Next(entities.Count)]!["props"] = new JsonObject { ["unknown"] = 1e300 }; } mutant = entityCase.ToJsonString(); break;
                case 7:
                    using (var malformedZip = new MemoryStream(Enumerable.Range(0, rng.Next(1, 200)).Select(_ => (byte)rng.Next(256)).ToArray()))
                    { if (LevelPackage.Read(malformedZip).Success) { throw new InvalidDataException("Random bytes unexpectedly passed ZIP structure checks."); } }
                    mutant = source.Replace("36.5", "\"NaN\""); break;
                default:
                    JsonObject root = original.DeepClone().AsObject(); string[] keys = root.Select(p => p.Key).ToArray(); string key = keys[rng.Next(keys.Length)];
                    if (rng.Next(2) == 0) { root.Remove(key); } else { root[key] = rng.Next(4) switch { 0 => JsonValue.Create("NaN"), 1 => new JsonArray(1, 2, 3), 2 => new JsonObject { ["invalid"] = true }, _ => null }; }
                    mutant = root.ToJsonString(); break;
            }
            long started = Stopwatch.GetTimestamp(); LevelLoadResult result = LevelLoader.Load(mutant, content);
            double elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds; longest = System.Math.Max(longest, elapsed); cases++; if (!result.Success) { errors++; }
            if (elapsed > 2 || result.Diagnostics.Any(d => d.Code == DiagnosticCode.LVL9999)) { Console.Error.WriteLine("FUZZ FAILURE case=" + cases + " seconds=" + elapsed); Print(result.Diagnostics); return 1; }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, seconds = timer.Elapsed.TotalSeconds, cases, rejected = errors, longestCaseSeconds = longest, seed = 87231 })); return 0;
    }
    private static int Replay(string levelPath, string replayPath, string? output, ContentRegistry content)
    {
        LevelLoadResult loaded = Load(levelPath, content); Print(loaded.Diagnostics); if (loaded.Document is not LevelDocument level) { return 1; }
        ReplayLoadResult read = ReplayJson.Read(File.ReadAllText(replayPath)); if (read.Document is not ReplayDocument replay) { Console.Error.WriteLine(read.Error); return 1; }
        if (output is null)
        {
            ReplayVerification result = ReplayVerifier.Verify(new GameSession(level, replay.Snapshot.Seed, difficulty: replay.Snapshot.Difficulty, startOverride: replay.Snapshot.StartOverride), replay);
            Console.WriteLine($"{result.Message} tick={result.DivergingTick} expected={result.ExpectedHash:x16} actual={result.ActualHash:x16}"); return result.Success ? 0 : 1;
        }
        var session = new GameSession(level, replay.Snapshot.Seed, record: true, replayCapacity: System.Math.Max(1, replay.Snapshot.TickCount), difficulty: replay.Snapshot.Difficulty, startOverride: replay.Snapshot.StartOverride);
        Span<PlayerInput> inputs = stackalloc PlayerInput[GameConstants.MaxPlayers];
        foreach (ReplayFrame frame in replay.Snapshot.Frames)
        {
            for (int p = 0; p < frame.PlayerCount; p++) { inputs[p] = frame[p]; }
            for (int tick = 0; tick < frame.Duration; tick++) { session.Step(inputs[..frame.PlayerCount]); }
        }
        string json = ReplayJson.Write(new(session.Recorder!.Capture(), level.SourceHash, content.Hash));
        using var writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)); writer.WriteLine(json);
        Console.WriteLine($"Rerecorded {session.Tick} ticks; finalHash={session.Hash():x16}. New golden fingerprints require reviewer approval before merging."); return 0;
    }
    private static int RecordIdle(string path, int ticks, string output, ContentRegistry content)
    {
        if (ticks < 0 || ticks > GameConstants.DefaultReplayTicks) { throw new ArgumentOutOfRangeException(nameof(ticks)); }
        LevelLoadResult result = Load(path, content); Print(result.Diagnostics); if (result.Document is not LevelDocument level) { return 1; }
        var session = new GameSession(level, 1, record: true, replayCapacity: System.Math.Max(1, ticks));
        var input = new PlayerInput[session.World.Players.Count];
        for (int i = 0; i < ticks; i++) { session.Step(input.AsSpan()); }
        string json = ReplayJson.Write(new(session.Recorder!.Capture(), level.SourceHash, content.Hash));
        using var file = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)); file.WriteLine(json);
        Console.WriteLine($"Recorded {ticks} idle fixture ticks, finalHash={session.Hash():x16}; this is not a gameplay clear."); return 0;
    }
    private static void Print(IEnumerable<Diagnostic> diagnostics) { foreach (Diagnostic diagnostic in diagnostics) { if (diagnostic.Severity != DiagnosticSeverity.Info) { Console.Error.WriteLine(diagnostic); } } }
    private static void PrintDiagnosticCatalogue()
    {
        string path = Path.ChangeExtension(typeof(DiagnosticCode).Assembly.Location, ".xml");
        var descriptions = File.Exists(path) ? System.Xml.Linq.XDocument.Load(path).Descendants("member").Where(e => e.Attribute("name")?.Value.StartsWith("F:Dungeons.Core.Level.DiagnosticCode.", StringComparison.Ordinal) == true)
            .ToDictionary(e => e.Attribute("name")!.Value.Split('.').Last(), e => e.Element("summary")!.Value.Trim()) : new Dictionary<string, string>();
        Console.WriteLine("# Diagnostic catalogue\n\nGenerated from `DiagnosticCode` XML documentation. Code names are stable.\n\n| Code | Rule |\n|---|---|");
        foreach (DiagnosticCode code in Enum.GetValues<DiagnosticCode>()) { Console.WriteLine($"| {code} | {descriptions.GetValueOrDefault(code.ToString(), "See specification section 10.")} |"); }
    }
}
