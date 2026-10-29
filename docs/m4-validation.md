# M4 local hero validation — 2026-10-26

Local M4 acceptance passed under the user's instruction to proceed while platform checks remain pending. This does not close the iPhone/Mac, hosted CI, reference-GPU or clean Android rendering gates.

Run from the repository root:

```powershell
. ./tools/Common.ps1
./tools/Test-M4.ps1 -Rendered
```

Completed run: `artifacts/m4/gate-e830189da5424693b0cc04e8968cc392/result.json`. Its underlying complete Core/content/Godot/render gate is `artifacts/m3/gate-c4c6ce4620df413f861f429ba456d4dc`. These identify local evidence folders, not hosted CI runs or published builds.

- 126 Release xUnit cases pass, including optimized allocation/performance checks. Both Core targets (`net8.0`, `netstandard2.1`), Tools and Benchmarks build with zero warnings/errors. Separate coverage run: 124 cases (the two wall-clock performance cases are excluded after passing normally), Core line coverage **97.03%**, required ≥85%.
- 11 real Godot/GdUnit cases pass: the eight rendering/catalogue cases plus short input taps through the real InputMap, default gamepad action mappings, reused arrow views with new ids, independent Core transform/roll rig animation, pose reset and bound ammunition/potion/roll/scheme HUD text.
- 44 shipped JSON files validate independently against draft-2020-12 schemas; atlas, 22 referenced model variants and 22 lossless texture import inputs pass checks. Editable model sources and exported clips match.
- Rendered RTX 3060 / Mobile Vulkan bridge, stairs and ruin captures pass. Uncapped p95: **1.783 / 1.897 / 1.825 ms**; maximum **179 draws**, **69,110 triangles**, **126,576,128 texture bytes**, seven enabled local lights. This is not a reference-phone or Iris Xe measurement. This incremental import had no engine diagnostics; earlier fresh headless import logs retain the pinned engine's documented preview error.
- `roll.png`, `bow.png`, `melee.png`, `pickup.png` and corresponding logs in the M4 gate show actual production-scene states: roll tick 8 has 17 ticks remaining; bow tick 8 is in recovery with 29 arrows; melee tick 8 is active; walking east collects the authored five-emerald pile by tick 60. The capture harness uses ordinary Core commands and does not alter inventory or combat state.

The data foundation adds `content/weapons/{sword,bow}.json` and `core:arrow_hero`. `WeaponDef`, `WeaponLoadout`, `HeroRules`, `ProjectileSpec` and `PickupSpec` are immutable; authored metadata is parsed outside ticks, validated and fingerprinted. Fractional tick/amount/XP values and invalid factions are rejected. The source generator produces the same definitions. Original hand-mounted sword/bow and collectible models extend the existing editable cuboid pipeline.

Core keeps the specified stage order. Combat checks targets in ascending id order on the first active tick, queues value-only hits, and resolves them after movement. Rounding is explicitly away from zero at midpoints, with double intermediates and integer saturation. Hero grace prevents same-tick multiple hits; roll invulnerability is tested against real queued damage on every one of its 24 frames. The third swing staggers, and recovery can chain the next buffered swing. Ground movement remains capped and circle-separated while rolling.

Arrows capture owner, faction, velocity and gear multiplier at launch, retain altitude, and sweep four segments per tick against exact rounded cell bounds and hostile circles. Nearest impact wins; terrain wins equal-distance ties, then ascending entity ids. Water and friendly actors do not consume the arrow. A 512-slot pool and stable end-of-tick removal avoid spawn allocations; ids never repeat. Presentation looks actors up by id and independently reuses nodes after removal. Callers must discard references to removed pooled entities.

Recorded Core scenarios cover melee timing/arcs/reach/heights, armour/grace/death, roll cancellation and hard contacts, cooldown/potion timing, food/ammunition eligibility and caps, magnet attraction, water/terrain/projectile collision, shot lifetime/cap/reuse, aim selection/LOS/leading/mouse snap and state identity. Optimized measured loops include actual melee hits and projectile spawning/sweeps/expiration/hashing and allocate **0 bytes after warm-up**. The first projectile allocation measurement exposed insufficient path warm-up; the test now warms that actual sweep/hash path before measuring, and the full normal plus coverage runners pass.

The movement walkthrough copies the built-in level's baked terrain into an all-gates-open, enemy-free scenario, follows production A* from spawn through bridge/clearing/stairs to the exit area, and verifies the recorded stream. It does not claim mission completion, trigger processing or enemy combat. Those remain M5/M6 requirements. Likewise, current lives/death snapshots await M6 respawn/failure flow; enemy loot drops remain M5.

At the M4 checkpoint, a fresh Windows export and rendered packaged probe also passed. The packaged app launched the real level/HUD and saved `artifacts/m4/windows-app.png` with no runtime errors (`windows-app.log`). Exported executable SHA256: `41E2DA43C2BB7F39E17743D69742B390B488BCB81598BD0E66FBB4F2B664C670`. Its packaged 120-tick idle probe hash was `12137b298aa3eb62`. Later M5 changes alter simulation state and are not claimed to be in this checkpoint binary.

Roslyn MCP was unavailable (closed transport); reference searches and the pinned warning-as-error compiler/runtime gates provided the contract checks. No source has been staged, committed or pushed. Physical gamepad behaviour, touch/rebinding/settings UI, final pixel font/VFX/audio and target-device performance remain later validation. Android still reports `mWakefulness=Dozing`; the earlier wake/unlock request remains pending, and no failed window creation is counted as a rendering pass.
