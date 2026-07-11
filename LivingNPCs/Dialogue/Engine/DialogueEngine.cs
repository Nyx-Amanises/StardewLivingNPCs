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
/// → 解析/校验/后处理 → 收尾（历史 + 行为系统回传）。异步时序（单飞行/主线程交接）在
/// <see cref="GenerationScheduler"/>。
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
        var prepared = await this.PrepareAsync(request).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();

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

        watch.Stop();
        return this.FinalizeResult(prepared, response, failure, Math.Min(attempt, MaxAttempts), watch.ElapsedMilliseconds);
    }

    // ---- 流式（§4.13） ----

    public async Task StreamAsync(GenerationRequest request, IStreamSink sink, CancellationToken ct)
    {
        var prepared = await this.PrepareAsync(request).ConfigureAwait(false);

        LlmResponse? response = null;
        bool languageRetry = false;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
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

        var result = this.FinalizeResult(prepared, response, null, MaxAttempts, 0);
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

    private async Task<PreparedGeneration> PrepareAsync(GenerationRequest request)
    {
        var config = DialogueServices.Config ?? new DialogueConfig();
        var snapshot = request.Snapshot;
        var bio = this.services.GetBio(request.NpcName);
        string displayName = this.services.GetNpcDisplayName(request.NpcName);

        // 会话续接：与上次会话上下文按元素 GUID 去重合并（§4.3/§4.14）。
        List<ConversationTurn> conversation = request.Trigger == GenerationTrigger.Conversation
            ? this.services.History.MergeConversationContext(request.NpcName, request.Conversation)
            : new List<ConversationTurn>(request.Conversation);
        string lastPlayerLine = conversation.LastOrDefault(turn => turn.IsPlayerLine)?.Text ?? string.Empty;

        // 决策通道输入（搬运件消费的上下文视图）。
        var context = new DialogueContext
        {
            Accept = request.Trigger == GenerationTrigger.Gift ? request.GiftItemId : null,
            ChatHistory = conversation
                .Select(turn => new ConversationElement(turn.Text, turn.IsPlayerLine) { Id = turn.Id })
                .ToList(),
            LivingNpcExtraPrompt = request.BehaviorContext,
            Hearts = snapshot.FriendshipExists ? snapshot.Hearts : null,
            Location = snapshot.LocationName,
            TimeOfDay = $"{GameStateSnapshot.ClockText(snapshot.TimeOfDay)}",
            Weather = snapshot.WeatherFlags.ToList(),
            CurrentActivity = snapshot.CurrentActivity,
            NextScheduleLocation = snapshot.NextScheduleLocation,
            MinutesUntilNextSchedule = snapshot.MinutesUntilNextSchedule
        };

        var character = new Character(request.NpcName, TryGetNpc(request.NpcName))
        {
            Bio = ToCharacterBio(bio)
        };

        // 上下文路由（提示词构造之前，§4.5）。
        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(character, context).ConfigureAwait(false);

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
            ? this.SelectSamples(request.NpcName, bio, currentFacts, snapshot)
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
        }

        var variantGender = bio.ResolvedGender;
        var input = new PromptAssemblyInput
        {
            Request = request,
            Plan = plan,
            Bio = bio,
            NpcName = request.NpcName,
            NpcDisplayName = displayName,
            NpcGender = variantGender,
            Samples = samples,
            HistoryLines = historyLines,
            Conversation = conversation,
            JustSpoke = justSpoke,
            WorldSummaryFull = this.services.GetWorldSummaryText(config.UseOptimizedPrompts),
            WorldSummaryBrief = this.services.GetWorldSummaryText(true),
            PreoccupationTopic = this.PickPreoccupation(request.NpcName, bio, conversation.Count),
            GiftDisplayName = request.Trigger == GenerationTrigger.Gift
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
            ValidPortraits = new HashSet<string>(bio.ValidPortraits, StringComparer.OrdinalIgnoreCase),
            FixPunctuation = this.services.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        };
        prepared.Prompt = new PromptAssembler(input).Assemble();
        if (prepared.ValidPortraits.Count == 0)
        {
            prepared.ValidPortraits.UnionWith(new[] { "h", "s", "l", "a" });
        }

        return prepared;
    }

    private List<DialogueSample> SelectSamples(string npcName, NpcBio bio, DialogueKeyFacts current, GameStateSnapshot snapshot)
    {
        lock (this.gate)
        {
            if (!this.sampleLibraries.TryGetValue(npcName, out var library))
            {
                library = SampleSelector.BuildLibrary(this.services.GetSamples(npcName, bio));
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

    private GenerationResult FinalizeResult(
        PreparedGeneration prepared,
        LlmResponse? response,
        Exception? failure,
        int attempts,
        long elapsedMilliseconds)
    {
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
            return new GenerationResult
            {
                FormattedLine = EngineConstants.SkipPrefix + EngineConstants.FallbackLine,
                ParsedLines = new[] { EngineConstants.FallbackLine },
                AnalysisJson = ConversationAnalysis.Empty.ToJson(),
                EndConversation = true,
                DialogueKey = this.ResolveDialogueKey(request),
                Usage = response?.Usage ?? new TokenUsage()
            };
        }

        var parsed = this.ParseResponse(prepared, response.Text);
        var analysis = ConversationAnalysis.Parse(response.Text);

        // 行动决策补充（主响应解析成功且有台词后，§4.5）。
        List<string> lines = new() { parsed.DialogueLine };
        lines.AddRange(parsed.Options);
        LivingNpcActionDecisionDiagnostics? actionDiagnostics = null;
        try
        {
            var supplemented = LivingNpcActionDecisionPass
                .TrySupplementAsync(prepared.Character, prepared.Context, analysis, lines)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            analysis = supplemented.Analysis;
            actionDiagnostics = supplemented.Diagnostics;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.actionDecisionFailed", new { npc = request.NpcName, error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
        }

        // 元数据裁决：EndConversation 且行数 >1 → 只保留台词行（§4.10.8）。
        var options = parsed.Options;
        if (analysis.EndConversation && options.Count > 0)
        {
            options = new List<string>();
        }

        // 后处理（§4.11）。
        string dialogueLine = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(
            parsed.DialogueLine, prepared.LastPlayerLine);
        bool ended = ResponseFormatter.IsConversationEnded(analysis, prepared.LastPlayerLine, dialogueLine);

        // 主动送礼标记：成功生成才附加 [物品ID]（裁决 4）。
        if (!string.IsNullOrWhiteSpace(prepared.GivingGiftItemId))
        {
            dialogueLine += $"[{prepared.GivingGiftItemId}]";
        }

        bool allowTypedFallback = string.Equals(typedResponses, "With Generated", StringComparison.OrdinalIgnoreCase)
            && options.Count > 0;
        string formatted = ResponseFormatter.BuildFormattedLine(
            dialogueLine, options, typedResponses, ended, allowTypedFallback, dontSkipNext: false);
        formatted = ConversationTextPostProcessor.NormalizeImmediateNicknameReply(formatted, prepared.LastPlayerLine);

        var parsedLines = new List<string> { dialogueLine };
        parsedLines.AddRange(options);

        var result = new GenerationResult
        {
            FormattedLine = formatted,
            ParsedLines = parsedLines.ToArray(),
            AnalysisJson = analysis.ToJson(),
            EndConversation = ended,
            DialogueKey = this.ResolveDialogueKey(request),
            Usage = response.Usage
        };

        this.FinishTrigger(prepared, result, dialogueLine, analysis);
        this.ExportAttempt(prepared, response, analysis, parsedLines, attempts, "success", actionDiagnostics);
        this.LogDiagnostics(prepared, response, attempts, elapsedMilliseconds, parsedLines.Count);
        return result;
    }

    /// <summary>各触发的收尾动作（§4.12）。Scheduled/Marriage 不回传、不写会话历史。</summary>
    private void FinishTrigger(PreparedGeneration prepared, GenerationResult result, string dialogueLine, ConversationAnalysis analysis)
    {
        var request = prepared.Request;
        StardewTime now = this.services.Now();
        try
        {
            switch (request.Trigger)
            {
                case GenerationTrigger.Conversation:
                {
                    var npcTurn = new ConversationTurn(dialogueLine, false, Guid.NewGuid().ToString("N"));
                    this.services.History.AppendToConversationContext(request.NpcName, npcTurn);
                    var turns = this.services.History.PeekConversationContext(request.NpcName);
                    this.services.History.UpsertConversation(request.NpcName, turns, now);
                    RecordExchangeCallback?.Invoke(request.NpcName, prepared.LastPlayerLine, dialogueLine, result.AnalysisJson);
                    break;
                }

                case GenerationTrigger.Gift:
                {
                    string giftName = this.services.GetItemDisplayName(request.GiftItemId);
                    RecordExchangeCallback?.Invoke(
                        request.NpcName,
                        $"The farmer offered {giftName}.",
                        dialogueLine,
                        result.AnalysisJson);

                    string playerLine = Util.GetString(
                        "transcriptGiftPlayerLine",
                        new { npcName = prepared.NpcDisplayName, giftName },
                        returnNull: true) ?? $"The farmer gave {prepared.NpcDisplayName} {giftName}.";
                    var turns = new List<ConversationTurn>
                    {
                        new(playerLine, true, Guid.NewGuid().ToString("N")),
                        new(dialogueLine, false, Guid.NewGuid().ToString("N"))
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

    private static NPC? TryGetNpc(string npcName)
    {
        try
        {
            return Game1.getCharacterFromName(npcName);
        }
        catch
        {
            return null;
        }
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

    /// <summary>用真实服务装配引擎（游戏侧调用；测试自行构造 DialogueEngine）。</summary>
    public static DialogueEngine CreateDefault(DialogueContentService content)
    {
        var services = new DialogueEngineServices
        {
            GetBio = content.GetBio,
            LookupPrompt = (key, bio, variant, tokens) => content.LookupPrompt(key, bio, variant, tokens, returnNull: true),
            GetWorldSummaryText = content.GetWorldSummaryText,
            GetSamples = (npcName, bio) => DialogueSampleLoader.GetSamples(
                TryGetNpc(npcName),
                bio,
                SveContentRules.IsExpansionCompatibilityEnabled(
                    SveContentRules.NormalizeName(npcName),
                    DialogueServices.Config?.EnableSveCompatibility ?? true)),
            GetClient = () => LlmClientHost.Instance.Current
        };
        var engine = new DialogueEngine(services);
        Instance = engine;
        return engine;
    }

    private static NPC? TryGetNpc(string npcName)
    {
        try
        {
            return Game1.getCharacterFromName(npcName);
        }
        catch
        {
            return null;
        }
    }
}
