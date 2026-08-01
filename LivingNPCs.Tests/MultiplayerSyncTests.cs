using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Tests.Dialogue.Llm;
using Newtonsoft.Json;
using System.Text.Json.Nodes;
using StardewValley;
using StardewValley.Network;

namespace LivingNPCs.Tests;

/// <summary>
/// 多人 v1（主机权威）的纯逻辑面：远程交换净化策略、协议兼容、角色判定、
/// farmhand 开书决策、关系视图缓存与消息序列化往返。
/// </summary>
public sealed class MultiplayerSyncTests
{
    // ---- RemoteExchangePolicy.SanitizeAnalysisJson ----

    private const string FullAnalysisJson = """
        {
          "rapportDelta": 9,
          "endConversation": false,
          "ambientFollowUp": { "text": "回头见啦。", "delayMinutes": 30 },
          "emotionImpact": { "emotion": "happy", "intensityDelta": 12, "apology": false, "repairDelta": 0, "reason": "warm chat" },
          "memories": [ { "kind": "fact", "summary": "the farmer keeps bees", "importance": 62, "playerPreference": false } ],
          "conflicts": [],
          "behaviorInfluences": [ { "type": "stay_near", "summary": "wants to keep chatting", "durationDays": 1, "intensity": 40 } ],
          "Actions": [
            { "Type": "companion_outing", "targetLocation": "Beach", "travelConsent": "accepted_now" },
            { "type": "give_small_gift", "itemId": "(O)395" }
          ],
          "helpRequests": [ { "type": "item_request", "summary": "bring me a quartz", "requestedItemId": "(O)80" } ],
          "HELPREQUESTUPDATES": [ { "summary": "the farmer agreed", "status": "accepted" } ]
        }
        """;

    [Fact]
    public void SanitizeKeepsAllowedActionAndHelpFieldsWhileDroppingOuting()
    {
        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(FullAnalysisJson, out var droppedActionTypes);

        Assert.Equal(new[] { "companion_outing" }, droppedActionTypes);

        var analysis = ValleyTalkExchangeParser.Parse(sanitized);
        var action = Assert.Single(analysis.Actions);
        Assert.Equal("give_small_gift", action.Type);
        Assert.Equal("(O)395", action.ItemId);
        Assert.Single(analysis.HelpRequests);
        Assert.Equal("item_request", analysis.HelpRequests[0].Type);
        Assert.Equal("(O)80", analysis.HelpRequests[0].RequestedItemId);
        Assert.Single(analysis.HelpRequestUpdates);
        Assert.Equal("accepted", analysis.HelpRequestUpdates[0].Status);

        // 心智账本字段原样保留。
        Assert.Equal(9, analysis.RapportDelta);
        Assert.Single(analysis.Memories);
        Assert.Equal("the farmer keeps bees", analysis.Memories[0].Summary);
        Assert.Single(analysis.BehaviorInfluences);
        Assert.Equal("Happy", analysis.EmotionImpact.Emotion);
        Assert.Equal("回头见啦。", analysis.AmbientFollowUp.Text);
        Assert.Equal(30, analysis.AmbientFollowUp.DelayMinutes);
    }

    [Fact]
    public void SanitizePassesThroughInvalidOrEmptyJson()
    {
        Assert.Equal("not json {", RemoteExchangePolicy.SanitizeAnalysisJson("not json {", out var dropped1));
        Assert.Empty(dropped1);

        Assert.Equal(string.Empty, RemoteExchangePolicy.SanitizeAnalysisJson(string.Empty, out var dropped2));
        Assert.Empty(dropped2);

        // 非对象根：原样返回（解析器自会兜底）。
        Assert.Equal("[1,2]", RemoteExchangePolicy.SanitizeAnalysisJson("[1,2]", out var dropped3));
        Assert.Empty(dropped3);
    }

    [Fact]
    public void SanitizeLeavesJsonWithoutStrippedFieldsUntouched()
    {
        const string json = """{"rapportDelta":3,"memories":[]}""";
        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(json, out var dropped);

        Assert.Empty(dropped);
        Assert.Equal(json, sanitized);
    }

    [Fact]
    public void SanitizeCollectsNoTypesWhenActionsAreMalformed()
    {
        const string json = """{"actions":[42,{"noType":true},{"type":"  "}],"rapportDelta":1}""";
        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(json, out var dropped);

        Assert.Empty(dropped);
        var analysis = ValleyTalkExchangeParser.Parse(sanitized);
        Assert.Empty(analysis.Actions);
        Assert.Equal(1, analysis.RapportDelta);
    }

    [Theory]
    [InlineData("give_small_gift")]
    [InlineData("give_meaningful_gift")]
    [InlineData("give_money")]
    public void SanitizeKeepsEveryAllowedRemoteWorldAction(string actionType)
    {
        string json = $$"""{"actions":[{"type":"{{actionType}}","amount":999}],"rapportDelta":2}""";

        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(json, out var dropped);

        Assert.Empty(dropped);
        Assert.Equal(json, sanitized);
        var action = Assert.Single(ValleyTalkExchangeParser.Parse(sanitized).Actions);
        Assert.Equal(actionType, action.Type);
        Assert.Equal(250, action.Amount);
    }

    [Fact]
    public void SanitizeDropsOutingFestivalAliasAndUnknownButKeepsAllowedAction()
    {
        const string json = """
            {
              "actions": [
                { "type": "companion_outing", "targetLocation": "Beach" },
                { "type": "festival_interaction" },
                { "type": "escort_to_location", "targetLocation": "Town" },
                { "type": "teleport_farmer" },
                { "type": "give_money", "amount": 75 }
              ],
              "helpRequests": [{ "type": "item_request", "summary": "Quartz", "requestedItemId": "(O)80" }],
              "helpRequestUpdates": [{ "summary": "Quartz", "status": "accepted" }],
              "memories": [{ "kind": "fact", "summary": "the farmer likes rain", "importance": 60 }]
            }
            """;

        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(json, out var dropped);

        Assert.Equal(
            new[] { "companion_outing", "festival_interaction", "escort_to_location", "teleport_farmer" },
            dropped);
        var analysis = ValleyTalkExchangeParser.Parse(sanitized);
        Assert.Equal("give_money", Assert.Single(analysis.Actions).Type);
        Assert.Single(analysis.HelpRequests);
        Assert.Single(analysis.HelpRequestUpdates);
        Assert.Equal("the farmer likes rain", Assert.Single(analysis.Memories).Summary);
    }

    [Fact]
    public void SanitizeFiltersEveryCaseVariantOfActionsAndType()
    {
        const string json = """
            {
              "Actions": [
                { "TYPE": "COMPANION_OUTING" },
                { "Type": "GIVE_SMALL_GIFT", "itemId": "(O)20" }
              ],
              "HELPREQUESTS": [{ "TYPE": "ITEM_REQUEST", "SUMMARY": "Quartz", "REQUESTEDITEMID": "(O)80" }]
            }
            """;

        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(json, out var dropped);

        Assert.Equal(new[] { "COMPANION_OUTING" }, dropped);
        var analysis = ValleyTalkExchangeParser.Parse(sanitized);
        Assert.Equal("give_small_gift", Assert.Single(analysis.Actions).Type);
        Assert.Single(analysis.HelpRequests);
    }

    [Fact]
    public void SanitizePreservesEveryNonActionNode()
    {
        const string json = """
            {
              "rapportDelta": -3,
              "endConversation": true,
              "emotionImpact": { "emotion": "worried", "intensityDelta": 7, "reason": null },
              "memories": [{ "kind": "shared_event", "summary": "rainy walk", "importance": 71 }],
              "conflicts": [{ "summary": "late delivery", "severity": 24 }],
              "behaviorInfluences": [{ "type": "avoid_topic", "summary": "money", "durationDays": 2 }],
              "helpRequests": [{ "type": "item_request", "summary": "Quartz", "requestedItemId": "(O)80" }],
              "helpRequestUpdates": [{ "summary": "Quartz", "status": "accepted" }],
              "futureMindField": { "nested": [1, true, "keep me", null] },
              "actions": [
                { "type": "companion_outing", "targetLocation": "Beach" },
                { "type": "give_money", "amount": 80 }
              ]
            }
            """;

        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(json, out var dropped);

        Assert.Equal(new[] { "companion_outing" }, dropped);
        JsonObject expected = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        JsonObject actual = Assert.IsType<JsonObject>(JsonNode.Parse(sanitized));
        expected.Remove("actions");
        actual.Remove("actions");
        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
    }

    // ---- 开书决策 / 角色判定 / 协议 ----

    [Theory]
    [InlineData(true, false, false, (int)FarmhandBookAction.OpenFresh)]
    [InlineData(true, true, true, (int)FarmhandBookAction.OpenFresh)]
    [InlineData(false, false, false, (int)FarmhandBookAction.Wait)]
    [InlineData(false, false, true, (int)FarmhandBookAction.Wait)]
    [InlineData(false, true, true, (int)FarmhandBookAction.OpenStaleWithNotice)]
    [InlineData(false, true, false, (int)FarmhandBookAction.GiveUpWithNotice)]
    public void PlansFarmhandBookOpen(bool snapshotArrived, bool timedOut, bool mirrorHasData, int expected)
    {
        Assert.Equal(
            (FarmhandBookAction)expected,
            RemoteExchangePolicy.PlanBookOpen(snapshotArrived, timedOut, mirrorHasData));
    }

    [Theory]
    [InlineData(true, true, (int)MultiplayerRole.HostPlayer)]
    [InlineData(true, false, (int)MultiplayerRole.HostPlayer)]
    [InlineData(false, true, (int)MultiplayerRole.SplitScreenSecondary)]
    [InlineData(false, false, (int)MultiplayerRole.RemoteFarmhand)]
    public void DecidesMultiplayerRole(bool isMainPlayer, bool isOnHostComputer, int expected)
    {
        Assert.Equal((MultiplayerRole)expected, MultiplayerRoles.Decide(isMainPlayer, isOnHostComputer));
    }

    [Fact]
    public void ProtocolAcceptsOnlyItsOwnVersion()
    {
        Assert.True(SyncProtocol.IsCompatible(SyncProtocol.Version));
        Assert.False(SyncProtocol.IsCompatible(SyncProtocol.Version + 1));
        Assert.False(SyncProtocol.IsCompatible(0));
    }

    // ---- 分屏副屏对话门（v1 决策 4） ----

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    [InlineData(true, 2, true)]
    public void BlocksAiDialogueOnlyForSplitScreenSecondaryScreens(bool isSplitScreen, int screenId, bool expectedBlocked)
    {
        LivingNPCs.Dialogue.GameHooks.PatchGuards.ResetSplitScreenNoticeForTests();
        try
        {
            LivingNPCs.Dialogue.GameHooks.PatchGuards.SplitScreenStateResolverForTests = () => (isSplitScreen, screenId);
            Assert.Equal(
                expectedBlocked,
                LivingNPCs.Dialogue.GameHooks.PatchGuards.IsSplitScreenSecondaryBlocked(notifyPlayer: false));
        }
        finally
        {
            LivingNPCs.Dialogue.GameHooks.PatchGuards.ResetSplitScreenNoticeForTests();
        }
    }

    // ---- 关系视图：序列化往返 + 只读缓存 ----

    private static NpcRelationshipViewMessage BuildSampleView(string npcName = "Emily")
    {
        var state = TestScenarios.TrustedState(npcName);
        state.FarmerNickname = "小雪";
        state.LongTermMemories.Add(TestScenarios.Memory("the farmer keeps bees", importance: 70));

        return new NpcRelationshipViewMessage
        {
            NpcName = npcName,
            BehaviorContextSummary = $"relationship summary for {npcName}",
            BookState = state,
            UpdatedTotalDays = 100,
            UpdatedTimeOfDay = 1200
        };
    }

    [Fact]
    public void NpcRelationshipViewRoundTripsThroughJson()
    {
        // SMAPI 的 ModMessage 负载走 Newtonsoft 序列化；这里做同型往返。
        NpcRelationshipViewMessage original = BuildSampleView();
        string json = JsonConvert.SerializeObject(original);
        NpcRelationshipViewMessage? restored = JsonConvert.DeserializeObject<NpcRelationshipViewMessage>(json);

        Assert.NotNull(restored);
        Assert.Equal(SyncProtocol.Version, restored!.SchemaVersion);
        Assert.Equal("Emily", restored.NpcName);
        Assert.Equal("relationship summary for Emily", restored.BehaviorContextSummary);
        Assert.NotNull(restored.BookState);
        Assert.Equal("小雪", restored.BookState!.FarmerNickname);
        Assert.Equal(80, restored.BookState.RelationshipTrust);
        Assert.Equal("the farmer keeps bees", Assert.Single(restored.BookState.LongTermMemories).Summary);
        Assert.Equal(100, restored.UpdatedTotalDays);
        Assert.Equal(1200, restored.UpdatedTimeOfDay);
    }

    [Fact]
    public void RelationshipViewStoreClonesInputAndOutput()
    {
        var store = new NpcRelationshipViewStore();
        NpcRelationshipViewMessage input = BuildSampleView();
        store.Apply(input);
        input.BehaviorContextSummary = "mutated input";
        input.BookState!.RelationshipTrust = 1;

        Assert.True(store.TryGet("emily", out NpcRelationshipViewMessage? first));
        Assert.NotNull(first);
        Assert.Equal("relationship summary for Emily", first!.BehaviorContextSummary);
        Assert.Equal(80, first.BookState!.RelationshipTrust);

        first.BehaviorContextSummary = "mutated output";
        first.BookState.RelationshipTrust = 2;
        Assert.True(store.TryGet("Emily", out NpcRelationshipViewMessage? second));
        Assert.Equal("relationship summary for Emily", second!.BehaviorContextSummary);
        Assert.Equal(80, second.BookState!.RelationshipTrust);
    }

    [Fact]
    public void RelationshipViewStoreRetainsOnlyAuthoritativeJoinSet()
    {
        var store = new NpcRelationshipViewStore();
        store.Apply(BuildSampleView("Emily"));
        store.Apply(BuildSampleView("Haley"));
        Assert.Equal(2, store.Count);

        store.RetainOnly(new HashSet<string>(new[] { "Haley" }, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet("haley", out _));
        Assert.False(store.TryGet("Emily", out _));
    }

    [Fact]
    public void HostAuthorityContextUsesOnlyRelationshipViewAndFallsBackToEmpty()
    {
        var store = new NpcRelationshipViewStore();
        int localBuilds = 0;

        string missing = ValleyTalkContextService.ResolveBasePromptContext(
            useHostRelationshipView: true,
            "Emily",
            store,
            () => { localBuilds++; return "local context"; },
            out bool missingHasView);
        store.Apply(BuildSampleView());
        string mirrored = ValleyTalkContextService.ResolveBasePromptContext(
            useHostRelationshipView: true,
            "Emily",
            store,
            () => { localBuilds++; return "local context"; },
            out bool mirroredHasView);
        string local = ValleyTalkContextService.ResolveBasePromptContext(
            useHostRelationshipView: false,
            "Emily",
            store,
            () => { localBuilds++; return "local context"; },
            out bool localHasContext);

        Assert.Empty(missing);
        Assert.False(missingHasView);
        Assert.Equal("relationship summary for Emily", mirrored);
        Assert.True(mirroredHasView);
        Assert.Equal("local context", local);
        Assert.True(localHasContext);
        Assert.Equal(1, localBuilds);
    }

    [Fact]
    public void FarmhandHostViewKeepsGiftAndHelpOpportunitiesButAlwaysDisablesOuting()
    {
        var originalWorldState = Game1.netWorldState;
        Game1.netWorldState = new(new NetWorldState());
        try
        {
            int today = Game1.Date.TotalDays;
            NpcRelationshipViewMessage view = BuildSampleView();
            view.BehaviorContextSummary = "host-owned relationship context";
            view.BookState!.DailyGiftOpportunityTotalDays = today;
            view.BookState.DailyGiftOpportunityReason = "host-authorized gift opportunity";
            view.BookState.LastAiSmallGiftTotalDays = today - 1;
            view.BookState.LastAiMeaningfulGiftTotalDays = today - 1;
            view.BookState.DailyHelpRequestOpportunityTotalDays = today;
            var relationshipViews = new NpcRelationshipViewStore();
            relationshipViews.Apply(view);
            var config = new ModConfig
            {
                EnableAiWorldActions = true,
                AllowAiSmallGifts = true,
                EnableHelpRequests = true,
                MaxPendingHelpRequestsPerNpc = 1
            };
            var npc = new NPC { Name = "Emily", displayName = "Emily" };
            var service = new ValleyTalkContextService(
                new FakeMonitor(),
                config,
                new BehaviorMemory(),
                new GiftSelector(new Random(1)),
                null!,
                _ => "host outing context must not be used",
                relationshipViews,
                useHostRelationshipViews: () => true,
                suppressCompanionOutingOpportunity: () => true);

            string context = service.BuildPromptContext(npc);

            Assert.Contains("host-owned relationship context", context);
            Assert.Contains("## LivingNPCs Gift Opportunity", context);
            Assert.Contains("host-authorized gift opportunity", context);
            Assert.Contains("## LivingNPCs Help Request Opportunity", context);
            Assert.Contains("## Companion Outing Unavailable", context);
            Assert.DoesNotContain("host outing context must not be used", context);
        }
        finally
        {
            Game1.netWorldState = originalWorldState;
        }
    }
}
