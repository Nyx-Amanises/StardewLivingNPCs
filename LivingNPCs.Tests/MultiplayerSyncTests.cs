using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using Newtonsoft.Json;

namespace LivingNPCs.Tests;

/// <summary>
/// 多人 v1（主机权威）的纯逻辑面：远程交换净化策略、协议兼容、角色判定、
/// farmhand 开书决策、心智视图的导入导出与消息序列化往返。
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
    public void SanitizeStripsWorldActionsAndHelpFieldsCaseInsensitively()
    {
        string sanitized = RemoteExchangePolicy.SanitizeAnalysisJson(FullAnalysisJson, out var droppedActionTypes);

        Assert.Equal(new[] { "companion_outing", "give_small_gift" }, droppedActionTypes);

        var analysis = ValleyTalkExchangeParser.Parse(sanitized);
        Assert.Empty(analysis.Actions);
        Assert.Empty(analysis.HelpRequests);
        Assert.Empty(analysis.HelpRequestUpdates);

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

    // ---- 心智视图：序列化往返 + 镜像导入导出 ----

    /// <param name="includeMemory">
    /// 长期记忆的 Clamp 排序在无游戏环境会读 Game1.Date（生产导入总在游戏内跑），
    /// 走 ImportNpcView 的用例传 false。
    /// </param>
    private static NpcMindViewMessage BuildSampleView(string npcName = "Emily", bool includeMemory = true)
    {
        var state = TestScenarios.TrustedState(npcName);
        state.FarmerNickname = "小雪";
        if (includeMemory)
        {
            state.LongTermMemories.Add(TestScenarios.Memory("the farmer keeps bees", importance: 70));
        }

        return new NpcMindViewMessage
        {
            NpcName = npcName,
            State = state,
            Entries = new List<BehaviorMemoryEntry>
            {
                new() { NpcName = npcName, Kind = "Conversation", Action = "ConversationStarted", TotalDays = 99, TimeOfDay = 900 },
                new() { NpcName = npcName, Kind = "Gift", Action = "GiftOffered", TotalDays = 100, TimeOfDay = 1200 }
            }
        };
    }

    [Fact]
    public void NpcMindViewRoundTripsThroughJson()
    {
        // SMAPI 的 ModMessage 负载走 Newtonsoft 序列化；这里做同型往返。
        NpcMindViewMessage original = BuildSampleView();
        string json = JsonConvert.SerializeObject(original);
        NpcMindViewMessage? restored = JsonConvert.DeserializeObject<NpcMindViewMessage>(json);

        Assert.NotNull(restored);
        Assert.Equal(SyncProtocol.Version, restored!.SchemaVersion);
        Assert.Equal("Emily", restored.NpcName);
        Assert.NotNull(restored.State);
        Assert.Equal("小雪", restored.State!.FarmerNickname);
        Assert.Equal(80, restored.State.RelationshipTrust);
        Assert.Equal("the farmer keeps bees", Assert.Single(restored.State.LongTermMemories).Summary);
        Assert.Equal(2, restored.Entries.Count);
        Assert.Equal("GiftOffered", restored.Entries[1].Action);
    }

    [Fact]
    public void ImportNpcViewNormalizesSortsAndTrimsEntries()
    {
        var memory = new BehaviorMemory();
        var view = BuildSampleView(includeMemory: false);
        view.Entries = new List<BehaviorMemoryEntry>
        {
            new() { NpcName = "Emily", Action = "newest", TotalDays = 102, TimeOfDay = 900 },
            new() { NpcName = "Emily", Action = "oldest", TotalDays = 98, TimeOfDay = 900 },
            new() { NpcName = string.Empty, Action = "dropped-no-name", TotalDays = 101, TimeOfDay = 900 },
            new() { NpcName = "Emily", Action = "middle", TotalDays = 100, TimeOfDay = 900 }
        };

        memory.ImportNpcView(view, maxEntriesPerNpc: 2, stateNormalizerOverride: _ => { });

        LivingNpcState state = Assert.Single(memory.GetTrackedStates());
        Assert.Equal("Emily", state.NpcName);

        NpcMindViewMessage exported = memory.ExportNpcView("Emily")!;
        Assert.Equal(new[] { "middle", "newest" }, exported.Entries.Select(entry => entry.Action));
    }

    [Fact]
    public void ImportNpcViewWithNullStateClearsMirroredState()
    {
        var memory = new BehaviorMemory();
        memory.ImportNpcView(BuildSampleView(includeMemory: false), maxEntriesPerNpc: 10, stateNormalizerOverride: _ => { });
        Assert.Single(memory.GetTrackedStates());

        memory.ImportNpcView(
            new NpcMindViewMessage
            {
                NpcName = "Emily",
                State = null,
                Entries = new List<BehaviorMemoryEntry>
                {
                    new() { NpcName = "Emily", Action = "kept", TotalDays = 100, TimeOfDay = 900 }
                }
            },
            maxEntriesPerNpc: 10,
            stateNormalizerOverride: _ => { });

        Assert.Empty(memory.GetTrackedStates());
        Assert.Equal("kept", Assert.Single(memory.ExportNpcView("Emily")!.Entries).Action);
    }

    [Fact]
    public void RetainOnlyNpcsPrunesStaleMirrorEntries()
    {
        var memory = new BehaviorMemory();
        memory.ImportNpcView(BuildSampleView("Emily", includeMemory: false), maxEntriesPerNpc: 10, stateNormalizerOverride: _ => { });
        memory.ImportNpcView(BuildSampleView("Haley", includeMemory: false), maxEntriesPerNpc: 10, stateNormalizerOverride: _ => { });
        Assert.Equal(2, memory.ExportAllNpcViews().Count);

        memory.RetainOnlyNpcs(new HashSet<string>(new[] { "Haley" }, StringComparer.OrdinalIgnoreCase));

        NpcMindViewMessage remaining = Assert.Single(memory.ExportAllNpcViews());
        Assert.Equal("Haley", remaining.NpcName);
        Assert.Null(memory.ExportNpcView("Emily"));
    }

    [Fact]
    public void ExportNpcViewClonesStateInsteadOfSharingIt()
    {
        var memory = new BehaviorMemory();
        memory.ImportNpcView(BuildSampleView(includeMemory: false), maxEntriesPerNpc: 10, stateNormalizerOverride: _ => { });

        NpcMindViewMessage exported = memory.ExportNpcView("Emily")!;
        exported.State!.RelationshipTrust = 1;

        Assert.Equal(80, Assert.Single(memory.GetTrackedStates()).RelationshipTrust);
    }
}
