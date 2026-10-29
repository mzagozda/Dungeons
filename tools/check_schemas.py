"""Independent draft-2020-12 validation of every shipped content/level JSON file."""
import json
import pathlib
import sys
from jsonschema import Draft202012Validator

root = pathlib.Path(__file__).resolve().parent.parent
content_schema = json.loads((root / "src/Dungeons.Core/Content/Schema/content.v1.schema.json").read_text(encoding="utf-8"))
level_schema = json.loads((root / "src/Dungeons.Core/Level/Schema/level.v1.schema.json").read_text(encoding="utf-8"))
for schema in (content_schema, level_schema):
    Draft202012Validator.check_schema(schema)
errors = 0
count = 0
for base in (root / "game/content", root / "game/levels"):
    for path in sorted(base.rglob("*.json")):
        if base.name == "levels" and path.name == "level.json":
            schema = level_schema
        else:
            kind = {"entities": "entity", "tiles": "tile", "loot": "loot", "weapons": "weapon"}.get(path.parent.name, path.stem)
            if kind not in content_schema["$defs"]:
                print(f"No schema for {path.relative_to(root)}", file=sys.stderr)
                errors += 1
                continue
            schema = {**content_schema, "$ref": f"#/$defs/{kind}"}
        raw = path.read_bytes()
        if raw.startswith(b"\xef\xbb\xbf") or b"\r" in raw:
            print(f"{path.relative_to(root)}: shipped JSON must be UTF-8 without BOM and use LF line endings", file=sys.stderr)
            errors += 1
        data = json.loads(raw.decode("utf-8"))
        for error in Draft202012Validator(schema).iter_errors(data):
            pointer = "/" + "/".join(str(part).replace("~", "~0").replace("/", "~1") for part in error.absolute_path)
            print(f"{path.relative_to(root)} {pointer}: {error.message}", file=sys.stderr)
            errors += 1
        count += 1
print(f"JSON Schema: {count} files, {errors} errors")
raise SystemExit(1 if errors else 0)
