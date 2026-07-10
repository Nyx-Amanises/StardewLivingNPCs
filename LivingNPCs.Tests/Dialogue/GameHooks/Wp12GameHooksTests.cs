using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.GameHooks;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Ui;
using StardewValley;
using Xunit;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Tests.Dialogue.GameHooks;

/// <summary>
/// WP12 §3 补丁目标签名表：19 个目标逐一按程序集实际形状断言（游戏升级破坏契约时此处先红）。
/// </summary>
public class Wp12PatchTargetSignatureTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static MethodInfo? Method(Type type, string name, params Type[] parameters)
    {
        return type.GetMethod(name, All, binder: null, types: parameters, modifiers: null);
    }

    [Fact]
    public void Npc_Patch_Targets_Exist()
    {
        // P1
        var p1 = Method(typeof(NPC), "checkAction", typeof(Farmer), typeof(GameLocation));
        Assert.NotNull(p1);
        Assert.Equal(typeof(bool), p1!.ReturnType);

        // P2：属性 getter
        var p2 = typeof(NPC).GetProperty("CurrentDialogue", All);
        Assert.NotNull(p2?.GetMethod);
        Assert.Equal(typeof(Stack<GameDialogue>), p2!.PropertyType);

        // P3
        var p3 = Method(typeof(NPC), "checkForNewCurrentDialogue", typeof(int), typeof(bool));
        Assert.NotNull(p3);
        Assert.Equal(typeof(bool), p3!.ReturnType);

        // P4
        var p4 = Method(typeof(NPC), "GetGiftReaction", typeof(Farmer), typeof(StardewValley.Object), typeof(int));
        Assert.NotNull(p4);
        Assert.Equal(typeof(GameDialogue), p4!.ReturnType);

        // P5
        var p5 = Method(typeof(NPC), "addMarriageDialogue", typeof(string), typeof(string), typeof(bool), typeof(string[]));
        Assert.NotNull(p5);

        // P6：私有，按名字定位
        var p6 = Method(typeof(NPC), "_PushTemporaryDialogue", typeof(string));
        Assert.NotNull(p6);
        Assert.False(p6!.IsPublic);

        // P7 / P8
        var p7 = Method(typeof(NPC), "tryToGetMarriageSpecificDialogue", typeof(string));
        Assert.NotNull(p7);
        Assert.Equal(typeof(GameDialogue), p7!.ReturnType);
        var p8 = Method(typeof(NPC), "tryToRetrieveDialogue", typeof(string), typeof(int), typeof(string));
        Assert.NotNull(p8);
        Assert.Equal(typeof(GameDialogue), p8!.ReturnType);

        // 补丁消费的其他成员
        Assert.NotNull(Method(typeof(NPC), "grantConversationFriendship", typeof(Farmer), typeof(int)));
        Assert.NotNull(Method(typeof(NPC), "CanReceiveGifts"));
        Assert.NotNull(typeof(NPC).GetField("shouldSayMarriageDialogue", All));
        Assert.NotNull(typeof(NPC).GetField("currentMarriageDialogue", All));
    }

    [Fact]
    public void Dialogue_And_Game1_Patch_Targets_Exist()
    {
        // P9
        var p9 = Method(typeof(GameDialogue), "chooseResponse", typeof(Response));
        Assert.NotNull(p9);
        Assert.Equal(typeof(bool), p9!.ReturnType);

        // P10：静态
        var p10 = Method(typeof(GameDialogue), "TryGetDialogue", typeof(NPC), typeof(string));
        Assert.NotNull(p10);
        Assert.True(p10!.IsStatic);

        // P11：静态 DrawDialogue(Dialogue)
        var p11 = Method(typeof(Game1), "DrawDialogue", typeof(GameDialogue));
        Assert.NotNull(p11);
        Assert.True(p11!.IsStatic);

        // P12：无参 drawDialogueBox 实为私有实例方法（§3 表注"静态"与程序集不符，实现按实际）。
        var p12 = Method(typeof(Game1), "drawDialogueBox", Type.EmptyTypes);
        Assert.NotNull(p12);
        Assert.False(p12!.IsStatic);
        Assert.False(p12.IsPublic);

        // P13
        var p13 = Method(typeof(GameLocation), "GetLocationOverrideDialogue", typeof(NPC));
        Assert.NotNull(p13);
        Assert.Equal(typeof(string), p13!.ReturnType);

        // P14 + 引用属性
        var p14 = Method(typeof(MarriageDialogueReference), "GetDialogue", typeof(NPC));
        Assert.NotNull(p14);
        Assert.NotNull(typeof(MarriageDialogueReference).GetProperty("DialogueFile"));
        Assert.NotNull(typeof(MarriageDialogueReference).GetProperty("DialogueKey"));
        Assert.NotNull(typeof(MarriageDialogueReference).GetProperty("IsGendered"));
        Assert.NotNull(typeof(MarriageDialogueReference).GetProperty("Substitutions"));

        // P9/P16 消费的成员
        Assert.NotNull(Method(typeof(GameDialogue), "getResponseOptions"));
        Assert.NotNull(typeof(GameDialogue).GetField("TranslationKey", All));
        Assert.NotNull(typeof(Response).GetField("responseKey", All));
        Assert.NotNull(typeof(Response).GetField("responseText", All));

        // 共存 HUD 与呈现用到的构造
        Assert.NotNull(typeof(HUDMessage).GetConstructor(new[] { typeof(string), typeof(int) }));
        Assert.NotNull(typeof(GameDialogue).GetConstructor(new[] { typeof(NPC), typeof(string), typeof(string) }));
        Assert.NotNull(Method(typeof(Game1), "LoadStringByGender", typeof(Gender), typeof(string), typeof(object[])));
    }

    [Fact]
    public void Carryover_Patch_Targets_Exist()
    {
        // P15（xTile 类型经参数名匹配，测试工程不引用 xTile/MonoGame 程序集）
        var p15 = typeof(Event).GetMethods(All).FirstOrDefault(m =>
            m.Name == "checkAction"
            && m.GetParameters().Length == 3
            && m.GetParameters()[0].ParameterType.FullName == "xTile.Dimensions.Location"
            && m.GetParameters()[1].ParameterType.FullName == "xTile.Dimensions.Rectangle"
            && m.GetParameters()[2].ParameterType == typeof(Farmer));
        Assert.NotNull(p15);
        Assert.Equal(typeof(bool), p15!.ReturnType);

        // P16–P19（DialogueBox 四连搬运件）
        var box = typeof(StardewValley.Menus.DialogueBox);
        Assert.NotNull(Method(box, "getCurrentString"));
        Assert.NotNull(box.GetMethods(All).FirstOrDefault(m =>
            m.Name == "draw" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "SpriteBatch"));
        Assert.NotNull(Method(box, "receiveLeftClick", typeof(int), typeof(int), typeof(bool)));
        Assert.NotNull(box.GetMethods(All).FirstOrDefault(m =>
            m.Name == "receiveKeyPress" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "Keys"));
    }

    [Fact]
    public void Reserved_Sld_Keys_Match_Compat_Contract()
    {
        Assert.Equal("SLD_", EngineConstants.KeyPrefix);
        Assert.Equal("SLD_Silent", EngineConstants.KeySilent);
        Assert.Equal("SLD_TypedResponse", EngineConstants.KeyTypedResponse);
        Assert.Equal("SLD_Input", EngineConstants.KeyInput);
        Assert.Equal("SLD_Thinking", EngineConstants.KeyThinking);
        Assert.Equal("SLD_Streaming", EngineConstants.KeyStreaming);
        Assert.Equal("SLD_Error", EngineConstants.KeyError);
        Assert.Equal("SLD_Conversation", EngineConstants.KeyConversation);
        Assert.Equal("$$$%%%", EngineConstants.DialogueGenerationTag);
        Assert.Equal("$$%%", EngineConstants.DialogueSkipTag);
    }
}

[Collection("LlmLayer")]
public class Wp12PatchGuardsTests : IDisposable
{
    public Wp12PatchGuardsTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        LivingNPCs.Dialogue.Content.ThirdPartyContentPolicy.ResetForTests();
        EnableGate.EngineDisabledByCoexistence = false;
        LivingNPCs.Dialogue.Content.DialogueEngineGate.RuntimeDisabled = false;
    }

    public void Dispose()
    {
        PatchGuards.EngineResolverForTests = null;
        PatchGuards.CanGenerateResolverForTests = null;
        EnableGate.EngineDisabledByCoexistence = false;
        LivingNPCs.Dialogue.Content.DialogueEngineGate.RuntimeDisabled = false;
        NetworkGate.ResetForTests();
        AndroidPlatform.OverrideForTests = null;
    }

    private static DialogueEngine NewEngine()
    {
        return new DialogueEngine(new DialogueEngineServices
        {
            GetBio = _ => new LivingNPCs.Dialogue.Content.NpcBio { Biography = "test biography" }
        });
    }

    private void UseEngine(DialogueEngine? engine, bool canGenerate = true)
    {
        PatchGuards.EngineResolverForTests = () => engine;
        PatchGuards.CanGenerateResolverForTests = () => canGenerate;
    }

    [Fact]
    public void Frequency_Mapping_Follows_Spec()
    {
        var config = new DialogueConfig { GeneralFrequency = 3, MarriageFrequency = 2, GiftFrequency = 1 };
        Assert.Equal((3, true), PatchGuards.ResolveFrequency(config, GenerationFrequencyKind.General));
        Assert.Equal((2, false), PatchGuards.ResolveFrequency(config, GenerationFrequencyKind.Marriage));
        Assert.Equal((1, false), PatchGuards.ResolveFrequency(config, GenerationFrequencyKind.Gift));
    }

    [Fact]
    public void EngineReady_Requires_All_Gates()
    {
        UseEngine(NewEngine());
        Assert.True(PatchGuards.EngineReady);

        UseEngine(null);
        Assert.False(PatchGuards.EngineReady);

        UseEngine(NewEngine(), canGenerate: false);
        Assert.False(PatchGuards.EngineReady);

        UseEngine(NewEngine());
        DialogueServices.Config.EnableDialogueEngine = false;
        Assert.False(PatchGuards.EngineReady);
        DialogueServices.Config.EnableDialogueEngine = true;

        LivingNPCs.Dialogue.Content.DialogueEngineGate.RuntimeDisabled = true;
        Assert.False(PatchGuards.EngineReady);
        LivingNPCs.Dialogue.Content.DialogueEngineGate.RuntimeDisabled = false;

        EnableGate.EngineDisabledByCoexistence = true;
        Assert.False(PatchGuards.EngineReady);
    }

    [Fact]
    public void Passive_Gate_Requires_NormalRightClick_Config()
    {
        UseEngine(NewEngine());
        DialogueServices.Config.GenerateAiForNormalRightClick = false;
        Assert.False(PatchGuards.AllowPassiveGeneration(null, GenerationFrequencyKind.General, checkNetwork: false));

        DialogueServices.Config.GenerateAiForNormalRightClick = true;
        // NPC 为 null 仍拒绝（启用链）。
        Assert.False(PatchGuards.AllowPassiveGeneration(null, GenerationFrequencyKind.General, checkNetwork: false));
    }

    [Fact]
    public void Frequency_Zero_Blocks_And_Four_Passes()
    {
        UseEngine(NewEngine());
        DialogueServices.Config.GiftFrequency = 0;
        Assert.False(PatchGuards.PassFrequency("Abigail", GenerationFrequencyKind.Gift));

        DialogueServices.Config.GiftFrequency = 4;
        Assert.True(PatchGuards.PassFrequency("Abigail", GenerationFrequencyKind.Gift));
    }

    [Fact]
    public void General_Frequency_Retains_Daily_Result()
    {
        UseEngine(NewEngine());
        DialogueServices.Config.GeneralFrequency = 2;
        bool first = PatchGuards.PassFrequency("Abigail", GenerationFrequencyKind.General);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(first, PatchGuards.PassFrequency("Abigail", GenerationFrequencyKind.General));
        }
    }
}

[Collection("LlmLayer")]
public class Wp12NetworkGateTests : IDisposable
{
    public Wp12NetworkGateTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        NetworkGate.ResetForTests();
    }

    public void Dispose()
    {
        NetworkGate.ResetForTests();
        AndroidPlatform.OverrideForTests = null;
    }

    [Fact]
    public void Desktop_Is_Always_Available()
    {
        AndroidPlatform.OverrideForTests = false;
        NetworkGate.InstantProbeForTests = () => false;
        Assert.True(NetworkGate.IsAvailableForGeneration());
    }

    [Fact]
    public void SuppressConnectionCheck_Bypasses_Android_Probe()
    {
        AndroidPlatform.OverrideForTests = true;
        DialogueServices.Config.SuppressConnectionCheck = true;
        NetworkGate.InstantProbeForTests = () => false;
        Assert.True(NetworkGate.IsAvailableForGeneration());
    }

    [Fact]
    public async Task Android_Failure_Returns_Immediately_And_Probes_In_Background()
    {
        AndroidPlatform.OverrideForTests = true;
        bool instantAvailable = false;
        NetworkGate.InstantProbeForTests = () => instantAvailable;
        int backgroundProbes = 0;
        NetworkGate.BackgroundProbeForTests = () =>
        {
            backgroundProbes++;
            return Task.FromResult(true);
        };

        // 首查失败：立即 false（非阻塞），后台探测被踢出。
        Assert.False(NetworkGate.IsAvailableForGeneration());
        Assert.NotNull(NetworkGate.LastBackgroundProbe);
        await NetworkGate.LastBackgroundProbe!;
        Assert.True(backgroundProbes >= 1);

        // 网络恢复后即时查通过。
        instantAvailable = true;
        Assert.True(NetworkGate.IsAvailableForGeneration());
    }
}

[Collection("LlmLayer")]
public class Wp12TypedInputQueueTests : IDisposable
{
    public Wp12TypedInputQueueTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        TypedInputRequestQueue.ResetForTests();
    }

    public void Dispose()
    {
        TypedInputRequestQueue.ResetForTests();
    }

    [Fact]
    public void Queue_Keeps_Only_Last_Request_And_Consumes_Once()
    {
        var npc = new NPC();
        var started = new List<string>();
        TypedInputRequestQueue.CanConsumeForTests = () => true;
        TypedInputRequestQueue.InputStarterForTests = (prompt, _, _) => started.Add(prompt);

        TypedInputRequestQueue.Submit("first", npc, "default", null);
        TypedInputRequestQueue.Submit("second", npc, "default", null);
        Assert.True(TypedInputRequestQueue.HasPending);

        TypedInputRequestQueue.OnUpdateTicked();
        TypedInputRequestQueue.OnUpdateTicked();

        Assert.Equal(new[] { "second" }, started);
        Assert.False(TypedInputRequestQueue.HasPending);
    }

    [Fact]
    public void Queue_Waits_Until_Menu_Closed()
    {
        var npc = new NPC();
        bool menuOpen = true;
        var started = new List<string>();
        TypedInputRequestQueue.CanConsumeForTests = () => !menuOpen;
        TypedInputRequestQueue.InputStarterForTests = (prompt, _, _) => started.Add(prompt);

        TypedInputRequestQueue.Submit("hello", npc, "default", null);
        TypedInputRequestQueue.OnUpdateTicked();
        Assert.Empty(started);
        Assert.True(TypedInputRequestQueue.HasPending);

        menuOpen = false;
        TypedInputRequestQueue.OnUpdateTicked();
        Assert.Equal(new[] { "hello" }, started);
    }

    [Fact]
    public void Blank_Submission_Has_No_Side_Effects()
    {
        var npc = new NPC();
        bool friendshipGranted = false;
        bool enqueued = false;
        TypedInputRequestQueue.FriendshipGranterForTests = _ => friendshipGranted = true;
        TypedInputRequestQueue.EnqueueForTests = (_, _) => enqueued = true;

        TypedInputRequestQueue.HandleSubmitted(new TypedInputRequest("p", npc, "default", null), "   ");

        Assert.False(friendshipGranted);
        Assert.False(enqueued);
    }

    [Fact]
    public void Text_Submission_Grants_Friendship_And_Enqueues_Conversation()
    {
        var npc = new NPC();
        bool friendshipGranted = false;
        GenerationRequest? captured = null;
        TypedInputRequestQueue.FriendshipGranterForTests = _ => friendshipGranted = true;
        TypedInputRequestQueue.EnqueueForTests = (_, request) =>
        {
            captured = request;
            return true;
        };

        var carried = new List<ConversationTurn> { new("earlier npc line", false, "id-1") };
        TypedInputRequestQueue.HandleSubmitted(new TypedInputRequest("p", npc, "heart_8", carried), "hello there");

        Assert.True(friendshipGranted);
        Assert.NotNull(captured);
        Assert.Equal(GenerationTrigger.Conversation, captured!.Trigger);
        Assert.Equal("heart_8", captured.DialogueKey);
        Assert.Equal(2, captured.Conversation.Count);
        Assert.Equal("earlier npc line", captured.Conversation[0].Text);
        Assert.True(captured.Conversation[1].IsPlayerLine);
        Assert.Equal("hello there", captured.Conversation[1].Text);
    }
}

public class Wp12PatchLogicTests
{
    [Fact]
    public void Modified_Action_Claim_Begins_Input_Then_Suppresses_Action_Button()
    {
        var npc = new NPC();
        NPC? begunFor = null;
        bool suppressed = false;

        bool claimed = DialogueEngineBootstrapper.TryClaimTypedDialogueAction(
            npc,
            allowInteractiveInput: true,
            beginConversation: target => begunFor = target,
            suppressActionButton: () => suppressed = true);

        Assert.True(claimed);
        Assert.Same(npc, begunFor);
        Assert.True(suppressed);
    }

    [Fact]
    public void Modified_Action_Claim_Does_Not_Suppress_When_Input_Gate_Rejects_Npc()
    {
        var npc = new NPC();
        bool began = false;
        bool suppressed = false;

        bool claimed = DialogueEngineBootstrapper.TryClaimTypedDialogueAction(
            npc,
            allowInteractiveInput: false,
            beginConversation: _ => began = true,
            suppressActionButton: () => suppressed = true);

        Assert.False(claimed);
        Assert.False(began);
        Assert.False(suppressed);
    }

    [Fact]
    public void Scheduled_Key_Prefers_TemporaryKey_Then_Heart_Or_Default()
    {
        Assert.Equal("Resort_Entering", NPC_CheckForNewCurrentDialogue_Patch.BuildScheduledKey("Resort_Entering", 6, noPreface: false));
        Assert.Equal("heart_6", NPC_CheckForNewCurrentDialogue_Patch.BuildScheduledKey(null, 6, noPreface: false));
        Assert.Equal("default_4", NPC_CheckForNewCurrentDialogue_Patch.BuildScheduledKey(string.Empty, 4, noPreface: true));
    }

    [Fact]
    public void Placeholder_Key_Resolution_Order()
    {
        Assert.Equal("funReturn_Hoe", DialoguePatchHelpers.ResolvePlaceholderKey("funReturn_Hoe", "ignored"));
        Assert.Equal("heart_8", DialoguePatchHelpers.ResolvePlaceholderKey(null, "heart_8"));
        Assert.Equal("default", DialoguePatchHelpers.ResolvePlaceholderKey("", "  "));
    }

    [Fact]
    public void Placeholder_Text_Carries_Original_After_Hash()
    {
        Assert.Equal("$$$%%%", DialoguePatchHelpers.BuildPlaceholderText(""));
        Assert.Equal("$$$%%%#hi there", DialoguePatchHelpers.BuildPlaceholderText("hi there"));
    }

    [Fact]
    public void Morning_Chore_Keys_Match_Spec_List()
    {
        var expected = new[]
        {
            "NPC.cs.4463", "NPC.cs.4462", "NPC.cs.4470", "NPC.cs.4474", "NPC.cs.4481", "MultiplePetBowls_watered"
        };
        Assert.Equal(expected.OrderBy(k => k), NPC_AddMarriageDialogue_Patch.MorningChoreKeys.OrderBy(k => k));
    }

    [Fact]
    public void Recordable_Lines_Drop_Markers()
    {
        var lines = new List<string?> { "$$$%%%", "$$%%rest", "real line", " ", null };
        Assert.Equal(new[] { "real line" }, DisplayedDialogueRecorder.ExtractRecordableLines(lines));
    }

    [Fact]
    public void Displayed_Line_Dedup_Is_Per_Npc_Day_And_Text()
    {
        DisplayedDialogueRecorder.ResetForTests();
        try
        {
            var lines = new[] { "hello" };
            Assert.True(DisplayedDialogueRecorder.TryMarkRecorded("Abigail", 100, lines));
            Assert.False(DisplayedDialogueRecorder.TryMarkRecorded("Abigail", 100, lines));
            Assert.True(DisplayedDialogueRecorder.TryMarkRecorded("Leah", 100, lines));
            Assert.True(DisplayedDialogueRecorder.TryMarkRecorded("Abigail", 100, new[] { "hello", "again" }));

            // 游戏日变更后重新可记录。
            Assert.True(DisplayedDialogueRecorder.TryMarkRecorded("Abigail", 101, lines));
        }
        finally
        {
            DisplayedDialogueRecorder.ResetForTests();
        }
    }

    [Fact]
    public void Marriage_Chore_Buffer_Drains_Once()
    {
        MarriageChoreBuffer.Clear();
        MarriageChoreBuffer.Add("watered the pets");
        MarriageChoreBuffer.Add("  ");
        MarriageChoreBuffer.Add("fed the animals");
        Assert.Equal(2, MarriageChoreBuffer.Count);

        var drained = MarriageChoreBuffer.Drain();
        Assert.Equal(new[] { "watered the pets", "fed the animals" }, drained);
        Assert.Equal(0, MarriageChoreBuffer.Count);
        Assert.Empty(MarriageChoreBuffer.Drain());
    }
}
