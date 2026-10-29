using System.Globalization;
using Dungeons.Core;
using Godot;
using Dungeons.Core.Sim;

namespace Dungeons.Game;

/// <summary>M0 presentation/export probe. Contains no gameplay or simulation.</summary>
public partial class BootstrapProbe : Node3D
{
    private const string ProbePath = "res://content/bootstrap.json";
    private const string ReportPath = "user://m0-report.json";
    private const double SmokeDurationSeconds = 1.0;
    private MeshInstance3D _cube = null!;
    private Label _status = null!;
    private Label _details = null!;
    private Label _paths = null!;
    private float _rotationRadiansPerSecond;
    private double _elapsedSeconds;
    private bool _smokeTest;
    private bool _passed;
    private bool _finished;
    private string _screenshotPath = "";
    private string _failure = "";
    private bool _m2Passed;

    public override void _Ready()
    {
        _cube = GetNode<MeshInstance3D>("Cube");
        _status = GetNode<Label>("Overlay/Margin/Labels/Status");
        _details = GetNode<Label>("Overlay/Margin/Labels/Details");
        _paths = GetNode<Label>("Overlay/Margin/Labels/Paths");
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument == "--m0-smoke-test")
            {
                _smokeTest = true;
            }
            else if (argument.StartsWith("--m0-screenshot=", StringComparison.Ordinal))
            {
                _screenshotPath = argument["--m0-screenshot=".Length..];
            }
        }

        string coreName = typeof(AssemblyMarker).Assembly.GetName().Name ?? "";
        _details.Text = string.Format(CultureInfo.InvariantCulture, Tr("M0_DETAILS"), OS.GetName(), coreName);
        _paths.Text = string.Format(CultureInfo.InvariantCulture, Tr("M0_PATHS"), PlatformPaths.UserLevelsDir);
        _passed = ReadPackagedProbe() && coreName == "Dungeons.Core";
        if (_passed) { _m2Passed = RunContentRuntimeProbe(); _passed &= _m2Passed; }
        if (_passed && DirAccess.MakeDirRecursiveAbsolute(PlatformPaths.UserLevelsDir) != Error.Ok)
        {
            _failure = "Cannot create user levels directory.";
            _passed = false;
        }

        _status.Text = Tr(_passed ? "M0_JSON_OK" : "M0_JSON_FAILED");
        _status.Modulate = _passed ? new Color(0.6f, 1.0f, 0.6f) : new Color(1.0f, 0.4f, 0.4f);
        GD.Print($"M0_JSON_READ={(_passed ? "PASS" : "FAIL")} path={ProbePath} core={coreName}");
        if (!_passed)
        {
            GD.PushError(_failure);
        }
    }

    public override void _Process(double delta)
    {
        // Rotation belongs to presentation and intentionally uses render delta (§22.4.2).
        _cube.RotateY(_rotationRadiansPerSecond * (float)delta);
        _elapsedSeconds += delta;
        if (_finished || _elapsedSeconds < SmokeDurationSeconds)
        {
            return;
        }

        _finished = true;
        if (_passed && Mathf.IsZeroApprox(_cube.Rotation.Y))
        {
            _passed = false;
            _failure = "Cube did not rotate.";
        }

        if (_screenshotPath.Length > 0)
        {
            if (DisplayServer.GetName() == "headless")
            {
                _passed = false;
                _failure = "Screenshot requires a rendering display.";
            }
            else
            {
                using Image screenshot = GetViewport().GetTexture().GetImage();
                if (screenshot.SavePng(_screenshotPath) != Error.Ok)
                {
                    _passed = false;
                    _failure = "Cannot write screenshot.";
                }
            }
        }

        WriteReport();
        GD.Print($"M0_PROBE={(_passed ? "PASS" : "FAIL")} report={ProjectSettings.GlobalizePath(ReportPath)}");
        if (_smokeTest)
        {
            GetTree().Quit(_passed ? 0 : 1);
        }
    }

    private bool ReadPackagedProbe()
    {
        using Godot.FileAccess? file = Godot.FileAccess.Open(ProbePath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            _failure = $"Cannot open {ProbePath}: {Godot.FileAccess.GetOpenError()}";
            return false;
        }

        using var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
        {
            _failure = $"Invalid probe JSON: {json.GetErrorMessage()}";
            return false;
        }

        Godot.Collections.Dictionary data = json.Data.AsGodotDictionary();
        if (!data.TryGetValue("schemaVersion", out Variant version) ||
            (version.VariantType != Variant.Type.Float && version.VariantType != Variant.Type.Int) || version.AsDouble() != 1 ||
            !data.TryGetValue("probeId", out Variant id) || id.VariantType != Variant.Type.String || id.AsString() != "block_dungeons_m0" ||
            !data.TryGetValue("messageKey", out Variant key) || key.VariantType != Variant.Type.String || key.AsString() != "M0_JSON_OK" ||
            !data.TryGetValue("rotationDegreesPerSecond", out Variant degrees) ||
            (degrees.VariantType != Variant.Type.Float && degrees.VariantType != Variant.Type.Int) ||
            !double.IsFinite(degrees.AsDouble()) || degrees.AsDouble() <= 0 || degrees.AsDouble() > 360)
        {
            _failure = "Probe JSON has an invalid contract.";
            return false;
        }

        if (Tr(key.AsString()) == key.AsString())
        {
            _failure = "English translation missing from package.";
            return false;
        }

        _rotationRadiansPerSecond = Mathf.DegToRad(degrees.AsSingle());
        return true;
    }

    private void WriteReport()
    {
        var report = new Godot.Collections.Dictionary
        {
            ["milestone"] = "M0",
            ["m2ContentReplayPassed"] = _m2Passed,
            ["passed"] = _passed,
            ["failure"] = _failure,
            ["platform"] = OS.GetName(),
            ["model"] = OS.GetModelName(),
            ["engine"] = Engine.GetVersionInfo()["string"],
            // Desktop presets deliberately carry the editor feature for authoring tools.
            // The immutable template feature identifies an exported executable independently.
            ["packaged"] = OS.HasFeature("template"),
            ["display"] = DisplayServer.GetName(),
            ["jsonPath"] = ProbePath,
            ["coreAssembly"] = typeof(AssemblyMarker).Assembly.GetName().Name ?? "",
            ["userLevelsDir"] = PlatformPaths.UserLevelsDir,
            ["documentsDir"] = OS.GetSystemDir(OS.SystemDir.Documents),
            ["cubeRotationRadians"] = _cube.Rotation.Y,
            ["elapsedSeconds"] = _elapsedSeconds,
        };
        using Godot.FileAccess? file = Godot.FileAccess.Open(ReportPath, Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            _passed = false;
            _failure = "Cannot write probe report.";
            GD.PushError(_failure);
            return;
        }

        file.StoreString(Json.Stringify(report, "  "));
        file.Flush();
    }
    private bool RunContentRuntimeProbe()
    {
        try
        {
            Dungeons.Core.Content.ContentRegistry content = ContentBridge.LoadRegistry();
            IReadOnlyList<Dungeons.Core.Level.LevelDocument> levels = ContentBridge.LoadBuiltins(content);
            if (levels.Count == 0) { throw new InvalidDataException("Built-in level catalogue is empty."); }
            using (var previews = new UI.LevelPreviewPool())
            {
                var catalogue = CatalogBridge.Scan(content).Where(entry => entry.Source == Dungeons.Core.Level.LevelSource.BuiltIn).ToArray();
                foreach (var entry in catalogue)
                {
                    Texture2D? texture = previews.Get(entry);
                    if (!entry.IsPlayable || entry.Description.Length == 0 || texture is null || texture.GetSize() != new Vector2(320, 180))
                    { throw new InvalidDataException("Built-in catalogue metadata/preview failed: " + entry.Id); }
                }
                if (catalogue.Length != levels.Count) { throw new InvalidDataException("Built-in catalogue count mismatch."); }
                GD.Print($"CATALOGUE_PREVIEW=PASS count={catalogue.Length} size=320x180 packaged={OS.HasFeature("template")}");
            }
            var session = new GameSession(levels[0], 1, record: true, replayCapacity: 120);
            var input = new PlayerInput[session.World.Players.Count];
            for (int i = 0; i < 120; i++) { session.Step(input.AsSpan()); }
            var capture = new ReplayDocument(session.Recorder!.Capture(), levels[0].SourceHash, content.Hash);
            ReplayLoadResult parsed = ReplayJson.Read(ReplayJson.Write(capture));
            if (parsed.Document is null || !ReplayVerifier.Verify(new GameSession(levels[0], 1), parsed.Document).Success) { throw new InvalidDataException("Runtime replay JSON verification failed."); }
            GD.Print($"M2_CONTENT_REPLAY=PASS tiles={content.Tiles.Count} entities={content.Entities.Count} levels={levels.Count} finalHash={session.Hash():x16}");
            return true;
        }
        catch (Exception ex) { _failure = "M2 runtime content/replay probe failed: " + ex.Message; GD.PushError(_failure); return false; }
    }
}
