# Performance references

Store measured per-runner references here as `<runner-id>.json`, using `tools/benchmark_baseline.py record`. A full passing fixture/BenchmarkDotNet report is required and existing references cannot be overwritten by that command. The nightly gate never advances these files automatically. Retain the original measurements when intentionally changing a reference.

Runner context includes actual CPU identity/count, OS, runtime/SDK, architecture and hardware intrinsics. Matching workload inputs and job settings are required. A missing or incompatible runner fails; Windows references cannot satisfy the pending `linux-ubuntu24-x64` hosted measurement. Performance references do not grant visual or replay-golden approval or physical-device acceptance.

See [workloads, budgets and commands](../../../docs/performance-baselines.md).

`windows-r5-7600x.json` is the measured protocol-3 Windows reference. An independent full run passes all six workloads at `artifacts/nightly/benchmarks-three-process-comparison/`; this is not a Linux or physical-device reference.

`archive/windows-r5-7600x-v1.json` preserves the combined-setup reference; `archive/windows-r5-7600x-v2.json` preserves the first isolated-setup reference. Both have actual failed independent comparisons. Current protocol 3 uses isolated setup and three processes/all 24 samples per workload; it cannot silently reuse the earlier references.
