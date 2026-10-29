using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>Loader boundary outcome. Editing loads can retain an unplayable document with diagnostics.</summary>
public sealed class LevelLoadResult
{
    internal LevelLoadResult(LevelDocument? document, List<Diagnostic> diagnostics) { Document = document; Diagnostics = diagnostics.AsReadOnly(); }
    /// <summary>Bound document; normal loads return null on errors, editing loads can retain semantic errors.</summary>
    public LevelDocument? Document { get; }
    /// <summary>Author-facing stable errors, warnings and normalization information.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Whether a playable document was produced.</summary>
    public bool Success => Document is not null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Strict JSON loader without host filesystem assumptions.</summary>
public static class LevelLoader
{
    /// <summary>Loads UTF8 bytes, rejecting malformed encoding, duplicate keys, comments and excess nesting.</summary>
    public static LevelLoadResult Load(ReadOnlySpan<byte> bytes, ContentRegistry content, ILogger? log = null, IAssetSource? assets = null)
    {
        try
        {
            if (bytes.Length > JsonData.MaxBytes) { return Failure(DiagnosticCode.LVL0005, "Level JSON exceeds 64 MiB."); }
            return Load(new UTF8Encoding(false, true).GetString(bytes), content, log, assets);
        }
        catch (DecoderFallbackException ex) { return Failure(DiagnosticCode.LVL0001, ex.Message); }
    }
    /// <summary>Loads supplied JSON text; malformed input always returns a diagnostic result.</summary>
    public static LevelLoadResult Load(string json, ContentRegistry content, ILogger? log = null, IAssetSource? assets = null)
        => WithAssets(LoadText(json, content, log, false), assets);
    /// <summary>Loads a validated in-memory package and retains its reserved asset source.</summary>
    public static LevelLoadResult LoadPackage(LevelPackage package, ContentRegistry content, ILogger? log = null)
    {
        if (package is null) { throw new ArgumentNullException(nameof(package)); }
        return Load(package.Entries["level.json"].Span, content, log, package.Assets);
    }
    /// <summary>Loads a structurally valid editable source, retaining semantic diagnostics for repair.
    /// Success remains false on errors, and GameSession refuses an erroneous editing document.</summary>
    public static LevelLoadResult LoadForEditing(string json, ContentRegistry content, ILogger? log = null, IAssetSource? assets = null)
        => WithAssets(LoadText(json, content, log, true), assets);
    private static LevelLoadResult WithAssets(LevelLoadResult result, IAssetSource? assets)
    {
        if (assets is null) { return result; }
        if (result.Document is not null) { result.Document.Assets = assets; }
        if (assets.Paths.Count == 0) { return result; }
        var diagnostics = result.Diagnostics.ToList(); diagnostics.Add(MemoryAssetSource.UnsupportedWarning);
        if (result.Document is not null) { result.Document.Diagnostics = diagnostics.AsReadOnly(); }
        return new(result.Document, diagnostics);
    }
    private static LevelLoadResult LoadText(string json, ContentRegistry content, ILogger? log, bool editing)
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            if (json is null || json.Length > JsonData.MaxBytes) { return Failure(DiagnosticCode.LVL0005, "Missing or oversized level JSON."); }
            if (!content.IsValid) { return Failure(DiagnosticCode.LVL0003, "Content registry contains errors."); }
            JsonNode root = JsonData.Parse(json);
            if (root is not JsonObject obj) { return Failure(DiagnosticCode.LVL0003, "Level root must be an object."); }
            int version = JsonData.Integer(obj["formatVersion"], -1);
            if (version != LevelMigrations.CurrentVersion) { return Failure(DiagnosticCode.LVL0002, "Unsupported formatVersion; this build supports v1.", "/formatVersion"); }
            obj = LevelMigrations.Migrate(obj, version).AsObject();
            ValidateStructure(obj, diagnostics);
            if (HasErrors(diagnostics)) { return new(null, diagnostics); }
            var level = new LevelDocument(JsonData.Integer(obj["size"]!["width"]), JsonData.Integer(obj["size"]!["height"]), JsonData.Text(obj["id"]))
            { Source = obj, Content = content, ContentHash = content.Hash, Environment = new(obj["environment"] as JsonObject) };
            using (SHA256 sha = SHA256.Create()) { level.SourceHash = ContentRegistry.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(json))); }
            LevelValidator.BindAndValidate(level, obj, content, diagnostics, editing);
            level.Diagnostics = diagnostics.AsReadOnly(); level.EditingEnabled = editing;
            if (!HasErrors(diagnostics)) { _ = level.MissionSource; }
            return new(!editing && HasErrors(diagnostics) ? null : level, diagnostics);
        }
        catch (Exception ex) when (ex is JsonException or EncoderFallbackException or ArgumentException or InvalidOperationException or OverflowException)
        { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0001, ex.Message)); return new(null, diagnostics); }
        catch (Exception ex)
        {
            (log ?? NullLogger.Instance).Log(LogLevel.Error, "Level", ex.ToString());
            diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL9999, ex.ToString())); return new(null, diagnostics);
        }
    }
    internal static void ValidateStructure(JsonObject obj, List<Diagnostic> diagnostics)
    {
        diagnostics.AddRange(LevelSchema.Validate(obj));
        if (obj["objectives"] is JsonArray objectives)
            {
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (objectives[i] is not JsonObject objective || JsonData.Text(objective["type"]) != "survive") { continue; }
                    double seconds = JsonData.Number(objective["seconds"], double.NaN);
                    if (!double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0) { continue; }
                    string path = "/objectives/" + JsonData.Index(i) + "/seconds";
                    diagnostics.RemoveAll(d => d.JsonPath == path); diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL1024, "Survive objective requires positive finite seconds.", path));
                }
            }
            // Reserved actions have their own diagnostic instead of a generic enum failure.
            if (obj["triggers"] is JsonArray triggers)
            {
                for (int t = 0; t < triggers.Count; t++)
                {
                    if (triggers[t] is not JsonObject trigger || trigger["actions"] is not JsonArray actions) { continue; }
                    for (int a = 0; a < actions.Count; a++)
                    {
                        if (actions[a] is not JsonObject action || JsonData.Text(action["type"]) is not ("camera_focus" or "dialogue")) { continue; }
                        string path = "/triggers/" + JsonData.Index(t) + "/actions/" + JsonData.Index(a) + "/type";
                        diagnostics.RemoveAll(d => d.JsonPath == path); diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL1027, "Reserved action is not supported in this version.", path));
                    }
                }
            }
    }
    /// <summary>Loads a bounded stream without owning/closing it.</summary>
    public static LevelLoadResult Load(Stream stream, ContentRegistry content, ILogger? log = null, IAssetSource? assets = null)
    {
        try
        {
            using var copy = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
            { if (copy.Length + count > JsonData.MaxBytes) { return Failure(DiagnosticCode.LVL0005, "Level JSON exceeds 64 MiB."); } copy.Write(buffer, 0, count); }
            return Load(copy.ToArray().AsSpan(), content, log, assets);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException) { return Failure(DiagnosticCode.LVL0001, ex.Message); }
    }
    internal static LevelLoadResult Failure(DiagnosticCode code, string message, string path = "") => new(null, [new(DiagnosticSeverity.Error, code, message, path)]);
    private static bool HasErrors(List<Diagnostic> diagnostics) => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
