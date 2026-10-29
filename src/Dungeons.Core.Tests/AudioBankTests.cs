using Dungeons.Core.Content;
using Dungeons.Game.Audio;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class AudioBankTests
{
    [Theory]
    [InlineData("\"bus\":\"Other\"")]
    [InlineData("\"bus\":\"UI\",\"positional\":true")]
    [InlineData("\"maxInstances\":17")]
    [InlineData("\"pitchRange\":[0.1,1]")]
    [InlineData("\"volumeDb\":12")]
    [InlineData("\"loopOffset\":-1")]
    public void UnsafePresentationMetadataIsRejectedBeforeVoicePreparation(string invalid)
    {
        ContentRegistry registry = ContentRegistry.Load([("audio.json", "{\"core:test\":{\"files\":[\"audio/test.ogg\"]," + invalid + "}}")]);
        Assert.Throws<InvalidDataException>(() => new SoundBank(registry));
    }

    [Fact]
    public void VoicePreparationIsBoundedAcrossTheWholeBank()
    {
        string json = "{" + string.Join(',', Enumerable.Range(0, 17).Select(i => $"\"core:s{i}\":{{\"files\":[\"audio/test.ogg\"],\"maxInstances\":16}}")) + "}";
        Assert.Throws<InvalidDataException>(() => new SoundBank(ContentRegistry.Load([("audio.json", json)])));
    }
}
