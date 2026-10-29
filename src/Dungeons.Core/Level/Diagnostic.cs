using Dungeons.Core.Math;

namespace Dungeons.Core.Level;

/// <summary>Stable diagnostic importance for the CLI and future editor.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Prevents play.</summary>
    Error,
    /// <summary>Playable but requires author attention.</summary>
    Warning,
    /// <summary>Informational normalization.</summary>
    Info,
}

/// <summary>Versioned diagnostic catalogue; code names are the public wire identifiers.</summary>
public enum DiagnosticCode
{
    /// <summary>Invalid JSON, duplicate key, encoding or maximum nesting.</summary>
    LVL0001,
    /// <summary>Unsupported format version.</summary>
    LVL0002,
    /// <summary>Invalid required field, type, range or schema value.</summary>
    LVL0003,
    /// <summary>Invalid or unsafe package.</summary>
    LVL0004,
    /// <summary>Package size or entry limit exceeded.</summary>
    LVL0005,
    /// <summary>Unknown forward-compatible JSON property.</summary>
    LVL0100,
    /// <summary>Layer dimensions do not match level dimensions.</summary>
    LVL1001,
    /// <summary>Layer character is absent from the palette.</summary>
    LVL1002,
    /// <summary>Palette tile id is unknown.</summary>
    LVL1003,
    /// <summary>Invalid overlay/floor tile role or blocking overlay.</summary>
    LVL1004,
    /// <summary>Invalid hexadecimal height.</summary>
    LVL1005,
    /// <summary>Stairs do not connect to a walkable cell exactly one unit higher.</summary>
    LVL1006,
    /// <summary>Exactly one player spawn is required.</summary>
    LVL1010,
    /// <summary>Unknown entity type or invalid property.</summary>
    LVL1011,
    /// <summary>Entity is outside the level or lacks required ground.</summary>
    LVL1012,
    /// <summary>Duplicate authored entity id.</summary>
    LVL1013,
    /// <summary>Monster cannot reach the player spawn.</summary>
    LVL1014,
    /// <summary>Unknown trigger/action/objective reference.</summary>
    LVL1020,
    /// <summary>Completion references an unknown objective or exit.</summary>
    LVL1021,
    /// <summary>Objective is never activated.</summary>
    LVL1022,
    /// <summary>entities_dead tag is never produced.</summary>
    LVL1023,
    /// <summary>Survive objective needs positive seconds.</summary>
    LVL1024,
    /// <summary>Gate openWhen references an unknown objective.</summary>
    LVL1025,
    /// <summary>Spawner target must be an enemy and its marker must be walkable.</summary>
    LVL1026,
    /// <summary>Reserved action is not supported in this version.</summary>
    LVL1027,
    /// <summary>Exit is unreachable under the open-gate reachability check.</summary>
    LVL1030,
    /// <summary>Exit cannot be reached even with every gate open.</summary>
    LVL1031,
    /// <summary>No checkpoint is authored.</summary>
    LVL1040,
    /// <summary>Authored monsters exceed the live cap.</summary>
    LVL1050,
    /// <summary>Tile/property default was applied.</summary>
    LVL1060,
    /// <summary>Unexpected loader boundary error.</summary>
    LVL9999,
    /// <summary>Invalid content JSON.</summary>
    CNT0001,
    /// <summary>Duplicate content id.</summary>
    CNT1001,
    /// <summary>Invalid definition or property schema.</summary>
    CNT1002,
    /// <summary>Content references an unknown definition.</summary>
    CNT1003,
}

/// <summary>Structured diagnostic with a JSON Pointer and optional terrain coordinate.</summary>
public sealed class Diagnostic(DiagnosticSeverity severity, DiagnosticCode code, string message, string jsonPath = "", IntVec2? cell = null)
{
    /// <summary>Importance.</summary>
    public DiagnosticSeverity Severity { get; } = severity;
    /// <summary>Stable code.</summary>
    public DiagnosticCode Code { get; } = code;
    /// <summary>Human-readable detail.</summary>
    public string Message { get; } = message;
    /// <summary>RFC 6901 pointer, relative to the source document.</summary>
    public string JsonPath { get; } = jsonPath;
    /// <summary>Cell to focus in the editor.</summary>
    public IntVec2? Cell { get; } = cell;
    /// <inheritdoc />
    public override string ToString() => $"{Severity} {Code} {JsonPath}: {Message}";
}
