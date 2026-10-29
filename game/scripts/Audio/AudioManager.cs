using Godot;

namespace Dungeons.Game.Audio;

/// <summary>Presentation-only voices, separate random stream, music fades and lifecycle suspension.</summary>
public partial class AudioManager : Node
{
    private sealed class Voice
    {
        internal AudioStreamPlayer? Flat;
        internal AudioStreamPlayer3D? Spatial;
        internal ulong Started;
        internal bool Playing => Spatial?.Playing ?? Flat!.Playing;
        internal void Stop() { Spatial?.Stop(); Flat?.Stop(); }
        internal void Pause(bool paused) { if (Spatial is not null) { Spatial.StreamPaused = paused; } else { Flat!.StreamPaused = paused; } }
        internal void Play(AudioStream stream, float volume, float pitch, Vector3 position)
        {
            if (Spatial is not null) { Spatial.Stream = stream; Spatial.Position = position; Spatial.VolumeDb = volume; Spatial.PitchScale = pitch; Spatial.Play(); }
            else { Flat!.Stream = stream; Flat.VolumeDb = volume; Flat.PitchScale = pitch; Flat.Play(); }
        }
    }
    private sealed record Prepared(SoundDefinition Definition, AudioStreamOggVorbis[] Streams, Voice[] Voices);
    private readonly Dictionary<string, Prepared> _prepared = new(StringComparer.Ordinal);
    private readonly AudioStreamPlayer[] _music = new AudioStreamPlayer[2];
    private readonly float[] _gain = new float[2], _from = new float[2], _to = new float[2], _volume = new float[2];
    private readonly Random _random = new();
    private SoundBank _bank = null!;
    private ulong _sequence;
    private float _fade = 1;
    private object? _worldOwner;
    private Node? _uiRoot;
    private SceneTree? _uiTree;
    private ulong _lastHover;
    public static AudioManager? Instance { get; private set; }
    public string MusicId { get; private set; } = "";
    public bool BackgroundPaused { get; private set; }
    public float MusicDuckDb { get; private set; }
    public string Driver { get; private set; } = "";
    public int ActiveVoiceCount
    {
        get { int count = 0; foreach (Prepared sound in _prepared.Values) { foreach (Voice voice in sound.Voices) { if (voice.Playing) { count++; } } } return count; }
    }
    public int PreparedVoiceCount { get; private set; }
    public int ActiveVoices(string id) => _prepared.TryGetValue(id, out Prepared? p) ? p.Voices.Count(v => v.Playing) : 0;
    public float MusicGain(int channel) => _gain[channel];
    public override void _EnterTree() { Instance = this; }
    public override void _ExitTree() { StopForShutdown(); if (Instance == this) { Instance = null; } }
    /// <summary>Detach playback while the audio server is alive, before the app requests tree shutdown.</summary>
    internal void StopForShutdown()
    {
        if (_uiTree is not null) { _uiTree.NodeAdded -= BindButton; _uiTree = null; } _uiRoot = null;
        BackgroundPaused = true; _worldOwner = null;
        foreach (Prepared sound in _prepared.Values)
        {
            foreach (Voice voice in sound.Voices)
            { voice.Stop(); if (voice.Spatial is not null) { voice.Spatial.Stream = null; } else { voice.Flat!.Stream = null; } }
        }
        _prepared.Clear();
        foreach (AudioStreamPlayer player in _music) { if (player is not null) { player.Stop(); player.Stream = null; } }
    }

    public void Initialize(SoundBank bank)
    {
        if (_bank is not null) { throw new InvalidOperationException("Audio manager is already initialized."); }
        _bank = bank;
        Driver = AudioServer.GetDriverName();
        foreach (string name in new[] { "Music", "SFX", "UI", "Ambience" })
        {
            if (AudioServer.GetBusIndex(name) >= 0) { continue; }
            AudioServer.AddBus(); int index = AudioServer.BusCount - 1; AudioServer.SetBusName(index, name); AudioServer.SetBusSend(index, "Master");
        }
        for (int i = 0; i < 2; i++) { _music[i] = new() { Bus = "Music", VolumeDb = -80 }; AddChild(_music[i]); }
        foreach (SoundDefinition definition in bank.Sounds.Values)
        {
            if (definition.Bus == "Music") { continue; } // Compressed music is opened on demand, never expanded to a PCM buffer here.
            AudioStreamOggVorbis[] streams = definition.Files.Select(path => Load(path, definition)).ToArray();
            Voice[] voices = new Voice[definition.MaximumInstances];
            PreparedVoiceCount += voices.Length;
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new Voice(); voices[i] = voice;
                if (definition.Positional)
                { voice.Spatial = new() { Bus = definition.Bus, UnitSize = 8, MaxDistance = 30, MaxPolyphony = 1 }; AddChild(voice.Spatial); }
                else { voice.Flat = new() { Bus = definition.Bus, MaxPolyphony = 1 }; AddChild(voice.Flat); }
            }
            _prepared.Add(definition.Id, new(definition, streams, voices));
        }
    }
    private static AudioStreamOggVorbis Load(string path, SoundDefinition definition)
    {
        var stream = GD.Load<AudioStreamOggVorbis>("res://" + path) ?? throw new InvalidDataException("Missing audio: " + path);
        if (stream.Loop != definition.Loop || System.Math.Abs(stream.LoopOffset - definition.LoopOffset) > .001 || definition.LoopOffset >= stream.GetLength())
        { throw new InvalidDataException("Audio import differs from its loop metadata: " + path); }
        return stream;
    }
    public bool Play(string id, Vector3 position = default)
    {
        if (BackgroundPaused || !_prepared.TryGetValue(id, out Prepared? sound)) { return false; }
        Voice? selected = null;
        foreach (Voice voice in sound.Voices)
        { if (!voice.Playing) { selected = voice; break; } if (selected is null || voice.Started < selected.Started) { selected = voice; } }
        selected!.Stop(); selected.Started = ++_sequence;
        selected.Play(sound.Streams[_random.Next(sound.Streams.Length)], sound.Definition.VolumeDb,
            sound.Definition.MinimumPitch + _random.NextSingle() * (sound.Definition.MaximumPitch - sound.Definition.MinimumPitch), position);
        return true;
    }
    public void Stop(string id) { if (_prepared.TryGetValue(id, out Prepared? sound)) { foreach (Voice voice in sound.Voices) { voice.Stop(); } } }
    public void StopWorld()
    { foreach (Prepared sound in _prepared.Values) { if (sound.Definition.Bus != "UI") { foreach (Voice voice in sound.Voices) { voice.Stop(); } } } }
    public void BeginWorld(object owner) { StopWorld(); _worldOwner = owner; SetDucked(false); }
    public void EndWorld(object owner) { if (ReferenceEquals(owner, _worldOwner)) { StopWorld(); _worldOwner = null; SetDucked(false); } }
    public void UpdateLoopSources(string id, ReadOnlySpan<Vector3> positions)
    {
        if (BackgroundPaused || !_prepared.TryGetValue(id, out Prepared? sound) || !sound.Definition.Loop || !sound.Definition.Positional) { return; }
        for (int i = 0; i < sound.Voices.Length; i++)
        {
            Voice voice = sound.Voices[i];
            if (i >= positions.Length) { voice.Stop(); continue; }
            if (voice.Playing) { voice.Spatial!.Position = positions[i]; }
            else { voice.Started = ++_sequence; voice.Play(sound.Streams[0], sound.Definition.VolumeDb, 1, positions[i]); }
        }
    }
    public void BindUi(Node root) { _uiRoot = root; _uiTree = root.GetTree(); _uiTree.NodeAdded += BindButton; }
    private void BindButton(Node node)
    {
        if (node is not BaseButton button || _uiRoot?.IsAncestorOf(button) != true) { return; }
        button.Pressed += () => Play(button is Button label && label.Text == Tr("BACK") ? "core:sfx_ui_back" : "core:sfx_ui_click");
        void Hover() { ulong now = Time.GetTicksMsec(); if (!button.Disabled && now - _lastHover >= 60) { _lastHover = now; Play("core:sfx_ui_hover"); } }
        button.MouseEntered += Hover; button.FocusEntered += Hover;
    }
    public void ChangeMusic(string id)
    {
        if (id == MusicId || BackgroundPaused) { return; }
        if (!_bank.Sounds.TryGetValue(id, out SoundDefinition? definition) || definition.Bus != "Music") { return; }
        int channel = _gain[0] <= _gain[1] ? 0 : 1;
        _music[channel].Stop(); _gain[channel] = 0; _volume[channel] = definition.VolumeDb;
        _music[channel].Stream = Load(definition.Files[_random.Next(definition.Files.Count)], definition); _music[channel].Play();
        for (int i = 0; i < 2; i++) { _from[i] = _gain[i]; _to[i] = i == channel ? 1 : 0; }
        _fade = 0; MusicId = id; ApplyMusicVolumes();
    }
    public void SetDucked(bool ducked) { MusicDuckDb = ducked ? -6 : 0; ApplyMusicVolumes(); }
    public override void _Process(double delta)
    {
        if (_bank is null || BackgroundPaused) { return; }
        _fade = MathF.Min(1, _fade + (float)delta);
        for (int i = 0; i < 2; i++) { _gain[i] = Mathf.Lerp(_from[i], _to[i], _fade); if (_fade == 1 && _to[i] == 0) { _music[i].Stop(); } }
        ApplyMusicVolumes();
    }
    private void ApplyMusicVolumes()
    { for (int i = 0; i < 2; i++) { if (_music[i] is not null) { _music[i].VolumeDb = _volume[i] + MusicDuckDb + (_gain[i] > .0001f ? Mathf.LinearToDb(_gain[i]) : -80); } } }
    public void SetBackgroundPaused(bool paused)
    {
        BackgroundPaused = paused;
        foreach (Prepared sound in _prepared.Values) { foreach (Voice voice in sound.Voices) { voice.Pause(paused); } }
        foreach (AudioStreamPlayer player in _music) { if (player is not null) { player.StreamPaused = paused; } }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationPaused) { SetBackgroundPaused(true); }
        else if (what == NotificationApplicationResumed) { SetBackgroundPaused(false); }
    }
}
