# Nightly performance baselines

On **2026-10-29**, the owner requested relaxing the combat stress-test timing requirement after the same PR head passed at **1.4552 ms/tick** and failed at **1.6609 ms/tick** on hosted Windows. `CombatStressTests` now permits **2 ms/tick**, matching the desktop benchmark ceiling, instead of 1.5 ms. The 600 measured ticks, workload/hash checks and exact zero-allocation assertion remain unchanged. This allowance applies to the normal optimized Core test gate on local and hosted machines. The per-runner benchmark references, +20% regression limit and other performance assertions retain their existing values.

The full local `tools/Test-Core.ps1` runner passes **478 optimized cases** (464 functional + 14 performance), **464 coverage cases**, and **98.42% Core line coverage** after this change. Scoped Roslyn compiler/analyzer diagnostics report no warnings or errors. Evidence: `artifacts/performance-budget-2ms/full-core.log`.

On **2026-10-28**, the owner approved the exact proposed Windows .NET 10/twelve-warmup reference. `assets/baselines/performance/windows-r5-7600x.json` now contains those unchanged proposed bytes; the adjacent `.review.json` records the explicit approval, context, hashes and excluded platforms. The old .NET 8 reference is preserved byte-for-byte at `archive/windows-r5-7600x-net8-pre-host47.json`. Both independent datasets pass against the adopted canonical reference. Earlier sections below retain historical checkpoints; the final section records this adoption. Linux/device/hosted acceptance remains separate.

`tools/Test-NightlyBenchmarks.ps1 -Runner <id>` implements the six §20.7 BenchmarkDotNet workloads and a stored per-runner regression gate. The nightly workflow has a separate `benchmark-regression` job. Hosted execution remains pending; a Windows measurement is not a Linux or mobile baseline.

| Benchmark | Timed work |
|---|---|
| `Tick_MossyHollow_Replay` | Verify all 11,591 ticks of the unmodified mission, including hashes; mean is per tick. |
| `Tick_Stress_64Monsters_32Arrows` | 6,000 combat ticks, starting with 64 active monsters, 32 moving arrows and 128 pickups; mean is per tick. Arrows subsequently expire, hit and are replaced by the archers' normal firing. |
| `AStar_256_Open` | One corner-to-corner query on a 256×256 open grid, using the production A*. |
| `AStar_256_Maze` | One query through a seeded depth-first 256×256 perfect maze: 32,257 walkable cells, 6,868-step route, no diagonal corner cutting. The returned waypoint buffer is bounded while the search still reaches its goal. |
| `ChunkMesher_256x256` | All 256 real 16×16 CPU mesh chunks, including cliffs, water and foliage. Snapshot construction is excluded; geometry allocations are included. |
| `LevelLoad_256x256` | Parse, validate and compile a 256×256 JSON level with 64 monsters using the production content registry. Source-string/registry construction is excluded. |

The benchmarks link the same engine-free mesher source used by the game and pure tests; they do not reimplement its algorithm or start Godot. Two pure fixture cases verify repeatable maze topology/reachability and all full-size chunk/material outputs. The existing combat allocation/budget test now also checks the 128 initial pickups. A real `--nightly-contracts` execution runs every workload once and checks counts, actual mission victory, successful level loading, nontrivial paths and geometry. It records fixture version/input fingerprint, source/content/replay/atlas identity, actual OS-provided CPU identity and logical processor count. BenchmarkDotNet's Windows host report currently says `Unknown processor`, so that additional CPU identity is required rather than pooling unknown machines.

The runner first executes `Test-Core.ps1`: both Core targets, complete optimized tests, exact zero-allocation probes, the queued path-service 1 ms budget and separate instrumented Core coverage gate. These are not inferred from BenchmarkDotNet's allocation estimates. The maze benchmark measures a whole long-path query, not a frame's queued path-service workload; its mean is reported separately. Full-map loading/meshing may allocate; steady-state simulation still requires zero allocations. Renderer, GPU, physical load-to-playable and reference-device budgets remain separate checks.

BenchmarkDotNet 0.14.0 runs three independent processes per workload, now with **twelve explicit warmups** and eight measured iterations. All 24 samples are retained (`OutlierMode=DontRemove`), including slower process results. The earlier .NET 8 reference used three warmups. Its [iteration setup](https://benchmarkdotnet.org/articles/features/setup-and-cleanup.html) resets mutable combat/replay sessions outside each timed macro invocation and forces one invocation/unroll. The other four workloads use the normal pilot/invocation policy. Reports use the pinned exporter's [nanosecond statistics and memory fields](https://github.com/dotnet/BenchmarkDotNet/blob/v0.14.0/src/BenchmarkDotNet/Exporters/Json/JsonExporterBase.cs). Baseline comparison requires all eight results from each of the three launches, all six successful workloads, Release configuration, no debugger, matching SDK/runtime/OS/CPU/architecture/intrinsics/job settings and identical workload inputs. Generated random `Job-XXXXXX` labels are ignored; their explicit settings are compared. The 20% regression and absolute tick/allocation limits are unchanged.

`tools/benchmark_baseline.py check <bdn-results-directory> <contracts.json> --runner <id> --baseline <reference.json> --output <new-report.json>` fails if any mean exceeds 120% of its baseline; exactly +20% passes. Both tick workloads also must remain within the absolute desktop 2 ms budget. It records allocation estimates without treating them as exact allocation-test evidence. Missing baseline returns 3, a regression returns 1, and invalid/incomplete/mismatched inputs return 2. Existing report/reference/source files are preserved. CPU, runtime or fixture changes require a separately established matching baseline and never silently reuse another runner's numbers.

Each full runner invocation saves real measurements and a new `candidate.json` before checking the stored baseline at `assets/baselines/performance/<id>.json`. A missing baseline deliberately fails after saving that evidence. To establish a new reference, inspect its complete successful measurements, then explicitly record them to a new path:

```powershell
./tools/Test-NightlyBenchmarks.ps1 -Runner windows-r5-7600x
# Use the actual evidence directory printed by the runner:
python tools/benchmark_baseline.py record <evidence>/bdn/results <evidence>/contracts.json --runner windows-r5-7600x --output assets/baselines/performance/windows-r5-7600x.json
# A subsequent independent measurement must pass against the stored reference.
./tools/Test-NightlyBenchmarks.ps1 -Runner windows-r5-7600x
```

Recording has no overwrite/force switch. Expected values never advance automatically after either a pass or a regression. Preserve and explain an intentional rebaseline as a separate reviewed source change; keep the old measurement evidence. This performance reference process does not approve visual images or replay goldens.

The configured hosted runner id is `linux-ubuntu24-x64`. Its actual CPU identity is checked, so a hosted pool moving to different hardware fails for a matching new reference instead of masking a regression. No Linux reference is fabricated from desktop numbers. Eleven synthetic gate tests cover the exact +20% boundary, all six individual regressions, absolute tick limits, missing/partial/duplicate/failed reports and process samples, context changes, generated job labels and output preservation; those tests do not establish real performance acceptance.

The first complete run (`artifacts/nightly/benchmarks-missing-reference/`) passes 447 normal Release and 434 coverage cases with 98.31% Core coverage, executes all six benchmarks, records its candidate and deliberately fails for a missing reference. The next independent run (`benchmarks-reference-comparison/`) passes five benchmarks but correctly rejects meshing at +37.02% (22.57 ms reference versus 30.93 ms actual). The original reference remains unchanged in `assets/baselines/performance/archive/windows-r5-7600x-v1.json`.

That first benchmark setup loaded and retained all six workloads' data in every process, including unrelated replay/world/pathfinding data during meshing. Meshing's generation-2 collection count varied from 43 to 62 per 32 map builds alongside the timing change. Fixture protocol version 2 gives each timed method its own targeted setup; preflight still executes/checks all six. Timed algorithms, terrain sizes, allocations and the 20% threshold are unchanged. A new matching measured reference and independent comparison are required for this explicit protocol change; the archived failure is not converted into a pass.

The version-2 independent run (`artifacts/nightly/benchmarks-isolated-comparison/`) keeps meshing within 0.06% (32.14 versus 32.16 ms), but correctly fails loading at +22.78% (5.19 versus 6.37 ms). Its reference is preserved under `archive/windows-r5-7600x-v2.json`. Protocol version 3 retains isolated setup and requires three independent launches/all 24 samples, so the stored mean represents multiple process/GC histories rather than a single process. The threshold and actual timed workloads remain unchanged. Both earlier failed comparisons remain failed evidence.

The then-current .NET 8 Windows reference came from `artifacts/nightly/benchmarks-three-process-reference/`; its missing-reference final check correctly returned 3 after recording all measurements. An explicit record established `assets/baselines/performance/windows-r5-7600x.json`, with fixture identity `642fa75a195a4e5b90b6a4e053288fa2e4ed675f76b4716fe86818d0bad3b2c6`. The independent full runner at `artifacts/nightly/benchmarks-three-process-comparison/` exits zero: all 18 benchmark processes complete, all 144 samples are present and every mean passes the original +20% limit. Its full Core gate again passes 447 optimized cases, 434 coverage cases and 98.31% Core line coverage. Both simulation benchmark allocation estimates are zero; exact allocation acceptance comes from the separate passing Core probes. This old reference is now archived by the approved adoption below.

| Workload | Independent mean | Change from stored reference |
|---|---:|---:|
| Full replay tick | 23.54 µs | +0.36% |
| Stress tick | 104.61 µs | −1.12% |
| Open A* query | 49.13 µs | +0.12% |
| Maze A* query | 2.021 ms | −0.03% |
| All 256 mesh chunks | 31.769 ms | −0.21% |
| 256×256 JSON load | 5.638 ms | −16.94% |

This is one complete local per-runner regression pass, not proof of long-term stability, reference-device budgets or five green hosted nights. In particular, loading retains substantial process-to-process GC variation, now included in the combined mean instead of discarded; the full raw samples and statistics remain available. The measured Windows reference does not satisfy the missing Linux profile. Runtime source hashes for all 109 packaged files remain unchanged (`artifacts/nightly/benchmark-runtime-source-check.json`), so no new runtime export/install is claimed. Eleven comparator cases, workflow lint and PowerShell parsing pass; Shellcheck/Pyflakes remain unavailable for local actionlint, as in the earlier workflow check.
## Current-source context check — 2026-10-27

The current gate-fix/service-adapter checkpoint was measured again at `artifacts/platform-services/benchmarks-current/`. All 478 normal/464 coverage Core cases, 98.42% line coverage, allocation/path budgets, six workload contracts and all 18 BenchmarkDotNet processes/144 samples pass their execution/absolute-budget checks. The stored-reference comparison correctly fails; it is not reported as a green regression gate.

There are two independent context differences, inventoried in `context-diff.json`: fixture SHA256 changed from `642fa75a...` to `8f2b106e...`, and Windows changed from `10.0.26300.9550` to `10.0.26300.9711`. Comparing the archived pre-loot-fix recording to the current one shows that only `tickHashes` changed; input commands, tick count, checkpoints and initial/final identities remain equal. Expected intermediate state changes are part of the existing strict recording fingerprint, so a correct simulation repair still invalidates that old fixture reference. The OS context also independently prevents acceptance.

No stored reference, fingerprint rule, workload, outlier rule or +20% threshold has been changed. Raw timing differences from the old context are diagnostic only. A separate current-context reproducibility run uses the first new `candidate.json` as an explicit temporary reference at `artifacts/platform-services/benchmarks-candidate-independent/`. Its result must be distinguished from the unchanged stored-reference gate. Updating the canonical reference remains pending; a candidate comparison cannot silently approve that update or count as a hosted green night.

That independent temporary-reference run now passes all six workloads, all 18 processes/144 samples, the unchanged +20% checks and absolute tick limits. Its full Core gate again passes 478 normal/464 coverage cases and 98.42% coverage. Both simulation allocation estimates are zero, with exact allocation acceptance supplied by the Core probes. The two ordinary native soak processes were running during these measurements; this remains local desktop evidence.

| Workload | Independent mean | Change from temporary candidate |
| --- | ---: | ---: |
| Full replay tick | 23.232 µs | +1.18% |
| Stress tick | 101.663 µs | +1.01% |
| Open A* | 47.143 µs | -1.79% |
| Maze A* | 1.868 ms | -0.27% |
| All 256 mesh chunks | 36.528 ms | -0.72% |
| 256×256 load | 5.459 ms | +2.66% |

The first default comparison remains failed and the canonical reference remains unchanged. This passing reproducibility comparison does not remove its fixture/OS context mismatch.

## Adopted .NET 10 host — 2026-10-27

The SDK 10.0.402/runtime 10.0.13 migration adds two more context differences to the replay fingerprint and Windows build changes above. The stored .NET 8 reference remains unchanged. `artifacts/host-migration/performance-context-differences.json` records all four differences; incompatible comparisons remain failures.

The first two attempts produced no measurements because BenchmarkDotNet's project discovery recursively found the same project in the main workspace, isolated port, preserved originals and source seal. A benchmark-local solution now bounds discovery to the real benchmark/Core projects; all runners build explicit project paths. The failed discovery logs remain under `benchmarks-net10/` and `benchmarks-net10-project-scoped/`. No timed C# workload changed.

The first successful three-warmup independent comparison (`benchmarks-net10-independent/`) correctly fails level loading at **+44.56%** (4.314 to 6.236 ms), while the other five workloads pass. Early loading samples retain 11–13 ms cold-process results. Two bounded loading-only diagnostic runs with twelve warmups reduce the observed spread; neither is acceptance evidence. The runner now explicitly requires twelve warmups, with the same three launches, eight measured iterations per launch, all outliers retained, workload inputs and +20%/absolute/allocation limits. The comparator rejects 0/3/11 warmups; all twelve comparator contracts pass. Job settings remain part of baseline identity, so the previous three-warmup reference cannot be silently reused.

`benchmarks-net10-warm12-candidate/` completes all six workloads and records a temporary reference at `performance-warm12-reference-candidate.json`. The independent full runner at **`artifacts/host-migration/benchmarks-net10-warm12-independent/`** finishes successfully at **18:31:58 UTC**: 18 benchmark processes, all 144 measured samples, 478 optimized Core cases, 464 coverage cases and 98.42% coverage. Exact zero-allocation/path budgets pass separately; both simulation benchmark allocation estimates are zero. The sealed native soak was running concurrently, so this is local reproducibility evidence under that load.

| Workload | Independent mean | Change from temporary reference |
| --- | ---: | ---: |
| Full replay tick | 24.195 µs | -0.56% |
| Stress tick | 97.384 µs | +1.24% |
| Open A* | 42.938 µs | +1.72% |
| Maze A* | 1.565 ms | +0.73% |
| All 256 mesh chunks | 31.985 ms | -0.05% |
| 256×256 load | 4.184 ms | -2.40% |

This temporary matching-reference comparison passes. Canonical-reference acceptance, Linux/reference-device measurements and five hosted green nights remain pending. No stored reference, replay golden or visual baseline was promoted.

## Repaired main checkpoint and explicit reference proposal

The repaired main checkpoint passes a further independent full run at `artifacts/host-migration/native-aot-main-benchmarks/` (finished 20:35:56 UTC): 478 normal/464 coverage Core cases, 98.42% coverage, all six workloads, 18 processes/144 samples and every unchanged +20%/absolute/allocation limit. Its fixture/context matches the temporary twelve-warmup reference. The separate sealed package soak was running concurrently, as during the preceding independent comparison; no other build/capture job ran during measurements.

| Workload | Repaired main mean | Change from proposed reference |
| --- | ---: | ---: |
| Full replay tick | 24.229 µs | -0.42% |
| Stress tick | 98.682 µs | +2.59% |
| Open A* | 42.421 µs | +0.50% |
| Maze A* | 1.546 ms | -0.45% |
| All 256 mesh chunks | 32.168 ms | +0.52% |
| 256×256 load | 4.225 ms | -1.44% |

The concrete proposal is `artifacts/host-migration/native-aot-main/performance-reference-proposal/`: `proposed-reference.json` contains the previously recorded .NET 10/Windows 26300.9711/fixture `8f2b…`/twelve-warmup reference, and `previous-reference.json` preserves the incompatible .NET 8 reference. `proposal.json` records their hashes and both independent comparison paths. Its original pending status remains preserved as historical evidence; adoption is recorded separately below. This is an explicit one-time migration-context review, not automatic advancement after a regression; Linux/device/hosted acceptance remains separate.

## Approved canonical adoption — 2026-10-28

The human reply to `call_946d8252037142e880ec05c101116391` explicitly approved adopting the verified reference. The canonical file's SHA256 is **`67f9f14de69afdfdca35d1451f0a0a50ec3be5ca13dd5a3210c08a89d90454bc`**; the archived previous reference retains **`644f4c541a3121cafbeb7717ef8c3d940415c8b0869e46d39461b6fff0517798`**. Proposed measurement bytes, recording timestamp, fixture/context and all thresholds are unchanged.

Fresh read-only comparisons of both preserved independent datasets pass against that canonical path at `artifacts/approvals/baselines-20261028/`, each covering all six workloads/18 processes/144 samples. This reuses the complete real measurements, without claiming a new timing run. The repaired-main largest increase remains **+2.59%**. All twelve comparator contracts pass. Automatic advancement stays disabled; future context changes still require separate review and incompatible prior attempts remain failed evidence.

