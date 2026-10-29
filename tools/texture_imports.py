"""Shared desktop import policy and isolated native mobile-import preparation."""
from __future__ import annotations

import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHARACTERS = {"hero", "zombie", "skeleton", "arrow", "emerald", "food", "arrow_bundle"}


def textures(root=ROOT):
    game = root / "game"
    return sorted([*game.joinpath("textures").rglob("*.png"), *game.joinpath("models").rglob("*.png")])


def mipmaps(relative: str) -> bool:
    path = Path(relative)
    if path.parts[:2] == ("models", "characters"):
        return False
    if path.parts[:2] == ("models", "props") and path.stem == "arrow_0":
        return False
    return not (path.parts[:2] == ("textures", "models") and path.stem in CHARACTERS)


def parameters(source: str) -> dict[str, str]:
    result, section = {}, ""
    for line in source.splitlines():
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1]
        if section == "params" and "=" in line:
            key, value = line.split("=", 1)
            if key in result:
                raise ValueError("Duplicate texture import option: " + key)
            result[key] = value
    return result


def expected(relative: str, mobile=False) -> dict[str, str]:
    return {"compress/mode": "2" if mobile else "0", "compress/high_quality": "false",
            "mipmaps/generate": str(mipmaps(relative)).lower(), "detect_3d/compress_to": "0"}


def rewrite(source: str, policy: dict[str, str]) -> str:
    present = parameters(source)
    if any(key not in present for key in policy):
        raise ValueError("Texture import options are missing; import in pinned Godot first")
    output, section = [], ""
    for line in source.splitlines():
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1]
        key = line.partition("=")[0]
        output.append(key + "=" + policy[key] if section == "params" and key in policy else line)
    return "\n".join(output) + "\n"


def validate(source: str, relative: str, mobile=False):
    actual = parameters(source)
    for key, value in expected(relative, mobile).items():
        if actual.get(key) != value:
            raise ValueError(f"{relative}: {key} must be {value}")


def sha(path: Path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()
