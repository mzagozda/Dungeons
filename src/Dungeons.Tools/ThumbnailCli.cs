using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dungeons.Core.Level;

namespace Dungeons.Tools;

/// <summary>Invokes the desktop game host without coupling the portable tools/Core to Godot assemblies.</summary>
internal static class ThumbnailCli
{
    internal static void Capture(string level, string output, string content, LevelDocument document)
    {
        string root = FindCheckout();
        string? engine = Environment.GetEnvironmentVariable("GODOT_BIN");
        if (string.IsNullOrWhiteSpace(engine))
        {
            string pathFile = Path.Combine(root, ".toolchain/godot-path.txt");
            if (!File.Exists(pathFile)) { throw new InvalidOperationException("Install the pinned Godot .NET host with tools/Install-Godot.ps1 or set GODOT_BIN."); }
            engine = File.ReadAllText(pathFile).Trim();
        }
        engine = Path.GetFullPath(engine, root);
        if (OperatingSystem.IsWindows() && engine.EndsWith("_console.exe", StringComparison.OrdinalIgnoreCase))
        {
            string direct = engine[..^"_console.exe".Length] + ".exe"; if (File.Exists(direct)) { engine = direct; }
        }
        if (!File.Exists(engine)) { throw new FileNotFoundException("Godot .NET executable not found.", engine); }
        using JsonDocument toolchain = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/toolchain.json")));
        string expectedVersion = toolchain.RootElement.GetProperty("godotVersion").GetString()!;
        string version = Run(engine, ["--version"], root, 15).Trim();
        if (!version.StartsWith(expectedVersion + ".stable.mono.", StringComparison.Ordinal)) { throw new InvalidOperationException("PNG capture requires pinned Godot " + expectedVersion + " .NET; received " + version); }
        // Compile the current sources, rather than silently rendering with an older host assembly.
        Run(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", ["build", "game/Dungeons.Game.csproj", "-c", "Debug", "--no-restore", "-warnaserror"], root, 120);
        string temporary = Path.Combine(Path.GetTempPath(), "dungeons-thumbnail-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            string log = Run(engine, ["--audio-driver", "Dummy", "--rendering-method", "mobile", "--path", Path.Combine(root, "game"),
                "--resolution", "320x180", "--minimized", "res://editor/preview_capture.tscn", "--", "--quality=High",
                "--thumbnail-level=" + level, "--thumbnail-output=" + temporary, "--thumbnail-content=" + content,
                "--thumbnail-source-hash=" + document.SourceHash, "--thumbnail-content-hash=" + document.ContentHash], root, 60);
            if (!log.Contains("M9_THUMBNAIL=PASS width=320 height=180", StringComparison.Ordinal) || Regex.IsMatch(log, @"(?m)^(ERROR:|SCRIPT ERROR:)"))
            { throw new InvalidOperationException("The game host did not confirm thumbnail capture.\n" + log); }
            byte[] bytes = File.ReadAllBytes(temporary);
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature)
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) != 320
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) != 180)
            { throw new InvalidDataException("The game host returned an invalid 320×180 PNG."); }
            // CreateNew preserves an existing destination even if another process created it during capture.
            using Stream file = new FileStream(output, FileMode.CreateNew, FileAccess.Write); file.Write(bytes);
            Console.Write(log);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private static string FindCheckout()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "game/project.godot")) && File.Exists(Path.Combine(directory.FullName, "tools/toolchain.json"))) { return directory.FullName; }
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("PNG preview needs this source checkout and its imported Godot assets. Run from the repository after toolchain setup.");
    }

    private static string Run(string executable, string[] arguments, string directory, int timeoutSeconds)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        try
        {
            using Process process = Process.Start(start) ?? throw new IOException("Cannot start " + executable);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutSeconds * 1000))
            { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new InvalidOperationException("Thumbnail command timed out: " + executable); }
            string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) { throw new InvalidOperationException("Thumbnail command failed (" + process.ExitCode + "): " + executable + "\n" + log); }
            return log;
        }
        catch (Win32Exception error) { throw new IOException("Cannot start thumbnail dependency: " + executable, error); }
    }
}
