using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using ValleyTalk;
using StardewValley;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Content;
using StardewModdingAPI.Events;
using System.Threading;
using StardewValley.GameData.Characters;

namespace ValleyTalk;

public class Character
{
    private BioData _bioData;
    private bool? _bioLoadedWithExpansionCompatibilityEnabled;
    private bool? _dialogueLoadedWithExpansionCompatibilityEnabled;
    private bool _dialogueLoading;

    private static readonly Dictionary<string,TimeSpan> filterTimes = new() { { "House", TimeSpan.Zero }, { "Action", TimeSpan.Zero }, { "Received Gift", TimeSpan.Zero }, { "Given Gift", TimeSpan.Zero }, { "Editorial", TimeSpan.Zero }, { "Gender", TimeSpan.Zero }, { "Question", TimeSpan.Zero } };
    private static readonly HashSet<string> SveCompatibilityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alesia", "Andy", "Apples", "Bear", "Camilla", "Charlie", "CharlieChicken", "Claire", "Dusty", "Gunther", "GuntherSilvian", "Isaac", "Jadu", "Jolyne",
        "Lance", "Magnus", "Martin", "Morgan", "Morris", "Olivia", "Scarlett", "Sophia", "Susan", "Victor", "Gil",
        "MarlonFay", "MorrisTod", "HankSVE", "MrQi", "Qi"
    };
    // SVE ships some characters under suffixed internal IDs (e.g. "GuntherSilvian"), but their curated
    // ValleyTalk bios are registered under the vanilla short name ("Gunther"). Map the internal ID to the
    // bio name so the curated bio actually applies instead of falling back to a generated Data/Characters one.
    private static readonly Dictionary<string,string> SveBioNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GuntherSilvian"] = "Gunther",
        ["MarlonFay"] = "Marlon",
        ["MorrisTod"] = "Morris",
        ["HankSVE"] = "Hank",
    };
    private StardewEventHistory eventHistory = new();
    private DialogueFile dialogueData;
    private Season? _sampleCacheSeason;
    private int? _sampleCacheDay;
    private int? _sampleCacheHeartLevel;
    private DialogueValue[] _sampleCache;
    private StardewTime _historyCutoff;
    private WorldDate _historyCutoffCacheDate;

    internal IEnumerable<Tuple<StardewTime,IHistory>> EventHistory => eventHistory.AllTypes;
    internal ConversationAnalysis LastConversationAnalysis { get; private set; } = ConversationAnalysis.Empty;

    public NPC StardewNpc { get; internal set; }
    public List<string> ValidPortraits { get; internal set; }
    private readonly Dictionary<string,string> HistoryEvents = new()
    {
        { "cc_Bus", Util.GetString("cc_Bus_Repaired") },
        { "cc_Boulder", Util.GetString("cc_Boulder_Removed") },
        { "cc_Bridge", Util.GetString("cc_Bridge") },
        { "cc_Complete", Util.GetString("cc_Complete") },
        { "cc_Greenhouse", Util.GetString("cc_Greenhouse") },
        { "cc_Minecart", Util.GetString("cc_Minecart") },
        { "wonIceFishing", Util.GetString("wonIceFishing") },
        { "wonGrange", Util.GetString("wonGrange") },
        { "wonEggHunt", Util.GetString("wonEggHunt") }
    };

    public Character(string name, NPC stardewNpc)
    {
        Name = name;
        BioFilePath = $"{VtConstants.BiosPath}/{ResolveBioName(Name)}";
        StardewNpc = stardewNpc;

        ModEntry.SHelper.Events.Content.AssetRequested += (sender, e) =>
        {
            if (e.Name.IsEquivalentTo(BioFilePath))
            {
                e.LoadFrom(() => new BioData(), AssetLoadPriority.High);
            }
        };
        ModEntry.SHelper.Events.Content.AssetsInvalidated += (object sender, AssetsInvalidatedEventArgs e) =>
        {
            if (e.NamesWithoutLocale.Any(an => an.IsEquivalentTo(BioFilePath)))
            {
                _bioData = null;
            }
        };

        LoadEventHistory();
    }

    private string RemoveDotSuffixes(string name)
    {
        var suffixCharacters = new char[] {'·', '•' ,'-' };
        var result = name.TrimEnd(suffixCharacters);
        return result;
    }

    // Resolve the bio asset base name: strip trailing separators, then remap known SVE internal IDs
    // to the short name their curated bio is registered under.
    private string ResolveBioName(string name)
    {
        string baseName = RemoveDotSuffixes(name);
        return SveBioNameAliases.TryGetValue(baseName, out string alias) ? alias : baseName;
    }

    private bool IsExpansionCompatibilityEnabledForCharacter()
    {
        string baseName = RemoveDotSuffixes(Name);
        if (SveCompatibilityNames.Contains(baseName) && ModEntry.Config?.EnableSveCompatibility == false)
        {
            return false;
        }

        if (RsvAiPolicy.IsBlockedNpcName(baseName))
        {
            return false;
        }

        return true;
    }

    private IEnumerable<string> GetLovedAndHatedGiftNames()
    {
        if (!Game1.NPCGiftTastes.TryGetValue(Name, out var npcGiftTastes))
        {
            return Array.Empty<string>();
        }

        string[] tasteLevels = npcGiftTastes.Split('/');
        if (tasteLevels.Length <= 7)
        {
            ModEntry.SMonitor.Log($"Gift taste data for {Name} has {tasteLevels.Length} fields; expected at least 8. Skipping gift-based prompt hints.", StardewModdingAPI.LogLevel.Debug);
            return Array.Empty<string>();
        }

        var lovedGifts = ArgUtility.SplitBySpace(tasteLevels[1]);
        var hatedGifts = ArgUtility.SplitBySpace(tasteLevels[7]);

        List<string> returnList = new();
        foreach (var gift in lovedGifts)
        {
            Game1.objectData.TryGetValue(gift, out var data);
            if (data != null)
            {
                returnList.Add(data.DisplayName);
            }
            
        }
        foreach (var gift in hatedGifts)
        {
            Game1.objectData.TryGetValue(gift, out var data);
            if (data != null)
            {
                returnList.Add(data.DisplayName);
            }
        }
        return returnList;
    }

    private void LoadDialogue()
    {
        if (_dialogueLoading)
        {
            return;
        }

        _dialogueLoading = true;
        dialogueData ??= new DialogueFile();
        Dictionary<string, string> canonDialogue = new();
        try
        {
            bool expansionCompatibilityEnabled = IsExpansionCompatibilityEnabledForCharacter();
            BioData bio = Bio;
            bool blockPatchedDialogue = ModEntry.BlockModdedContent || !expansionCompatibilityEnabled;
            if (blockPatchedDialogue && !bio.UsePatchedDialogue)
            {
                var manager = new ContentManager(Game1.content.ServiceProvider, Game1.content.RootDirectory);
                try
                {
                    string assetName = $"Characters\\Dialogue\\{Name}";
                    foreach(var langSuffix in ModEntry.LanguageFileSuffixes)
                    {
                        var path = $"{assetName}{langSuffix}";
                        var unmarriedDialogue = manager.Load<Dictionary<string, string>>(path);
                        if (unmarriedDialogue != null)
                        {
                            canonDialogue = unmarriedDialogue;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // If it fails, just continue
                }
                try
                {
                    string assetName = $"Characters\\Dialogue\\MarriageDialogue{Name}";
                    foreach(var langSuffix in ModEntry.LanguageFileSuffixes)
                    {
                        var path = $"{assetName}{langSuffix}";
                        var marriedDialogue = manager.Load<Dictionary<string, string>>(path);
                        if (marriedDialogue != null)
                        {
                            foreach (var dialogue in marriedDialogue)
                            {
                                canonDialogue.Add($"M_{dialogue.Key}", dialogue.Value);
                            }
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // If it fails, just continue
                }
            }
            else
            {
                canonDialogue = StardewNpc.Dialogue;
            }
            if (bio.Dialogue != null)
            {
                foreach (var dialogue in bio.Dialogue)
                {
                    canonDialogue[dialogue.Key] = dialogue.Value;
                }
                
            }
            var loadedDialogue = new DialogueFile();
            foreach (var dialogue in canonDialogue)
            {
                var context = new DialogueContext(dialogue.Key);
                var value = new DialogueValue(dialogue.Value);
                if (value is DialogueValue)
                {
                    loadedDialogue.Add("Base",context, value);
                }
            }
            dialogueData = loadedDialogue;
            _dialogueLoadedWithExpansionCompatibilityEnabled = expansionCompatibilityEnabled;
        }
        finally
        {
            _dialogueLoading = false;
        }
    }

    private void CheckBio()
    {
        bool expansionCompatibilityEnabled = IsExpansionCompatibilityEnabledForCharacter();
        if (_bioData != null
            && _bioLoadedWithExpansionCompatibilityEnabled == expansionCompatibilityEnabled
            && ( _bioData.Biography.Length > 0 || _bioData.Missing))
        {
            return;
        }

        BioData bioData;
        try
        {
            bioData = Game1.content.LoadLocalized<BioData>(BioFilePath);
        }
        catch (Exception)
        {
            var fallbackBio = BuildFallbackBioData();
            if (fallbackBio != null)
            {
                _bioData = fallbackBio;
                _bioLoadedWithExpansionCompatibilityEnabled = expansionCompatibilityEnabled;
                ValidPortraits = new List<string>() { "h", "s", "l", "a" };
                PossiblePreoccupations = new List<string>(_bioData.Preoccupations);
                PossiblePreoccupations.AddRange(GetLovedAndHatedGiftNames());
                ModEntry.SMonitor.Log($"Generated fallback bio for custom NPC {Name} from Data/Characters.", StardewModdingAPI.LogLevel.Debug);
                return;
            }

            _bioData = new BioData
            {
                Name = Name,
                Missing = true
            };
            _bioLoadedWithExpansionCompatibilityEnabled = expansionCompatibilityEnabled;
            ValidPortraits = new List<string>() { "h", "s", "l", "a" };
            PossiblePreoccupations = GetLovedAndHatedGiftNames().ToList();
            ModEntry.SMonitor.Log($"No bio file found for {Name}.", StardewModdingAPI.LogLevel.Warn);
            return;
        }

        bioData.Name = Name;
        if (!expansionCompatibilityEnabled)
        {
            bioData = BuildFallbackBioData() ?? new BioData
            {
                Name = Name,
                Missing = true
            };
            ModEntry.SMonitor.Log($"Expansion compatibility is disabled for {Name}; using lightweight Data/Characters bio for AI context.", StardewModdingAPI.LogLevel.Debug);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(bioData.Biography))
            {
                var fallbackBio = BuildFallbackBioData();
                if (fallbackBio != null)
                {
                    bioData = fallbackBio;
                    ModEntry.SMonitor.Log($"Generated fallback bio for custom NPC {Name} from Data/Characters because no ValleyTalk bio content was provided.", StardewModdingAPI.LogLevel.Debug);
                }
                else
                {
                    bioData.Missing = true;
                    ModEntry.SMonitor.Log($"No bio content found for {Name}.", StardewModdingAPI.LogLevel.Warn);
                }
            }
            else
            {
                bioData.Missing = false;
            }
        }

        _bioData = bioData;
        _bioLoadedWithExpansionCompatibilityEnabled = expansionCompatibilityEnabled;
        ValidPortraits = new List<string>() { "h", "s", "l", "a" };
        ValidPortraits.AddRange(_bioData.ExtraPortraits.Keys);
        PossiblePreoccupations = new List<string>(_bioData.Preoccupations);
        PossiblePreoccupations.AddRange(GetLovedAndHatedGiftNames());
    }

    private BioData BuildFallbackBioData()
    {
        if (RsvAiPolicy.IsBlockedNpc(StardewNpc) || RsvAiPolicy.IsBlockedNpcName(Name))
        {
            return null;
        }

        if (Game1.characterData == null || !Game1.characterData.TryGetValue(Name, out CharacterData data))
        {
            return null;
        }

        var bio = new BioData
        {
            Name = Name,
            Biography = BuildFallbackBiography(data),
            BiographyEnd = "This biography was generated locally from Stardew Valley 1.6 Data/Characters because no ValleyTalk bio file was found. Treat it as lightweight guidance, not full canon.",
            UsePatchedDialogue = false,
            Missing = false
        };

        bio.Traits["manner"] = new BioData.ListEntry
        {
            id = "manner",
            Heading = "Manner",
            Description = data.Manner.ToString()
        };
        bio.Traits["social"] = new BioData.ListEntry
        {
            id = "social",
            Heading = "Social style",
            Description = data.SocialAnxiety.ToString()
        };
        bio.Traits["outlook"] = new BioData.ListEntry
        {
            id = "outlook",
            Heading = "Outlook",
            Description = data.Optimism.ToString()
        };

        if (data.FriendsAndFamily != null)
        {
            foreach (var pair in data.FriendsAndFamily.Take(8))
            {
                bio.Relationships[pair.Key] = new BioData.ListEntry
                {
                    id = pair.Key,
                    Heading = pair.Key,
                    Description = string.IsNullOrWhiteSpace(pair.Value) ? "known connection" : pair.Value
                };
            }
        }

        bio.Preoccupations.AddRange(BuildFallbackPreoccupations(data));
        return bio;
    }

    private string BuildFallbackBiography(CharacterData data)
    {
        string displayName = StardewNpc?.displayName ?? Name;
        var details = new List<string>
        {
            $"{displayName} is a loaded Stardew Valley character without a dedicated ValleyTalk biography file.",
            $"Use the game's Data/Characters profile as a conservative baseline: age category {data.Age}, home region {data.HomeRegion}, manner {data.Manner}, social style {data.SocialAnxiety}, outlook {data.Optimism}.",
            data.CanBeRomanced
                ? $"{displayName} is marked romanceable, so closer relationship states may allow warmer and more personal dialogue."
                : $"{displayName} is not marked romanceable, so keep personal warmth appropriate to friendship and context."
        };

        if (data.FriendsAndFamily is { Count: > 0 })
        {
            details.Add($"Known family or close connections include {string.Join(", ", data.FriendsAndFamily.Keys.Take(6))}.");
        }

        details.Add("Content-pack dialogue samples are used only when the pack explicitly permits AI use; otherwise rely on this baseline profile and current game context.");

        return string.Join(" ", details);
    }

    private IEnumerable<string> BuildFallbackPreoccupations(CharacterData data)
    {
        foreach (string value in new[] { data.HomeRegion, data.Manner.ToString(), data.SocialAnxiety.ToString(), data.Optimism.ToString() })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }

        if (data.FriendsAndFamily != null)
        {
            foreach (string name in data.FriendsAndFamily.Keys.Take(4))
            {
                yield return name;
            }
        }
    }

    private void LoadEventHistory()
    {
        eventHistory = EventHistoryReader.Instance.GetEventHistory(Name);
    }

    internal IEnumerable<DialogueValue> SelectDialogueSample(DialogueContext context)
    {
        if (_sampleCache != null &&
            _sampleCacheSeason == context.Season &&
            _sampleCacheHeartLevel == context.Hearts &&
            _sampleCacheDay == context.DayOfSeason)
        {
            return _sampleCache;
        }
        _sampleCacheSeason = context.Season;
        _sampleCacheDay = context.DayOfSeason;
        _sampleCacheHeartLevel = context.Hearts;
        // Pick 20 most relevant dialogue entries
        var orderedDialogue = DialogueData
                    ?.AllEntries
                   .OrderBy(x => context.CompareTo(x.Key));
        var firstStep = orderedDialogue
                    ?.Where(x => x.Value != null);
        if (firstStep == null || !firstStep.Any())
        {
            _sampleCache = Array.Empty<DialogueValue>();
            return _sampleCache;
        }
        _sampleCache = firstStep
                    .SelectMany(x => x.Value.AllValues)
                    .Take(20).ToArray()
                    ?? Array.Empty<DialogueValue>();
        return _sampleCache;
    }
    
    public async Task<string[]> CreateBasicDialogue(DialogueContext context, Action<string> onToken = null)
    {
        this.LastConversationAnalysis = ConversationAnalysis.Empty;
        if (RsvAiPolicy.IsBlockedNpc(StardewNpc) || RsvAiPolicy.IsBlockedNpcName(Name))
        {
            return Array.Empty<string>();
        }

        var totalWatch = Stopwatch.StartNew();
        var promptInitWatch = Stopwatch.StartNew();
        string[] results = Array.Empty<string>();
        ContextRoutingPlan contextRoutingPlan = await ContextRoutingDecisionPass.BuildPlanAsync(this, context);
        var prompts = new Prompts(context, this, contextRoutingPlan);
        promptInitWatch.Stop();

        int maxRetryAttempts = 1;
        int timeoutSeconds = ModEntry.Config.QueryTimeout;
        int retryCount = 0;
        bool loggedTiming = false;
        long promptBuildMilliseconds = 0;
        long inferenceMilliseconds = 0;
        int promptCharacters = 0;
        int systemPromptCharacters = 0;
        int gameConstantContextCharacters = 0;
        int npcConstantContextCharacters = 0;
        int corePromptCharacters = 0;
        int instructionsCharacters = 0;
        int commandCharacters = 0;
        int responseStartCharacters = 0;
        string corePromptSectionCharacters = "none";
        int responseCharacters = 0;
        Exception lastException = null;
        LlmResponse result;

        string LocalizeTimingOutcome(string outcome)
        {
            if (!Util.IsChineseLocale())
            {
                return outcome;
            }

            return outcome switch
            {
                "success" => "成功",
                "empty response" => "空回复",
                "failed" => "失败",
                "not-run" => "未运行",
                "disabled-full" => "已禁用，使用完整上下文",
                "cached" => "使用缓存",
                "timeout-brief" => "超时，使用精简上下文",
                "failed-brief" => "失败，使用精简上下文",
                "response-failed-brief" => "回复失败，使用精简上下文",
                "low-confidence-full" => "低置信度，使用完整上下文",
                "parse-failed-brief" => "解析失败，使用精简上下文",
                _ => outcome
            };
        }

        string BuildPromptSectionCharacters()
        {
            return Util.IsChineseLocale()
                ? $"系统={systemPromptCharacters}, 游戏={gameConstantContextCharacters}, NPC={npcConstantContextCharacters}, 核心={corePromptCharacters}, 指令={instructionsCharacters}, 命令={commandCharacters}, 回复开头={responseStartCharacters}"
                : $"system={systemPromptCharacters}, game={gameConstantContextCharacters}, npc={npcConstantContextCharacters}, core={corePromptCharacters}, instructions={instructionsCharacters}, command={commandCharacters}, responseStart={responseStartCharacters}";
        }

        void LogTiming(string outcome, int dialogueLines = 0)
        {
            if (!ModEntry.Config.Debug)
            {
                return;
            }

            ModEntry.SMonitor.Log(
                Util.GetConsoleString(
                    "logValleyTalkTiming",
                    new
                    {
                        Name,
                        Outcome = LocalizeTimingOutcome(outcome),
                        TotalMs = totalWatch.ElapsedMilliseconds,
                        PromptInitMs = promptInitWatch.ElapsedMilliseconds,
                        PromptBuildMs = promptBuildMilliseconds,
                        ModelMs = inferenceMilliseconds,
                        RouteOutcome = LocalizeTimingOutcome(contextRoutingPlan.RoutingOutcome),
                        RouteMs = contextRoutingPlan.RoutingMilliseconds,
                        RouteTimeoutSeconds = contextRoutingPlan.RoutingTimeoutSeconds,
                        PromptChars = promptCharacters,
                        PromptSections = BuildPromptSectionCharacters(),
                        CoreSections = corePromptSectionCharacters,
                        ResponseChars = responseCharacters,
                        Attempts = retryCount,
                        Lines = dialogueLines
                    },
                    "[ValleyTalk timing] {{Name}}: {{Outcome}}; total={{TotalMs}}ms, promptInit={{PromptInitMs}}ms, promptBuild={{PromptBuildMs}}ms, model={{ModelMs}}ms, route={{RouteOutcome}}, routeMs={{RouteMs}}, routeTimeout={{RouteTimeoutSeconds}}s, promptChars={{PromptChars}}, promptSections={{{PromptSections}}}, coreSections={{{CoreSections}}}, responseChars={{ResponseChars}}, attempts={{Attempts}}, lines={{Lines}}."
                ),
                StardewModdingAPI.LogLevel.Info
            );
        }
        
        for (int attempt = 0; attempt <= maxRetryAttempts; attempt++)
        {
            retryCount = attempt + 1;

            try
            {
                // Keep retries short enough that the player doesn't get stuck in a thinking window.
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                // Execute with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                string[] resultsInternal;

                try
                {
                    var promptBuildWatch = Stopwatch.StartNew();
                    string systemPrompt = prompts.System;
                    string gameConstantContext = prompts.GameConstantContext;
                    string npcConstantContext = prompts.NpcConstantContext;
                    string corePrompt = prompts.CorePrompt;
                    string instructions = prompts.Instructions;
                    string command = prompts.Command;
                    if (attempt > 0)
                    {
                        command += ConversationTextPostProcessor.GetLanguageRetryInstruction();
                    }
                    string generatedPrompt = $"{corePrompt}{instructions}{command}";
                    string responseStart = prompts.ResponseStart;
                    promptBuildWatch.Stop();

                    promptBuildMilliseconds += promptBuildWatch.ElapsedMilliseconds;
                    systemPromptCharacters = systemPrompt.Length;
                    gameConstantContextCharacters = gameConstantContext.Length;
                    npcConstantContextCharacters = npcConstantContext.Length;
                    corePromptCharacters = corePrompt.Length;
                    corePromptSectionCharacters = string.Join(
                        ", ",
                        prompts.CorePromptSections
                            .Where(section => section.Value > 0)
                            .Select(section => $"{section.Key}={section.Value}")
                    );
                    if (string.IsNullOrWhiteSpace(corePromptSectionCharacters))
                    {
                        corePromptSectionCharacters = "none";
                    }
                    instructionsCharacters = instructions.Length;
                    commandCharacters = command.Length;
                    responseStartCharacters = responseStart.Length;
                    promptCharacters = systemPromptCharacters
                        + gameConstantContextCharacters
                        + npcConstantContextCharacters
                        + corePromptCharacters
                        + instructionsCharacters
                        + commandCharacters
                        + responseStartCharacters;

                    var inferenceWatch = Stopwatch.StartNew();
                    try
                    {
                        if (onToken != null && Llm.Instance is IStreamingLlm streamingLlm)
                        {
                            result = await streamingLlm.RunInferenceStreaming(
                                systemPrompt,
                                gameConstantContext,
                                npcConstantContext,
                                generatedPrompt,
                                onToken,
                                cts.Token,
                                responseStart,
                                allowRetry: false
                            );
                        }
                        else
                        {
                            var inferenceTask = Llm.Instance.RunInference(
                                systemPrompt,
                                gameConstantContext,
                                npcConstantContext,
                                generatedPrompt,
                                responseStart,
                                allowRetry: false
                            );
                            result = await inferenceTask.WaitAsync(cts.Token);
                        }
                    }
                    finally
                    {
                        inferenceWatch.Stop();
                        inferenceMilliseconds += inferenceWatch.ElapsedMilliseconds;
                    }
                    responseCharacters = result.Text?.Length ?? 0;
                    TokenUsage usage = result.Usage.HasAnyTokens
                        ? result.Usage
                        : TokenUsage.Estimate(
                            string.Concat(systemPrompt, gameConstantContext, npcConstantContext, generatedPrompt, responseStart),
                            result.Text ?? result.ErrorMessage ?? string.Empty
                        );
                    TokenUsageTracker.Instance.Record(
                        Name,
                        usage,
                        ModEntry.Config.Provider,
                        ModEntry.Config.ModelName,
                        result.IsSuccess ? "success" : "failed"
                    );
                    this.LastConversationAnalysis = ConversationAnalysis.Parse(result.Text);

                    LivingNpcActionDecisionDiagnostics actionDecisionDiagnostics = null;
                    if (result.IsSuccess)
                    {
                        // Apply relaxed validation if this is the second retry
                        resultsInternal = ProcessLines(result.Text, retryCount > 2).ToArray();
                        if (this.LastConversationAnalysis.EndConversation && resultsInternal.Length > 1)
                        {
                            resultsInternal = resultsInternal.Take(1).ToArray();
                        }
                        if (resultsInternal.Length > 0 && ConversationTextPostProcessor.LooksLikeWrongLanguage(resultsInternal[0]))
                        {
                            Log.Warning($"AI response for {Name} used the wrong language. Retrying with a stronger language instruction.");
                            resultsInternal = Array.Empty<string>();
                        }
                        if (resultsInternal.Length > 0)
                        {
                            var actionDecision = await LivingNpcActionDecisionPass.TrySupplementAsync(
                                this,
                                context,
                                this.LastConversationAnalysis,
                                resultsInternal);
                            this.LastConversationAnalysis = actionDecision.Analysis;
                            actionDecisionDiagnostics = actionDecision.Diagnostics;
                        }
                    }
                    else
                    {
                        resultsInternal = Array.Empty<string>();
                    }

                    AiResponseLogExporter.Append(
                        Name,
                        context,
                        result,
                        this.LastConversationAnalysis,
                        resultsInternal,
                        retryCount,
                        promptCharacters,
                        result.IsSuccess && resultsInternal.Length > 0 ? "parsed" : result.IsSuccess ? "unparseable" : "failed",
                        actionDecisionDiagnostics
                    );
                    PromptLogExporter.Append(
                        Name,
                        context,
                        systemPrompt,
                        gameConstantContext,
                        npcConstantContext,
                        corePrompt,
                        instructions,
                        command,
                        responseStart,
                        result,
                        resultsInternal,
                        retryCount,
                        result.IsSuccess && resultsInternal.Length > 0 ? "parsed" : result.IsSuccess ? "unparseable" : "failed"
                    );
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error generating AI response for {StardewNpc.displayName}");
                    throw;
                }

                if (resultsInternal.Length > 0)
                {
                    results = resultsInternal;
                    LogTiming("success", resultsInternal.Length);
                    loggedTiming = true;
                    break; // Success, exit retry loop
                }

                Log.Warning("No valid response generated from AI model.");
                if (result != null && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    Log.Warning($"API Error Message: {result.ErrorMessage}");
                }
                else if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                {
                    Log.Warning($"API Response: {result.Text}");
                }
            
                if (ModEntry.Config.Debug)
                {
                    // Open 'generation.log' and append values to it
                    Log.Debug($"Context:");
                    Log.Debug($"-------------------");
                    Log.Debug($"Name: {Name}");
                    Log.Debug($"Marriage: {context.Married}");
                    Log.Debug($"Birthday: {context.Birthday}");
                    Log.Debug($"Location: {context.Location}");
                    Log.Debug($"Weather: {string.Concat(context.Weather)}");
                    Log.Debug($"Time of Day: {context.TimeOfDay}");
                    Log.Debug($"Day of Season: {context.DayOfSeason}");
                    Log.Debug($"Gift: {context.Accept}");
                    Log.Debug($"Spouse Action: {context.SpouseAct}");
                    Log.Debug($"Random Action: {context.RandomAct}");
                    if (context.ScheduleLine != "")
                    {
                        Log.Debug($"Original Line: {context.ScheduleLine}");
                    }
                    Log.Debug($"-------------------");
                    Log.Debug($"System Prompt: {prompts.System}");
                    Log.Debug($"Game Constant Context: {prompts.GameConstantContext}");
                    Log.Debug($"NPC Constant Context: {prompts.NpcConstantContext}");
                    Log.Debug($"Core Prompt: {prompts.CorePrompt}");
                    Log.Debug($"Instructions: {prompts.Instructions}");
                    Log.Debug($"Command: {prompts.Command}");
                    Log.Debug($"Response Start: {prompts.ResponseStart}");
                    Log.Debug($"-------------------");
                    if (resultsInternal.Length > 0)
                    {
                        Log.Debug($"Results: {resultsInternal[0]}");
                        if (resultsInternal.Length > 1)
                        {
                            foreach (var resultLine in resultsInternal.Skip(1))
                            {
                                Log.Debug($"Response: {resultLine}");
                            }
                        }
                    }
                    else
                    {
                        Log.Debug("Results: <no parseable dialogue line>");
                    }
                    Log.Debug("--------------------------------------------------");
                }

            }
            catch (Exception ex)
            {
                lastException = ex;

                // If this is the last attempt, don't continue
                if (attempt == maxRetryAttempts)
                {
                    break;
                }
            }
        }

        if (!loggedTiming)
        {
            LogTiming(lastException == null ? "empty response" : "failed");
        }

        // Handle final result
        if (results.Length == 0)
        {
            if (lastException != null)
            {
                ModEntry.SMonitor.Log($"Error generating AI response for {Name}: {lastException}", StardewModdingAPI.LogLevel.Error);
            }
            else
            {
                ModEntry.SMonitor.Log($"AI response for {Name} could not be parsed. Using fallback dialogue.", StardewModdingAPI.LogLevel.Warn);
            }
            results = new string[] { "..." };
        }

        if (!string.IsNullOrWhiteSpace(prompts.GiveGift) && results.Length > 0)
        {
            results[0] += $"[{prompts.GiveGift}]";
        }

        return results;
    }

    public IEnumerable<string> ProcessLines(string resultString,bool relaxedValidation = false)
    {
        resultString = NormalizeRawModelOutput(ConversationTextPostProcessor.RemoveInvisibleCharacters(resultString));
        var resultLines = resultString.Split('\n').AsEnumerable();
        // Remove any line breaks
        resultLines = resultLines.Select(x => x.Replace("\n", "").Replace("\r", "").Trim());
        var cleanedLines = resultLines
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !IsLikelyMetadataLine(x))
            .ToList();
        // Find the first line that starts with '-' and remove any lines before it
        resultLines = NormalizeModelOutputLines(cleanedLines);
        var dialogueLine = resultLines.FirstOrDefault();
        if (dialogueLine == null || !dialogueLine.StartsWith("-"))
        {
            //Log.Debug("Invalid layout detected in AI response.  Returning the full response.");
            return Array.Empty<string>();
        }
        dialogueLine = CommonCleanup(dialogueLine);
        dialogueLine = DialogueLineCleanup(dialogueLine, relaxedValidation);
        if (string.IsNullOrWhiteSpace(dialogueLine))
        {
            //Log.Debug("Empty dialogue line detected in AI response.  Returning nothing.");
            return Array.Empty<string>();
        }
        var responseLines = resultLines
            .Skip(1)
            .Select(NormalizePotentialResponseLine)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (responseLines.Any())
        {
            responseLines = responseLines.Select(x => CommonCleanup(x)).ToList();
            responseLines = responseLines.Select(x => ResponseLineCleanup(x)).ToList();
            responseLines = responseLines.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
        resultLines = new List<string>(){dialogueLine}.Concat(responseLines);
        return resultLines;
    }

    private IEnumerable<string> NormalizeModelOutputLines(List<string> cleanedLines)
    {
        var firstDialogueIndex = cleanedLines.FindIndex(x => x.StartsWith("-"));
        if (firstDialogueIndex >= 0)
        {
            return cleanedLines.Skip(firstDialogueIndex);
        }

        var firstResponseIndex = cleanedLines.FindIndex(x => x.StartsWith("%"));
        var dialogueCandidates = firstResponseIndex >= 0
            ? cleanedLines.Take(firstResponseIndex).ToList()
            : cleanedLines;

        var dialogueCandidate = dialogueCandidates
            .LastOrDefault(x => !IsLikelyResponsePreamble(x));

        if (string.IsNullOrWhiteSpace(dialogueCandidate) || dialogueCandidate.StartsWith("%"))
        {
            return Array.Empty<string>();
        }

        if (ModEntry.Config.Debug)
        {
            Log.Warning($"Accepted AI response for {Name} even though it was missing the required '-' prefix.");
        }

        var normalized = new List<string> { $"- {dialogueCandidate.TrimStart('-', ' ')}" };
        if (firstResponseIndex >= 0)
        {
            normalized.AddRange(cleanedLines.Skip(firstResponseIndex));
        }
        else
        {
            normalized.AddRange(cleanedLines.Skip(cleanedLines.IndexOf(dialogueCandidate) + 1).Where(x => x.StartsWith("%")));
        }
        return normalized;
    }

    private bool IsLikelyResponsePreamble(string line)
    {
        return line.StartsWith("Here is", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Sure", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("以下", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("当然", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLikelyMetadataLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.Trim().TrimStart('{', ',', '"');
        return trimmed.StartsWith("rapportDelta", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("endConversation", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ambientFollowUp", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("emotionImpact", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("actions", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("behaviorInfluences", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("helpRequests", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("helpRequestUpdates", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("conflicts", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("memories", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizePotentialResponseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        string trimmed = line.Trim();
        if (trimmed.StartsWith("%", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("!", StringComparison.Ordinal)
            || trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("}", StringComparison.Ordinal)
            || IsLikelyMetadataLine(trimmed))
        {
            return string.Empty;
        }

        string candidate = System.Text.RegularExpressions.Regex.Replace(
            trimmed,
            @"^\s*(?:玩家(?:回应|回复|选项)?|农夫(?:回应|回复|选项)?|回应|回复|选项|response|reply|option)\s*\d*\s*[:：\-]\s*",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        candidate = System.Text.RegularExpressions.Regex.Replace(
            candidate,
            @"^\s*(?:\(?\d{1,2}\)?[\.、:：\)]|[-*•])\s+",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        candidate = candidate.Trim();
        if (!LooksLikePlayerResponseCandidate(candidate))
        {
            return string.Empty;
        }

        return $"% {candidate}";
    }

    private bool LooksLikePlayerResponseCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 160)
        {
            return false;
        }

        if (text.Contains("#$", StringComparison.Ordinal)
            || text.Contains("!LIVINGNPCS_META", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("-", StringComparison.Ordinal)
            || text.StartsWith("###", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private string CommonCleanup(string line)
    {
        line = ConversationTextPostProcessor.RemoveInvisibleCharacters(line);
        line = StreamingDialoguePreview.StripHiddenAndResponseTail(line);
        // Remove any leading punctuation and trailing quotation marks
        line = line.Trim().TrimStart('-', ' ', '"', '“', '%');
        line = line.TrimEnd('"', '”');
        // If the string starts or ends with #$b# ot #$e# remove it.
        line = line.StartsWith("#$b#") ? line[4..] : line;
        line = line.EndsWith("#$b#") ? line[..^4] : line;
        line = line.StartsWith("#$e#") ? line[4..] : line;
        line = line.EndsWith("#$e#") ? line[..^4] : line;
        // Remove any quotation marks
        line = line.Replace("\"", "");
        line = line.Replace("“", "").Replace("”", "");
        return line;
    }

    private string DialogueLineCleanup(string line,bool relaxedValidation = false)
    {
        line = StreamingDialoguePreview.StripHiddenAndResponseTail(line);
        line = ConversationTextPostProcessor.NormalizeStardewDialogueCommands(line);
        line = line.Replace("#$c .5#","");
        line = line.Replace("@@","@");
        // If the string contains any emotion indicators ($0, $s, $l, $a or $h) with a # before them, remove the #
        foreach (var indicator in ValidPortraits)
        {
            line = line.Replace($"#${indicator}", $"${indicator}");
        }

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '$')
            {
                if (i + 1 < line.Length)
                {
                    var nextChar = line[i + 1];
                    if (nextChar == 'e' || nextChar == 'c' || nextChar == 'b')
                    {
                        i++; // Skip the next character
                    }
                    else
                    {
                        // Collect the string up to the next # or the end of the line
                        var end = line.IndexOf('#', i);
                        if (end == -1)
                        {
                            end = line.Length;
                        }
                        var remainder = line.Substring(i+1, end - i - 1);
                        if (!ValidPortraits.Contains(remainder))
                        {
                            line = line.Remove(i, 1 + remainder.Length);
                            i--; // Adjust index after removal
                        }
                    }
                }
                else
                {
                    line = line.Remove(i, 1);
                    i--; // Adjust index after removal
                }
            }
        }
        
        line = line.Trim();
        var elements = line.Split('#');
        if (elements.Any(x => x.Length > 200 && !relaxedValidation))
        {
            // Iterate through the elements, building a new list that splits any element longer than 200 characters into multiple elements at a full stop
            List<string> newElements = new();
            foreach (var element in elements)
            {
                if (element.Length <= 200)
                {
                    newElements.Add(element);
                }
                else
                {
                    // Check if the element ends with a portrait indicator
                    string remainder;
                    string indicator;
                    if (element.Length > 2 && element[^2] == '$' && ValidPortraits.Contains(element[^1].ToString()))
                    {
                        // Split the element into the main text and the indicator
                        remainder = element[..^2];
                        indicator = element[^2..];
                    }
                    else
                    {
                        indicator = "";
                        remainder = element;
                    }
                    while (remainder.Length > 200 - indicator.Length)
                    {
                        var elementStart = remainder.Substring(0, 200 - indicator.Length);
                        var lastPeriod = elementStart.LastIndexOfAny(new char[] { '.', '!', '?' });
                        if (lastPeriod != -1)
                        {
                            newElements.Add(remainder.Substring(0, lastPeriod + 1) + indicator);
                            remainder = remainder.Substring(lastPeriod + 1).Trim();
                        }
                        else
                        {
                            // If there is no full stop, just add the first 200 characters and continue
                            newElements.Add(remainder.Substring(0, 200 - indicator.Length) + indicator);
                            remainder = string.Empty;
                        }
                    }
                    if (remainder.Length > 0)
                    {
                        newElements.Add(remainder + indicator);
                    }
                }
            }

            if (newElements.Any(x => x.Length > 200 && !relaxedValidation))
            {
                //Log.Debug("Excessively long element detected in AI response.  Returning nothing.");
                return string.Empty;
            }
            elements = newElements.ToArray();
        }
        if (ModEntry.FixPunctuation)
        {
            // For each element, check if the last character before a $ (if any) is a punctuation mark and add a period if not
            for (int i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var dollarIndex = element.IndexOf('$');
                var upToDollar = dollarIndex == -1 ? element : element[..dollarIndex];
                upToDollar = upToDollar.Trim();
                if (upToDollar.Length > 0 && !upToDollar.EndsWith(".") && !upToDollar.EndsWith("!") && !upToDollar.EndsWith("?"))
                {
                    elements[i] = upToDollar + "." + ((element.Length > upToDollar.Length && dollarIndex > 0 ) ? element[dollarIndex..] : "");
                }
            }
            line = string.Join("#", elements);
        }
        return line;
    }

    private string NormalizeRawModelOutput(string resultString)
    {
        if (string.IsNullOrWhiteSpace(resultString))
        {
            return string.Empty;
        }

        string normalized = resultString.Replace("\r", string.Empty);
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"\s*!+LIVINGNPCS_META",
            "\n!LIVINGNPCS_META",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"\s+(%{1,2}\s*)",
            "\n% ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return normalized;
    }

    private string ResponseLineCleanup(string line)
    {
        // Remove any hashes
        line = line.Replace("#", "");
        // If the string contains any commands preceded by a $, remove them
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '$')
            {
                if (i + 1 < line.Length)
                {
                    line = line.Remove(i, 2);
                }
                else
                {
                    line = line.Remove(i, 1);
                }
            }
        }
        if (line.Contains('@'))
        {
            var farmerName = Game1.player.Name;
            line = line.Replace("@", farmerName);
        }
        line = line.Trim();
        // If the line doesn't end with a sentence end punctuation, add a period
        if (ModEntry.FixPunctuation && !line.EndsWith(".") && !line.EndsWith("!") && !line.EndsWith("?"))
        {
            line += ".";
        }
        if (line.Length > 90)
        {
            //Log.Debug("Long line detected in AI response.  Returning nothing.");
            return string.Empty;
        }
        return line;
    }

    internal void AddDialogue(IEnumerable<StardewValley.DialogueLine> dialogues, int year, StardewValley.Season season, int dayOfMonth, int timeOfDay)
    {
        var time = new StardewTime(year,season, dayOfMonth, timeOfDay);
        
        AddHistory(new DialogueHistory(dialogues),time);
    }

    internal void AddHistory(IHistory theEvent, StardewTime time)
    {
        eventHistory.Add(time,theEvent);
        EventHistoryReader.Instance.UpdateEventHistory(Name, eventHistory);
    }

    internal bool MatchLastDialogue(List<StardewValley.DialogueLine> dialogues)
    {
        // Find the last dialogues in the event history
        if (!eventHistory.Any())
        {
            return false;
        }
        var tail = eventHistory.Last().Item2;
        if (tail is DialogueHistory)
        {
            if (((DialogueHistory)tail).Dialogues.Select(x => x.Text).SequenceEqual(dialogues.Select(x => x.Text)))
            {
                return true;
            }
        }
        // Check if the last dialogues match the given dialogues
        return false;
    }

    internal void AddEventDialogue(List<StardewValley.DialogueLine> filteredDialogues, IEnumerable<NPC> actors, string festivalName, int year, StardewValley.Season season, int dayOfMonth, int timeOfDay)
    {
        var time = new StardewTime(year,season, dayOfMonth, timeOfDay);
        var newHistory = new DialogueEventHistory(actors,filteredDialogues,festivalName);
        AddHistory(newHistory,time);
        foreach(var listener in actors)
        {
            var listenerObject = DialogueBuilder.Instance.GetCharacter(listener);
            var thirdPartyHistory = new ThirdPartyHistory(this, filteredDialogues, festivalName);
            listenerObject.AddHistory(thirdPartyHistory, time);
        }
    }

    internal void AddOverheardDialogue(NPC speaker, List<StardewValley.DialogueLine> filteredDialogues, int year, StardewValley.Season season, int dayOfMonth, int timeOfDay)
    {
        var time = new StardewTime(year,season, dayOfMonth, timeOfDay);
        var newHistory = new DialogueEventOverheard(speaker.Name,filteredDialogues);
        eventHistory.RemoveOverheardOverlapping(speaker.Name, filteredDialogues);
        AddHistory(newHistory,time);
    }

    internal void AddConversation(List<ConversationElement> chatHistory, int year, StardewValley.Season season, int dayOfMonth, int timeOfDay)
    {
        var time = new StardewTime(year,season, dayOfMonth, timeOfDay);
        var newHistory = new ConversationHistory(chatHistory);
        // Remove any items in the dialogue history that duplicate this conversation
        eventHistory.RemoveDialogueOverlapping(chatHistory);
        AddHistory(newHistory,time);
    }

    internal IEnumerable<Tuple<StardewTime, IHistory>> EventHistorySample()
    {

        var allPreviousActivities = Game1.getPlayerOrEventFarmer().previousActiveDialogueEvents.First();
        var previousActivites = allPreviousActivities.Where(x => HistoryEvents.ContainsKey(x.Key) && (x.Value < 112 || x.Value % 112 == 0)).ToList();

        var fullHistory = EventHistory.Concat(previousActivites.Select(x => MakeActivityHistory(x)));
        if (!fullHistory.Any())
        {
            return Array.Empty<Tuple<StardewTime, IHistory>>();
        }
        if (Game1.Date != _historyCutoffCacheDate)
        {
            _historyCutoff = fullHistory.OrderBy(x => x.Item1).TakeLast(20).FirstOrDefault()?.Item1;
            _historyCutoffCacheDate = Game1.Date;
        }
        return fullHistory.Where(x => x.Item1.After(_historyCutoff)).OrderBy(x => x.Item1);
    }

    private Tuple<StardewTime, IHistory> MakeActivityHistory(KeyValuePair<string, int> x)
    {
        var timeNow = new StardewTime(Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);
        var targetDate = timeNow.AddDays(-x.Value);
        return new(targetDate, new ActivityHistory(x.Key));
    }

    internal bool SpokeJustNow()
    {
        if (!eventHistory.Any())
        {
            return false;
        }
        var lastEvent = eventHistory.Last();
        if (lastEvent.Item2 is DialogueHistory || lastEvent.Item2 is ConversationHistory || lastEvent.Item2 is DialogueEventHistory)
        {
            return lastEvent.Item1.IsJustNow();
        }
        return false;
    }

    internal void ClearConversationHistory()
    {
        eventHistory.ClearConversationHistory();
        EventHistoryReader.Instance.UpdateEventHistory(Name, eventHistory);
    }

    public string Name { get; }
    public string DialogueFilePath { get; }
    public string BioFilePath { get; }
    public DialogueFile DialogueData 
    { 
        get 
        {
            if (dialogueData == null)
            {
                LoadDialogue();
            }
            else if (!_dialogueLoading && _dialogueLoadedWithExpansionCompatibilityEnabled != IsExpansionCompatibilityEnabledForCharacter())
            {
                _sampleCacheSeason = null;
                _sampleCacheDay = null;
                _sampleCacheHeartLevel = null;
                _sampleCache = null;
                LoadDialogue();
            }
            return dialogueData;  
        }
        private set => dialogueData = value; 
    }
    public ConcurrentBag<Tuple<DialogueContext,DialogueValue>> CreatedDialogue { get; private set; } = new ();
    internal BioData Bio
    {
        get
        { 
            CheckBio(); 
            return _bioData; 
        }
    }

    public List<string> PossiblePreoccupations { get; internal set;}
    public string Preoccupation { get; internal set; }
    public WorldDate PreoccupationDate { get; internal set; }
}
