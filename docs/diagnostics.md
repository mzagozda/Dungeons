# Diagnostic catalogue

Generated from `DiagnosticCode` by `python tools/generate_diagnostics.py`.

| Code | Severity | Rule |
|---|---|---|
| LVL0001 | Error | Invalid JSON, duplicate key, encoding or maximum nesting. |
| LVL0002 | Error | Unsupported format version. |
| LVL0003 | Error | Invalid required field, type, range or schema value. |
| LVL0004 | Error | Invalid or unsafe package. |
| LVL0005 | Error | Package size or entry limit exceeded. |
| LVL0100 | Warning | Unknown forward-compatible JSON property. |
| LVL1001 | Error | Layer dimensions do not match level dimensions. |
| LVL1002 | Error | Layer character is absent from the palette. |
| LVL1003 | Error | Palette tile id is unknown. |
| LVL1004 | Error | Invalid overlay/floor tile role or blocking overlay. |
| LVL1005 | Error | Invalid hexadecimal height. |
| LVL1006 | Error | Stairs do not connect to a walkable cell exactly one unit higher. |
| LVL1010 | Error | Exactly one player spawn is required. |
| LVL1011 | Error | Unknown entity type or invalid property. |
| LVL1012 | Error | Entity is outside the level or lacks required ground. |
| LVL1013 | Error | Duplicate authored entity id. |
| LVL1014 | Warning | Monster cannot reach the player spawn. |
| LVL1020 | Error | Unknown trigger/action/objective reference. |
| LVL1021 | Error | Completion references an unknown objective or exit. |
| LVL1022 | Warning | Objective is never activated. |
| LVL1023 | Warning | entities_dead tag is never produced. |
| LVL1024 | Error | Survive objective needs positive seconds. |
| LVL1025 | Error | Gate openWhen references an unknown objective. |
| LVL1026 | Error | Spawner target must be an enemy and its marker must be walkable. |
| LVL1027 | Error | Reserved action is not supported in this version. |
| LVL1030 | Warning | Exit is unreachable under the open-gate reachability check. |
| LVL1031 | Error | Exit cannot be reached even with every gate open. |
| LVL1040 | Warning | No checkpoint is authored. |
| LVL1050 | Warning | Authored monsters exceed the live cap. |
| LVL1060 | Info | Tile/property default was applied. |
| LVL9999 | Error | Unexpected loader boundary error. |
| CNT0001 | Error | Invalid content JSON. |
| CNT1001 | Error | Duplicate content id. |
| CNT1002 | Error | Invalid definition or property schema. |
| CNT1003 | Error | Content references an unknown definition. |

Locations use JSON Pointer syntax; terrain diagnostics also carry a cell when available.

The specification gives LVL1030 and LVL1031 the same all-gates-open reachability predicate with different severities. An unreachable exit currently emits both; LVL1031 prevents play.
