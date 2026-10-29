"""Enforce desktop lossless/mipmap settings, including extracted GLB textures."""
import sys
from texture_imports import ROOT, expected, rewrite, textures, validate

failures = []
inputs = textures()
for texture in inputs:
    path = texture.with_suffix(texture.suffix + ".import")
    if not path.exists():
        failures.append(f"Import once in pinned Godot, then commit {path.relative_to(ROOT)}")
        continue
    source = path.read_text(encoding="utf-8")
    relative = texture.relative_to(ROOT / "game").as_posix()
    try:
        if "--write" in sys.argv:
            changed = rewrite(source, expected(relative))
            if source != changed:
                path.write_text(changed, encoding="utf-8")
            source = changed
        validate(source, relative)
    except ValueError as error:
        failures.append(str(error))
if failures:
    raise SystemExit("\n".join(failures))
print(f"Texture import checks passed: {len(inputs)} desktop lossless inputs with explicit mipmap/disabled auto-detection policy")
