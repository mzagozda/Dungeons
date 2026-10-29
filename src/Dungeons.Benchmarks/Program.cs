using BenchmarkDotNet.Running;
using Dungeons.Benchmarks;

if (args.SequenceEqual(new[] { "--nightly-contracts" }))
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new NightlyBenchmarks().Contracts()));
    return 0;
}
var summaries = BenchmarkSwitcher.FromAssembly(typeof(CoreBenchmarks).Assembly).Run(args).ToArray();
// BenchmarkDotNet prints failed build/run reports but does not set the process exit code.
return summaries.Length > 0 && summaries.All(summary => summary.Reports.Any() && summary.Reports.All(report => report.Success)) ? 0 : 1;
