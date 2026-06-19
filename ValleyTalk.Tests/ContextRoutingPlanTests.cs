using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using ValleyTalk;

namespace ValleyTalk.Tests;

public sealed class ContextRoutingPlanTests
{
    [Fact]
    public void ConservativeBriefKeepsHardBoundaryModules()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();

        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.NpcProfile));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.DateTime));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Location));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.LivingNpc));
        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.CurrentConversation));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Relationship));
        Assert.Equal(ContextDetail.None, plan.Get(ContextModule.SampleDialogue));
    }

    [Fact]
    public void GiftDependencyPromotesRelationshipAndLivingNpcContext()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();
        plan.Set(ContextModule.LivingNpc, ContextDetail.None);
        plan.Set(ContextModule.Relationship, ContextDetail.None);
        plan.Set(ContextModule.Gift, ContextDetail.Full);

        plan.ApplyDependencies();

        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.Gift));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.LivingNpc));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Relationship));
    }

    [Fact]
    public void FullLocationPromotesSceneDependencies()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();
        plan.Set(ContextModule.DateTime, ContextDetail.None);
        plan.Set(ContextModule.Weather, ContextDetail.None);
        plan.Set(ContextModule.Location, ContextDetail.Full);

        plan.ApplyDependencies();

        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.Location));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.DateTime));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Weather));
    }

    [Fact]
    public void NewContextRoutingConfigKeysAreLocalizedAndInjected()
    {
        string root = FindRepositoryRoot();
        string promptsPath = Path.Combine(root, "ValleyTalk", "ContentPack", "assets", "Prompts.json");
        string defaultI18nPath = Path.Combine(root, "ValleyTalk", "ContentPack", "i18n", "default.json");
        string zhI18nPath = Path.Combine(root, "ValleyTalk", "ContentPack", "i18n", "zh.json");

        using JsonDocument prompts = JsonDocument.Parse(File.ReadAllText(promptsPath));
        using JsonDocument defaultI18n = JsonDocument.Parse(File.ReadAllText(defaultI18nPath));
        using JsonDocument zhI18n = JsonDocument.Parse(File.ReadAllText(zhI18nPath));

        JsonElement entries = prompts.RootElement.GetProperty("Changes")[0].GetProperty("Entries");
        string[] requiredKeys =
        {
            "configEnableSemanticContextRouting",
            "configEnableSemanticContextRoutingTooltip",
            "configSemanticContextRoutingTimeoutSeconds",
            "configSemanticContextRoutingTimeoutSecondsTooltip",
            "configEnableLivingNpcActionDecisionPass",
            "configEnableLivingNpcActionDecisionPassTooltip",
            "configLivingNpcActionDecisionTimeoutSeconds",
            "configLivingNpcActionDecisionTimeoutSecondsTooltip"
        };

        foreach (string key in requiredKeys)
        {
            Assert.True(defaultI18n.RootElement.TryGetProperty(key, out _), $"Missing default i18n key: {key}");
            Assert.True(zhI18n.RootElement.TryGetProperty(key, out _), $"Missing zh i18n key: {key}");
            Assert.True(entries.TryGetProperty(key, out JsonElement promptEntry), $"Missing Prompts.json injection key: {key}");
            Assert.Equal($"{{{{i18n:{key}}}}}", promptEntry.GetString());
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "ValleyTalk"))
                && Directory.Exists(Path.Combine(directory.FullName, "ValleyTalk.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}