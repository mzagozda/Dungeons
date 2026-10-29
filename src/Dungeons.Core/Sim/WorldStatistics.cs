using System.Collections.ObjectModel;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class World
{
    private readonly Dictionary<string, int> _killsByType = new(GameConstants.MaxEntities, StringComparer.Ordinal);
    private readonly List<string> _killTypes = new(GameConstants.MaxEntities);
    private IReadOnlyDictionary<string, int>? _killView;
    /// <summary>Mission kill counters keyed by definition id, ready for profile/achievement persistence.</summary>
    public IReadOnlyDictionary<string, int> KillsByType => _killView ??= new ReadOnlyDictionary<string, int>(_killsByType);
    private void InitializeStatistics() { _killView = new ReadOnlyDictionary<string, int>(_killsByType); }
    internal void RecordKill(string definitionId)
    {
        if (_killsByType.TryGetValue(definitionId, out int current)) { _killsByType[definitionId] = System.Math.Min(int.MaxValue - 1, current) + 1; }
        else
        {
            if (_killTypes.Count == GameConstants.MaxEntities) { throw new InvalidOperationException("Kill counter type capacity exceeded."); }
            int index = _killTypes.BinarySearch(definitionId, StringComparer.Ordinal); _killTypes.Insert(~index, definitionId); _killsByType.Add(definitionId, 1);
        }
        Kills = System.Math.Min(int.MaxValue - 1, Kills) + 1;
    }
    internal void HashStatistics(ref Fnv1a hash)
    {
        hash.Add(_killTypes.Count);
        for (int i = 0; i < _killTypes.Count; i++) { string id = _killTypes[i]; hash.Add(id.Length); hash.Add(id); hash.Add(_killsByType[id]); }
    }
}
