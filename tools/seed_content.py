"""One-time transcription of specification examples into reviewable JSON sources.

Run explicitly from the repo root; existing authored files are preserved unless --force.
Rendering/audio files referenced by this M2 data are supplied by later milestones.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SPEC = (ROOT / "specification.md").read_text(encoding="utf-8")
FORCE = "--force" in sys.argv


def write(relative, data):
    path = ROOT / "game" / relative
    if path.exists() and not FORCE:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


appendix = SPEC.split("## Appendix C — Content definition examples", 1)[1].split("## Appendix D", 1)[0]
for path, source in re.findall(r"`(content/[^`]+\.json)`\s*```json\s*\n(.*?)\n```", appendix, re.S):
    data = json.loads(source)
    if path == "content/entities/arrow_skeleton.json":
        data["faction"] = "Monster"
    write(path, data)

zombie = SPEC.split("### 11.3 Entity definition", 1)[1].split("### 11.4", 1)[0]
source = re.search(r"```jsonc\s*\n(.*?)\n```", zombie, re.S).group(1)
write("content/entities/zombie.json", json.loads(re.sub(r"\s*//[^\n]*", "", source)))

tiles = {
    "dirt_path": ("Dirt path", "path", "#8a6a3f", 0.9),
    "stone_floor": ("Stone floor", "stone_top", "#8c8c8c", 1),
    "stone_floor_cracked": ("Cracked stone floor", "stone_cracked", "#6f6f6f", 1),
    "bridge_planks": ("Bridge planks", "planks", "#8a6a3f", 1),
}
for key, (name, texture, tint, cost) in tiles.items():
    render = {"kind": "block", "top": f"atlas:{texture}", "side": "atlas:dirt_side", "tint": tint}
    if key == "bridge_planks":
        render.pop("tint")
        render.update({"thickness": 0.1, "waterBelow": True})
    write(f"content/tiles/{key}.json", {"id": f"core:{key}", "displayName": name, "category": "ground", "walkable": True, "solid": False, "moveCost": cost, "render": render})
write("content/tiles/tree_oak.json", {"id": "core:tree_oak", "displayName": "Oak tree", "category": "nature", "baseFloor": "core:grass", "walkable": False, "solid": True, "blocksProjectiles": True, "blocksVision": True, "render": {"kind": "model", "model": "models/tiles/tree_oak.glb", "variants": 3, "randomRotation": True, "castsShadow": True}})
write("content/tiles/rock.json", {"id": "core:rock", "displayName": "Rock", "category": "nature", "baseFloor": "core:grass", "walkable": False, "solid": True, "blocksProjectiles": True, "blocksVision": True, "render": {"kind": "model", "model": "models/tiles/rock.glb"}})
write("content/tiles/flowers.json", {"id": "core:flowers", "displayName": "Flowers", "category": "decoration", "overlay": True, "walkable": True, "solid": False, "render": {"kind": "cross", "texture": "atlas:flowers", "sway": 0.6}})
write("content/tiles/void.json", {"id": "core:void", "displayName": "Void", "category": "ground", "walkable": False, "solid": False, "render": {"kind": "none"}})


def entity(key, kind, *, radius=0, solid=False, props=None, **extra):
    category = {"Player": "hero", "Pickup": "pickup", "Light": "light"}.get(kind, "logic" if radius == 0 else "prop")
    write(f"content/entities/{key}.json", {"id": f"core:{key}", "displayName": key.replace("_", " ").title(), "category": category, "kind": kind, "radius": radius, "solid": solid, "requiresGround": True, "props": props or {}, **extra})


write("content/weapons/sword.json", {"id": "core:sword", "kind": "melee", "swings": [
    {"windupTicks": w, "activeTicks": a, "recoveryTicks": r, "damage": d, "knockback": k, "halfAngleDeg": angle, "reach": reach}
    for w, a, r, d, k, angle, reach in [(6, 6, 12, 12, 1, 60, 1.7), (6, 6, 12, 12, 1, 60, 1.7), (9, 7, 21, 18, 2.5, 70, 1.9)]]})
write("content/weapons/bow.json", {"id": "core:bow", "kind": "ranged", "projectile": "core:arrow_hero", "windupTicks": 6, "recoveryTicks": 12, "cooldownTicks": 30})
entity("arrow_hero", "Projectile", radius=0.15, requiresGround=False, faction="Hero", projectile={"damage": 10, "speed": 14, "lifetimeTicks": 90, "height": 1, "pierce": 0, "knockback": 0}, render={"model": "models/props/arrow.glb", "stickOnTerrain": 2})
entity("hero", "Player", radius=0.35, stats={"hp": 100, "armor": 0, "speed": 4.5}, faction="Hero", weapons={"melee": "core:sword", "ranged": "core:bow"},
       control={"rollTicks": 24, "rollDistance": 3, "rollInvulnerableFirst": 3, "rollInvulnerableLast": 20, "rollCooldownTicks": 36, "potionCooldownTicks": 1800, "arrowsStart": 30, "arrowsMax": 99, "pickupRadius": 1, "magnetRadius": 2.5, "magnetSpeed": 6, "recoveryWalk": 0.4},
       render={"model": "models/characters/hero.glb", "animations": {name: name for name in ("idle", "walk", "attack", "roll", "aim", "fire", "hit", "death")}})
entity("spawn_marker", "SpawnMarker")
entity("chest", "Chest", radius=0.35, solid=True, props={"loot": {"type": "ref", "refKind": "loot", "default": "core:loot_chest_common"}}, render={"model": "models/props/chest.glb"})
entity("emerald_pile", "Pickup", radius=0.2, props={"amount": {"type": "int", "default": 1, "min": 1, "max": 999}}, pickup={"kind": "emerald"}, render={"model": "models/props/emerald.glb"})
entity("food", "Pickup", radius=0.2, pickup={"kind": "food", "healPercent": 25}, render={"model": "models/props/food.glb"})
entity("arrow_bundle", "Pickup", radius=0.2, pickup={"kind": "arrows", "amount": 10}, render={"model": "models/props/arrow_bundle.glb"})
for key in ("prop_barrel", "prop_crate", "prop_pillar_broken"):
    entity(key, "Prop", radius=0.35, solid=True, render={"model": f"models/props/{key.removeprefix('prop_')}.glb"})
entity("light", "Light", props={"color": {"type": "color", "default": "#ffa64d"}, "radius": {"type": "float", "default": 6, "min": 1, "max": 20}, "energy": {"type": "float", "default": 2, "min": 0, "max": 8}, "flicker": {"type": "bool", "default": True}}, render={"light": True})
write("content/loot/loot_zombie.json", {"id": "core:loot_zombie", "rows": [{"item": "emerald", "chance": 0.6, "min": 1, "max": 3}, {"item": "food", "chance": 0.1}]})
write("content/loot/loot_skeleton.json", {"id": "core:loot_skeleton", "rows": [{"item": "emerald", "chance": 0.6, "min": 1, "max": 3}, {"item": "arrows", "chance": 0.5}]})

# Audio metadata is valid catalogue data; actual recordings are M8 work.
sounds = {"core:music_forest", "core:amb_forest_night", "core:sfx_ambush_horn"}
for path in (ROOT / "game/content").glob("*/*.json"):
    data = json.loads(path.read_text(encoding="utf-8"))
    sounds.update(v for v in data.get("audio", {}).values() if isinstance(v, str) and v.startswith("core:"))
write("content/audio.json", {key: {"files": [f"audio/{key.split(':')[1]}.ogg"], "volumeDb": -3, "pitchRange": [0.95, 1.05], "maxInstances": 4} for key in sorted(sounds)})
write("content/index.json", sorted(p.relative_to(ROOT / "game/content").as_posix() for p in (ROOT / "game/content").glob("*/*.json")) + ["audio.json"])
write("levels/index.json", ["mossy_hollow"])
