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
    public void CloneCopiesDetailsAndStaysIndependent()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();
        plan.Set(ContextModule.World, ContextDetail.Full);

        var clone = plan.Clone();
        Assert.Equal(ContextDetail.Full, clone.Get(ContextModule.World));
        Assert.Equal(plan.Get(ContextModule.LivingNpc), clone.Get(ContextModule.LivingNpc));

        // The conversation cache stores one raw plan and hands out a clone every turn, then applies
        // per-turn boundaries to that clone. Mutating a clone must never leak back into the cached plan.
        clone.Set(ContextModule.World, ContextDetail.None);
        clone.Set(ContextModule.Farm, ContextDetail.Full);

        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.World));
        Assert.NotEqual(ContextDetail.Full, plan.Get(ContextModule.Farm));
    }

    [Fact]
    public void ConversationCuesMatchTravelAndScheduleLinesCaseInsensitively()
    {
        // Travel/companion intent drives the routing location promotion.
        Assert.True(ConversationCues.ContainsAny("你今天要去哪？", ConversationCues.LocationPromotion));
        Assert.True(ConversationCues.ContainsAny("Can I COME WITH you?", ConversationCues.LocationPromotion));
        Assert.True(ConversationCues.ContainsAny("一起去镇上吧", ConversationCues.LocationPromotion));

        // Schedule inquiries drive the future-schedule heuristic; "going"/"plan" are schedule-only.
        Assert.True(ConversationCues.ContainsAny("接下来有什么计划吗", ConversationCues.FutureSchedule));
        Assert.True(ConversationCues.ContainsAny("Where are you GOING next?", ConversationCues.FutureSchedule));

        // Both sets share the whereabouts core but stay distinct otherwise, and empty input is safe.
        Assert.True(ConversationCues.ContainsAny("where to?", ConversationCues.FutureSchedule));
        Assert.False(ConversationCues.ContainsAny("   ", ConversationCues.LocationPromotion));
        Assert.False(ConversationCues.ContainsAny("今天天气真好", ConversationCues.FutureSchedule));
    }

    [Fact]
    public void BriefLivingNpcContextKeepsUnenumeratedLivingNpcSectionsWhole()
    {
        // "Immediate Help Request Delivery" is a real LivingNPCs section that the old enumerated
        // critical-heading list did not cover. With prefix matching its body must survive brief
        // routing even though the lines match no individual cue fragment.
        string fullContext = string.Join(
            "\n",
            "## LivingNPCs Context: Abigail",
            "- low-priority anecdote that should be dropped in brief mode.",
            "## LivingNPCs Immediate Help Request Delivery",
            "- Acknowledge the item and thank them warmly.",
            "- Do not invent a new errand.");

        string brief = LivingNpcContextCompressor.BuildBriefContext(fullContext);

        Assert.Contains("## LivingNPCs Immediate Help Request Delivery", brief);
        Assert.Contains("Acknowledge the item and thank them warmly", brief);
        Assert.Contains("Do not invent a new errand", brief);
        Assert.DoesNotContain("low-priority anecdote", brief);
    }

    [Fact]
    public void LivingNpcThirdPartyOverrideCanBeDeduplicatedWithoutDroppingOtherMods()
    {
        Assert.True(Prompts.IsLivingNpcThirdPartyOverride("## LivingNPCs Context: Haley\n- mood: happy"));
        Assert.True(Prompts.IsLivingNpcThirdPartyOverride("## Active Companion Outing\n- Phase: spending time together."));

        Assert.False(Prompts.IsLivingNpcThirdPartyOverride("## Other Mod Context\n- The town is decorated for a festival."));
        Assert.False(Prompts.IsLivingNpcThirdPartyOverride(""));
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
    public void BriefLivingNpcContextKeepsActionCriticalSections()
    {
        string noisyMemory = string.Join("\n", Enumerable.Range(1, 120).Select(i => $"- low-priority anecdote {i}: background flavor that should not survive brief routing."));
        string fullContext = string.Join(
            "\n",
            "## LivingNPCs Context: Penny",
            "- Mood: Calm; emotion: grateful; inclination: Open.",
            noisyMemory,
            "- Help-request fit: theme library; currently reasonable item requests: (O)388 Wood, (O)422 Purple Mushroom; allowed help request type: item_request only; request relationship tier: friendly; request depth: modest.",
            "## LivingNPCs Gift Opportunity",
            "- Gift cue: The relationship is warm enough that the NPC may offer a small everyday gift today.",
            "- Shared small gift IDs: (O)194 Fried Egg, (O)216 Bread.",
            "- Penny's personalized small gift IDs: (O)340 Honey.",
            "## LivingNPCs Help Request Opportunity",
            "- Today Penny is inclined to ask the farmer for one small favor during this conversation.",
            "## Active Companion Outing",
            "- Phase: traveling naturally toward the destination.",
            "- destination: Town; targetLocation: Town; travelConsent: accepted_now; plans to remain 60 minutes.");

        string brief = LivingNpcContextCompressor.BuildBriefContext(fullContext);

        Assert.Contains("## LivingNPCs Context", brief);
        Assert.Contains("Help-request fit", brief);
        Assert.Contains("currently reasonable item requests", brief);
        Assert.Contains("## LivingNPCs Gift Opportunity", brief);
        Assert.Contains("Shared small gift IDs", brief);
        Assert.Contains("## LivingNPCs Help Request Opportunity", brief);
        Assert.Contains("## Active Companion Outing", brief);
        Assert.Contains("targetLocation", brief);
        Assert.DoesNotContain("low-priority anecdote 120", brief);
    }

    [Fact]
    public void BriefLivingNpcContextKeepsChineseFunctionalCueLines()
    {
        string fullContext = string.Join(
            "\n",
            "## 第三方上下文",
            "- 普通闲聊背景：这一行不重要。",
            "- 求助：如果玩家答应帮忙，需要保留请求状态。",
            "- 出游/带路：如果 NPC 答应一起去某处，应保留目标地点和同意状态。",
            "- 回礼/谢礼：这是之后寄送礼物的提醒，不要误判成立刻给物品。",
            "- 冲突/信任：关系边界会影响是否接受邀请。");

        string brief = LivingNpcContextCompressor.BuildBriefContext(fullContext);

        Assert.Contains("求助", brief);
        Assert.Contains("出游/带路", brief);
        Assert.Contains("回礼/谢礼", brief);
        Assert.Contains("冲突/信任", brief);
    }

    [Fact]
    public void PromptOptimizationAndRoutingConfigKeysAreLocalizedAndInjected()
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
            "configUseOptimizedPrompts",
            "configUseOptimizedPromptsTooltip",
            "configEnableSemanticContextRouting",
            "configEnableSemanticContextRoutingTooltip",
            "configSemanticContextRoutingTimeoutSeconds",
            "configSemanticContextRoutingTimeoutSecondsTooltip"
        };

        foreach (string key in requiredKeys)
        {
            Assert.True(defaultI18n.RootElement.TryGetProperty(key, out _), $"Missing default i18n key: {key}");
            Assert.True(zhI18n.RootElement.TryGetProperty(key, out _), $"Missing zh i18n key: {key}");
            Assert.True(entries.TryGetProperty(key, out JsonElement promptEntry), $"Missing Prompts.json injection key: {key}");
            Assert.Equal($"{{{{i18n:{key}}}}}", promptEntry.GetString());
        }
    }

    [Fact]
    public void CachedRoutingRefreshesWhenConversationTurnsToWorldLore()
    {
        var context = BuildConversationContext("社区中心为什么会变成这样？祝尼魔是真的吗？");
        var cached = ContextRoutingPlan.ConservativeBrief();

        bool refresh = ContextRoutingDecisionPass.ShouldRefreshCachedPlanForTopicShift(context, cached, out string reason);

        Assert.True(refresh);
        Assert.Contains(nameof(ContextModule.World), reason);
        Assert.Contains(nameof(ContextModule.GameState), reason);
    }

    [Fact]
    public void CachedRoutingDoesNotRefreshWhenTopicAlreadyHasFullCoverage()
    {
        var context = BuildConversationContext("社区中心为什么会变成这样？祝尼魔是真的吗？");
        var cached = ContextRoutingPlan.Full();

        bool refresh = ContextRoutingDecisionPass.ShouldRefreshCachedPlanForTopicShift(context, cached, out string reason);

        Assert.False(refresh);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void CachedRoutingLeavesTravelCuesToDeterministicBoundaries()
    {
        var context = BuildConversationContext("那我们一起去海边吧。");
        var cached = ContextRoutingPlan.ConservativeBrief();

        bool refresh = ContextRoutingDecisionPass.ShouldRefreshCachedPlanForTopicShift(context, cached, out _);

        Assert.False(refresh);
    }
    private static DialogueContext BuildConversationContext(string latestPlayerText)
    {
        return new DialogueContext
        {
            ChatHistory = new()
            {
                new ConversationElement("早上好。", true),
                new ConversationElement("早上好，今天很安静。", false),
                new ConversationElement(latestPlayerText, true)
            }
        };
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
