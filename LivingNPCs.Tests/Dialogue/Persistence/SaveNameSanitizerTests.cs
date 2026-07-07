using Xunit;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue.Persistence;

public sealed class SaveNameSanitizerTests
{
    [Theory]
    [InlineData("Abigail", "Abigail")]
    [InlineData("Marlon Fay", "MarlonFay")]           // 空格不在合法集合中，删除
    [InlineData("Mr. Qi-2_x", "Mr.Qi-2_x")]           // 点/连字符/下划线保留
    [InlineData("Léah", "Lah")]                       // 变音字符删除，其余保留
    public void KeepsOnlyLegalCharacters(string name, string expected)
    {
        Assert.Equal(expected, SaveNameSanitizer.Sanitize(name));
    }

    [Fact]
    public void FullyIllegalNameFallsBackToUppercaseHexOfCharBytes()
    {
        // '雪' = U+96EA，强转 byte 取低位 0xEA；'月' = U+6708 → 0x08。
        Assert.Equal("EA08", SaveNameSanitizer.Sanitize("雪月"));
    }

    [Fact]
    public void LongNamesAreTruncatedToFiftyCharacters()
    {
        string name = new string('a', 60);
        Assert.Equal(50, SaveNameSanitizer.Sanitize(name).Length);
    }

    [Fact]
    public void HexFallbackIsAlsoTruncated()
    {
        string name = new string('雪', 40); // 80 个十六进制字符 → 截到 50
        string sanitized = SaveNameSanitizer.Sanitize(name);
        Assert.Equal(50, sanitized.Length);
        Assert.StartsWith("EAEA", sanitized);
    }

    [Fact]
    public void SanitizeLowerMatchesSmapiPhysicalKeyCasing()
    {
        // SMAPI 物理键整体转小写：新键的名字段直接用小写形态，避免大小写陷阱（§5.1）。
        Assert.Equal("marlonfay", SaveNameSanitizer.SanitizeLower("Marlon Fay"));
    }

    [Fact]
    public void EmptyNameYieldsEmptySegment()
    {
        Assert.Equal(string.Empty, SaveNameSanitizer.Sanitize(""));
    }
}
