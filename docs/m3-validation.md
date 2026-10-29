# M3 rendering evidence (in progress)

The rendering implementation and desktop gate pass. M3 remains unchecked overall until a clean Android rerun and the pending platform checks are completed. The phone is currently asleep; Vulkan cannot create a window in that state, and its firmware denies ADB wake input. Wake/unlock has been requested. The M0 iPhone/hosted-CI exception remains in force.

Desktop checkpoint `artifacts/m3/gate-456c0d3f624344f3a7a88b8f902c2b67/`: 90 Release Core tests, 96.54% Core line coverage and 8 Godot tests pass. Bridge, stairs and ruin captures pass desktop frame/draw/triangle limits; the prior exact measured table under `gate-2e21324e3e114f7c87d4a3fc0dfd8c1e/` reports 1.857/1.835/1.820 ms p95 and 142/156/133 draw calls respectively. This desktop has an RTX 3060, not the Iris Xe reference GPU.

The exported Windows real app also passes: `artifacts/m3/windows-packaged.log` / `windows-packaged.png` show six chunks, `100/100` and `Cross the river`. Its artifact SHA256 at that checkpoint was `2A9352C0225E8C013E6AF21AFE3350DC108FDE4B095EBFA7C45FB6AE6DBA041F`. Subsequent small host hardening changes are verified separately; this is checkpoint evidence rather than a claim about every later binary.

Android rendering checkpoints are **not a clean passed gate**. At native 3D scale the phone measured 16.62 ms p95; at scale 0.75 it measured 8.409 ms p95 / 8.695 ms p99, 101 draw calls, 44,272 triangles and 54,803,072 texture bytes (10 s capture). However, later PID-specific runtime-log review found `FSR1 is only available when using the Forward+ renderer`. The scale had worked via the existing supported path, but that diagnostic invalidates the run. The unsupported setting has been removed; only the 0.75 3D scale remains. Evidence: `artifacts/m3/android-4c9f309ee07e43789c7b3890cc2f70e6/`. Its original `budgetsPassed` records the numerical checks, not runtime acceptance. The updated device runner requires both the success marker and no engine errors in the exact app process log.

Implemented and checked:

- Original 48-colour palette, 17 terrain textures, bleed-padded atlas. `python tools/atlas_pack.py --check` checks byte-for-byte staleness.
- 18 referenced model variants (three oak trees, two walls), original palette sheets, editable Blockbench cuboids/rigid keyframes and corresponding GLB exports. `python tools/check_clips.py` checks referenced files, texture palette/dimensions, rigid animation, editable/GLB clip names and required nonzero clips.
- `python tools/check_imports.py` checks committed lossless/mipmap settings. Terrain/props have mipmaps; characters/arrows use nearest without mipmaps. Run once through pinned Godot before `--write` can create corrected settings.
- CPU mesher test checks all six Mossy Hollow chunks, water separation, four-step stairs, boundary cliffs, indices and deterministic vertices. Included in Core's Release suite.
- Eight real GdUnit4 runtime tests pass on Godot 4.4.1: full scene wiring (six chunks, water, hero, `100/100`, `Cross the river` from Core), render foundation, axes/movement projection, material filtering including intentional linear-material rejection, 20 extra torches capped to eight lights, catalogue IO with valid/invalid packages, 60/120/144 Hz synthetic render-cadence continuity, and changed authored sun/fog/camera-bound binding. Scene wiring also sends F3–F7 input events and verifies generated debug geometry. Evidence: `artifacts/m3/gdunit/`, `artifacts/m3/gdunit.log`. No orphan-node warnings after the explicit SceneRunner cleanup fix.
- Rendered Mobile/Vulkan preview on NVIDIA RTX 3060: six terrain chunks, four water meshes, seven lights, one hero. `artifacts/m3/preview-2.png` and `rendered.log`; no runtime errors. These are local desktop checks, not reference-device acceptance.
- Normal application boot/menu/level launch is now wired; `--level core.mossy_hollow --seed 1 --screenshot-test=<absolute.png>` renders the actual HUD and closes after 120 simulation ticks. `artifacts/m3/app-scene.png` and `app-scene.log` passed with real objective text. Explicit M0 flags retain the standalone probe; its scene change is deferred to avoid removing a node during `_Ready`.
- Original tree crowns were narrowed to their cell footprint after rendered review exposed coplanar overlaps in dense forest. Neutral static props/lights are grouped into chunk/model MultiMeshes; interactive chests/gates and rigid characters retain individual animation nodes.
- Static instances and animated gates share the dither shader; transition now spans the full 0.15 s. Completed chunk tasks release CPU mesh arrays after GPU upload. A 0.25 vignette sits below the HUD.
- Adreno captures exposed smoothed magnified terrain/prop texels despite nearest-family sampler declarations. `pixel_sample.gdshaderinc` uses exact texel fetches while magnified and retains mipmapped anisotropic sampling when minified. Retested phone captures show sharp source texels. Authored sRGB terrain tints are explicitly converted to linear vertex colours.
- Normal boot now opens the menu without running QA replays. M0 flags retain the standalone probe via a deferred scene change. Android QA sends the explicit probe flag, clears only this app's task and checks that the phone is awake before installing/launching.
- Export hardening now rejects engine errors even with exit code zero and moves an old artifact aside before export, so an old APK cannot satisfy a failed build. The first Android export failed with CS0246 because the test adapter generated a runner source in the game tree; non-Debug configurations now exclude that runner, test source and Android-generated source copies. Test/runner resources are excluded from every export preset. Exported dependency metadata contains no GdUnit/TestHost/Moq packages.

`tools/Test-M3.ps1 -Rendered` is the repeatable local runner: Release Core gate, asset/schema/import checks, Debug engine tests, actual app screenshot, and uncapped bridge/stairs/ruin captures. A headless-only run leaves rendered acceptance pending. Each run uses a unique evidence directory and records pending device/reference checks explicitly. The parser allows only the exact two-line known glTF preview diagnostic; other engine errors fail the run.

The first strict runner failed on a separate native `_EDITOR_GET` teardown diagnostic (`editor_settings.cpp:1445`). A subsequent initialized headless import was clean; this intermittent engine diagnostic is not allowlisted. The failed log is retained under `artifacts/m3/gate-fe37c0b110534ac1a54fc6ffbd9e1fbc/import.log`.

Initial profiling found this desktop display reports **32 Hz**. VSync-on samples near 31.25 ms therefore do not measure the renderer's capacity. The harness now records real monotonic frame intervals plus engine delta, prints refresh/VSync context, and supports an explicit `--perf-uncapped` benchmark mode. A 15 s uncapped ruin capture on RTX 3060 measured 1.332 ms p50 / 1.622 ms p95 / 2.130 ms p99, 170 draw calls and 53,108 triangles before the static-prop batching refinement. This is not an Iris Xe/mobile/iPhone performance claim.

Core now owns initial objective states and the sequential level-start objective/message/delay cursor used by the HUD. These mutable states are hashed. Full conditional/area/gameplay triggers remain M6 work; unfinished startup action types throw an explicit error rather than being silently ignored. Negative or unrepresentable authored tick durations are rejected during loading. Full combat, enemy animation/state, profile/settings and pixel-font UI polish remain later milestones.

Reproduce scene tests from the repository root:

```powershell
. ./tools/Common.ps1
$env:GODOT_BIN = Get-GodotPath
dotnet test game/Dungeons.Game.csproj -c Debug --settings tools/godot.runsettings --logger trx --results-directory artifacts/m3/gdunit
```

GdUnit4 API 5.0.0 / adapter 3.0.0 are development-only Debug dependencies; Release and ExportDebug exclude the test source/dependencies. The adapter requires VSTest package 17.14.1. The exact .NET SDK remains 8.0.426. [Upstream v5 compatibility](https://github.com/godot-gdunit-labs/gdUnit4Net/tree/v5.0.0) covers Godot 4.4.1/.NET 8/C# 12.

Fresh headless GLB imports emit `Parameter "t" is null` in `servers/rendering/dummy/storage/texture_storage.h:107`. The exact version/operation/stack matches the upstream [glTF editor scene-preview bug #108994](https://github.com/godotengine/godot/issues/108994), fixed for 4.5. Logs: `artifacts/m3/import.log`. The assets render and the runtime tests pass; import exit code zero is not represented as a diagnostic-free import. The SDK/platform pin remains provisional per M0; no engine upgrade has been made to hide this issue.

The preview harness supports `--m3-location=x,z` by changing only a cloned fixture's player-spawn placement through the normal loader, plus `--perf-capture=seconds` after 120 warm-up frames. This is a test scene; gameplay camera parameters remain authored data.

## Clean available-Android rerun

After the user woke/unlocked the device, a fresh current-source Android export installed and cold-launched successfully on `d211b69`. The packaged JSON/translation/Core/replay probe passed, followed by the native rendered capture. PID-specific runtime logs contain no engine errors. The previous unsupported FSR1 error is absent; mobile 3D scale remains 0.75. Screenshot inspected: `artifacts/m3/android-0faeebf013c74f37931cf821f532a6ba/scene.png`.

The 15.002-second measurement contains 1,814 frames: p50 8.240 ms, p95 8.448 ms, p99 8.859 ms, render CPU p95 1.939 ms and GPU p95 6.865 ms. Maxima: 127 draw calls, 44,584 triangles, 54,864,512 texture bytes and seven local lights. All available-device budgets passed. APK SHA256: `F30BFBF1799670C28BB5E5BB40EE33EA705F4A7574FB7DCFE2BE982AF609AA39`. Evidence/result JSON and raw logs are retained in the same directory. iPhone, specified reference GPUs/devices and hosted CI remain pending.
