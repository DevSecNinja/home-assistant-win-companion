using WindowsCompanion.Core.App;

namespace WindowsCompanion.Core.Tests;

public class ServerUrlNormalizerTests
{
    [Theory]
    [InlineData("ha.local:8123", "https://ha.local:8123/")]
    [InlineData(" http://192.168.1.2:8123/lovelace ", "http://192.168.1.2:8123/")]
    [InlineData("https://example.test/", "https://example.test/")]
    public void Normalizes_to_an_origin(string input, string expected)
    {
        Assert.Equal(expected, ServerUrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.test")]
    public void Rejects_invalid_urls(string input)
    {
        Assert.Throws<ArgumentException>(() => ServerUrlNormalizer.Normalize(input));
    }
}
