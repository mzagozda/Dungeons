# Named visual captures

Application arguments follow Godot's `--` separator. List the fixed fixtures without opening a profile or creating a world:

```powershell
. ./tools/Common.ps1
$godot = Get-GodotPath
& $godot --headless --path game -- --screenshot-list
& $godot --audio-driver Dummy --path game -- --screenshot-test hero-roll --screenshot-output C:/Captures/roll.png
& $godot --audio-driver Dummy --path game -- --screenshot-test mission-complete --replay C:/Replays/full-clear.dreplay --screenshot-output C:/Captures/complete.png
```

Omitting `--screenshot-output` writes a new run folder below `user://screenshots/` and prints its absolute PNG path. Explicit outputs must be new `.png` files; existing files are refused. Named fixtures reject `--level`, `--seed` and `--replay-quit`; mission completion requires a compatible winning recording, while other fixtures reject `--replay`. Renderer/quality overrides remain available, and the manifest records what actually ran. Named captures require a real renderer/display; `--headless` is rejected. The earlier `--level … --screenshot-test=<absolute.png>` form remains available for arbitrary normal-game captures.

| Group | Scenario names |
|---|---|
| Terrain/camera/water | `terrain-bridge`, `terrain-stairs`, `terrain-ruin` |
| Hero actions/pickups | `hero-roll`, `hero-bow`, `hero-melee`, `hero-pickup` |
| Combat feedback | `combat` |
| Mission/death/results/objects | `mission-death`, `mission-failed`, `mission-complete`, `mission-objects` |
| Touch and safe areas | `touch-phone`, `touch-aim`, `touch-left`, `touch-tablet` |
| Desktop settings | `settings-video`, `settings-audio`, `settings-controls`, `settings-accessibility` |
| Maximum-scale phone UI | `phone-hud`, `phone-layout` |

Desktop fixtures use the requested window resolution. Phone fixtures use 1760×800 and tablet 1280×960, matching their simulated density/safe-area inputs. These remain desktop previews. Each fixture uses the actual existing acceptance scene and its assertions. Completion replays are bounded/parsed before scene creation, and the rendered session uses their recorded seed/difficulty/editor origin. Output settings stores are isolated beside the capture; the player's profile is not opened.

Terrain loading does not advance the named presentation clock. Named action captures advance at 60 fixed visual steps per second, and water/foliage shader time is held at a fixed phase. Normal play retains shader time with the default `capture_time=-1`; a real SubViewport test checks that water pixels keep changing. The pinned engine's global-uniform getter is [restricted to editor use](https://github.com/godotengine/godot/blob/4.4.1-stable/servers/rendering/renderer_rd/storage_rd/material_storage.cpp#L1558), so the test checks the configured default and actual rendering instead of calling that getter at runtime.

Each PNG has a `.capture.json` sidecar: image SHA256, scenario, dimensions, actual renderer/adapter/quality, pinned engine version, source/content identity, seed, capture tick and state hash. Touch capture metadata refers to the captured tick before the probe's later bow-release verification. Both `baselineApproved` and `physicalAcceptance` remain false.

```powershell
./tools/Test-ScreenshotScenarios.ps1 -Repeat
./tools/Test-ScreenshotScenarios.ps1 -Renderer gl_compatibility -Repeat
# After exporting current game sources:
./tools/Test-ScreenshotScenarios.ps1 -Executable artifacts/windows/BlockDungeons.exe -Repeat
# A smaller focused run:
./tools/Test-ScreenshotScenarios.ps1 -Scenarios terrain-bridge,combat,phone-hud -Repeat
```

The runner discovers fixtures from the executable, checks seven rejected-input/output-preservation contracts (including existing companion metadata), validates sidecar hashes and camera/screen dimensions, and optionally repeats each fixture in a separate process. Core identity, seed, capture tick, hash, renderer/adapter, quality and dimensions must agree. M8's rendered runner includes all 22 Mobile fixture pairs; Compatibility is also exercised separately. The pure parser and six image-comparison cases run without Godot; CI includes the comparison cases. PNG and sidecar creation use create-new writes for named captures.

`tools/compare_images.py expected.png actual.png --report result.json --diff difference.png` enforces specification §20.1: each RGBA channel may differ by at most 2% of 255; a pixel mismatches when any channel exceeds that tolerance, and at most 0.5% of all pixels may mismatch. Exactly 0.5% passes. Dimensions must match, differences use signed arithmetic, and source images cannot be overwritten by report/diff outputs. Exit codes: 0 pass, 1 image mismatch, 2 input/output error. Difference images show mismatching pixels in red over a darkened actual image.

Repeatability passes alone do not approve visual regression baselines. Generated files remain candidates until explicit human review; replay golden approval is separate. The later approved profile below covers one Windows still-image context. Full motion/audio/device review, other profiles and hosted comparison remain pending.

`tools/visual_baseline.py prepare <capture-run> <new-candidate-directory> --profile <id>` validates the complete executable catalogue, passed run, source/state fingerprints and image hashes, then copies its first capture per scenario into a pending manifest. It refuses existing destinations and destinations inside the source run. It ignores the runner's deliberate non-JSON preservation sentinel. Preparation never grants approval.

`tools/visual_baseline.py check <baseline-profile> <capture-run> --profile <id> --output <new-result-directory>` requires explicit approved status and a complete human review record, verifies baseline file hashes and exact catalogue coverage, then compares render/source/state context and pixels. A context change fails even with identical pixels. Exit codes are 0 pass, 1 regression, 2 invalid input/output or changed baseline files, and 3 missing/pending review. Comparison reports and red difference images use a new output directory outside both inputs; existing reports are preserved.

Current unreviewed 22-case candidates are `artifacts/m8/baseline-candidates/windows-mobile-rtx3060/` and `windows-gl-rtx3060/`, prepared from the final local Mobile capture run and packaged Windows Compatibility run. Both return exit 3 while pending. Eleven baseline contract cases use synthetic 4×4 images, including test-only approval records; they never approve game art. M8 and CI run them alongside the six pixel-comparison cases. See [review records](../assets/baselines/visual/README.md) and [nightly orchestration](nightly-hardening.md).

On 2026-10-27 the human user approved the exact adopted-host 22-scene Windows RTX 3060 Mobile/Vulkan still-image candidate. Its unchanged files are stored at `assets/baselines/visual/windows-rtx3060-mobile-host47/`, with the explicit review reference and candidate/contact-sheet hashes. A fresh full rendered run at `artifacts/host-migration/approved-visual-independent/` passes all 92 native Godot cases, all 22 repeated capture pairs and all 22 reviewed comparisons with zero mismatched pixels. The old pending candidates remain pending; this approval does not cover other renderers/devices, motion/audio or gameplay replay goldens.

The separate full-playback capture form (`--replay … --replay-quit --screenshot-test=…`) disables VSync for that QA run, so an engine `--fixed-fps` fast-forward is not held at presentation rate. Both flags are required; ordinary replay viewing and named fixtures keep their existing presentation settings. The 120-second process bound is unchanged.
