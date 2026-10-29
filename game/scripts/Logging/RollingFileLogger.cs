using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Dungeons.Core.Util;

namespace Dungeons.Game.Logging;

/// <summary>Context is captured at enqueue time, including the exact simulation tick.</summary>
public readonly record struct LogContext(string LevelId, ulong Seed, int Tick);

/// <summary>Bounded, IO-free enqueueing; the host flushes UTF-8 JSON lines outside simulation ticks.</summary>
public sealed class RollingFileLogger : ILogger, IDisposable
{
    public const int MaximumFileBytes = 1_000_000, MaximumFiles = 5, MaximumMessageChars = 4096;
    private readonly record struct Entry(DateTimeOffset Utc, LogLevel Level, string Category, string Message, LogContext Context, bool Truncated);
    private readonly Entry[] _queue;
    private readonly object _queueGate = new(), _flushGate = new();
    private readonly string _directory;
    private readonly int _maximumBytes;
    private readonly ArrayBufferWriter<byte> _record = new(32768);
    private readonly byte[] _tailBuffer = new byte[4096];
    private readonly Func<DateTimeOffset> _utcNow;
    private FileStream? _file;
    private Entry _pending;
    private bool _hasPending, _disposed;
    private int _head, _count;
    private long _dropped;
    public long RecoveredTailBytes { get; private set; }
    public string? LastError { get; private set; }
    public long DroppedEntries => Interlocked.Read(ref _dropped);
    public int PendingCount { get { lock (_flushGate) { lock (_queueGate) { return _count + (_hasPending ? 1 : 0); } } } }
    public RollingFileLogger(string directory, int maximumBytes = MaximumFileBytes, int queueCapacity = 256, Func<DateTimeOffset>? utcNow = null)
    {
        if (maximumBytes is < 1024 or > MaximumFileBytes || queueCapacity is < 1 or > 1024) { throw new ArgumentOutOfRangeException(nameof(maximumBytes)); }
        _directory = Path.GetFullPath(directory); _maximumBytes = maximumBytes; _queue = new Entry[queueCapacity];
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }
    public void Log(LogLevel level, string category, string message) => Write(level, category, message, default);
    public void Write(LogLevel level, string category, string message, LogContext context)
    {
#if !DEBUG
        if (level == LogLevel.Debug) { return; }
#endif
        category = category is "Level" or "Sim" or "Ai" or "View" or "Io" ? category : "Io";
        bool truncated = message.Length > MaximumMessageChars;
        if (truncated) { message = Clip(message, MaximumMessageChars); }
        if (context.LevelId?.Length > 64) { context = context with { LevelId = Clip(context.LevelId, 64) }; truncated = true; }
        var entry = new Entry(_utcNow(), level, category, message, context, truncated);
        lock (_queueGate)
        {
            if (_disposed) { return; }
            if (_count == _queue.Length) { _queue[_head] = default; _head = (_head + 1) % _queue.Length; _count--; _dropped++; }
            _queue[(_head + _count) % _queue.Length] = entry; _count++;
        }
    }
    [Conditional("DEBUG")]
    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
    private static string Clip(string value, int limit)
    {
        if (limit > 0 && limit < value.Length && char.IsHighSurrogate(value[limit - 1]) && char.IsLowSurrogate(value[limit])) { limit--; }
        return value[..limit];
    }
    private bool Next()
    {
        if (_hasPending) { return true; }
        lock (_queueGate)
        {
            if (_count == 0) { return false; }
            _pending = _queue[_head]; _queue[_head] = default; _head = (_head + 1) % _queue.Length; _count--; _hasPending = true; return true;
        }
    }
    private string FilePath(int index) => Path.Combine(_directory, index == 0 ? "dungeons.log" : "dungeons." + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".log");
    private void Open()
    {
        Directory.CreateDirectory(_directory);
        _file = new(FilePath(0), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        long length = _file.Length;
        if (length > 0)
        {
            _file.Position = length - 1;
            if (_file.ReadByte() != '\n')
            {
                long end = length, complete = 0;
                while (end > 0)
                {
                    int count = (int)System.Math.Min(end, _tailBuffer.Length); long start = end - count;
                    _file.Position = start; _file.ReadExactly(_tailBuffer.AsSpan(0, count));
                    for (int i = count - 1; i >= 0; i--) { if (_tailBuffer[i] == '\n') { complete = start + i + 1; break; } }
                    if (complete > 0) { break; } end = start;
                }
                _file.SetLength(complete); RecoveredTailBytes += length - complete;
            }
        }
        _file.Position = _file.Length;
    }
    private void Rotate()
    {
        Close();
        for (int i = MaximumFiles - 1; i >= 1; i--)
        { string previous = FilePath(i - 1); if (File.Exists(previous)) { File.Move(previous, FilePath(i), overwrite: true); } }
        Open();
    }
    private void Encode(Entry entry)
    {
        int length = entry.Message.Length;
        while (true)
        {
            _record.Clear();
            using (var writer = new Utf8JsonWriter(_record))
            {
                writer.WriteStartObject(); writer.WriteString("utc", entry.Utc); writer.WriteString("level", entry.Level switch { LogLevel.Debug => "Debug", LogLevel.Info => "Info", LogLevel.Warning => "Warning", _ => "Error" });
                writer.WriteString("category", entry.Category); writer.WriteString("levelId", entry.Context.LevelId ?? "");
                writer.WriteNumber("seed", entry.Context.Seed); writer.WriteNumber("tick", entry.Context.Tick);
                writer.WriteString("message", entry.Message.AsSpan(0, length)); writer.WriteBoolean("truncated", entry.Truncated || length != entry.Message.Length);
                writer.WriteNumber("dropped", DroppedEntries); writer.WriteNumber("recoveredTailBytes", RecoveredTailBytes); writer.WriteEndObject(); writer.Flush();
            }
            if (_record.WrittenCount + 1 <= _maximumBytes) { break; }
            length /= 2;
            if (length > 0 && char.IsHighSurrogate(entry.Message[length - 1]) && char.IsLowSurrogate(entry.Message[length])) { length--; }
        }
        _record.GetSpan(1)[0] = (byte)'\n'; _record.Advance(1);
    }
    public bool Flush(int maximumEntries = 64)
    {
        if (maximumEntries < 1) { throw new ArgumentOutOfRangeException(nameof(maximumEntries)); }
        lock (_flushGate)
        {
            try
            {
                for (int i = 0; i < maximumEntries && Next(); i++)
                {
                    if (_file is null) { Open(); } Encode(_pending);
                    if (_file!.Length + _record.WrittenCount > _maximumBytes) { Rotate(); }
                    long start = _file!.Length;
                    try { _file.Write(_record.WrittenSpan); }
                    catch { try { _file.SetLength(start); _file.Position = start; } catch (IOException) { } throw; }
                    _pending = default; _hasPending = false;
                }
                _file?.Flush(flushToDisk: true); Close(); LastError = null; return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { LastError = error.Message; Close(); return false; }
        }
    }
    private void Close()
    { try { _file?.Dispose(); } catch (IOException) { } finally { _file = null; } }
    public void Dispose()
    {
        lock (_flushGate)
        {
            lock (_queueGate) { if (_disposed) { return; } _disposed = true; }
            Flush(int.MaxValue); Close();
            lock (_queueGate) { Array.Clear(_queue); _count = 0; } _pending = default; _hasPending = false;
        }
    }
}
