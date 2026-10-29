"""Generate the author-facing stable diagnostic table directly from the enum."""
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent
source = (root / "src/Dungeons.Core/Level/Diagnostic.cs").read_text(encoding="utf-8")
rows = re.findall(r"/// <summary>([^<]+)</summary>\s+((?:LVL|CNT)\d+),", source)
warnings = {"LVL0100", "LVL1014", "LVL1022", "LVL1023", "LVL1030", "LVL1040", "LVL1050"}
text = "# Diagnostic catalogue\n\nGenerated from `DiagnosticCode` by `python tools/generate_diagnostics.py`.\n\n| Code | Severity | Rule |\n|---|---|---|\n"
for description, code in rows:
    severity = "Info" if code == "LVL1060" else "Warning" if code in warnings else "Error"
    text += f"| {code} | {severity} | {description} |\n"
text += "\nLocations use JSON Pointer syntax; terrain diagnostics also carry a cell when available.\n\nThe specification gives LVL1030 and LVL1031 the same all-gates-open reachability predicate with different severities. An unreachable exit currently emits both; LVL1031 prevents play.\n"
target = root / "docs/diagnostics.md"
if "--check" in sys.argv:
    if not target.exists() or target.read_text(encoding="utf-8") != text:
        raise SystemExit("Diagnostic catalogue is stale; regenerate it.")
else:
    target.write_text(text, encoding="utf-8")
