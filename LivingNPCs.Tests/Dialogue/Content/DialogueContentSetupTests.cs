using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;

namespace LivingNPCs.Tests.Dialogue.Content;

public sealed class DialogueContentSetupTests
{
    [Fact]
    public void MergeLocalizedPrompts_RemovesDefaultGenderVariantsWhenLocalizedBaseExists()
    {
        var table = new Dictionary<string, string>
        {
            ["systemPrompt"] = "default base",
            ["systemPrompt.MaleNpc"] = "default male",
            ["systemPrompt.FemaleNpc"] = "default female"
        };
        var localized = new Dictionary<string, string>
        {
            ["systemPrompt"] = "中文新版基础提示词"
        };

        DialogueContentSetup.MergeLocalizedPrompts(table, localized);

        Assert.Equal("中文新版基础提示词", table["systemPrompt"]);
        Assert.False(table.ContainsKey("systemPrompt.MaleNpc"));
        Assert.False(table.ContainsKey("systemPrompt.FemaleNpc"));
    }

    [Fact]
    public void MergeLocalizedPrompts_PreservesExplicitLocalizedGenderVariant()
    {
        var table = new Dictionary<string, string>
        {
            ["relationship"] = "default base",
            ["relationship.MaleNpc"] = "default male",
            ["relationship.FemaleNpc"] = "default female"
        };
        var localized = new Dictionary<string, string>
        {
            ["relationship"] = "中文基础文案",
            ["relationship.FemaleNpc"] = "她的中文性别文案"
        };

        DialogueContentSetup.MergeLocalizedPrompts(table, localized);

        Assert.Equal("中文基础文案", table["relationship"]);
        Assert.False(table.ContainsKey("relationship.MaleNpc"));
        Assert.Equal("她的中文性别文案", table["relationship.FemaleNpc"]);
    }
}