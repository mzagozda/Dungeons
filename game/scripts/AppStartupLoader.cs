using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Util;

namespace Dungeons.Game;

/// <summary>Managed results handed back to the main thread before any scene is created.</summary>
public sealed record AppStartupData(ContentRegistry Content, IReadOnlyList<CatalogEntry> Catalogue)
{
    internal int WorkerThreadId { get; init; }
}

/// <summary>Loaders must honor cancellation and must not access the active scene tree.</summary>
public interface IAppStartupLoader
{
    Task<AppStartupData> LoadAsync(string userLevelsDirectory, ILogger? log, CancellationToken cancellationToken);
}

/// <summary>GPU-backed preview reads stay on the caller; independent file IO and pure validation run on a worker.</summary>
public sealed class AppStartupLoader : IAppStartupLoader
{
    public Task<AppStartupData> LoadAsync(string userLevelsDirectory, ILogger? log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previews = CatalogBridge.PrepareBuiltinPreviews(log);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContentRegistry content = ContentBridge.LoadRegistry(log);
            cancellationToken.ThrowIfCancellationRequested();
            var catalogue = CatalogBridge.ScanForStartup(content, userLevelsDirectory, log, previews, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new AppStartupData(content, catalogue) { WorkerThreadId = Environment.CurrentManagedThreadId };
        }, cancellationToken);
    }
}
