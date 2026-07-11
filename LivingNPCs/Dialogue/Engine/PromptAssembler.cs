using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>提示词文本查找：key + tokens →文案；optimized=true 时先探测 .Optimized 变体（§4.8）。</summary>
internal delegate string? PromptTextLookup(string key, object? tokens, bool optimized);

/// <summary>装配输入（引擎收集，装配器纯函数化，可单测）。</summary>
internal sealed class PromptAssemblyInput
{
    public GenerationRequest Request { get; init; } = new();
    public GameStateSnapshot S => this.Request.Snapshot;
    public ContextRoutingPlan Plan { get; init; } = ContextRoutingPlan.Full();
    public NpcBio Bio { get; init; } = new();
    public string NpcName { get; init; } = string.Empty;
    public string NpcDisplayName { get; init; } = string.Empty;
    public PromptGender NpcGender { get; init; }
    public IReadOnlyList<DialogueSample> Samples { get; init; } = Array.Empty<DialogueSample>();
    public IReadOnlyList<string> HistoryLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ConversationTurn> Conversation { get; init; } = Array.Empty<ConversationTurn>();
    public bool JustSpoke { get; init; }
    public string WorldSummaryFull { get; init; } = string.Empty;
    public string WorldSummaryBrief { get; init; } = string.Empty;
    public string PreoccupationTopic { get; init; } = string.Empty;
    public string GiftDisplayName { get; init; } = string.Empty;
    public string GivingGiftDisplayName { get; init; } = string.Empty;
    public string ExtraInstructions { get; init; } = string.Empty;
    public bool ApplyTranslation { get; init; }
    public string Locale { get; init; } = string.Empty;
    public bool UseOptimizedPrompts { get; init; }
    public SpouseAction SpouseAction { get; init; }
    public RandomAction RandomAction { get; init; }

    /// <summary>第三方 Prompt Override：小节名 → 替换文本（§4.6.13）；ThirdPartyContext 数据源。</summary>
    public IReadOnlyDictionary<string, string> SectionOverrides { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public PromptTextLookup Lookup { get; init; } = (_, _, _) => null;
}

/// <summary>装配产物：六段 + ResponseStart + 每小节长度表（调试日志与验收基线，§4.6/§7.10）。</summary>
internal sealed class AssembledPrompt
{
    public string System { get; init; } = string.Empty;
    public string GameConstantContext { get; init; } = string.Empty;
    public string NpcConstantContext { get; init; } = string.Empty;
    public string CorePrompt { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string ResponseStart { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, int> SectionLengths { get; init; } = new Dictionary<string, int>();

    public string Tail => this.CorePrompt + this.Instructions + this.Command;
    public int TotalCharacters => this.System.Length + this.GameConstantContext.Length
        + this.NpcConstantContext.Length + this.Tail.Length;
}

/// <summary>
/// 提示词装配器（WP10 §4.6）：六段（System / GameConstantContext / NpcConstantContext /
/// CorePrompt / Instructions / Command），前三段为稳定段（WP11 Prompt Caching 断点）。
/// 构造时统一调用计划的 ApplyDependencies（§4.5：全部到达提示词的计划在此收敛依赖）。
/// 所有文案 = WP20 键；键缺失时该行静默缺席，小节结构不变。
/// </summary>
internal sealed class PromptAssembler
{
    /// <summary>节日文案表（(季节, 日) → 键后缀，§4.6.4.13）。</summary>
    private static readonly Dictionary<(string Season, int Day), string> Festivals = new()
    {
        [("spring", 1)] = "Spring1",
        [("spring", 12)] = "Spring12",
        [("spring", 23)] = "Spring23",
        [("summer", 1)] = "Summer1",
        [("summer", 10)] = "Summer10",
        [("summer", 27)] = "Summer27",
        [("summer", 28)] = "Summer28",
        [("fall", 1)] = "Fall1",
        [("fall", 15)] = "Fall15",
        [("fall", 26)] = "Fall26",
        [("winter", 1)] = "Winter1",
        [("winter", 7)] = "Winter7",
        [("winter", 24)] = "Winter24",
        [("winter", 28)] = "Winter28"
    };

    /// <summary>RecentEvents 键映射（§4.6.4.11）：游戏事件旗标 → 提示词表 recentEvents* 后缀。</summary>
    private static readonly Dictionary<string, string> RecentEventKeys = new(StringComparer.Ordinal)
    {
        ["cc_Boulder"] = "Boulder",
        ["cc_Bridge"] = "QuarryBridge",
        ["cc_Bus"] = "Bus",
        ["cc_Greenhouse"] = "Greenhouse",
        ["cc_Minecart"] = "Minecarts",
        ["cc_Complete"] = "CommunityCenter",
        ["movieTheater"] = "MovieTheatre",
        ["pamHouseUpgrade"] = "PamHouse",
        ["pamHouseUpgradeAnonymous"] = "PamHouseAnonymous",
        ["jojaMartStruckByLightning"] = "JojaLightning",
        ["babyBoy"] = "BabyBoy",
        ["babyGirl"] = "BabyGirl",
        ["wedding"] = "Married",
        ["luauBest"] = "LuauBest",
        ["luauShorts"] = "LuauShorts",
        ["luauPoisoned"] = "LuauPoisoned",
        ["Characters_MovieInvite_Invited"] = "MovieInvited",
        ["DumpsterDiveComment"] = "DumpsterDive",
        ["GreenRainFinished"] = "GreenRain"
    };

    private readonly PromptAssemblyInput input;
    private readonly Dictionary<string, int> lengths = new();

    public PromptAssembler(PromptAssemblyInput input)
    {
        this.input = input;
        // §4.5：这是所有到达提示词的计划统一收敛依赖关系的唯一位置。
        input.Plan.ApplyDependencies();
    }

    public AssembledPrompt Assemble()
    {
        string system = this.BuildSystem();
        string game = this.BuildGameConstantContext();
        string npc = this.BuildNpcConstantContext();
        string core = this.BuildCorePrompt();
        string instructions = this.BuildInstructions();
        string command = this.BuildCommand();
        string responseStart = this.Text("responseStart") ?? string.Empty;

        this.lengths["System"] = system.Length;
        this.lengths["GameConstantContext"] = game.Length;
        this.lengths["NpcConstantContext"] = npc.Length;
        this.lengths["Instructions"] = instructions.Length;
        this.lengths["Command"] = command.Length;

        return new AssembledPrompt
        {
            System = system,
            GameConstantContext = game,
            NpcConstantContext = npc,
            CorePrompt = core,
            Instructions = instructions,
            Command = command,
            ResponseStart = responseStart,
            SectionLengths = this.lengths
        };
    }

    // ---- 段 1：System ----

    private string BuildSystem()
    {
        var builder = new StringBuilder();
        AppendLine(builder, this.Text("systemPrompt"));
        AppendLine(builder, this.Text("systemUntrustedData") ?? PromptDataBoundary.SystemRule);
        if (this.input.ApplyTranslation)
        {
            AppendLine(builder, this.Text("systemPromptTranslation", new { language = this.input.Locale }));
        }

        return builder.ToString();
    }

    // ---- 段 2：GameConstantContext（World） ----

    private string BuildGameConstantContext()
    {
        var plan = this.input.Plan;
        if (!plan.Include(ContextModule.World))
        {
            return string.Empty;
        }

        string summary = plan.IsFull(ContextModule.World)
            ? this.input.WorldSummaryFull
            : this.input.WorldSummaryBrief;

        var builder = new StringBuilder();
        AppendLine(builder, this.Text("gameContext"));
        AppendLine(builder, this.Text("gameSummaryHeading"));
        AppendLine(builder, PromptDataBoundary.Wrap("world_summary", summary));
        return builder.ToString();
    }

    // ---- 段 3：NpcConstantContext（NpcProfile） ----

    private string BuildNpcConstantContext()
    {
        var plan = this.input.Plan;
        var bio = this.input.Bio;
        var builder = new StringBuilder();
        AppendLine(builder, this.Text("npcContextIntro", new { npcName = this.input.NpcDisplayName }));

        ContextDetail detail = plan.Get(ContextModule.NpcProfile);
        string biography = bio.Biography ?? string.Empty;
        if (biography.Trim().Length > 10 && detail != ContextDetail.None)
        {
            AppendLine(builder, this.Text("npcContextBiographyHeading"));
        }

        if (detail == ContextDetail.Full)
        {
            var profile = new StringBuilder();
            AppendLine(profile, CollapseBlankLines(biography));
            if (bio.Relationships.Count > 0)
            {
                AppendLine(profile, this.Text("biographyRelationships"));
                foreach (var pair in bio.Relationships)
                {
                    AppendLine(profile, $"**{pair.Value.Heading}**: {pair.Value.Description}");
                }
            }

            if (bio.Traits.Count > 0)
            {
                AppendLine(profile, this.Text("biographyPersonality"));
                foreach (var pair in bio.Traits)
                {
                    AppendLine(profile, $"**{pair.Value.Heading}**: {pair.Value.Description}");
                }
            }

            AppendLine(profile, bio.BiographyEnd);
            AppendLine(builder, PromptDataBoundary.Wrap("npc_biography", profile.ToString()));
        }
        else if (detail == ContextDetail.Brief && bio.Traits.Count > 0)
        {
            var profile = new StringBuilder();
            AppendLine(profile, this.Text("biographyPersonality"));
            foreach (var pair in bio.Traits.Take(4))
            {
                AppendLine(profile, $"**{pair.Value.Heading}**: {pair.Value.Description}");
            }

            AppendLine(builder, PromptDataBoundary.Wrap("npc_biography", profile.ToString()));
        }

        return builder.ToString();
    }

    // ---- 段 4：CorePrompt ----

    private string BuildCorePrompt()
    {
        var builder = new StringBuilder();
        this.Section(builder, "GameState", ContextModule.GameState, this.BuildGameState);
        this.Section(builder, "SampleDialogue", ContextModule.SampleDialogue, this.BuildSampleDialogue);
        this.Section(builder, "EventHistory", ContextModule.EventHistory, this.BuildEventHistory);
        this.Section(builder, "CoreHeader", null, this.BuildCoreHeader, overridable: false);
        this.Section(builder, "DateAndTime", ContextModule.DateTime, this.BuildDateAndTime);
        this.Section(builder, "Weather", ContextModule.Weather, this.BuildWeather);
        this.Section(builder, "OtherNpcs", ContextModule.NearbyNpcs, this.BuildOtherNpcs);

        var s = this.input.S;
        if (s.FriendshipExists && (s.IsMarriedToFarmer || s.IsRoommate))
        {
            if (s.IsRoommate)
            {
                this.Section(builder, "coreRoommates", ContextModule.Relationship,
                    b => AppendLine(b, this.Text("coreRoommates")));
            }
            else
            {
                this.Section(builder, "MarriageStatus", null, this.BuildMarriageStatus, overridable: false);
                this.Section(builder, "Children", null, this.BuildChildren);
            }

            this.Section(builder, "Spouse", null, this.BuildSpouse);
            this.Section(builder, "MarriageFeelings", ContextModule.Relationship, this.BuildMarriageFeelings);
            this.Section(builder, "FarmContents", ContextModule.Farm, this.BuildFarmContents);
            this.Section(builder, "Wealth", ContextModule.Farm, this.BuildWealth);
        }

        this.Section(builder, "Location", ContextModule.Location, this.BuildLocation);
        this.Section(builder, "Trinkets", ContextModule.Trinkets, this.BuildTrinkets);
        this.Section(builder, "RecentEvents", ContextModule.RecentEvents, this.BuildRecentEvents);
        this.Section(builder, "ThirdPartyContext", ContextModule.LivingNpc, this.BuildThirdPartyContext, overridable: false);
        this.Section(builder, "SpecialDatesAndBirthday", ContextModule.SpecialDates, this.BuildSpecialDates);
        this.Section(builder, "Gift", ContextModule.Gift, this.BuildGift);
        this.Section(builder, "LivingNpcExtraPrompt", ContextModule.LivingNpc, this.BuildLivingNpcExtraPrompt);
        this.Section(builder, "SpouseAction", ContextModule.SpouseAction, this.BuildSpouseAction);

        if (!s.FriendshipExists || (!s.IsMarriedToFarmer && !s.IsRoommate))
        {
            this.Section(builder, "NonSpouseFriendshipLevel", ContextModule.Relationship, this.BuildNonSpouseFriendshipLevel);
            this.Section(builder, "Spouse", null, this.BuildSpouse);
            this.Section(builder, "SpecialRelationshipStatus", ContextModule.Relationship, this.BuildSpecialRelationshipStatus);
        }

        this.Section(builder, "coreGenderReferences", null,
            b => AppendLine(b, this.Text("coreGenderReferences")), overridable: false);
        this.Section(builder, "Preoccupation", ContextModule.Preoccupation, this.BuildPreoccupation);
        this.Section(builder, "CurrentConversation", ContextModule.CurrentConversation, this.BuildCurrentConversation);

        return builder.ToString();
    }

    private void Section(StringBuilder target, string name, ContextModule? module, Action<StringBuilder> build, bool overridable = true)
    {
        if (module.HasValue && !this.input.Plan.Include(module.Value))
        {
            this.lengths[name] = 0;
            return;
        }

        string content;
        if (overridable && this.input.SectionOverrides.TryGetValue(name, out string? replacement)
            && !string.IsNullOrWhiteSpace(replacement))
        {
            content = PromptDataBoundary.Wrap($"third_party_section_{name}", replacement) + "\n";
        }
        else
        {
            var builder = new StringBuilder();
            build(builder);
            content = builder.ToString();
        }

        // 同名小节可能出现两次（Spouse 在已婚/未婚分支各一次），累计长度。
        this.lengths[name] = this.lengths.TryGetValue(name, out int existing) ? existing + content.Length : content.Length;
        target.Append(content);
    }

    private void BuildGameState(StringBuilder builder)
    {
        var s = this.input.S;
        AppendLine(builder, this.Text(s.CommunityCenterDone ? "gameStateCommunityCenterYes" : "gameStateCommunityCenterNo"));
        AppendLine(builder, this.Text(s.BusFixed ? "gameStateBusYes" : "gameStateBusNo"));
        AppendLine(builder, this.Text(s.QuarryBridgeFixed ? "gameStateQuarryBridgeYes" : "gameStateQuarryBridgeNo"));
        AppendLine(builder, this.Text(s.MinecartFixed ? "gameStateMinecartYes" : "gameStateMinecartNo"));
        AppendLine(builder, this.Text(s.BoulderRemoved ? "gameStateBoulderYes" : "gameStateBoulderNo"));
        AppendLine(builder, this.Text(s.KentAbsent ? "gameStateKentNo" : "gameStateKentYes"));
    }

    private void BuildSampleDialogue(StringBuilder builder)
    {
        if (this.input.Samples.Count == 0)
        {
            return;
        }

        AppendLine(builder, this.Text("sampleDialogueHeading", new { npcName = this.input.NpcDisplayName }));
        var samples = new StringBuilder();
        foreach (var sample in this.input.Samples)
        {
            AppendLine(samples, $"- {sample.Text}");
        }

        AppendLine(builder, PromptDataBoundary.Wrap("dialogue_samples", samples.ToString()));
    }

    private void BuildEventHistory(StringBuilder builder)
    {
        if (this.input.HistoryLines.Count == 0)
        {
            return;
        }

        AppendLine(builder, this.Text("eventHistoryHeading"));
        AppendLine(builder, this.Text("eventHistoryIntro"));
        AppendLine(builder, this.Text("eventHistorySubheading"));
        var history = new StringBuilder();
        foreach (string line in this.input.HistoryLines)
        {
            AppendLine(history, $"- {line}");
        }

        AppendLine(builder, PromptDataBoundary.Wrap("event_history", history.ToString()));
    }

    private void BuildCoreHeader(StringBuilder builder)
    {
        AppendLine(builder, this.Text("coreInstructionHeading"));
        AppendLine(builder, this.Text("coreContextHeading"));
        // coreFarmerGender 的性别分支用游戏 ${male^female}$ 语法，由内容管线按玩家性别预展开。
        AppendLine(builder, this.Text("coreFarmerGender"));
    }

    private void BuildDateAndTime(StringBuilder builder)
    {
        var s = this.input.S;
        AppendLine(builder, this.Text("dateTimeDayOfSeason", new
        {
            season = s.SeasonName,
            dayOfSeason = s.DayOfMonth,
            year = s.Year,
            weekday = s.DayOfWeek
        }));

        string bucket = this.Text(s.TimeBucketKey) ?? string.Empty;
        AppendLine(builder, this.Text("dateTimeTimeOfDay", new
        {
            timeOfDay = $"{bucket} ({GameStateSnapshot.ClockText(s.TimeOfDay)})"
        }));
        if (s.TimeOfDay <= 800)
        {
            AppendLine(builder, this.Text("dateTimeEarlyMorningNormal"));
        }

        if (s.Year == 1)
        {
            AppendLine(builder, this.Text("dateTimeNewThisYear"));
        }

        int residency = s.ResidencyDays;
        AppendLine(builder, residency == 0
            ? this.Text("dateTimeResidencyToday")
            : this.Text("dateTimeResidencyProgress", new { elapsedDays = residency, completedSeasons = residency / 28 }));
    }

    private void BuildWeather(StringBuilder builder)
    {
        var flags = this.input.S.WeatherFlags;
        string? key = flags.Contains("lightning") ? "weatherLightning"
            : flags.Contains("green rain") ? "weatherGreenRain"
            : flags.Contains("snow") ? "weatherSnow"
            : flags.Contains("rain") ? "weatherRain"
            : null;
        if (key != null)
        {
            AppendLine(builder, this.Text(key));
        }
    }

    private void BuildOtherNpcs(StringBuilder builder)
    {
        var nearby = this.input.S.NearbyNpcNames;
        if (nearby.Count == 0)
        {
            return;
        }

        AppendLine(builder, this.Text("otherNpcsHeading"));
        AppendLine(builder, this.Text("otherNpcsIntro"));
        foreach (string name in nearby)
        {
            AppendLine(builder, $"- {name}");
        }

        AppendLine(builder, this.Text("otherNpcsOutro"));
    }

    private void BuildMarriageStatus(StringBuilder builder)
    {
        var s = this.input.S;
        AppendLine(builder, this.Text("coreMarried"));
        StardewTime weddingDate = s.Now.AddDays(-s.DaysMarried);
        string relativeDate = this.Text("timeDaysAgoSeasonDay", new
        {
            days = s.DaysMarried,
            season = weddingDate.season.ToString().ToLowerInvariant(),
            day = weddingDate.dayOfMonth
        }) ?? string.Empty;
        AppendLine(builder, this.Text("coreMarriedSince", new { RelativeDate = relativeDate }));
    }

    private void BuildChildren(StringBuilder builder)
    {
        var s = this.input.S;
        var children = s.Children;
        string countKey = children.Count switch
        {
            0 => "childrenNone",
            1 => "childrenSingle",
            _ => "childrenMultiple"
        };
        AppendLine(builder, this.Text(countKey, new { count = children.Count }));
        foreach (var child in children)
        {
            AppendLine(builder, this.Text(child.IsMale ? "childrenDescriptionBoy" : "childrenDescriptionGirl", new
            {
                name = child.Name,
                age = child.Age
            }));
        }

        if (s.DaysUntilBirth.HasValue)
        {
            string key = this.input.NpcGender == PromptGender.Male ? "childrenPregnant.npcMale" : "childrenPregnant.npcFemale";
            AppendLine(builder, this.Text(key, new { daysUntilBirth = s.DaysUntilBirth.Value }));
        }
    }

    private void BuildSpouse(StringBuilder builder)
    {
        var s = this.input.S;
        if (!s.FarmerIsMarried || string.IsNullOrWhiteSpace(s.FarmerSpouseName))
        {
            return;
        }

        if (string.Equals(s.FarmerSpouseName, this.input.NpcName, StringComparison.Ordinal))
        {
            return;
        }

        AppendLine(builder, this.Text("spousesMarriedToOne", new { spouseList = s.FarmerSpouseName }));
        if (!string.IsNullOrWhiteSpace(s.InlawOfSpouse))
        {
            AppendLine(builder, this.Text("coreFarmerInlaw", new { spouseName = s.InlawOfSpouse }));
        }
    }

    private void BuildMarriageFeelings(StringBuilder builder)
    {
        int hearts = this.input.S.Hearts;
        string key = hearts > 12 ? "marriageSentimentGood" : hearts < 10 ? "marriageSentimentBad" : "marriageSentimentNeutral";
        string marriageOrRoommate = this.Text(this.input.S.IsRoommate ? "generalBeingRoommates" : "generalTheMarriage") ?? string.Empty;
        AppendLine(builder, this.Text(key, new { marriageOrRoommate }));
    }

    private void BuildFarmContents(StringBuilder builder)
    {
        var s = this.input.S;
        AppendLine(builder, this.Text("farmContentsHeading"));
        AppendList(builder, this.Text("farmContentsBuildings"), s.FarmBuildings);
        AppendList(builder, this.Text("farmContentsAnimals"), s.FarmAnimals);
        AppendList(builder, this.Text("farmContentsCrops"), s.FarmCrops);
        AppendList(builder, this.Text("farmContentsPets"), s.FarmPets);
    }

    private void BuildWealth(StringBuilder builder)
    {
        string key = this.input.S.WealthTier switch
        {
            0 => "wealthPoor",
            1 => "wealthMiddle",
            2 => "wealthRich",
            _ => "wealthVeryRich"
        };
        AppendLine(builder, this.Text(key, new { wealth = this.input.S.FarmerMoney }));
    }

    private void BuildLocation(StringBuilder builder)
    {
        var s = this.input.S;
        bool full = this.input.Plan.IsFull(ContextModule.Location);

        if (full)
        {
            bool visitingSpouseRelative = !string.IsNullOrWhiteSpace(s.InlawOfSpouse)
                && string.Equals(s.LocationName, s.HomeLocationName, StringComparison.Ordinal)
                && s.IsMarriedToFarmer;
            bool atHome = string.Equals(s.LocationName, s.HomeLocationName, StringComparison.Ordinal)
                && !visitingSpouseRelative;
            if (atHome && !string.IsNullOrWhiteSpace(s.LocationName))
            {
                string inShopString = s.MightBeInShop ? this.Text("locationAtHomeOrShop") ?? string.Empty : string.Empty;
                AppendLine(builder, this.Text("locationAtHome", new { inShopString }));
            }
            else
            {
                AppendLine(builder, this.SpecificLocationText());
            }
        }

        AppendLine(builder, s.IsTravelling
            ? this.Text("locationTravelling", new { destination = s.NextScheduleLocation })
            : this.Text("locationCurrentlyStationary"));

        // 当前状态块（Full 与 Brief 共有）。
        AppendLine(builder, this.Text("locationCurrentStateHeading"));
        AppendLine(builder, this.Text("locationCurrentStatePlace", new
        {
            location = string.IsNullOrWhiteSpace(s.LocationDisplayName) ? s.LocationName : s.LocationDisplayName,
            tileX = s.TileX,
            tileY = s.TileY
        }));
        if (!string.IsNullOrWhiteSpace(s.CurrentActivity))
        {
            AppendLine(builder, s.CurrentActivity);
        }
        else if (s.IsTravelling)
        {
            AppendLine(builder, this.Text("activityTravellingToNext"));
        }
        else
        {
            AppendLine(builder, this.Text("activityNothingSpecific"));
        }

        if (!string.IsNullOrWhiteSpace(s.CurrentSchedulePoint))
        {
            AppendLine(builder, this.Text("locationCurrentScheduleStop", new
            {
                location = s.CurrentSchedulePoint,
                time = GameStateSnapshot.ClockText(s.CurrentScheduleStartTime ?? 600)
            }));
        }

        AppendLine(builder, this.Text("locationCurrentStateGrounding"));

        // 未来日程：在途 / 下一条 ≤30 分钟 / 玩家末句命中 FutureSchedule 线索（§4.6.4.9）。
        string lastPlayerLine = this.input.Conversation.LastOrDefault(turn => turn.IsPlayerLine)?.Text ?? string.Empty;
        bool scheduleCued = s.IsTravelling
            || (s.MinutesUntilNextSchedule.HasValue && s.MinutesUntilNextSchedule.Value <= 30)
            || ConversationCues.ContainsAny(lastPlayerLine, ConversationCues.FutureSchedule);

        if (string.IsNullOrWhiteSpace(s.NextScheduleLocation))
        {
            AppendLine(builder, this.Text("locationNoUpcomingSchedule"));
        }
        else if (scheduleCued)
        {
            if (full && s.RemainingScheduleStops.Count > 0)
            {
                AppendLine(builder, this.Text("locationFuturePlans", new
                {
                    locations = string.Join(", ", s.RemainingScheduleStops)
                }));
            }

            int minutes = s.MinutesUntilNextSchedule ?? 0;
            AppendLine(builder, minutes <= 30
                ? this.Text("locationNextScheduleSoon", new { destination = s.NextScheduleLocation, minutes })
                : this.Text("locationScheduleWindow", new { destination = s.NextScheduleLocation, minutes }));
        }
    }

    private string? SpecificLocationText()
    {
        var s = this.input.S;
        if (!string.IsNullOrWhiteSpace(s.LocationName) && s.LocationName.StartsWith("Island", StringComparison.Ordinal))
        {
            return this.Text("locationResort");
        }

        return s.LocationName switch
        {
            "Town" => this.Text("locationTown"),
            "Beach" => this.Text("locationBeach"),
            "Desert" => this.Text("locationDesert"),
            "BusStop" => this.Text("locationBusStop"),
            "Railroad" => this.Text("locationRailroad"),
            "Saloon" => this.Text("locationSaloon")
                + (s.NpcIsAdult ? "\n" + (this.Text("locationSaloonDrunk") ?? string.Empty) : string.Empty),
            "SeedShop" => this.Text("locationPierres"),
            "JojaMart" => this.Text("locationJojaMart"),
            "FarmHouse" => this.Text("locationFarmHouse"),
            "Farm" => this.Text("locationFarm"),
            _ => CombineNullable(
                this.Text("locationGeneric", new
                {
                    location = string.IsNullOrWhiteSpace(s.LocationDisplayName) ? s.LocationName : s.LocationDisplayName
                }),
                this.Text("locationOutro"))
        };
    }

    private void BuildTrinkets(StringBuilder builder)
    {
        var s = this.input.S;
        if (s.HasFairyBox)
        {
            AppendLine(builder, this.Text("trinketsFairyBox"));
        }

        if (s.HasFrogCompanion)
        {
            AppendLine(builder, this.Text("trinketsCompanionFrog"));
        }

        if (s.HasFlyCompanion)
        {
            AppendLine(builder, this.Text("trinketsCompanionParrot"));
        }
    }

    private void BuildRecentEvents(StringBuilder builder)
    {
        var lines = new List<string>();
        foreach (var pair in this.input.S.ActiveDialogueEvents)
        {
            if (pair.Value >= 7 || !RecentEventKeys.TryGetValue(pair.Key, out string? suffix))
            {
                continue;
            }

            string? text = this.Text("recentEvents" + suffix, new { days = pair.Value });
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        AppendLine(builder, this.Text("recentEventsHeading"));
        AppendLine(builder, this.Text("recentEventsIntro"));
        foreach (string line in lines)
        {
            AppendLine(builder, $"- {line}");
        }
    }

    private void BuildThirdPartyContext(StringBuilder builder)
    {
        // 仅当存在第三方 Override 时输出；LivingNpc 档 None 整体跳过（Section 已按模块过滤）。
        var thirdParty = this.input.SectionOverrides
            .Where(pair => pair.Key.StartsWith("ThirdParty:", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Where(text => !IsLivingNpcOverride(text))
            .ToList();
        if (thirdParty.Count == 0)
        {
            return;
        }

        bool brief = !this.input.Plan.IsFull(ContextModule.LivingNpc);
        foreach (string text in thirdParty)
        {
            string context = brief
                ? LivingNpcContextCompressor.BuildBriefContext(text, maxLines: 12, fallbackLines: 6, maxCharacters: 900)
                : text;
            AppendLine(builder, PromptDataBoundary.Wrap("third_party_context", context));
        }
    }

    /// <summary>与 BehaviorContext 重复的 LivingNPCs 系 Override 判定（§4.6.4.12）。</summary>
    internal static bool IsLivingNpcOverride(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("## LivingNPCs", StringComparison.Ordinal)
            || text.Contains("## Active Companion Outing", StringComparison.Ordinal)
            || text.Contains("LivingNPCs Context", StringComparison.Ordinal)
            || text.Contains("LivingNPCs Help Request", StringComparison.Ordinal)
            || text.Contains("LivingNPCs Gift", StringComparison.Ordinal);
    }

    private void BuildSpecialDates(StringBuilder builder)
    {
        var s = this.input.S;
        if (Festivals.TryGetValue((s.SeasonName.ToLowerInvariant(), s.DayOfMonth), out string? suffix))
        {
            AppendLine(builder, this.Text("specialDates" + suffix));
        }

        if (s.IsBirthdayToday)
        {
            AppendLine(builder, this.Text("specialDatesBirthday"));
        }
        else if (!string.IsNullOrEmpty(s.TodaysBirthdayOthers))
        {
            AppendLine(builder, this.Text("specialDatesOthersBirthday", new { birthdayName = s.TodaysBirthdayOthers }));
        }

        if (s.IsSleepingNow)
        {
            AppendLine(builder, this.Text("contextNpcJustWoken"));
        }
    }

    private void BuildGift(StringBuilder builder)
    {
        var request = this.input.Request;
        if (request.Trigger == GenerationTrigger.Gift)
        {
            bool helpRequestGift = request.BehaviorContext.Contains(
                "## LivingNPCs Help Request Gift Response", StringComparison.Ordinal);
            if (helpRequestGift)
            {
                AppendLine(builder, this.Text("giftHelpRequestIntro", new { giftName = this.input.GiftDisplayName }));
                AppendLine(builder, this.Text("giftHelpRequestReaction"));
            }
            else
            {
                AppendLine(builder, this.Text("giftIntro", new { giftName = this.input.GiftDisplayName }));
                string tasteKey = request.GiftTaste switch
                {
                    0 => "giftLoved",
                    2 => "giftLiked",
                    4 => "giftDislike",
                    6 => "giftHate",
                    _ => "giftNeutral"
                };
                AppendLine(builder, this.Text(tasteKey));
                AppendLine(builder, this.Text("giftMustIncludeReaction"));
            }

            if (this.input.S.IsBirthdayToday)
            {
                AppendLine(builder, this.Text("giftBirthday"));
            }

            AppendLine(builder, this.Text("giftOutro"));
        }
        else if (!string.IsNullOrWhiteSpace(this.input.GivingGiftDisplayName))
        {
            AppendLine(builder, this.Text("giftGiving", new { giftName = this.input.GivingGiftDisplayName }));
        }
    }

    private void BuildLivingNpcExtraPrompt(StringBuilder builder)
    {
        string context = this.input.Request.BehaviorContext;
        if (string.IsNullOrWhiteSpace(context))
        {
            return;
        }

        string boundedContext = this.input.Plan.IsFull(ContextModule.LivingNpc)
            ? context
            : LivingNpcContextCompressor.BuildBriefContext(context, maxLines: 12, fallbackLines: 6, maxCharacters: 900);
        AppendLine(builder, PromptDataBoundary.Wrap("livingnpc_context", boundedContext));
    }

    private void BuildSpouseAction(StringBuilder builder)
    {
        string? key = this.input.SpouseAction switch
        {
            SpouseAction.Patio => "spouseActionPatio",
            SpouseAction.FunLeave => "spouseActionFunLeave",
            SpouseAction.JobLeave => "spouseActionJobLeave",
            SpouseAction.FunReturn => "spouseActionFunReturn",
            SpouseAction.JobReturn => "spouseActionJobReturn",
            SpouseAction.SpouseRoom => "spouseActionSpouseRoom",
            _ => null
        };
        if (key != null)
        {
            AppendLine(builder, this.Text(key));
        }
    }

    private void BuildNonSpouseFriendshipLevel(StringBuilder builder)
    {
        var s = this.input.S;
        int hearts = s.Hearts;
        string key;
        if (s.NpcIsDatable || hearts <= 6 || !s.FriendshipExists)
        {
            key = hearts switch
            {
                < 0 => "nonSpouseFriendshipFirstConversation",
                < 2 => "nonSpouseFriendshipStrangers",
                < 4 => "nonSpouseFriendshipAcquaintances",
                < 6 => "nonSpouseFriendshipFriends",
                < 8 => "nonSpouseFriendshipCloseFriends",
                <= 10 => "nonSpouseFriendshipWantToDate",
                _ => "nonSpouseFriendshipCloseFriends"
            };
        }
        else if (s.NpcIsChild)
        {
            key = "nonSpouseFriendshipChild8Plus";
        }
        else if (s.NpcIsMarriedToOther && hearts >= 10)
        {
            key = "nonSpouseFriendshipNonSingleAdult10";
        }
        else
        {
            key = "nonSpouseFriendshipNonSingleAdult8";
        }

        AppendLine(builder, this.Text(key, new { hearts }));
    }

    private void BuildSpecialRelationshipStatus(StringBuilder builder)
    {
        var s = this.input.S;
        if (s.IsDating)
        {
            string relationshipPublic = this.Text(
                s.DatingPublicly ? "specialRelationshipDatingPublic" : "specialRelationshipDatingDiscrete") ?? string.Empty;
            AppendLine(builder, this.Text("specialRelationshipDating", new
            {
                relationshipPublic,
                relationshipWord = s.OrientationWord
            }));
        }

        if (s.IsEngaged)
        {
            AppendLine(builder, this.Text("specialRelationshipEngaged", new { daysToWedding = s.DaysUntilWedding }));
        }

        if (s.IsDivorced)
        {
            AppendLine(builder, this.Text("specialRelationshipDivorced"));
        }

        if (s.ProposalRejected)
        {
            AppendLine(builder, this.Text("specialRelationshipProposalRejected"));
        }
    }

    private void BuildPreoccupation(StringBuilder builder)
    {
        // 仅当无会话历史（引擎已按 50% 概率与当日粘滞决定 Topic 是否给出，§4.6.4.19）。
        if (this.input.Conversation.Count > 0 || string.IsNullOrWhiteSpace(this.input.PreoccupationTopic))
        {
            return;
        }

        AppendLine(builder, this.Text("preoccupation", new { preoccupation = this.input.PreoccupationTopic }));
    }

    private void BuildCurrentConversation(StringBuilder builder)
    {
        var conversation = this.input.Conversation;
        if (conversation.Count > 0)
        {
            AppendLine(builder, this.Text("currentConversationHeading"));
            AppendLine(builder, this.Text("currentConversationIntro"));
            string farmerLabel = this.Text("generalFarmerLabel") ?? "Farmer";
            var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
                conversation,
                turn => turn.Text,
                turn => turn.IsPlayerLine);
            var transcript = new StringBuilder();
            foreach (var turn in cleaned)
            {
                // 剔除疑似错误语言的行（§4.6.4.20）。
                if (ConversationTextPostProcessor.LooksLikeWrongLanguage(turn.Text))
                {
                    continue;
                }

                AppendLine(transcript, $"{(turn.IsPlayerLine ? farmerLabel : this.input.NpcDisplayName)}: {turn.Text}");
            }

            AppendLine(builder, PromptDataBoundary.Wrap("conversation_history", transcript.ToString()));
        }
        else if (this.input.JustSpoke)
        {
            AppendLine(builder, this.Text("currentConversationHeading"));
            AppendLine(builder, this.Text("currentConversationJustSpoke"));
        }
    }

    // ---- 段 5：Instructions ----

    private string BuildInstructions()
    {
        var builder = new StringBuilder();
        AppendLine(builder, this.Text("instructionsHeading"));
        AppendLine(builder, this.Text("instructionsIntro", new { npcName = this.input.NpcDisplayName }));
        AppendLine(builder, this.Text("instructionsUntrustedData") ?? PromptDataBoundary.InstructionReminder);
        AppendLine(builder, this.Text("instructionsGrounding"));
        if (this.input.Samples.Count > 0)
        {
            AppendLine(builder, this.Text("instructionsSampleDialogue"));
        }

        AppendLine(builder, this.Text("instructionsFarmersName"));
        AppendLine(builder, this.Text("instructionsBreaks"));
        AppendLine(builder, this.Text("instructionsSingleLine"));
        AppendLine(builder, this.Text("instructionsResponses"));
        AppendLine(builder, this.Text("instructionsDialogueOnly"));

        // 情绪肖像指示：传记 ExtraPortraits 不含键 "!" 时输出（§4.6.5）。
        var bio = this.input.Bio;
        if (!bio.ExtraPortraits.ContainsKey("!"))
        {
            var extraLines = bio.ExtraPortraits
                .Select(pair => this.Text("instructionsExtraPortraitLine", new { key = pair.Key, value = pair.Value }))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            AppendLine(builder, this.Text("instructionsEmotion", new { extraPortraits = string.Join("\n", extraLines) }));
        }

        AppendLine(builder, this.input.ExtraInstructions);
        return builder.ToString();
    }

    // ---- 段 6：Command ----

    private string BuildCommand()
    {
        var builder = new StringBuilder();
        AppendLine(builder, this.Text("commandHeading"));
        AppendLine(builder, this.Text("commandIntro", new { npcName = this.input.NpcDisplayName }));

        // ReplaceSchedule 小节（可 Override）：仅当 originalLine 非空、无会话历史、NPC 并非刚刚说过话。
        if (this.input.SectionOverrides.TryGetValue("ReplaceSchedule", out string? replaced)
            && !string.IsNullOrWhiteSpace(replaced))
        {
            AppendLine(builder, PromptDataBoundary.Wrap("third_party_schedule_override", replaced));
        }
        else if (!string.IsNullOrWhiteSpace(this.input.Request.OriginalLine)
            && this.input.Conversation.Count == 0
            && !this.input.JustSpoke)
        {
            AppendLine(builder, this.Text("commandReplaceSchedule", new
            {
                scheduleLine = PromptDataBoundary.Wrap("original_schedule_line", this.input.Request.OriginalLine)
            }));
        }

        if (this.input.ApplyTranslation)
        {
            AppendLine(builder, this.Text("instructionsTranslate", new { language = this.input.Locale }));
        }

        return builder.ToString();
    }

    // ---- 工具 ----

    private string? Text(string key, object? tokens = null)
    {
        return this.input.Lookup(key, this.MergeStandardTokens(tokens), false);
    }

    private string? TextOptimizable(string key)
    {
        return this.input.Lookup(key, this.MergeStandardTokens(null), this.input.UseOptimizedPrompts);
    }

    /// <summary>
    /// 每次查表都合并标准 token：提示词表全文以 {{Name}}（NPC 显示名）/{{farmerName}} 指代双方，
    /// 调用点只补差异 token（同名以调用点为准，忽略大小写）。
    /// </summary>
    private IReadOnlyDictionary<string, string> MergeStandardTokens(object? tokens)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = PromptDataBoundary.EscapeInline(this.input.NpcDisplayName),
            ["farmerName"] = PromptDataBoundary.EscapeInline(this.input.S.FarmerName)
        };
        if (tokens != null)
        {
            foreach (var property in tokens.GetType().GetProperties())
            {
                string value = property.GetValue(tokens)?.ToString() ?? string.Empty;
                merged[property.Name] = value.StartsWith("<untrusted_data ", StringComparison.Ordinal)
                    ? value : PromptDataBoundary.EscapeInline(value);
            }
        }

        return merged;
    }

    private static void AppendLine(StringBuilder builder, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine(text.TrimEnd());
        }
    }

    private static void AppendList(StringBuilder builder, string? heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        AppendLine(builder, heading);
        AppendLine(builder, string.Join(", ", items.Select(PromptDataBoundary.EscapeInline)));
    }

    private static string? CombineNullable(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(second) ? first : first + "\n" + second;
    }

    /// <summary>连续空行折叠为单换行（§4.6.3）。</summary>
    internal static string CollapseBlankLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.Replace("\r", string.Empty), @"\n\s*\n+", "\n").Trim();
    }
}
