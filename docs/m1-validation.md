# M1 simulation foundation — 2026-10-26

M1's local acceptance gate passed. The user's explicit sequencing exception permits implementation while the M0 physical iPhone/Mac and hosted CI checks remain pending. The game host still displays the M0 probe; M1 is the headless simulation foundation.

Run from the repository root:

```powershell
./tools/Test-Core.ps1
dotnet run --project src/Dungeons.Benchmarks -c Release -- --filter '*CoreBenchmarks*' --job short --artifacts artifacts/m1/benchmarks
```

The Release runner builds Core (both net8.0/netstandard2.1), Tests, Tools and Benchmarks with warnings as errors. It runs the whole suite normally, then collects Core-only Cobertura coverage in a separate run, excluding the already-passed wall-clock timing test from instrumentation. It enforces 85% line coverage and fails on missing/ambiguous reports. TRX may create an identical copy of an attachment; those copies are checked by SHA256 before one is read.

Verified results:

- 39 tests passed, zero failed/skipped in the normal Release run. Coverage run: 38 passed; only the performance test was filtered. Core line coverage **98.41%**. Evidence: `artifacts/core/checks-transcript.log`, `coverage-summary.json` and TRX/Cobertura attachments.
- SC-MOV-01: 4.5 m east in 60 ticks; previous transforms retain the preceding tick for interpolation. SC-MOV-02: wall stops normal velocity and preserves tangent velocity. SC-MOV-03: stairs ascend/descend monotonically and block sideways entry.
- Circle collision tested from eight directions; speed cap, water, grid edges, height barriers and diagonal corner rules. A seam tangency bug found by SC-MOV-02 was fixed with a squared-distance grazing tolerance.
- A* weighted optimal cost compared with an independent Dijkstra oracle on 80 random maps. Tests cover obstacles, unreachable endpoints, truncated buffers, repeated searches and zero query allocations. The 256×256 open diagonal query passes the 2 ms smoke threshold.
- Spatial separation: deterministic ordered pairs, two passes, hard hero contacts, soft enemy contacts, static obstacles and terrain revalidation.
- Seed-1 xoshiro128** known-answer sequence, exact RNG restoration and stream independence; FNV-1a known vectors/UTF-8. Bounded events warn once per drain and do not change deterministic state hashes.
- RLE replay checkpoints every 60 ticks, every-tick verification, first-diverging-tick detection, initial-state mismatch and capacity truncation. Document snapshots, pause, multiple input slots, FIFO path budget, stable removals and hashes tested.
- Warmed recorded sessions: **0 bytes** allocated over 600 empty ticks and over 600 ticks with one hero + 64 stationary enemies. This is the M1 allocation gate; SC-ALLOC-01 on the complete Mossy Hollow combat replay remains pending M5/M6.
- Roslyn Core diagnostics delta: baseline 0, current 0, introduced 0, resolved 0. Roslyn uses a separate installed SDK for analysis; actual build/test uses the pinned 8.0.426 SDK. The engine source generator is verified by the real host build, not the Roslyn workspace snapshot.

BenchmarkDotNet 0.14.0 ShortRun, Windows 11 x64, .NET 8.0.32, SDK 8.0.426, Ryzen 5 7600X: empty tick **211 ns**, hero + 64 stationary enemies **9.65 µs**, open 256×256 diagonal A* **123 µs**. MemoryDiagnoser reports no per-operation managed allocations. Three measured iterations give broad confidence intervals; these are initial local fixtures, not the §20.7 nightly baseline or combat/device performance proof. Full output: `artifacts/m1/benchmarks/results/Dungeons.Benchmarks.CoreBenchmarks-report-github.md`.

The first benchmark attempt found the M0 fresh-source copy as a duplicate project. That copy was preserved outside the repository in the temporary directory recorded by `artifacts/m0/fresh-source-location.txt`; benchmarks then built and ran all three fixtures. The benchmark executable now returns failure for an empty/failed run instead of trusting BenchmarkDotNet's default process status.

Two specification clarifications are recorded in §7: end-of-tick compaction is stable rather than swap-removal to retain ascending runtime ids; previous transforms are copied at the beginning of a tick, because copying at the end would erase interpolation history. No gameplay behavior was removed by these corrections.

Remaining scope: actual later-stage systems are explicitly reserved in the ordered scaffold. Content/level serialization, SHA256 package/content replay identity and `.dreplay` JSON follow in M2. Rendering, host input, combat, AI, mission logic and full-clear goldens are not claimed by M1.

During M2, a machine SDK update removed 8.0.426 and left 8.0.427 installed. The exact pin was retained. `Install-Dotnet.ps1` now supplies a checksum-verified workspace-local copy, used by the existing build/test/export scripts. An incomplete, locked global Godot.SourceGenerators cache was similarly bypassed by restoring the host packages locally; the real all-project Debug build, 39 tests and headless engine probe then passed. Roslyn's transport later closed; subsequent verification uses the pinned authoritative build/tests until that service is available again.

References: the RNG follows the [public-domain xoshiro128** reference](https://prng.di.unimi.it/xoshiro128starstar.c) with [SplitMix64 seeding](https://prng.di.unimi.it/splitmix64.c); the benchmark runner uses the official [BenchmarkSwitcher](https://benchmarkdotnet.org/articles/guides/how-to-run.html) and [MemoryDiagnoser](https://benchmarkdotnet.org/articles/configs/diagnosers.html) APIs.
