using System.Text.Json;
using Dungeons.Game.Persistence;
using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void ExtendedControllerButtonsAreSupportedAndInvalidInputCannotEnterADraft()
    {
        Assert.True(SettingsValidation.IsValidBinding(new(BindingKind.JoyButton, 35))); Assert.True(SettingsValidation.IsValidBinding(new(BindingKind.JoyButton, 127)));
        Assert.False(SettingsValidation.IsValidBinding(new(BindingKind.JoyButton, 128))); Assert.False(SettingsValidation.IsValidBinding(new(BindingKind.JoyAxis, -1)));
    }
    private static string Folder() => Path.Combine(MissionAssets.Root, "artifacts/m8/settings-tests", Guid.NewGuid().ToString("N"));
    private static GameSettings Defaults => GameSettings.Defaults(QualityPreset.High, false);
    [Fact]
    public void FirstLaunchChoiceIsPersistedAndDoesNotFollowLaterPlatformDefaults()
    {
        string path = Folder(); var store = new SettingsStore(path, GameSettings.Defaults(QualityPreset.Low, true));
        Assert.Equal(QualityPreset.Low, store.Current.Video.Preset); Assert.Equal(.75f, store.Current.Video.ResolutionScale);
        Assert.Equal(QualityPreset.Low, new SettingsStore(path, Defaults).Current.Video.Preset); Assert.Empty(Directory.GetFiles(path, "*.tmp"));
    }
    [Fact]
    public void AllSettingsRoundTripAndTheStoreOwnsItsSnapshot()
    {
        string path = Folder(); var store = new SettingsStore(path, Defaults);
        var bindings = new Dictionary<string, InputBinding[]> { ["attack"] = [new(BindingKind.Key, 74, Shift: true), new(BindingKind.JoyButton, 2)] };
        var positions = new Dictionary<string, TouchPosition> { ["Potion"] = new(.3f, .2f) };
        var draft = Defaults with { Video = Defaults.Video with { Preset = QualityPreset.Custom, ResolutionScale = .8f, Fullscreen = true, WindowX = -700, WindowY = 100, UiScale = 1.3f },
            Audio = new() { Master = 0, Music = 40, Sfx = 50, Ui = 60, Ambience = 70 }, Controls = new() { AutoAim = AimAssist.Strong, LeftHanded = true, TouchScale = 1.2f, Vibration = false, Bindings = bindings, TouchPositions = positions },
            Accessibility = new() { DamageNumbers = false, HighContrastHealth = true, HealthPalette = HealthPalette.Tritanopia, ReduceMotion = true, TextScale = 1.4f } };
        Assert.True(store.Save(draft)); bindings["attack"][0] = new(BindingKind.Key, 75); positions["Potion"] = new(.9f, .9f);
        var actual = new SettingsStore(path, Defaults).Current;
        Assert.Equal(draft.Video, actual.Video); Assert.Equal(draft.Audio, actual.Audio); Assert.Equal(draft.Accessibility, actual.Accessibility);
        Assert.False(actual.Controls.Vibration); Assert.True(actual.Controls.LeftHanded); Assert.Equal(AimAssist.Strong, actual.Controls.AutoAim);
        Assert.Equal(74, actual.Controls.Bindings["attack"][0].Code); Assert.Equal(new TouchPosition(.3f, .2f), actual.Controls.TouchPositions["Potion"]);
        Assert.Equal(74, store.Current.Controls.Bindings["attack"][0].Code); Assert.Equal(new TouchPosition(.3f, .2f), store.Current.Controls.TouchPositions["Potion"]);
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\":2}")]
    [InlineData("{\"formatVersion\":1,\"formatVersion\":1}")]
    [InlineData("{\"formatVersion\":1,\"unknown\":true}")]
    [InlineData("{\"formatVersion\":1,\"video\":null}")]
    [InlineData("{\"formatVersion\":1,\"video\":{\"uiScale\":999}}")]
    [InlineData("{\"formatVersion\":1,\"controls\":{\"bindings\":{\"attack\":[{\"kind\":99,\"code\":1}]}}}")]
    public void CorruptSettingsAreRetainedByteForByteAndDefaultsRecreated(string corrupt)
    {
        string path = Folder(); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "settings.json"), corrupt);
        var warnings = new List<string>(); var store = new SettingsStore(path, Defaults, warnings.Add); Assert.NotEmpty(warnings); Assert.Equal(Defaults.Video, store.Current.Video);
        string backup = Assert.Single(Directory.GetFiles(path, "settings.json.corrupt-*")); Assert.Equal(corrupt, File.ReadAllText(backup));
        Assert.Equal(QualityPreset.High, SettingsStore.Read(File.ReadAllText(Path.Combine(path, "settings.json"))).Video.Preset);
    }
    [Fact]
    public void InvalidEditsNeverReplaceLastGoodFileAndNonFiniteValuesAreRejected()
    {
        string path = Folder(); var store = new SettingsStore(path, Defaults); string before = File.ReadAllText(Path.Combine(path, "settings.json"));
        GameSettings[] invalid = [Defaults with { Video = Defaults.Video with { ResolutionScale = float.NaN } }, Defaults with { Audio = new() { Music = 101 } },
            Defaults with { Controls = new() { TouchPositions = new() { ["Attack"] = new(float.PositiveInfinity, 0) } } },
            Defaults with { Controls = new() { Bindings = new() { ["not_a_game_action"] = [] } } },
            Defaults with { Controls = new() { Bindings = new() { ["roll"] = [new(BindingKind.JoyAxis, 99)] } } },
            Defaults with { Controls = new() { Bindings = new() { ["roll"] = [new(BindingKind.JoyButton, 1, Shift: true)] } } },
            Defaults with { Video = Defaults.Video with { WindowX = 0, WindowY = null } }];
        foreach (GameSettings edit in invalid) { Assert.Throws<ArgumentException>(() => store.Save(edit)); Assert.Equal(before, File.ReadAllText(Path.Combine(path, "settings.json"))); }
        Assert.Throws<JsonException>(() => SettingsStore.Read(" " + new string(' ', SettingsStore.MaxBytes)));
    }
    [Fact]
    public void StorageFailureReportsItAndKeepsTheLastCommittedSnapshot()
    {
        string path = Folder(); var warnings = new List<string>(); var store = new SettingsStore(path, Defaults, warnings.Add);
        File.Delete(Path.Combine(path, "settings.json")); Directory.CreateDirectory(Path.Combine(path, "settings.json"));
        Assert.False(store.Save(Defaults with { Audio = new() { Music = 0 } })); Assert.Equal(70, store.Current.Audio.Music); Assert.NotEmpty(warnings);
    }
    [Fact]
    public void PresetSelectionPreservesWindowAndUiPreferencesWhileCustomFieldsChange()
    {
        VideoSettings initial = new() { UiScale = 1.5f, Fullscreen = true, WindowX = 100, WindowY = 200 };
        VideoSettings low = initial.WithPreset(QualityPreset.Low, true), high = low.WithPreset(QualityPreset.High, false);
        Assert.Equal(ShadowQuality.Off, low.Shadows); Assert.Equal(4, low.OmniLights); Assert.True(low.Adaptive); Assert.False(low.Msaa);
        Assert.Equal(ShadowQuality.High, high.Shadows); Assert.Equal(8, high.OmniLights); Assert.True(high.Msaa); Assert.False(high.Adaptive);
        Assert.Equal(1.5f, high.UiScale); Assert.True(high.Fullscreen); Assert.Equal(100, high.WindowX); Assert.Equal(200, high.WindowY);
    }
}
