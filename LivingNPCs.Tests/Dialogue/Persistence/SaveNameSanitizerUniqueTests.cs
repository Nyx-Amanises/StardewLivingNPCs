using System.Text.RegularExpressions;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Persistence;

/// <summary>
/// F8 修复验证：v2 无碰撞净化。常规名字与 v1 逐字一致（键不变、零迁移）；
/// v1 的三类碰撞源（删字符合并、低字节十六进制截断、50 字符截断）在 v2 下均得到
/// 互不相同的键；输出只含 SMAPI 存档键合法字符。旧 Sanitize/SanitizeLower 契约不动
///（迁移反查旧 ValleyTalk 键仍依赖它，另有 SaveNameSanitizerTests 钉死）。
/// </summary>
public sealed class SaveNameSanitizerUniqueTests
{
    [Theory]
    [InlineData("Abigail")]
    [InlineData("Mr.Qi-2_x")]
    [InlineData("SomeUnloadedNpc")]
    public void Legal_Names_Keep_The_Exact_V1_Key(string name)
    {
        Assert.Equal(SaveNameSanitizer.SanitizeLower(name), SaveNameSanitizer.SanitizeUniqueLower(name));
    }

    [Fact]
    public void Low_Byte_Hex_Collision_Pair_Gets_Distinct_Keys()
    {
        // U+963F 与 U+543F 低字节同为 0x3F：v1 撞键（历史互相覆盖的根源），v2 必须区分。
        const string a = "阿";
        const string b = "吿";
        Assert.Equal(SaveNameSanitizer.SanitizeLower(a), SaveNameSanitizer.SanitizeLower(b));
        Assert.NotEqual(SaveNameSanitizer.SanitizeUniqueLower(a), SaveNameSanitizer.SanitizeUniqueLower(b));
        Assert.Equal("-u963f", SaveNameSanitizer.SanitizeUniqueLower(a));
        Assert.Equal("-u543f", SaveNameSanitizer.SanitizeUniqueLower(b));
    }

    [Fact]
    public void Removed_Character_Merge_Pair_Gets_Distinct_Keys()
    {
        // v1 删掉空格后 "Marlon Fay" 与 "MarlonFay" 同键；v2 用全名码元编码区分。
        Assert.Equal(SaveNameSanitizer.SanitizeLower("Marlon Fay"), SaveNameSanitizer.SanitizeLower("MarlonFay"));
        Assert.NotEqual(
            SaveNameSanitizer.SanitizeUniqueLower("Marlon Fay"),
            SaveNameSanitizer.SanitizeUniqueLower("MarlonFay"));
        Assert.Equal(
            "marlonfay-u004d00610072006c006f006e0020004600610079",
            SaveNameSanitizer.SanitizeUniqueLower("Marlon Fay"));
    }

    [Fact]
    public void Truncation_Collision_Pair_Gets_Distinct_Keys()
    {
        string prefix = new string('a', 50);
        string nameA = prefix + "one";
        string nameB = prefix + "two";
        Assert.Equal(SaveNameSanitizer.SanitizeLower(nameA), SaveNameSanitizer.SanitizeLower(nameB));
        Assert.NotEqual(SaveNameSanitizer.SanitizeUniqueLower(nameA), SaveNameSanitizer.SanitizeUniqueLower(nameB));
    }

    [Theory]
    [InlineData("Abigail")]
    [InlineData("Marlon Fay")]
    [InlineData("雪月")]
    [InlineData("Abby雪")]
    [InlineData("")]
    public void Outputs_Use_Only_Legal_Save_Key_Characters(string name)
    {
        string slug = SaveNameSanitizer.SanitizeUniqueLower(name);
        Assert.Matches(new Regex("^[a-z0-9_.\\-]*$"), slug);
    }
}
