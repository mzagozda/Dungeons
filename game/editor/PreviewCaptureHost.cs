using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Game.Rendering;
using Godot;

namespace Dungeons.Game.Editor;

/// <summary>Desktop CLI adapter. Rendering itself lives in WorldView and is shared with future editor saves.</summary>
public partial class PreviewCaptureHost : Node
{
    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            string Arg(string name) => args.Single(argument => argument.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];
            string path = Arg("--thumbnail-level"), directory = Arg("--thumbnail-content"), output = Arg("--thumbnail-output");
            string[] manifest = JsonSerializer.Deserialize<string[]>(System.IO.File.ReadAllText(Path.Combine(directory, "index.json"))) ?? throw new InvalidDataException("Invalid content manifest.");
            ContentRegistry content = ContentRegistry.Load(manifest.Select(relative =>
            {
                if (!LevelPackage.IsSafePath(relative)) { throw new InvalidDataException("Unsafe content manifest path."); }
                return (relative, System.IO.File.ReadAllText(Path.Combine(directory, relative)));
            }));
            if (!content.IsValid || content.Hash != Arg("--thumbnail-content-hash")) { throw new InvalidDataException("Content changed before thumbnail capture."); }
            LevelLoadResult loaded;
            using (Stream input = System.IO.File.OpenRead(path))
            {
                if (path.EndsWith(".dlvl", StringComparison.OrdinalIgnoreCase))
                {
                    PackageLoadResult package = LevelPackage.Read(input);
                    if (package.Package is null) { throw new InvalidDataException(string.Join('\n', package.Diagnostics)); }
                    loaded = LevelLoader.LoadPackage(package.Package, content);
                }
                else { loaded = LevelLoader.Load(input, content); }
            }
            if (!loaded.Success || loaded.Document is not LevelDocument document || document.SourceHash != Arg("--thumbnail-source-hash"))
            { throw new InvalidDataException("Level changed or failed validation before thumbnail capture: " + string.Join('\n', loaded.Diagnostics)); }
            byte[] png = await WorldView.CapturePreview(document, 320, 180);
            using (Stream file = new FileStream(output, FileMode.CreateNew, System.IO.FileAccess.Write)) { file.Write(png); }
            GD.Print($"M9_THUMBNAIL=PASS width=320 height=180 source={document.SourceHash} content={content.Hash} renderer={RenderingServer.GetCurrentRenderingMethod()}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
