using Xunit;

namespace Dungeons.Core.Tests;

// Wall-clock and exact allocation acceptance must not compete with parallel stress/fuzz work.
[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceCollection;
