# Release toolchain candidate — 2026-10-27

The user approved the Godot 4.7.2/.NET 10 host and SDK migration on 2026-10-27, retaining C# 12 and Core's net8.0/netstandard2.1 targets. This overrides the original host/build pins in §3.1/§3.5. The full-game port is active in the main workspace; post-activation regression and fresh packages pass. Performance-reference acceptance and the new-host two-hour run remain pending. See `host-migration.md`. The earlier minimal experiment remains under `artifacts/release-stack-probe/`.

| Part | Earlier frozen checkpoint | Approved host / isolated full-game validation |
| --- | --- | --- |
| Engine | Godot 4.4.1 .NET | Official Godot 4.7.2 .NET, release asset SHA256 and SHA512 verified |
| Build SDK / language | 8.0.426 / C# 12 | Installed SDK 10.0.402 / C# 12 |
| Host framework/runtime | net8.0 | net10.0 / 10.0.13 |
| Core | net8.0 + netstandard2.1 | Same source/targets, rebuilt with the new SDK; existing recordings verify |
| Android target/min SDK | 34 / 26 | 36 / 26 |
| Native compatibility | Eleven libraries fail 16 KB ELF alignment | All eleven packaged arm64 libraries pass ELF LOAD and ZIP 16 KB checks |
| Core determinism | Existing development recordings | Full-clear, ambush and death/respawn recordings verify on both native desktop renderers with unchanged content/level/final hashes |

The candidate APK is **98,201,323 bytes** with SHA256 `6752a6bf2537e5e50ba374e30dc37b444ca3b198bbde64e06a0878ca6b4a371d`. Its Core bytes exactly match the active ExportDebug DLL, SHA256 `c6beed976edb2272673a3fb32586ab722a2e237d3cd3a53466ecafcf7a1a8078`. The actual loaded Core module ID on both desktop renderers is `a1d715f5-deea-49b4-b103-988d642156cf`. `evidence.json`, raw export/runtime logs, three copied recordings and `apk-audit/result.json` retain the measurements.

The full release audit still rejects this debug APK for debuggability and missing expected publisher signing. Its API/min SDK, size, ELF and ZIP checks pass. Android build-tools 36.0.0 independently confirms the candidate with `zipalign -c -P 16 -v 4`; the current development APK fails that same command (`zipalign16.log` / `current-zipalign16.log`). Optional LLVM tools were unavailable; no second external ELF parser result is claimed. No device was connected, inspected, installed or launched. This proves a minimal host/native packaging route, not full-game API/render compatibility, AAB acceptance or a signed release.

The versioned [Godot Android issue](https://github.com/godotengine/godot/issues/110262) identifies .NET 8 native alignment as the problem; the [upstream fix requires net9.0 or newer](https://github.com/godotengine/godot/pull/110263). [Godot 4.5's release notes](https://godotengine.org/releases/4.5/) also specify .NET 9+ for this feature. Godot 4.7.2 is a stable release in the [official release index](https://editor.godotengine.org/releases/). The experiment uses .NET 10 rather than introducing a short-lived .NET 9 bridge.

## Approved adoption scope

The approved host/build-SDK exception to §3.1/§3.5 is implemented: Godot 4.7.2/.NET 10, C# 12 and Core's net8.0/netstandard2.1 targets, with simulation rules/data formats unchanged. The main project is retargeted, including the validated §16.1 boot-flow repair. Exact release servicing pins must be rechecked before final store submission.

After that specification decision, the implementation needs a private full-game port, compiler/API/test-adapter repairs as required, all strict data/asset/import/payload checks, the complete Core/rendered/replay/benchmark gates, new native package checks and a soak on the adopted host. Physical Android and Mac/iPhone acceptance remain deferred under the user's existing instruction. Signed AAB/bundletool validation, store metadata and hosted execution remain separate requirements.

Observed CLI/config differences are retained rather than hidden: Godot 4.7's template installation flag must accompany `--export-debug`/`--export-release`; the Android preset requires ETC2 import enabled even for a minimal scene; missing project icons now log an error despite export exit zero. The corrected strict export passes after reusing the existing original probe icon. Failed attempts remain in `android-export*.log`. A failed configuration scan also uses a changed ObjectDB leak message, which must be covered when adapting strict shutdown checks to the new host.

The earlier service soak keeps its own sealed old-host executable/payload and 188 archived source files. Source freezing ended at adoption; its final result belongs to that archived checkpoint. The new host uses versioned editor/export-template directories, and previous main-workspace files/pins are backed up under `artifacts/host-migration/pre-adoption-20261027/`. Physical checks remain deferred.
