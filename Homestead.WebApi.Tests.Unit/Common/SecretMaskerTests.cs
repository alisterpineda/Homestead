using Homestead.WebApi.Common;

namespace Homestead.WebApi.Tests.Unit.Common;

public class SecretMaskerTests
{
    [Fact]
    public void Mask_Null_ReturnsNull()
    {
        Assert.Null(SecretMasker.Mask(null));
    }

    [Fact]
    public void Mask_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SecretMasker.Mask(""));
    }

    [Theory]
    [InlineData("a", "••••")]
    [InlineData("ab", "••••")]
    [InlineData("abcd", "••••")]
    public void Mask_Short_ReturnsAllDots(string input, string expected)
    {
        Assert.Equal(expected, SecretMasker.Mask(input));
    }

    [Theory]
    [InlineData("abcde", "••••bcde")]
    [InlineData("super-secret-key-1234", "••••1234")]
    public void Mask_Long_ReturnsDotsPlusLast4(string input, string expected)
    {
        Assert.Equal(expected, SecretMasker.Mask(input));
    }
}
