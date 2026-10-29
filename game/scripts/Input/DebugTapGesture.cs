using Dungeons.Core.Math;

namespace Dungeons.Game.Input;

/// <summary>Four simultaneous fingers, released within 350 ms and 16 dp of their origins.</summary>
public sealed class DebugTapGesture
{
    private struct Finger { internal int Id; internal bool Active; internal Vec2 Origin; }
    private readonly Finger[] _fingers = new Finger[10];
    private int _count;
    private bool _invalid;
    private double _started;
    /// <summary>Once four fingers overlap, consume the chord until every finger is lifted.</summary>
    public bool IsCaptured { get; private set; }

    public void Press(int id, Vec2 point, double seconds)
    {
        if (id < 0 || Find(id) >= 0) { return; }
        if (_count == 0) { _started = seconds; }
        int slot = -1;
        for (int i = 0; i < _fingers.Length; i++) { if (!_fingers[i].Active) { slot = i; break; } }
        if (slot < 0) { _invalid = true; return; }
        _fingers[slot] = new() { Id = id, Active = true, Origin = point }; _count++;
        CheckTime(seconds); _invalid |= !point.IsFinite || _count > 4;
        if (_count == 4) { IsCaptured = true; }
    }

    public void Drag(int id, Vec2 point, double seconds)
    {
        int slot = Find(id); if (slot < 0) { return; }
        CheckTime(seconds); _invalid |= !point.IsFinite || Vec2.DistanceSquared(point, _fingers[slot].Origin) > 16 * 16;
    }

    public bool Release(int id, Vec2 point, double seconds, bool cancelled = false)
    {
        int slot = Find(id); if (slot < 0) { return false; }
        Drag(id, point, seconds); _invalid |= cancelled || !IsCaptured;
        _fingers[slot] = default; _count--;
        if (_count != 0) { return false; }
        bool fired = IsCaptured && !_invalid; Clear(); return fired;
    }

    public void Clear() { Array.Clear(_fingers); _count = 0; _invalid = false; IsCaptured = false; _started = 0; }
    private void CheckTime(double seconds) => _invalid |= !double.IsFinite(seconds) || !double.IsFinite(_started) || seconds < _started || seconds - _started > .35;
    private int Find(int id)
    { for (int i = 0; i < _fingers.Length; i++) { if (_fingers[i].Active && _fingers[i].Id == id) { return i; } } return -1; }
}
