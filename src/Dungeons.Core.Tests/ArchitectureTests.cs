using System.Reflection;
using Xunit;

namespace Dungeons.Core.Tests;

/// <summary>Enforces specification §4.2 before gameplay is introduced.</summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void CoreReferencesOnlyPlatformIndependentAssemblies()
    {
        string[] forbidden = ["Godot", "System.Drawing", "System.Windows", "PresentationFramework", "Microsoft.Win32", "WindowsBase"];
        AssemblyName[] references = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();
        Assert.NotEmpty(references);
        foreach (AssemblyName reference in references)
        {
            foreach (string prefix in forbidden)
            {
                Assert.False(reference.Name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true,
                    $"Core must not reference {reference.Name} (specification §4.2).");
            }
        }
    }

    [Fact]
    public void CoreUsesOnlyPortableBclAndJsonReferences()
    {
        // M2 adds portable JSON, schema regex and SHA256. Host/UI assemblies stay forbidden.
        AssemblyName[] references = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();
        // Collection expressions may compile to CollectionsMarshal helpers in this BCL assembly.
        Assert.All(references, reference => Assert.Contains(reference.Name, new[] { "System.Runtime", "System.Collections", "System.Memory", "netstandard", "System.Text.Json", "System.Linq", "System.Text.RegularExpressions", "System.Security.Cryptography", "System.Text.Encoding.Extensions", "System.Runtime.InteropServices", "System.IO.Compression" }));
    }
}
