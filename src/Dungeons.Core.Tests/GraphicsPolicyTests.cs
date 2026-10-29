using System.Text.Json;
using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class GraphicsPolicyTests
{
    [Fact]
    public void GodotRemovedSeparatorIsRestoredWithoutDuplicatingUserArguments()
    {
        string[] user = ["--level", "custom.level", "--rendering-driver=keep-user-text", RendererArguments.AttemptedFlag];
        string[] engine = ["--path", "my project", "--rendering-method", "mobile"];
        string[] combined = [.. engine, .. user];
        string[] expected = ["--path", "my project", "--rendering-method", "gl_compatibility", "--", "--level", "custom.level", "--rendering-driver=keep-user-text", RendererArguments.AttemptedFlag];
        Assert.Equal(expected, RendererArguments.FromGodot(combined, user));
        Assert.Equal(expected, RendererArguments.FromGodot([.. engine, "--", .. user], user));
        Assert.Equal(expected, RendererArguments.FromGodot([.. engine, "++", .. user], user));
        Assert.Equal(RendererArguments.Compatibility(engine), RendererArguments.FromGodot(engine, []));
    }
    [Fact]
    public void AdaptiveDropsAfterSixtySlowFramesAndStaysAtMinimum()
    {
        var model = new AdaptiveResolutionModel();
        for (int i = 0; i < 59; i++) { Assert.False(model.Sample(19)); } Assert.Equal(1, model.Scale);
        Assert.True(model.Sample(19)); Assert.Equal(.95f, model.Scale);
        for (int i = 0; i < 1200; i++) { model.Sample(19); } Assert.Equal(.6f, model.Scale);
        double before = model.ActiveSeconds; Assert.False(model.Sample(double.NaN)); Assert.False(model.Sample(0)); Assert.False(model.Sample(-1)); Assert.Equal(before, model.ActiveSeconds);
    }
    [Fact]
    public void AdaptiveRequiresThreeHundredGoodFramesAndHonorsPresetCeiling()
    {
        var model = new AdaptiveResolutionModel(.6f, .75f);
        for (int i = 0; i < 299; i++) { Assert.False(model.Sample(16)); } Assert.Equal(.6f, model.Scale);
        Assert.True(model.Sample(16)); Assert.Equal(.65f, model.Scale);
        for (int i = 0; i < 900; i++) { model.Sample(16); } Assert.Equal(.75f, model.Scale);
        model.Configure(.6f, 1); for (int i = 0; i < 299; i++) { model.Sample(16); } model.Sample(19);
        for (int i = 0; i < 299; i++) { model.Sample(16); } Assert.Equal(.6f, model.Scale);
        model.Sample(16); Assert.Equal(.65f, model.Scale);
    }
    [Fact]
    public void ThermalHistorySurvivesPauseAndLowersThresholdAfterTenMinutes()
    {
        var model = new AdaptiveResolutionModel(); for (int i = 0; i < 60; i++) { model.Sample(14); }
        model.ResetWindow(); Assert.True(model.ActiveSeconds > 0); Assert.Equal(0, model.AverageFrameMs);
        while (model.ActiveSeconds < 600) { model.Sample(16); }
        Assert.False(model.ThermalLimited); Assert.Equal(1, model.Scale);
        for (int i = 0; i < 60; i++) { model.Sample(17.2); }
        Assert.True(model.ThermalLimited); Assert.Equal(16, model.ThresholdMs); Assert.Equal(.95f, model.Scale);
        double elapsed = model.ActiveSeconds; model.Configure(.75f, .75f); Assert.True(model.ThermalLimited); Assert.Equal(elapsed, model.ActiveSeconds);
    }
    [Fact]
    public void AdaptiveSamplingAllocatesNothingAfterWarmup()
    {
        var model = new AdaptiveResolutionModel(); for (int i = 0; i < 1200; i++) { model.Sample(i % 600 < 300 ? 20 : 16); }
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1200; i++) { model.Sample(i % 600 < 300 ? 20 : 16); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Theory]
    [InlineData("*Adreno*3??", "Adreno (TM) 330", true)]
    [InlineData("*Adreno*3??", "Adreno (TM) 630", false)]
    [InlineData("Mali-G31*", "mali-g31 MP2", true)]
    [InlineData("*ab?c", "xxababbc", true)]
    [InlineData("a?", "a", false)]
    [InlineData("*", "", true)]
    public void DevicePatternsAreCaseInsensitiveAndBounded(string pattern, string adapter, bool matches)
        => Assert.Equal(matches, DeviceProfileCatalog.Matches(pattern, adapter));
    [Fact]
    public void OrderedDeviceRulesMatchGpuAndModelAndDefaultsTrackBackend()
    {
        var catalog = DeviceProfileCatalog.Parse("""{"formatVersion":1,"rules":[{"id":"model-first","platform":"android","model":"legacy*","preset":"Low","renderer":"compatibility"},{"id":"gpu","adapter":"test*","preset":"Medium"}]}""");
        Assert.Equal(new DeviceSelection(QualityPreset.Low, RendererOverride.Compatibility, "model-first"), catalog.Select(new("android", "TEST GPU", "LegacyPhone", true, true)));
        Assert.Equal("gpu", catalog.Select(new("ios", "Test GPU", "iPhone", true, true)).Rule);
        Assert.Equal(QualityPreset.Medium, catalog.Select(new("ios", "Apple GPU", "iPhone", true, true)).Preset);
        Assert.Equal(QualityPreset.High, catalog.Select(new("windows", "NVIDIA", "PC", false, true)).Preset);
        Assert.Equal(QualityPreset.Low, catalog.Select(new("android", "Test GPU", "phone", true, false)).Preset);
    }
    [Theory]
    [InlineData("{\"formatVersion\":2,\"rules\":[]}")]
    [InlineData("{\"formatVersion\":1,\"formatVersion\":1,\"rules\":[]}")]
    [InlineData("{\"formatVersion\":1,\"rules\":[{\"id\":\"a\",\"preset\":\"Custom\"}]}")]
    [InlineData("{\"formatVersion\":1,\"rules\":[{\"id\":\"a\",\"preset\":\"Low\",\"renderer\":\"compatibility\"}]}")]
    [InlineData("{\"formatVersion\":1,\"rules\":[{\"id\":\"a\",\"preset\":\"Low\"},{\"id\":\"a\",\"preset\":\"High\"}]}")]
    public void InvalidDeviceRulesAreRejected(string json) => Assert.Throws<JsonException>(() => DeviceProfileCatalog.Parse(json));
    [Fact]
    public void QualityPresetsMatchShadowLightScaleAndMobileMsaaRules()
    {
        GraphicsOptions low = GraphicsOptions.ForPreset(QualityPreset.Low, true), medium = GraphicsOptions.ForPreset(QualityPreset.Medium, true), high = GraphicsOptions.ForPreset(QualityPreset.High, false);
        Assert.False(low.Shadows); Assert.False(low.Glow); Assert.Equal(4, low.OmniLights); Assert.Equal(.75f, low.ResolutionScale);
        Assert.True(medium.Shadows); Assert.Equal(1024, medium.ShadowAtlas); Assert.True(medium.Glow); Assert.True(medium.Adaptive); Assert.False(medium.Msaa);
        Assert.Equal(4096, high.ShadowAtlas); Assert.True(high.Msaa); Assert.False(high.Adaptive); Assert.False(GraphicsOptions.ForPreset(QualityPreset.High, true).Msaa);
    }
    [Fact]
    public void RendererRelaunchKeepsUserArgumentsAndRemovesConflictingDriver()
    {
        string[] args = RendererArguments.Compatibility(["--path", "game", "--rendering-method", "mobile", "--rendering-driver=vulkan", "--", "--level", "user.level", "--seed=7", RendererArguments.AttemptedFlag]);
        Assert.Equal(new[] { "--path", "game", "--rendering-method", "gl_compatibility", "--", "--level", "user.level", "--seed=7", RendererArguments.AttemptedFlag }, args);
        Assert.Equal(args, RendererArguments.Compatibility(args));
    }
}
