using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level.Commands;

/// <summary>A serializable, reversible edit of the single authored level document.</summary>
public interface ILevelCommand
{
    /// <summary>Applies a new edit or redoes an undone edit atomically. Conflicts leave the document unchanged.</summary>
    void Apply(LevelDocument document);
    /// <summary>Undoes an applied edit atomically. Undo in reverse history order.</summary>
    void Undo(LevelDocument document);
}

/// <summary>A rejected edit or history conflict, with author-facing validation diagnostics when available.</summary>
public sealed class LevelEditException : InvalidOperationException
{
    internal LevelEditException(string message, IReadOnlyList<Diagnostic>? diagnostics = null) : base(message) { Diagnostics = diagnostics ?? Array.Empty<Diagnostic>(); }
    /// <summary>Diagnostics explaining a structurally rejected edit; semantic errors remain on the editable document.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}

/// <summary>Shared transactional edit/history behavior for the built-in command set.</summary>
public abstract class LevelCommand : ILevelCommand
{
    private JsonArray? _changes;
    private string _before = "", _after = "", _beforeSource = "", _afterSource = "", _content = "";
    private bool _applied;
    internal JsonObject Operation { get; }
    /// <summary>Constructs and validates an immutable copy of a built-in operation payload.</summary>
    protected LevelCommand(string kind, JsonObject arguments)
    {
        Operation = arguments.DeepClone().AsObject(); Operation["type"] = kind;
        CommandOperations.Validate(Operation); _ = JsonData.Parse(JsonData.Stringify(Operation), 64);
    }
    internal LevelCommand(JsonObject operation)
    { Operation = operation.DeepClone().AsObject(); CommandOperations.Validate(Operation); }
    /// <summary>Stable command wire name, such as set_tile or move_entity.</summary>
    public string Kind => Operation["type"]!.GetValue<string>();
    /// <summary>Whether this instance currently has an applied edit to undo.</summary>
    public bool IsApplied => _applied;
    /// <summary>Whether this command has recorded reversible history, including after undo.</summary>
    public bool HasHistory => _changes is not null;
    /// <inheritdoc/>
    public void Apply(LevelDocument document)
    {
        RequireDocument(document);
        if (_applied) { throw new LevelEditException("This command is already applied."); }
        if (_changes is not null) { Restore(document, true); _applied = true; return; }
        JsonObject before = document.ToJsonObject(), candidate = before.DeepClone().AsObject();
        CommandOperations.Execute(Operation, candidate, document.Content!);
        JsonArray changes = JsonDelta.Between(before, candidate); string fingerprint = Fingerprint(document);
        LevelDocument next = changes.Count == 0 ? document : Bind(candidate, document.Content!, document.Assets);
        _before = fingerprint; _after = Fingerprint(next); _beforeSource = document.SourceHash; _afterSource = next.SourceHash; _content = document.ContentHash; _changes = changes;
        if (changes.Count > 0) { document.AdoptEditable(next); }
        _applied = true;
    }
    /// <inheritdoc/>
    public void Undo(LevelDocument document)
    {
        RequireDocument(document); if (!_applied || _changes is null) { throw new LevelEditException("This command has no applied edit to undo."); }
        Restore(document, false); _applied = false;
    }
    private void Restore(LevelDocument document, bool forward)
    {
        if (document.ContentHash != _content || Fingerprint(document) != (forward ? _before : _after)) { throw new LevelEditException("The document does not match this command's history; undo in reverse order."); }
        if (_changes!.Count == 0) { return; }
        JsonObject candidate = document.ToJsonObject(); JsonDelta.Apply(candidate, _changes, forward); LevelDocument next = Bind(candidate, document.Content!, document.Assets);
        if (Fingerprint(next) != (forward ? _after : _before)) { throw new LevelEditException("The stored command history is inconsistent."); }
        next.SourceHash = forward ? _afterSource : _beforeSource; document.AdoptEditable(next);
    }
    private static void RequireDocument(LevelDocument document)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        if (document.Source is null || document.Content is null) { throw new LevelEditException("Commands require a bound authored document; code-only scenarios have no package source."); }
    }
    private static LevelDocument Bind(JsonObject candidate, ContentRegistry content, IAssetSource assets)
    {
        LevelLoadResult result = LevelLoader.LoadForEditing(JsonData.Stringify(candidate), content, assets: assets);
        return result.Document ?? throw new LevelEditException("The edit would create structurally invalid level JSON.", result.Diagnostics);
    }
    private static string Fingerprint(LevelDocument document)
    { using SHA256 sha = SHA256.Create(); return ContentRegistry.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(LevelSerializer.Write(document)))); }
    internal JsonObject Envelope() => new()
    {
        ["version"] = 1, ["operation"] = Operation.DeepClone(), ["state"] = _changes is null ? "new" : _applied ? "applied" : "undone",
        ["contentHash"] = _content, ["before"] = _before, ["after"] = _after, ["beforeSource"] = _beforeSource, ["afterSource"] = _afterSource,
        ["changes"] = _changes?.DeepClone()
    };
    internal void RestoreEnvelope(JsonObject envelope)
    {
        JsonDelta.Keys(envelope, "version", "operation", "state", "contentHash", "before", "after", "beforeSource", "afterSource", "changes");
        if (!envelope.ContainsKey("changes")) { throw new JsonException("A command record needs an explicit changes field."); }
        if (envelope["version"] is not JsonValue version || !version.TryGetValue<int>(out int number) || number != 1) { throw new JsonException("Unsupported command version."); }
        string state = JsonData.Text(envelope["state"]); if (state is not ("new" or "applied" or "undone")) { throw new JsonException("Unknown command state."); }
        foreach (string key in new[] { "contentHash", "before", "after", "beforeSource", "afterSource" })
        {
            if (envelope[key] is not JsonValue text || !text.TryGetValue<string>(out string? value) || (state == "new" ? value.Length != 0 : value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))) { throw new JsonException("Invalid command identity."); }
        }
        if (state == "new") { if (envelope["changes"] is not null) { throw new JsonException("New commands cannot contain undo changes."); } return; }
        if (envelope["changes"] is not JsonArray changes) { throw new JsonException("History needs reversible changes."); }
        JsonDelta.Validate(changes); _changes = changes.DeepClone().AsArray(); _content = envelope["contentHash"]!.GetValue<string>();
        _before = envelope["before"]!.GetValue<string>(); _after = envelope["after"]!.GetValue<string>(); _beforeSource = envelope["beforeSource"]!.GetValue<string>(); _afterSource = envelope["afterSource"]!.GetValue<string>(); _applied = state == "applied";
    }
}

/// <summary>Versioned command/macro serialization, including compact undo state when the command was applied.</summary>
public static class LevelCommandSerializer
{
    /// <summary>Maximum UTF-8 history size: before/after deltas and an operation can exceed one level's 64 MiB limit.</summary>
    public const int MaxBytes = JsonData.MaxBytes * 4;
    /// <summary>Writes a built-in command with LF and stable key order. External implementations need their own codec.</summary>
    public static string Write(ILevelCommand command)
    {
        if (command is not LevelCommand known) { throw new ArgumentException("Only registered built-in commands can be serialized.", nameof(command)); }
        string json = JsonData.Stringify(known.Envelope(), true, true).Replace("\r\n", "\n") + "\n";
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) { throw new JsonException("Command history exceeds its byte limit."); } return json;
    }
    /// <summary>Reads a bounded duplicate-free v1 command or undo record; unknown fields/types are rejected.</summary>
    public static LevelCommand Read(string json)
    {
        if (json is null || json.Length > MaxBytes) { throw new JsonException("Missing or oversized command history."); }
        JsonObject envelope = JsonData.Parse(json, 64, MaxBytes) as JsonObject ?? throw new JsonException("A command record must be an object.");
        if (envelope["operation"] is not JsonObject operation) { throw new JsonException("A command needs an operation."); }
        var command = new StoredCommand(operation); command.RestoreEnvelope(envelope); return command;
    }
    private sealed class StoredCommand(JsonObject operation) : LevelCommand(operation);
}
