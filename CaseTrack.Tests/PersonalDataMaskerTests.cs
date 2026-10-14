using CaseTrack.Application.Privacy;

namespace CaseTrack.Tests;

public class PersonalDataMaskerTests
{
    [Theory]
    [InlineData("王", "*")]
    [InlineData("王明", "王*")]
    [InlineData("王小明", "王*明")]
    [InlineData("abcd", "a**d")]
    [InlineData("歐陽小明", "歐**明")]
    [InlineData("0912345678", "09******78")]
    [InlineData("test@example.com", "test********.com")]
    public void Mask_VariousLengths_KeepsAboutAQuarterAtEachEnd(string value, string expected)
    {
        Assert.Equal(expected, PersonalDataMasker.Mask(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mask_NullOrEmpty_ReturnsAsIs(string? value)
    {
        Assert.Equal(value, PersonalDataMasker.Mask(value));
    }

    // 𠀀 是罕見字，在 .NET 字串裡佔兩個 char；用 char 計算長度會把它切成兩半
    [Fact]
    public void Mask_RareCharacter_TreatedAsOneCharacter()
    {
        Assert.Equal("𠀀*明", PersonalDataMasker.Mask("𠀀小明"));
    }
}
