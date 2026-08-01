using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StardewValley;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>引擎外部依赖束（全部可注入，整链单测）。</summary>
internal sealed class DialogueEngineServices
{
    /// <summary>传记加载（WP15）。</summary>
    public Func<string, NpcBio> GetBio { get; init; } = _ => new NpcBio { Missing = true };

    /// <summary>提示词查找（WP15 LookupPrompt 语义：key, bio, variant, tokens, returnNull）。</summary>
    public Func<string, NpcBio?, PromptVariant, object?, string?> LookupPrompt { get; init; }
        = (_, _, _, _) => null;

    /// <summary>世界摘要文本（optimized → 精简版）。</summary>
    public Func<bool, string> GetWorldSummaryText { get; init; } = _ => string.Empty;

    /// <summary>台词样本（WP15 DialogueSampleLoader；键 → 台词）。</summary>
    public Func<string, NpcBio, IReadOnlyDictionary<string, string>> GetSamples { get; init; }
        = (_, _) => new Dictionary<string, string>();

    /// <summary>当前 LLM 客户端（WP11 Host；可能为 null = 未配置）。</summary>
    public Func<ILlmClient?> GetClient { get; init; } = () => null;

    /// <summary>历史写入语义（§4.14）。</summary>
    public EngineHistoryWriter History { get; init; } = new(DialogueHistoryStore.Instance);

    /// <summary>历史存储读取（采样用）。</summary>
    public Func<string, StardewEventHistory> GetHistory { get; init; }
        = npcName => DialogueHistoryStore.Instance.GetHistory(npcName);

    /// <summary>游戏时间（测试可注入）。</summary>
    public Func<StardewTime> Now { get; init; } = () =>
    {
        try
        {
            return new StardewTime(Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);
        }
        catch
        {
            return new StardewTime(1, Season.Spring, 1, 600);
        }
    };

    /// <summary>物品显示名（礼物文案用；测试注入）。</summary>
    public Func<string, string> GetItemDisplayName { get; init; } = itemId =>
    {
        try
        {
            return ItemRegistry.GetDataOrErrorItem(itemId).DisplayName;
        }
        catch
        {
            return itemId;
        }
    };

    /// <summary>NPC 显示名。</summary>
    public Func<string, string> GetNpcDisplayName { get; init; } = npcName =>
    {
        try
        {
            return Game1.getCharacterFromName(npcName)?.displayName ?? npcName;
        }
        catch
        {
            return npcName;
        }
    };

    /// <summary>游戏语言 locale。</summary>
    public Func<string> GetLocale { get; init; } = () => DialogueServices.Helper?.Translation.Locale ?? string.Empty;

    /// <summary>随机源（Preoccupation 概率与抽取；测试注入）。</summary>
    public Random Random { get; init; } = Random.Shared;
}

/// <summary>
/// 对话生成引擎（WP10 核心编排）：上下文装配 → 路由 → 提示词拼装 → LLM 调用（重试/超时）
/// → 解析/校验/后处理。结果提交（历史 + 行为系统回传）由 <see cref="GenerationScheduler"/>
/// 在主线程确认展示成功后负责。
/// </summary>
internal sealed class DialogueEngine : IDialogueEngine
{
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly DialogueEngineServices services;
    private readonly EnableGate enableGate;
    private readonly object gate = new();

    /// <summary>Preoccupation 当日粘滞（NPC → (日戳, 话题/空=当日未通过概率)，§4.6.4.19）。</summary>
    private readonly Dictionary<string, (int Day, string Topic)> preoccupationMemo = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>样本库缓存（NPC → 库）；选择结果缓存键 =（季节, 日, 心数）（§4.7.3）。</summary>
    private readonly Dictionary<string, List<DialogueSample>> sampleLibraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ((int Season, int Day, int Hearts) Key, List<DialogueSample> Samples)> selectedSamples
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>行为系统回传入口（WP16 接线）：(npcName, playerText, npcLine, analysisJson)。</summary>
    public static Action<string, string, string, string>? RecordExchangeCallback { get; set; }

    /// <summary>配偶主动送礼抽取（§4.1/§3.2）：返回物品 ID 或 null。默认策略见 SpouseGiftPolicy
    /// （用户裁决：5% 概率 + 该 NPC 最爱/喜欢口味池）；WP16/测试可整体替换或置 null 关闭。</summary>
    public static Func<string, GameStateSnapshot, string?>? SpouseGiftPicker { get; set; }
        = SpouseGiftPolicy.PickForGame;

    /// <summary>第三方 Prompt Override 注册表（新 interop 命名归 WP16/WP12；引擎只消费）。</summary>
    public static IReadOnlyDictionary<string, string> PromptOverrides { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public DialogueEngine(DialogueEngineServices services, EnableGate? enableGate = null)
    {
        this.services = services;
        this.enableGate = enableGate ?? new EnableGate(services.GetBio);
    }

    public EnableGate Gate => this.enableGate;
    public EngineHistoryWriter History => this.services.History;

    public bool IsEnabledFor(NPC npc)
    {
        return this.enableGate.IsEnabledFor(npc);
    }

    // ---- 非流式 ----

    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var prepared = await this.PrepareAsync(request, ct).ConfigureAwait(false);

        LlmResponse? response = null;
        Exception? failure = null;
        int attempt = 0;
        bool languageRetry = false;
        for (attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }

            try
            {
                response = await this.RunAttemptAsync(prepared, languageRetry, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failure = ex;
                response = null;
                continue;
            }

            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
            {
                continue;
            }

            var probe = this.ParseResponse(prepared, response.Text);
            if (!probe.Success)
            {
                this.ExportAttempt(prepared, response, ConversationAnalysis.Empty, Array.Empty<string>(), attempt, "unparseable");
                response = null;
                continue;
            }

            // 首行疑似错误语言 → 清空结果并加语言强化指示重试一次（§4.9/§4.16）。
            if (!languageRetry && ConversationTextPostProcessor.LooksLikeWrongLanguage(probe.DialogueLine))
            {
                this.ExportAttempt(prepared, response, ConversationAnalysis.Empty, new[] { probe.DialogueLine }, attempt, "wrong-language");
                languageRetry = true;
                response = null;
                continue;
            }

            break;
        }

        ct.ThrowIfCancellationRequested();
        return await this.FinalizeResultAsync(
            prepared,
            response,
            failure,
            Math.Min(attempt, MaxAttempts),
            watch,
            ct).ConfigureAwait(false);
    }

    // ---- 流式（§4.13） ----

    public async Task StreamAsync(GenerationRequest request, IStreamSink sink, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var prepared = await this.PrepareAsync(request, ct).ConfigureAwait(false);

        LlmResponse? response = null;
        bool languageRetry = false;
        int attempts = 0;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            attempts = attempt;
            if (attempt > 1)
            {
                // 上一次尝试已作废：先让预览清空，再等待重试间隔，attempt 2 从干净窗开始。
                sink.OnToken(StreamingControlTokens.RetryReset);
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }

            try
            {
                response = await this.RunStreamingAttemptAsync(prepared, sink, languageRetry, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                response = null;
                continue;
            }

            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
            {
                continue;
            }

            var probe = this.ParseResponse(prepared, response.Text);
            if (!probe.Success)
            {
                response = null;
                continue;
            }

            if (!languageRetry && ConversationTextPostProcessor.LooksLikeWrongLanguage(probe.DialogueLine))
            {
                languageRetry = true;
                response = null;
                continue;
            }

            break;
        }

        var result = await this.FinalizeResultAsync(
            prepared,
            response,
            null,
            attempts,
            watch,
            ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var options = ResponseFormatter.BuildStreamingOptions(
            result.ParsedLines.Skip(1).ToList(),
            DialogueServices.Config?.TypedResponses ?? "With Generated",
            result.EndConversation);
        sink.OnCompleted(result, options);
    }

    private async Task<LlmResponse> RunStreamingAttemptAsync(PreparedGeneration prepared, IStreamSink sink, bool languageRetry, CancellationToken ct)
    {
        ILlmClient? client = this.services.GetClient();
        if (client == null)
        {
            return new LlmResponse { IsSuccess = false, ErrorMessage = "LLM client is not configured" };
        }

        var request = this.BuildLlmRequest(prepared, languageRetry);
        int timeoutSeconds = Math.Max(5, DialogueServices.Config?.QueryTimeout ?? 85);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var text = new System.Text.StringBuilder();
        TokenUsage usage = new();
        await foreach (var streamEvent in client.StreamAsync(request, cts.Token).ConfigureAwait(false))
        {
            switch (streamEvent.Kind)
            {
                case LlmStreamEventKind.TextDelta:
                    text.Append(streamEvent.Text);
                    sink.OnToken(streamEvent.Text);
                    break;
                case LlmStreamEventKind.Usage when streamEvent.Usage != null:
                    usage = streamEvent.Usage;
                    break;
            }
        }

        var response = new LlmResponse { IsSuccess = text.Length > 0, Text = text.ToString(), Usage = usage };
        this.RecordUsage(prepared, request, response);
        return response;
    }

    // ---- 装配 ----

    private sealed class PreparedGeneration
    {
        public GenerationRequest Request { get; init; } = new();
        public NpcBio Bio { get; init; } = new();
        public Character Character { get; init; } = new(string.Empty);
        public DialogueContext Context { get; init; } = new();
        public ContextRoutingPlan Plan { get; init; } = ContextRoutingPlan.Full();
        public AssembledPrompt Prompt { get; set; } = new();
        public PromptAssemblyInput AssemblyInput { get; init; } = new();
        public string NpcDisplayName { get; init; } = string.Empty;
        public List<ConversationTurn> Conversation { get; init; } = new();
        public string LastPlayerLine { get; init; } = string.Empty;
        public string GivingGiftItemId { get; init; } = string.Empty;
        public HashSet<string> ValidPortraits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FixPunctuation { get; init; }
    }

    private async Task<PreparedGeneration> PrepareAsync(GenerationRequest request, CancellationToken ct)
    {
        var config = DialogueServices.Config ?? new DialogueConfig();
        var snapshot = request.Snapshot;
        var bio = request.ContentSnapshot?.Bio ?? this.services.GetBio(request.NpcName);
        string displayName = string.IsNullOrWhiteSpace(request.NpcDisplayName)
            ? this.services.GetNpcDisplayName(request.NpcName)
            : request.NpcDisplayName;

        // 会话续接：与上次会话上下文按元素 GUID 去重合并（§4.3/§4.14）。
        List<ConversationTurn> conversation = request.Trigger == GenerationTrigger.Conversation
            ? this.services.History.BuildMergedConversationContext(request.NpcName, request.Conversation)
            : new List<ConversationTurn>(request.Conversation);
        int latestPlayerIndex = conversation.FindLastIndex(turn => turn.IsPlayerLine);
        bool latestPlayerMessageWithheld = latestPlayerIndex >= 0
            && RsvAiPolicy.ContainsBlockedReference(conversation[latestPlayerIndex].Text);

        // If the current question itself contains unavailable third-party data, don't expose an
        // older question as the apparent latest turn. Keep one explicit prompt marker and remove
        // the preceding transcript for this exceptional reply; the original conversation remains
        // available for normal history bookkeeping and is filtered again on every future prompt.
        List<ConversationTurn> promptConversation = latestPlayerMessageWithheld
            ? new List<ConversationTurn>
            {
                new(RsvAiPolicy.WithheldPlayerMessage, true, conversation[latestPlayerIndex].Id)
            }
            : conversation
                .Select(turn => RsvAiPolicy.ContainsBlockedReference(turn.Text)
                    ? new ConversationTurn(string.Empty, turn.IsPlayerLine, turn.Id)
                    : turn)
                .ToList();
        string lastPlayerLine = latestPlayerMessageWithheld
            ? string.Empty
            : promptConversation.LastOrDefault(turn => turn.IsPlayerLine)?.Text ?? string.Empty;

        List<ConversationTurn> routingConversation = promptConversation
            .Select(turn => RsvAiPolicy.IsWithheldPlayerMessage(turn.Text)
                ? new ConversationTurn(string.Empty, turn.IsPlayerLine, turn.Id)
                : turn)
            .ToList();

        // 决策通道输入（搬运件消费的上下文视图）。
        var context = new DialogueContext
        {
            Accept = request.Trigger == GenerationTrigger.Gift && !RsvAiPolicy.IsBlockedContentId(request.GiftItemId)
                ? request.GiftItemId
                : null,
            ChatHistory = routingConversation
                .Select(turn => new ConversationElement(turn.Text, turn.IsPlayerLine) { Id = turn.Id })
                .ToList(),
            LivingNpcExtraPrompt = RsvAiPolicy.RemoveBlockedLines(request.BehaviorContext),
            Hearts = snapshot.FriendshipExists ? snapshot.Hearts : null,
            Location = snapshot.LocationName,
            TimeOfDay = $"{GameStateSnapshot.ClockText(snapshot.TimeOfDay)}",
            Weather = snapshot.WeatherFlags.ToList(),
            CurrentActivity = snapshot.CurrentActivity,
            NextScheduleLocation = snapshot.NextScheduleLocation,
            MinutesUntilNextSchedule = snapshot.MinutesUntilNextSchedule
        };

        var character = new Character(request.NpcName, displayName: displayName)
        {
            Bio = ToCharacterBio(bio)
        };

        // 上下文路由（提示词构造之前，§4.5）。
        ct.ThrowIfCancellationRequested();
        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(character, context, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // 键解析（触发上下文 + 样本选择）。
        var keyFacts = DialogueKeyGrammar.Parse(request.DialogueKey);
        var currentFacts = new DialogueKeyFacts
        {
            Season = snapshot.SeasonName.ToLowerInvariant(),
            DayOfMonth = snapshot.DayOfMonth,
            Weekday = snapshot.DayOfWeek,
            Hearts = snapshot.FriendshipExists ? snapshot.Hearts : null,
            Year = snapshot.Year,
            IsGiftAccept = request.Trigger == GenerationTrigger.Gift,
            RandomAction = keyFacts.RandomAction,
            SpouseAction = keyFacts.SpouseAction,
            InlawName = snapshot.InlawOfSpouse,
            SpouseName = snapshot.IsMarriedToFarmer ? snapshot.FarmerSpouseName : string.Empty,
            TimeBucket = snapshot.TimeBucketKey,
            IsMarriageKey = snapshot.IsMarriedToFarmer
        };

        var samples = plan.Include(ContextModule.SampleDialogue)
            ? this.SelectSamples(
                request.NpcName,
                bio,
                currentFacts,
                snapshot,
                request.ContentSnapshot?.DialogueSamples)
            : new List<DialogueSample>();

        // 历史采样。
        var historyLines = plan.Include(ContextModule.EventHistory)
            ? HistorySampler.Sample(
                this.services.GetHistory(request.NpcName),
                snapshot.ActiveDialogueEvents,
                this.services.Now(),
                displayName,
                conversation.FirstOrDefault()?.Id ?? string.Empty)
            : new List<string>();

        bool justSpoke = this.services.History.JustSpoke(request.NpcName, this.services.Now());

        // 配偶主动送礼抽取（Scheduled 且 originalLine 为空，§4.1；裁决 4：仅成功结果附加标记）。
        string givingGiftId = string.Empty;
        if (request.Trigger == GenerationTrigger.Scheduled
            && string.IsNullOrWhiteSpace(request.OriginalLine)
            && snapshot.IsMarriedToFarmer)
        {
            givingGiftId = SpouseGiftPicker?.Invoke(request.NpcName, snapshot) ?? string.Empty;
            if (RsvAiPolicy.IsBlockedContentId(givingGiftId))
            {
                givingGiftId = string.Empty;
            }
        }

        var variantGender = bio.ResolvedGender;
        IReadOnlyList<PortraitFrameSemantics.Match> portraitFrames =
            request.ContentSnapshot?.ResolvedPortraitFrames ?? Array.Empty<PortraitFrameSemantics.Match>();
        int portraitFrameCount = request.ContentSnapshot?.PortraitFrameCount ?? 0;
        portraitFrames = ApplyGamePortraitOverrides(request, portraitFrames, ref portraitFrameCount);
        var input = new PromptAssemblyInput
        {
            Request = request,
            Plan = plan,
            Bio = bio,
            PortraitFrames = portraitFrames,
            PortraitFrameCount = portraitFrameCount,
            UsesRuntimePortraitWhitelist = request.UsesRuntimePortraitWhitelist || request.ContentSnapshot != null,
            NpcName = request.NpcName,
            NpcDisplayName = displayName,
            NpcGender = variantGender,
            Samples = samples,
            HistoryLines = historyLines,
            Conversation = promptConversation,
            JustSpoke = justSpoke,
            WorldSummaryFull = this.services.GetWorldSummaryText(config.UseOptimizedPrompts),
            WorldSummaryBrief = this.services.GetWorldSummaryText(true),
            PreoccupationTopic = this.PickPreoccupation(request.NpcName, bio, promptConversation.Count),
            GiftDisplayName = request.Trigger == GenerationTrigger.Gift
                && !RsvAiPolicy.IsBlockedContentId(request.GiftItemId)
                ? this.services.GetItemDisplayName(request.GiftItemId)
                : string.Empty,
            GivingGiftDisplayName = string.IsNullOrWhiteSpace(givingGiftId)
                ? string.Empty
                : this.services.GetItemDisplayName(givingGiftId),
            ExtraInstructions = (this.services.GetClient() as ILlmCapabilities)?.ExtraInstructions ?? string.Empty,
            ApplyTranslation = config.ApplyTranslation,
            Locale = this.services.GetLocale(),
            UseOptimizedPrompts = config.UseOptimizedPrompts,
            SpouseAction = keyFacts.SpouseAction,
            RandomAction = keyFacts.RandomAction,
            SectionOverrides = PromptOverrides,
            Lookup = (key, tokens, optimized) => this.services.LookupPrompt(
                key, bio, new PromptVariant(variantGender, optimized), tokens)
        };

        HashSet<string> validPortraits;
        bool usesRuntimePortraitWhitelist = request.UsesRuntimePortraitWhitelist
            || request.ContentSnapshot != null;
        if (!usesRuntimePortraitWhitelist)
        {
            // Tests and legacy callers without a game-thread snapshot retain the content contract.
            validPortraits = new HashSet<string>(bio.ValidPortraits, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // A captured runtime texture is fail-closed: neutral is always safe; every other marker
            // must either match a reviewed frame or be an explicit, physically present bio frame.
            validPortraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0" };
            foreach (PortraitFrameSemantics.Match frame in portraitFrames)
            {
                if (PortraitMarkerRules.NormalizeGameMarker(frame.Marker) is { } marker)
                {
                    validPortraits.Add(marker);
                }
            }

            int frameCount = portraitFrameCount;
            if (frameCount > 0)
            {
                foreach (string configuredMarker in bio.ExtraPortraits.Keys)
                {
                    if (PortraitMarkerRules.NormalizeExtraMarker(configuredMarker) is { } marker
                        && PortraitMarkerRules.TryGetFrameIndex(marker, out int frameIndex)
                        && frameIndex < frameCount)
                    {
                        validPortraits.Add(marker);
                    }
                }
            }
        }

        var prepared = new PreparedGeneration
        {
            Request = request,
            Bio = bio,
            Character = character,
            Context = context,
            Plan = plan,
            AssemblyInput = input,
            NpcDisplayName = displayName,
            Conversation = conversation,
            LastPlayerLine = lastPlayerLine,
            GivingGiftItemId = givingGiftId,
            ValidPortraits = validPortraits,
            FixPunctuation = this.services.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        };
        prepared.Prompt = new PromptAssembler(input).Assemble();
        if (prepared.ValidPortraits.Count == 0)
        {
            prepared.ValidPortraits.Add("0");
        }

        return prepared;
    }

    private static IReadOnlyList<PortraitFrameSemantics.Match> ApplyGamePortraitOverrides(
        GenerationRequest request,
        IReadOnlyList<PortraitFrameSemantics.Match> portraitFrames,
        ref int portraitFrameCount)
    {
        GameStateSnapshot snapshot = request.Snapshot;
        if (string.Equals(request.NpcName, "Demetrius", StringComparison.OrdinalIgnoreCase)
            && snapshot.Year == 1
            && snapshot.IsGreenRain)
        {
            // Stardew Dialogue.getPortraitIndex ignores every dialogue marker in this state.
            // Frame 7 is a forced hazmat costume, not an emotion the model can select.
            portraitFrameCount = 0;
            return Array.Empty<PortraitFrameSemantics.Match>();
        }

        if (snapshot.IsDivorced && snapshot.NpcShouldWearIslandAttire)
        {
            // Stardew prepareDialogueForDisplay forcibly changes $u to neutral for divorced NPCs
            // wearing island attire. Suppress that frame and explicit unchecked overrides.
            portraitFrameCount = 0;
            return portraitFrames.Where(frame => frame.FrameIndex != 3).ToArray();
        }

        return portraitFrames;
    }

    private List<DialogueSample> SelectSamples(
        string npcName,
        NpcBio bio,
        DialogueKeyFacts current,
        GameStateSnapshot snapshot,
        IReadOnlyDictionary<string, string>? capturedSamples)
    {
        lock (this.gate)
        {
            if (!this.sampleLibraries.TryGetValue(npcName, out var library))
            {
                IReadOnlyDictionary<string, string> source = capturedSamples
                    ?? this.services.GetSamples(npcName, bio);
                library = SampleSelector.BuildLibrary(source);
                this.sampleLibraries[npcName] = library;
            }

            var cacheKey = (snapshot.SeasonIndex, snapshot.DayOfMonth, snapshot.Hearts);
            if (this.selectedSamples.TryGetValue(npcName, out var cached) && cached.Key == cacheKey)
            {
                return cached.Samples;
            }

            var selected = SampleSelector.Select(library, current, this.services.Random);
            this.selectedSamples[npcName] = (cacheKey, selected);
            return selected;
        }
    }

    /// <summary>资产失效时清空样本缓存（WP15 事件接线调用）。</summary>
    public void InvalidateSampleCaches()
    {
        lock (this.gate)
        {
            this.sampleLibraries.Clear();
            this.selectedSamples.Clear();
        }
    }

    private string PickPreoccupation(string npcName, NpcBio bio, int conversationCount)
    {
        if (conversationCount > 0 || bio.TopicPool.Count == 0)
        {
            return string.Empty;
        }

        int today = this.services.Now().ToAbsoluteDays();
        lock (this.gate)
        {
            if (this.preoccupationMemo.TryGetValue(npcName, out var memo) && memo.Day == today)
            {
                return memo.Topic;
            }

            // 50% 概率通过；当日粘滞（含"未通过"也粘滞，§4.6.4.19）。
            string topic = this.services.Random.Next(2) == 0
                ? bio.TopicPool[this.services.Random.Next(bio.TopicPool.Count)]
                : string.Empty;
            this.preoccupationMemo[npcName] = (today, topic);
            return topic;
        }
    }

    // ---- LLM 调用（§4.9） ----

    private LlmRequest BuildLlmRequest(PreparedGeneration prepared, bool languageRetry)
    {
        var prompt = prepared.Prompt;
        string command = prompt.Command;
        if (languageRetry)
        {
            command += ConversationTextPostProcessor.GetLanguageRetryInstruction();
        }

        return new LlmRequest
        {
            SystemPrompt = prompt.System,
            StableContext = prompt.GameConstantContext,
            NpcContext = prompt.NpcConstantContext,
            Tail = prompt.CorePrompt + prompt.Instructions + command,
            ResponseStart = prompt.ResponseStart,
            AllowRetry = false,
            MaxTokens = 2048
        };
    }

    private async Task<LlmResponse> RunAttemptAsync(PreparedGeneration prepared, bool languageRetry, CancellationToken ct)
    {
        ILlmClient? client = this.services.GetClient();
        if (client == null)
        {
            return new LlmResponse { IsSuccess = false, ErrorMessage = "LLM client is not configured" };
        }

        var request = this.BuildLlmRequest(prepared, languageRetry);
        int timeoutSeconds = Math.Max(5, DialogueServices.Config?.QueryTimeout ?? 85);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        LlmReply reply = await client.CompleteAsync(request, cts.Token).ConfigureAwait(false);
        var response = new LlmResponse
        {
            IsSuccess = reply.IsSuccess,
            Text = reply.Text,
            ErrorMessage = reply.ErrorMessage,
            Usage = reply.Usage
        };
        this.RecordUsage(prepared, request, response);
        return response;
    }

    private void RecordUsage(PreparedGeneration prepared, LlmRequest request, LlmResponse response)
    {
        TokenUsage usage = response.Usage.HasAnyTokens
            ? response.Usage
            : TokenUsage.Estimate(
                request.SystemPrompt + request.StableContext + request.NpcContext + request.Tail,
                response.Text ?? string.Empty);
        TokenUsageTracker.Instance.Record(
            prepared.Request.NpcName,
            usage,
            DialogueServices.Config?.Provider ?? "unset",
            DialogueServices.Config?.ModelName ?? "unset",
            response.IsSuccess ? "success" : "failed");
    }

    // ---- 解析与收尾 ----

    private ParsedResponse ParseResponse(PreparedGeneration prepared, string rawText)
    {
        return ResponseParser.Parse(
            rawText,
            prepared.ValidPortraits,
            prepared.Request.Snapshot.FarmerName,
            prepared.FixPunctuation,
            DialogueServices.Config?.Debug == true);
    }

    private async Task<GenerationResult> FinalizeResultAsync(
        PreparedGeneration prepared,
        LlmResponse? response,
        Exception? failure,
        int attempts,
        Stopwatch generationWatch,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var request = prepared.Request;
        string typedResponses = DialogueServices.Config?.TypedResponses ?? "With Generated";

        if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
        {
            // 全部尝试失败或不可解析 → 单行 ... 回退（§4.9；裁决 4：回退不附送礼标记）。
            if (failure != null)
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.generationFailed",
                        new { npc = request.NpcName, error = failure.Message },
                        $"Dialogue generation failed for {request.NpcName}: {failure.Message}"),
                    StardewModdingAPI.LogLevel.Error);
            }
            else
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.generationEmpty",
                        new { npc = request.NpcName },
                        $"Dialogue generation produced no usable response for {request.NpcName}."),
                    StardewModdingAPI.LogLevel.Warn);
            }

            this.ExportAttempt(prepared, response, ConversationAnalysis.Empty, Array.Empty<string>(), attempts, failure != null ? "error" : "unparseable");
            ct.ThrowIfCancellationRequested();
            return new GenerationResult
            {
                FormattedLine = EngineConstants.SkipPrefix + EngineConstants.FallbackLine,
                ParsedLines = new[] { EngineConstants.FallbackLine },
                AnalysisJson = ConversationAnalysis.Empty.ToJson(),
                EndConversation = true,
                DialogueKey = this.ResolveDialogueKey(request),
                IsFallback = true,
                Usage = response?.Usage ?? new TokenUsage()
            };
        }

        ct.ThrowIfCancellationRequested();
        var parsed = this.ParseResponse(prepared, response.Text);
        var legacyAnalysis = ConversationAnalysis.Parse(response.Text);
        var analysis = legacyAnalysis;

        List<string> lines = new() { parsed.DialogueLine };
        lines.AddRange(parsed.Options);
        LivingNpcActionDecisionDiagnostics? actionDiagnostics = null;
        if (request.Trigger is GenerationTrigger.Conversation or GenerationTrigger.Gift)
        {
            string playerText = request.Trigger == GenerationTrigger.Gift
                ? RsvAiPolicy.IsBlockedContentId(request.GiftItemId)
                    ? "The farmer gave the NPC an unsupported third-party item; no item text is available."
                    : $"The farmer gave {request.NpcName} item {request.GiftItemId}; gift taste code {request.GiftTaste}."
                : prepared.LastPlayerLine;
            try
            {
                LivingNpcMetadataExtractionResult extracted = await LivingNpcMetadataExtractionPass
                    .TryExtractAsync(prepared.Character, prepared.Context, playerText, parsed.DialogueLine, parsed.Options, ct)
                    .ConfigureAwait(false);
                if (extracted.Success)
                {
                    analysis = extracted.Analysis;
                }
                else
                {
                    // Compatibility fallback: an older/custom model may still have returned inline metadata.
                    // If the full classifier fails, retain that analysis and allow the old action-only pass
                    // to recover a clearly promised action without discarding the visible dialogue.
                    var supplemented = await LivingNpcActionDecisionPass
                        .TrySupplementAsync(prepared.Character, prepared.Context, legacyAnalysis, lines, ct)
                        .ConfigureAwait(false);
                    analysis = supplemented.Analysis;
                    actionDiagnostics = supplemented.Diagnostics;
                    DialogueServices.Monitor?.Log(
                        $"Metadata extraction for {request.NpcName} failed safely: {extracted.FailureReason}",
                        StardewModdingAPI.LogLevel.Trace);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                analysis = legacyAnalysis;
                DialogueServices.Monitor?.Log(
                    I18n.Get("log.dialogue.actionDecisionFailed", new { npc = request.NpcName, error = ex.Message }),
                    StardewModdingAPI.LogLevel.Trace);
            }

            // Apply the same evidence guard to the legacy-inline fallback as to authoritative
            // metadata extraction. It is idempotent when the extraction path already applied it.
            LivingNpcMetadataExtractionPass.ApplyConservativeInterpersonalEvidenceRules(
                analysis,
                prepared.Context,
                playerText,
                parsed.DialogueLine);
        }

        ct.ThrowIfCancellationRequested();

        // The model sometimes carries accepted_now across a multi-turn negotiation but omits the
        // destination once both speakers shorten it to "let's go". Recover only from a recent,
        // explicit player invitation in the sanitized current conversation. This also normalizes
        // every outing delay to zero now that departure begins as soon as the final line closes.
        RecentOutingInvitationResolver.NormalizeCompanionOutingActions(
            analysis,
            prepared.Context,
            parsed.DialogueLine);

        // 立即出游已经把这轮交流转化成世界动作；NPC 的确认台词应当像明确道别一样
        // 点完即关闭，不能继续显示模型误生成的回应选项。
        if (HasAcceptedImmediateCompanionOuting(analysis, prepared.LastPlayerLine, parsed.DialogueLine))
        {
            analysis.EndConversation = true;
        }

        // 元数据裁决：EndConversation 且行数 >1 → 只保留台词行（§4.10.8）。
        var options = parsed.Options;
        if (analysis.EndConversation && options.Count > 0)
        {
            options = new List<string>();
        }

        // 后处理（§4.11）。
        string visibleDialogueLine = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(
            parsed.DialogueLine, prepared.LastPlayerLine);
        visibleDialogueLine = ResponseParser.PromoteWeakPositivePortraitsForExplicitJoy(
            visibleDialogueLine,
            BuildAvailablePortraitDescriptions(prepared));
        if (IsCanonicalAngryPortraitActuallyAngry(prepared))
        {
            string fullVisibleDialogueLine = visibleDialogueLine;
            bool isMultiPage = fullVisibleDialogueLine.Contains("#$b#", StringComparison.OrdinalIgnoreCase)
                || fullVisibleDialogueLine.Contains("#$e#", StringComparison.OrdinalIgnoreCase);
            visibleDialogueLine = ResponseParser.DowngradeCanonicalAngryPortraitToNeutral(
                fullVisibleDialogueLine,
                pageText => LivingNpcMetadataExtractionPass.ShouldNeutralizeCanonicalAngryPortraitPage(
                    analysis,
                    prepared.Context,
                    prepared.LastPlayerLine,
                    fullVisibleDialogueLine,
                    pageText,
                    isMultiPage));
        }
        bool ended = ResponseFormatter.IsConversationEnded(analysis, prepared.LastPlayerLine, visibleDialogueLine);

        // 可信送礼命令只进入原生 Stardew 对话串。解析产物、历史和流式 UI 始终保留
        // 纯可见文本，避免把控制串画出来，也防止它污染后续模型上下文。
        string nativeDialogueLine = visibleDialogueLine;
        if (!string.IsNullOrWhiteSpace(prepared.GivingGiftItemId))
        {
            nativeDialogueLine += $"[{prepared.GivingGiftItemId}]";
        }

        bool allowTypedFallback = string.Equals(typedResponses, "With Generated", StringComparison.OrdinalIgnoreCase)
            && options.Count > 0;
        string formatted = ResponseFormatter.BuildFormattedLine(
            nativeDialogueLine, options, typedResponses, ended, allowTypedFallback, dontSkipNext: false);
        formatted = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(formatted, prepared.LastPlayerLine);

        var parsedLines = new List<string> { visibleDialogueLine };
        parsedLines.AddRange(options);

        var result = new GenerationResult
        {
            FormattedLine = formatted,
            ParsedLines = parsedLines.ToArray(),
            AnalysisJson = analysis.ToJson(),
            EndConversation = ended,
            DialogueKey = this.ResolveDialogueKey(request),
            Usage = response.Usage,
            Commit = new GenerationCommit
            {
                Request = request,
                NpcDisplayName = prepared.NpcDisplayName,
                LastPlayerLine = prepared.LastPlayerLine,
                DialogueLine = visibleDialogueLine,
                Conversation = prepared.Conversation.ToList()
            }
        };

        long generationMilliseconds = generationWatch.ElapsedMilliseconds;
        this.ExportAttempt(prepared, response, analysis, parsedLines, attempts, "success", actionDiagnostics);
        this.LogDiagnostics(prepared, response, attempts, generationMilliseconds, parsedLines.Count);
        return result;
    }

    /// <summary>与行为层裁决完全共享同一判定（ConversationActionCueRules.IsConfirmedImmediateOutingAcceptance）：
    /// 空目标/未知目标/被可见文本强矛盾否决的 accepted_now 出游，行为层不会执行，这里也绝不据此
    /// 强制关闭对话——两层不一致时玩家会看到"她答应了、对话关了、人却没动"。</summary>
    internal static bool HasAcceptedImmediateCompanionOuting(ConversationAnalysis analysis, string playerText, string npcVisibleLine)
    {
        return analysis.Actions.Any(action =>
            string.Equals(action.Type, "companion_outing", StringComparison.OrdinalIgnoreCase)
            && Behavior.ConversationActionCueRules.IsConfirmedImmediateOutingAcceptance(
                action.TargetLocation,
                action.TravelConsent,
                playerText,
                npcVisibleLine));
    }

    private static IReadOnlyDictionary<string, string> BuildAvailablePortraitDescriptions(
        PreparedGeneration prepared)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // A hash-reviewed match from the final texture is authoritative for its marker. Explicit
        // bio frames fill only still-unresolved, physically available markers.
        foreach (PortraitFrameSemantics.Match frame in prepared.AssemblyInput.PortraitFrames
                     .OrderBy(frame => frame.FrameIndex))
        {
            string? marker = PortraitMarkerRules.NormalizeGameMarker(frame.Marker);
            if (marker != null
                && prepared.ValidPortraits.Contains(marker)
                && !string.IsNullOrWhiteSpace(frame.Description)
                && !descriptions.ContainsKey(marker))
            {
                descriptions[marker] = frame.Description;
            }
        }

        foreach ((string rawMarker, string description) in prepared.Bio.ExtraPortraits)
        {
            string? marker = PortraitMarkerRules.NormalizeExtraMarker(rawMarker);
            if (marker != null
                && prepared.ValidPortraits.Contains(marker)
                && !string.IsNullOrWhiteSpace(description)
                && !descriptions.ContainsKey(marker))
            {
                descriptions[marker] = description;
            }
        }

        return descriptions;
    }

    private static bool IsCanonicalAngryPortraitActuallyAngry(PreparedGeneration prepared)
    {
        var descriptions = prepared.AssemblyInput.PortraitFrames
            .Where(frame => string.Equals(
                PortraitMarkerRules.NormalizeGameMarker(frame.Marker),
                "a",
                StringComparison.OrdinalIgnoreCase))
            .Select(frame => frame.Description)
            .Concat(prepared.Bio.ExtraPortraits
                .Where(pair => string.Equals(
                    PortraitMarkerRules.NormalizeExtraMarker(pair.Key),
                    "a",
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value))
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        // Legacy/test callers without a captured final texture retain Stardew's standard $a
        // meaning. Runtime snapshots require an explicit reviewed/configured angry description.
        return descriptions.Length == 0
            ? prepared.Request.ContentSnapshot == null
            : descriptions.Any(IsAngryPortraitDescription);
    }

    internal static bool IsAngryPortraitDescription(string description)
    {
        string[] fragments =
        {
            "angry", "irritated", "furious", "displeased", "confrontational", "scowl",
            "生气", "愤怒", "恼怒", "不悦", "冲突", "瞪"
        };
        return !string.IsNullOrWhiteSpace(description)
            && fragments.Any(fragment => description.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Commits a generated result after the caller has validated its generation/session and shown
    /// it to the player. The payload is claim-once so a stale completion or duplicate presenter
    /// cannot write history or enqueue behavior twice.
    /// </summary>
    public bool CommitResult(GenerationResult result)
    {
        GenerationCommit? commit = result.Commit;
        if (commit == null || !commit.TryClaim())
        {
            return false;
        }

        this.FinishTrigger(commit, result.AnalysisJson);
        return true;
    }

    /// <summary>各触发的收尾动作（§4.12）。Scheduled/Marriage 不回传、不写会话历史。</summary>
    private void FinishTrigger(GenerationCommit commit, string analysisJson)
    {
        var request = commit.Request;
        try
        {
            StardewTime now = this.services.Now();
            switch (request.Trigger)
            {
                case GenerationTrigger.ConversationOpening:
                {
                    var npcTurn = new ConversationTurn(commit.DialogueLine, false, Guid.NewGuid().ToString("N"));
                    this.services.History.AppendToConversationContext(request.NpcName, npcTurn);
                    var turns = this.services.History.PeekConversationContext(request.NpcName);
                    this.services.History.UpsertConversation(request.NpcName, turns, now);
                    break;
                }

                case GenerationTrigger.Conversation:
                {
                    this.services.History.MergeConversationContext(request.NpcName, commit.Conversation);
                    var npcTurn = new ConversationTurn(commit.DialogueLine, false, Guid.NewGuid().ToString("N"));
                    this.services.History.AppendToConversationContext(request.NpcName, npcTurn);
                    var turns = this.services.History.PeekConversationContext(request.NpcName);
                    this.services.History.UpsertConversation(request.NpcName, turns, now);
                    RecordExchangeCallback?.Invoke(request.NpcName, commit.LastPlayerLine, commit.DialogueLine, analysisJson);
                    break;
                }

                case GenerationTrigger.Gift:
                {
                    string giftName = this.services.GetItemDisplayName(request.GiftItemId);
                    RecordExchangeCallback?.Invoke(
                        request.NpcName,
                        $"The farmer offered {giftName}.",
                        commit.DialogueLine,
                        analysisJson);

                    string playerLine = Util.GetString(
                        "transcriptGiftPlayerLine",
                        new { npcName = commit.NpcDisplayName, giftName },
                        returnNull: true) ?? $"The farmer gave {commit.NpcDisplayName} {giftName}.";
                    var turns = new List<ConversationTurn>
                    {
                        new(playerLine, true, Guid.NewGuid().ToString("N")),
                        new(commit.DialogueLine, false, Guid.NewGuid().ToString("N"))
                    };
                    this.services.History.UpsertConversation(request.NpcName, turns, now);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = $"finish generation bookkeeping for {request.NpcName}", error = ex.Message },
                    $"Failed to finish generation bookkeeping for {request.NpcName}: {ex.Message}"),
                StardewModdingAPI.LogLevel.Warn);
        }
    }

    private string ResolveDialogueKey(GenerationRequest request)
    {
        return request.Trigger switch
        {
            GenerationTrigger.Gift => EngineConstants.GiftKeyPrefix + request.GiftItemId,
            GenerationTrigger.Conversation => string.IsNullOrWhiteSpace(request.DialogueKey)
                ? EngineConstants.KeyConversation
                : request.DialogueKey,
            _ => string.IsNullOrWhiteSpace(request.DialogueKey) ? EngineConstants.KeyDefault : request.DialogueKey
        };
    }

    private void ExportAttempt(
        PreparedGeneration prepared,
        LlmResponse? response,
        ConversationAnalysis analysis,
        IReadOnlyList<string> parsedLines,
        int attempt,
        string outcome,
        LivingNpcActionDecisionDiagnostics? actionDiagnostics = null)
    {
        try
        {
            var prompt = prepared.Prompt;
            AiResponseLogExporter.Append(
                prepared.Request.NpcName,
                prepared.Context,
                response ?? new LlmResponse { IsSuccess = false },
                analysis,
                parsedLines,
                attempt,
                prompt.TotalCharacters,
                outcome,
                actionDiagnostics!);
            PromptLogExporter.Append(
                prepared.Request.NpcName,
                prepared.Context,
                prompt.System,
                prompt.GameConstantContext,
                prompt.NpcConstantContext,
                prompt.CorePrompt,
                prompt.Instructions,
                prompt.Command,
                prompt.ResponseStart,
                response ?? new LlmResponse { IsSuccess = false },
                parsedLines,
                attempt,
                outcome);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.exportFailed", new { error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
        }
    }

    private void LogDiagnostics(PreparedGeneration prepared, LlmResponse response, int attempts, long elapsedMilliseconds, int lineCount)
    {
        if (DialogueServices.Config?.Debug != true)
        {
            return;
        }

        var prompt = prepared.Prompt;
        var plan = prepared.Plan;
        string sections = string.Join(", ", prompt.SectionLengths
            .Where(pair => pair.Value > 0)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        DialogueServices.Monitor?.Log(
            I18n.Get(
                "log.dialogue.generated",
                new
                {
                    npc = prepared.Request.NpcName,
                    totalMs = elapsedMilliseconds,
                    routingOutcome = plan.RoutingOutcome,
                    routingMs = plan.RoutingMilliseconds,
                    attempts,
                    promptChars = prompt.TotalCharacters,
                    responseChars = (response.Text ?? string.Empty).Length,
                    lines = lineCount,
                    sections
                }) + Environment.NewLine,
            StardewModdingAPI.LogLevel.Debug);
    }

    private static CharacterBio? ToCharacterBio(NpcBio bio)
    {
        if (bio == null || bio.Traits.Count == 0)
        {
            return null;
        }

        var characterBio = new CharacterBio();
        foreach (var pair in bio.Traits)
        {
            characterBio.Traits[pair.Key] = new CharacterBioTrait
            {
                Heading = pair.Value.Heading,
                Description = pair.Value.Description
            };
        }

        return characterBio;
    }
}

/// <summary>引擎进程级挂点：由装配代码（WP12/WP16 接线）赋值。</summary>
internal static class DialogueEngineHost
{
    public static DialogueEngine? Instance { get; set; }

    private static PortraitFrameSemantics portraitSemantics = PortraitFrameSemantics.Empty;
    private static Action? invalidatePortraitSnapshots;

    /// <summary>清空 final-portrait observations after a content or save-scope invalidation.</summary>
    public static void InvalidatePortraitCaches()
    {
        portraitSemantics.Invalidate();
        invalidatePortraitSnapshots?.Invoke();
    }

    /// <summary>
    /// Revalidate the final portrait on the game thread immediately before display. A different
    /// signature means the model selected expressions against another sheet, so only neutral is
    /// safe; menu, page, and trusted item commands remain untouched.
    /// </summary>
    internal static string RevalidatePortraitMarkers(
        NPC? npc,
        GenerationRequest request,
        string formattedLine)
    {
        GenerationContentSnapshot? snapshot = request.ContentSnapshot;
        if (npc == null || snapshot == null || string.IsNullOrWhiteSpace(snapshot.PortraitSignature))
        {
            return formattedLine;
        }

        return RevalidatePortraitSignature(
            formattedLine,
            snapshot.PortraitSignature,
            () =>
            {
                portraitSemantics.ResolveAllFresh(
                    npc.Portrait,
                    locale: null,
                    out string currentSignature,
                    out _);
                return currentSignature;
            });
    }

    /// <summary>
    /// Compare a captured portrait signature with a fresh game-thread read. Texture reads are an
    /// optional safety check, so a disposed/replaced GPU resource must not discard valid dialogue.
    /// Fail closed to neutral portraits while preserving all non-portrait dialogue commands.
    /// </summary>
    internal static string RevalidatePortraitSignature(
        string formattedLine,
        string expectedSignature,
        Func<string> readCurrentSignature)
    {
        try
        {
            string currentSignature = readCurrentSignature();
            return string.Equals(expectedSignature, currentSignature, StringComparison.Ordinal)
                ? formattedLine
                : ResponseParser.DowngradePortraitMarkersToNeutral(formattedLine);
        }
        catch (Exception)
        {
            return ResponseParser.DowngradePortraitMarkersToNeutral(formattedLine);
        }
    }

    /// <summary>用真实服务装配引擎（游戏侧调用；测试自行构造 DialogueEngine）。</summary>
    public static DialogueEngine CreateDefault(DialogueContentService content)
    {
        portraitSemantics = PortraitFrameSemantics.Empty;
        try
        {
            string catalogPath = System.IO.Path.Combine(
                DialogueServices.Helper!.DirectoryPath,
                ContentAssetNames.PortraitFrameSemanticsFile);
            portraitSemantics = PortraitFrameSemantics.FromJson(System.IO.File.ReadAllText(catalogPath));
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.assetLoadFailed",
                    new { asset = ContentAssetNames.PortraitFrameSemanticsFile, error = ex.Message },
                    $"Failed to load content asset '{ContentAssetNames.PortraitFrameSemanticsFile}': {ex.Message}"),
                StardewModdingAPI.LogLevel.Warn);
        }

        var contentSnapshotGate = new object();
        var contentSnapshots = new Dictionary<
            string,
            (NpcBio Bio, bool ExpansionCompatible, bool RsvBlocked, string Locale, string PortraitSignature, GenerationContentSnapshot Snapshot)>(
                StringComparer.OrdinalIgnoreCase);
        invalidatePortraitSnapshots = () =>
        {
            lock (contentSnapshotGate)
            {
                contentSnapshots.Clear();
            }
        };
        GameHooks.GenerationRequests.ContentSnapshotProvider = npc =>
        {
            NpcBio bio = content.GetBio(npc.Name);
            bool expansionCompatible = SveContentRules.IsExpansionCompatibilityEnabled(
                SveContentRules.NormalizeName(npc.Name),
                DialogueServices.Config?.EnableSveCompatibility ?? true);
            bool rsvBlocked = RsvAiPolicy.IsBlockedNpc(npc);
            string locale = DialogueServices.Helper?.GameContent.CurrentLocale ?? string.Empty;
            IReadOnlyList<PortraitFrameSemantics.Match> portraitFrames = portraitSemantics.ResolveAll(
                npc.Portrait,
                locale,
                out string portraitSignature,
                out int portraitFrameCount);
            lock (contentSnapshotGate)
            {
                if (contentSnapshots.TryGetValue(npc.Name, out var cached)
                    && ReferenceEquals(cached.Bio, bio)
                    && cached.ExpansionCompatible == expansionCompatible
                    && cached.RsvBlocked == rsvBlocked
                    && string.Equals(cached.PortraitSignature, portraitSignature, StringComparison.Ordinal)
                    && string.Equals(cached.Locale, locale, StringComparison.OrdinalIgnoreCase))
                {
                    return cached.Snapshot;
                }
            }

            IReadOnlyDictionary<string, string> samples = DialogueSampleLoader.GetSamples(
                npc,
                bio,
                expansionCompatible);
            var snapshot = new GenerationContentSnapshot(
                bio,
                samples,
                portraitFrames,
                portraitFrameCount,
                portraitSignature);
            lock (contentSnapshotGate)
            {
                contentSnapshots[npc.Name] = (bio, expansionCompatible, rsvBlocked, locale, portraitSignature, snapshot);
            }

            return snapshot;
        };

        var services = new DialogueEngineServices
        {
            GetBio = content.GetBio,
            LookupPrompt = (key, bio, variant, tokens) => content.LookupPrompt(key, bio, variant, tokens, returnNull: true),
            GetWorldSummaryText = content.GetWorldSummaryText,
            GetSamples = (_, bio) => DialogueSampleLoader.GetBioOnlySamples(bio),
            GetClient = () => LlmClientHost.Instance.Current
        };
        var engine = new DialogueEngine(services);
        Instance = engine;
        return engine;
    }

}
