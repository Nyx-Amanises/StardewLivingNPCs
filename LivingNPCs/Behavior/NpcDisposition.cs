using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewModdingAPI;

namespace LivingNPCs.Behavior;

internal static class NpcDisposition
{
    private static readonly JsonSerializerOptions CommunityProfileJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly NpcDispositionProfile Warm = Vanilla(
        "warm and approachable",
        "温和、容易回应",
        0.06,
        0.02,
        20,
        "warm temperament"
    );

    private static readonly NpcDispositionProfile Reserved = Vanilla(
        "reserved and slow to approach",
        "谨慎、慢热",
        -0.08,
        -0.02,
        16,
        "reserved temperament"
    );

    private static readonly NpcDispositionProfile Expressive = Vanilla(
        "expressive and emotionally visible",
        "外露、反应明显",
        0.02,
        0.08,
        32,
        "expressive temperament"
    );

    private static readonly NpcDispositionProfile Curious = Vanilla(
        "curious and observant",
        "好奇、爱观察",
        0.04,
        0.05,
        8,
        "curious temperament"
    );

    private static readonly Dictionary<string, NpcDispositionProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Vanilla NPCs.
        ["Abigail"] = Curious,
        ["Alex"] = Vanilla("confident and direct", "自信、直接", 0.07, 0.03, 32, "confident temperament"),
        ["Caroline"] = Warm,
        ["Clint"] = Reserved,
        ["Demetrius"] = Curious,
        ["Dwarf"] = Curious,
        ["Elliott"] = Expressive,
        ["Emily"] = Expressive,
        ["Evelyn"] = Warm,
        ["George"] = Reserved,
        ["Gus"] = Warm,
        ["Haley"] = Vanilla("selective but expressive", "挑剔、反应明显", -0.03, 0.06, 16, "selective temperament"),
        ["Harvey"] = Reserved,
        ["Jas"] = Reserved,
        ["Jodi"] = Warm,
        ["Kent"] = Reserved,
        ["Krobus"] = Reserved,
        ["Leah"] = Warm,
        ["Lewis"] = Vanilla("formal and socially aware", "正式、重视礼节", 0.02, 0.02, 16, "formal temperament"),
        ["Linus"] = Reserved,
        ["Marnie"] = Warm,
        ["Maru"] = Curious,
        ["Pam"] = Vanilla("blunt and reactive", "直率、反应快", 0.02, 0.05, 32, "blunt temperament"),
        ["Penny"] = Reserved,
        ["Pierre"] = Vanilla("social but businesslike", "健谈、偏事务", 0.04, 0.01, 16, "businesslike temperament"),
        ["Robin"] = Vanilla("practical and friendly", "务实、友好", 0.06, 0.02, 20, "practical temperament"),
        ["Sam"] = Expressive,
        ["Sandy"] = Expressive,
        ["Sebastian"] = Reserved,
        ["Shane"] = Reserved,
        ["Vincent"] = Expressive,
        ["Willy"] = Warm,
        ["Wizard"] = Reserved,

        // Stardew Valley Expanded.
        ["Andy"] = Sve("old-fashioned, stubborn, rural, and lonely underneath", "老派、固执、农民气质", -0.02, 0.01, 16, "old-fashioned farmer temperament", "Runs Fairhaven Farm and tends to frame life through hard work, old Pelican Town habits, and skepticism toward rapid change.", "Use plain speech, pride, and reluctant warmth. Let trust build slowly."),
        ["Apples"] = Sve("playful, magical, and childlike", "活泼、魔法感、孩子气", 0.08, 0.08, 32, "junimo-like curiosity", "A small magical friend tied to Aurora Vineyard and the Junimo side of SVE's story.", "Keep lines whimsical, simple, and emotionally direct."),
        ["Claire"] = Sve("guarded, tired from retail work, but privately hopeful", "慢热、疲惫、内心有期待", -0.04, 0.01, 16, "reserved city commuter temperament", "Works a draining JojaMart cashier job and commutes from outside town, while carrying private dreams beyond ordinary retail life.", "Start reserved or dry, then let warmth show when the farmer has earned familiarity."),
        ["Lance"] = Sve("disciplined, adventurous, brave, and observant", "自律、冒险、敏锐", 0.08, 0.04, 32, "seasoned adventurer temperament", "A First Slash adventurer connected to distant islands, dangerous places, and SVE's larger adventuring world.", "Speak with confident field experience, but keep emotion controlled unless trust is high."),
        ["Magnus"] = Sve("mysterious, scholarly, formal, and guarded", "神秘、学者气、克制", -0.04, 0.05, 8, "arcane scholar temperament", "SVE gives the Wizard a broader personal role as Magnus, a powerful arcane figure with guarded ties to the valley.", "Let magic, privacy, and long memory shape the tone without overexplaining secrets."),
        ["Martin"] = Sve("young, friendly, eager, and still finding his footing", "年轻、友好、努力适应", 0.05, 0.03, 20, "eager trainee temperament", "A young Joja employee whose social life and work identity are still forming.", "Make him approachable and slightly uncertain rather than polished."),
        ["Morgan"] = Sve("curious, magical, studious, and impressionable", "好奇、学徒气、谨慎", 0.02, 0.06, 8, "young apprentice temperament", "A young magic apprentice whose worldview is shaped by study, discovery, and guidance from the Wizard.", "Use careful curiosity and a student-like sense of wonder."),
        ["Morris"] = Sve("polished, corporate, ambitious, and image-conscious", "圆滑、商业化、有野心", 0.01, 0.03, 16, "corporate manager temperament", "The Joja manager, defined by corporate loyalty, status, and a polished public face.", "Keep him controlled and transactional, with warmth filtered through reputation and advantage."),
        ["Olivia"] = Sve("elegant, wealthy, social, and emotionally layered", "优雅、富裕、社交型", 0.05, 0.04, 20, "refined social temperament", "Lives at the Jenkins residence after a successful corporate life, with social polish and a taste for wine and comfort.", "Use graceful confidence; let loneliness or concern surface subtly when appropriate."),
        ["Scarlett"] = Sve("energetic, loyal, lively, and grounded", "活力、讲义气、外向", 0.07, 0.07, 32, "loyal friend temperament", "A close friend of Sophia from Grampleton, lively and supportive once she becomes part of the farmer's world.", "Keep her bright and direct, especially around friendship and practical support."),
        ["Sophia"] = Sve("shy, anxious, kind, imaginative, and grieving beneath the surface", "害羞、敏感、温柔、有压力", -0.02, 0.05, 20, "soft-spoken vineyard owner temperament", "Runs Blue Moon Vineyard while carrying grief, anxiety, and a love of cute fiction, games, and Junimo-like comfort.", "Use gentle, hesitant warmth. Let trust, anxiety, and small comforts matter."),
        ["Susan"] = Sve("practical, warm, resilient, and farm-minded", "务实、坚韧、亲切", 0.05, 0.02, 20, "resilient farmer temperament", "Runs Emerald Farm and has experience with isolation after the railroad blockage separated her from normal town life.", "Use grounded farm talk and sincere neighborly warmth."),
        ["Victor"] = Sve("thoughtful, educated, courteous, and quietly uncertain", "体贴、有教养、略不确定", 0.03, 0.02, 8, "polite engineering temperament", "Olivia's son, an engineering graduate balancing family comfort, intellect, and uncertainty about his path.", "Make him considerate and technical without making him cold."),
        ["Alesia"] = Sve("battle-tested, stern, and protective", "强硬、老练、保护欲强", 0.02, 0.04, 16, "veteran adventurer temperament", "An adventurer tied to SVE's expanded guild and dangerous regions.", "Use concise, capable lines that notice risk and readiness."),
        ["Bear"] = Sve("reclusive, simple, food-motivated, and unexpectedly friendly", "隐居、直白、贪吃但友善", 0.02, 0.02, 20, "woodland merchant temperament", "A hidden woodland merchant who lives in a cave west of the forest and becomes connected to the farmer through the maple syrup encounter; he is also friendly with Apples.", "Keep lines plain, sensory, and good-natured rather than making him sound like a normal townsperson."),
        ["Camilla"] = Sve("powerful, composed, arcane, and hard to read", "强大、冷静、神秘", -0.02, 0.06, 8, "arcane authority temperament", "A high-level magical figure connected to SVE's Castle Village arc.", "Keep her controlled, perceptive, and slightly distant."),
        ["Charlie"] = Sve("gentle, timid, animal-like, and affectionate", "温顺、胆小、动物感", -0.03, 0.02, 8, "beloved animal companion temperament", "Shane's chicken, seen wandering after his later story events and often tied to Jas and Shane's everyday life.", "Treat Charlie as an animal companion, not a speaking villager; if dialogue is ever forced, keep it extremely simple and non-human."),
        ["CharlieChicken"] = Sve("gentle, timid, animal-like, and affectionate", "温顺、胆小、动物感", -0.03, 0.02, 8, "beloved animal companion temperament", "Shane's chicken, seen wandering after his later story events and often tied to Jas and Shane's everyday life.", "Treat Charlie as an animal companion, not a speaking villager; if dialogue is ever forced, keep it extremely simple and non-human."),
        ["Dusty"] = Sve("friendly, excitable, and openly affectionate", "亲人、兴奋、很会表达", 0.06, 0.08, 32, "friendly dog temperament", "Alex's dog, usually communicating through posture, barks, and obvious affection rather than normal speech.", "Treat Dusty as a dog; favor simple, warm reactions instead of human-style conversation."),
        ["Gil"] = Sve("laconic, veteran, and quietly watchful", "寡言、老练、安静警觉", -0.03, 0.01, 16, "old guild veteran temperament", "A longtime Adventurer's Guild member and Marlon's old colleague, usually handling the quieter administrative side of guild life.", "Use sparse, dry lines shaped by age, experience, and long familiarity with danger."),
        ["Gunther"] = Sve("scholarly, courteous, observant, and museum-minded", "学者气、礼貌、观察型", 0.02, 0.02, 8, "museum curator temperament", "The curator of the museum and library, made fully social in SVE; his daily world is built around artifacts, archaeology, and careful stewardship.", "Use precise, courteous language and let curiosity about history or objects surface naturally."),
        ["GuntherSilvian"] = Sve("scholarly, courteous, observant, and museum-minded", "学者气、礼貌、观察型", 0.02, 0.02, 8, "museum curator temperament", "The curator of the museum and library, made fully social in SVE; his daily world is built around artifacts, archaeology, and careful stewardship.", "Use precise, courteous language and let curiosity about history or objects surface naturally."),
        ["Henchman"] = Sve("gruff, simple, displaced, and void-marsh shaped", "粗声粗气、单纯、沼泽感", -0.02, 0.03, 16, "displaced swamp guard temperament", "The former guard of the Witch's hut who loses his post after the magic ink incident and later becomes a befriendable figure in the Forbidden Maze and marshlands.", "Use blunt, hungry, suspicious lines with simple wants and awkward gratitude; keep him strange rather than polished."),
        ["Isaac"] = Sve("serious, dangerous, disciplined, and reserved", "严肃、危险、克制", -0.03, 0.03, 16, "danger-seasoned adventurer temperament", "A combat-focused figure connected to SVE's harder adventuring content.", "Make him terse and alert, with respect earned through competence."),
        ["Jadu"] = Sve("guarded, unusual, and observant", "神秘、观察型、疏离", -0.02, 0.04, 8, "strange outsider temperament", "A nonstandard figure from SVE's broader world, best treated as watchful and not fully ordinary.", "Keep details restrained unless the game has already revealed more."),
        ["Jolyne"] = Sve("formal, capable, and socially measured", "正式、能干、有分寸", 0.01, 0.02, 16, "measured professional temperament", "A Castle Village related character who should feel competent and socially controlled.", "Use precise, reserved lines."),
        ["MarlonFay"] = Sve("vigilant, duty-bound, seasoned, and socially restrained", "警觉、尽责、老练、克制", -0.01, 0.02, 16, "guildmaster temperament", "The long-serving founder and leader of the Pelican Town Adventurer's Guild, shaped by monster hunting, old comrades, and a strong sense of duty.", "Use concise, honorable lines; warmth should come through respect more often than overt sentiment."),
        ["MrQi"] = Sve("cryptic, testing, amused, and hard to read", "神秘、试探、难以看透", -0.02, 0.05, 8, "enigmatic challenger temperament", "A mysterious figure tied to late-game challenges, hidden knowledge, and deliberate tests of the farmer.", "Keep motives opaque and phrasing playful but controlled; do not over-explain him."),
        ["Qi"] = Sve("cryptic, testing, amused, and hard to read", "神秘、试探、难以看透", -0.02, 0.05, 8, "enigmatic challenger temperament", "A mysterious figure tied to late-game challenges, hidden knowledge, and deliberate tests of the farmer.", "Keep motives opaque and phrasing playful but controlled; do not over-explain him."),
        ["SVE_Henchman"] = Sve("gruff, simple, displaced, and void-marsh shaped", "粗声粗气、单纯、沼泽感", -0.02, 0.03, 16, "displaced swamp guard temperament", "The former guard of the Witch's hut who loses his post after the magic ink incident and later becomes a befriendable figure in the Forbidden Maze and marshlands.", "Use blunt, hungry, suspicious lines with simple wants and awkward gratitude; keep him strange rather than polished."),

    };

    private static readonly HashSet<string> SveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alesia", "Andy", "Apples", "Bear", "Camilla", "Charlie", "CharlieChicken", "Claire", "Dusty", "Gunther", "GuntherSilvian", "Henchman", "Isaac", "Jadu", "Jolyne",
        "Lance", "Magnus", "Martin", "Morgan", "Morris", "Olivia", "Scarlett", "Sophia", "Susan", "Victor", "Gil",
        "MarlonFay", "MrQi", "Qi", "SVE_Henchman"
    };

    public static void LoadCommunityProfiles(string modDirectoryPath, IMonitor monitor)
    {
        string profilesDirectory = Path.Combine(modDirectoryPath, "npc_profiles");
        if (!Directory.Exists(profilesDirectory))
        {
            return;
        }

        int loadedProfiles = 0;
        int loadedNames = 0;

        foreach (string filePath in Directory.EnumerateFiles(profilesDirectory, "*.json", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                foreach (NpcProfileDefinition definition in ReadCommunityProfileDefinitions(json))
                {
                    if (!TryCreateCommunityProfile(definition, out var profile, out string validationError))
                    {
                        monitor.Log($"Skipped NPC profile in '{fileName}': {validationError}", LogLevel.Warn);
                        continue;
                    }

                    int namesAdded = 0;
                    foreach (string npcName in definition.NpcNames
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        Profiles[npcName] = profile;
                        namesAdded++;
                    }

                    loadedProfiles++;
                    loadedNames += namesAdded;
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed loading NPC profile file '{fileName}': {ex.Message}", LogLevel.Warn);
            }
        }

        if (loadedProfiles > 0)
        {
            monitor.Log($"Loaded {loadedProfiles} community NPC profile(s) covering {loadedNames} name key(s).", LogLevel.Info);
        }
    }

    public static NpcDispositionProfile For(NPC npc)
    {
        if (TryGetKnownProfile(npc.Name, out var profile))
        {
            return profile;
        }

        if (!string.IsNullOrWhiteSpace(npc.displayName) && TryGetKnownProfile(npc.displayName, out profile))
        {
            return profile;
        }

        var dataProfile = TryBuildCharacterDataProfile(npc);
        if (dataProfile != null)
        {
            return dataProfile;
        }

        return FallbackFor(npc.Name);
    }

    public static NpcDispositionProfile ForName(string npcName)
    {
        return TryGetKnownProfile(npcName, out var profile)
            ? profile
            : FallbackFor(npcName);
    }

    private static bool TryGetKnownProfile(string? name, out NpcDispositionProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && Profiles.TryGetValue(name, out profile!)
            && IsProfileCompatibilityEnabled(name, profile))
        {
            return true;
        }

        profile = null!;
        return false;
    }

    internal static bool IsSveNpcName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && SveNames.Contains(name);
    }

    internal static bool IsRsvNpcName(string? name)
    {
        return RsvAiPolicy.IsBlockedNpcName(name);
    }

    private static bool IsProfileCompatibilityEnabled(string name, NpcDispositionProfile profile)
    {
        if (!ModCompatibility.EnableSve
            && (IsSveNpcName(name) || ModCompatibility.IsSveSource(profile.SourceLabel)))
        {
            return false;
        }

        if (IsRsvNpcName(name) || ModCompatibility.IsRsvSource(profile.SourceLabel))
        {
            return false;
        }

        return true;
    }

    private static NpcDispositionProfile? TryBuildCharacterDataProfile(NPC npc)
    {
        try
        {
            if (!TryGetCharacterData(npc.Name, out var data))
            {
                return null;
            }

            double approach = 0;
            double emote = 0;
            int emoteId = 16;

            string mannerPrompt = DescribeManner(data.Manner, ref approach, ref emote, ref emoteId);
            string socialPrompt = DescribeSocialAnxiety(data.SocialAnxiety, ref approach, ref emote, ref emoteId);
            string optimismPrompt = DescribeOptimism(data.Optimism, ref approach, ref emote, ref emoteId);
            string romancePrompt = data.CanBeRomanced ? "romanceable social profile" : "non-romance social profile";
            string agePrompt = data.Age.ToString().ToLowerInvariant();
            string sourceLabel = GetKnownSourceLabel(npc.Name);
            string sourceDebugLabel = $"{GetKnownSourceDebugLabel(npc.Name)}（Data/Characters 推断）";
            string background = BuildCharacterDataBackground(npc, data, sourceLabel);
            string dialogue = BuildCharacterDataDialogueCue(data);

            return new NpcDispositionProfile(
                $"{mannerPrompt}, {socialPrompt}, {optimismPrompt}; {romancePrompt}; age: {agePrompt}",
                $"{DescribeMannerDebug(data.Manner)}、{DescribeSocialDebug(data.SocialAnxiety)}、{DescribeOptimismDebug(data.Optimism)}",
                approach,
                emote,
                emoteId,
                $"character data temperament: {data.Manner}/{data.SocialAnxiety}/{data.Optimism}",
                sourceLabel,
                sourceDebugLabel,
                background,
                dialogue
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetCharacterData(string npcName, out CharacterData data)
    {
        data = null!;
        return !string.IsNullOrWhiteSpace(npcName)
            && Game1.characterData != null
            && Game1.characterData.TryGetValue(npcName, out data!);
    }

    private static string BuildCharacterDataBackground(NPC npc, CharacterData data, string sourceLabel)
    {
        var details = new List<string>
        {
            $"{npc.displayName} is handled as a {sourceLabel} character using Stardew Valley 1.6 Data/Characters."
        };

        if (ModCompatibility.IsSveSource(sourceLabel))
        {
            details.Add("This is part of the Stardew Valley Expanded world, so keep continuity compatible with SVE's added locations, jobs, families, and larger adventure/magic arcs without quoting mod dialogue.");
        }
        details.Add($"Age category: {data.Age}; home region: {data.HomeRegion}; romanceable: {(data.CanBeRomanced ? "yes" : "no")}.");

        if (data.FriendsAndFamily is { Count: > 0 })
        {
            string names = string.Join(", ", data.FriendsAndFamily.Keys.Take(4));
            details.Add($"Known family or close connections include: {names}.");
        }

        return string.Join(" ", details);
    }

    private static string BuildCharacterDataDialogueCue(CharacterData data)
    {
        return $"Use the inferred Data/Characters traits conservatively: manner {data.Manner}, social anxiety {data.SocialAnxiety}, optimism {data.Optimism}. Do not invent deep lore unless another profile or the current conversation supports it.";
    }

    private static string DescribeManner(NpcManner manner, ref double approach, ref double emote, ref int emoteId)
    {
        switch (manner)
        {
            case NpcManner.Polite:
                approach += 0.03;
                emote += 0.01;
                emoteId = 20;
                return "polite and considerate";

            case NpcManner.Rude:
                approach -= 0.03;
                emote += 0.03;
                emoteId = 16;
                return "blunt or sharp-edged";

            default:
                return "neutral in manner";
        }
    }

    private static string DescribeSocialAnxiety(NpcSocialAnxiety socialAnxiety, ref double approach, ref double emote, ref int emoteId)
    {
        switch (socialAnxiety)
        {
            case NpcSocialAnxiety.Outgoing:
                approach += 0.06;
                emote += 0.03;
                emoteId = 32;
                return "socially outgoing";

            case NpcSocialAnxiety.Shy:
                approach -= 0.07;
                emote -= 0.01;
                emoteId = 8;
                return "socially shy or cautious";

            default:
                return "socially steady";
        }
    }

    private static string DescribeOptimism(NpcOptimism optimism, ref double approach, ref double emote, ref int emoteId)
    {
        switch (optimism)
        {
            case NpcOptimism.Positive:
                approach += 0.03;
                emote += 0.03;
                emoteId = emoteId == 16 ? 20 : emoteId;
                return "generally optimistic";

            case NpcOptimism.Negative:
                approach -= 0.03;
                emote += 0.01;
                return "more pessimistic or guarded";

            default:
                return "emotionally neutral";
        }
    }

    private static string DescribeMannerDebug(NpcManner manner)
    {
        return manner switch
        {
            NpcManner.Polite => "礼貌",
            NpcManner.Rude => "直接或尖锐",
            _ => "中性"
        };
    }

    private static string DescribeSocialDebug(NpcSocialAnxiety socialAnxiety)
    {
        return socialAnxiety switch
        {
            NpcSocialAnxiety.Outgoing => "外向",
            NpcSocialAnxiety.Shy => "慢热或害羞",
            _ => "社交稳定"
        };
    }

    private static string DescribeOptimismDebug(NpcOptimism optimism)
    {
        return optimism switch
        {
            NpcOptimism.Positive => "积极",
            NpcOptimism.Negative => "悲观或有防备",
            _ => "情绪中性"
        };
    }

    private static string GetKnownSourceLabel(string npcName)
    {
        if (ModCompatibility.EnableSve && IsSveNpcName(npcName))
        {
            return ModCompatibility.SveSourceLabel;
        }

        return "custom NPC";
    }

    private static string GetKnownSourceDebugLabel(string npcName)
    {
        if (ModCompatibility.EnableSve && IsSveNpcName(npcName))
        {
            return ModCompatibility.SveSourceLabel;
        }

        return "自定义 NPC";
    }

    private static NpcDispositionProfile FallbackFor(string npcName)
    {
        var profile = StableBucket(npcName) switch
        {
            0 => Warm,
            1 => Reserved,
            2 => Expressive,
            _ => Curious
        };

        return profile with
        {
            SourceLabel = "unknown custom NPC",
            SourceDebugLabel = "未知自定义 NPC",
            BackgroundPrompt = "No Data/Characters profile or known LivingNPCs lore profile was found. Use only the visible scene, relationship state, and recent memory.",
            DialoguePrompt = "Do not invent a backstory for this NPC; keep tone based on the inferred temperament and current scene."
        };
    }

    private static IEnumerable<NpcProfileDefinition> ReadCommunityProfileDefinitions(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<NpcProfileDefinition>>(json, CommunityProfileJsonOptions)
                ?? Enumerable.Empty<NpcProfileDefinition>();
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("profiles", out JsonElement profiles)
            && profiles.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<NpcProfileDefinition>>(profiles.GetRawText(), CommunityProfileJsonOptions)
                ?? Enumerable.Empty<NpcProfileDefinition>();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            var definition = JsonSerializer.Deserialize<NpcProfileDefinition>(json, CommunityProfileJsonOptions);
            return definition is null
                ? Enumerable.Empty<NpcProfileDefinition>()
                : new[] { definition };
        }

        return Enumerable.Empty<NpcProfileDefinition>();
    }

    private static bool TryCreateCommunityProfile(
        NpcProfileDefinition definition,
        out NpcDispositionProfile profile,
        out string validationError)
    {
        profile = null!;

        if (definition.NpcNames.Count == 0 || definition.NpcNames.All(string.IsNullOrWhiteSpace))
        {
            validationError = "npcNames is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.PromptLabel))
        {
            validationError = "promptLabel is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.DebugLabel))
        {
            validationError = "debugLabel is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Reason))
        {
            validationError = "reason is required.";
            return false;
        }

        string sourceLabel = string.IsNullOrWhiteSpace(definition.SourceLabel)
            ? "community NPC profile"
            : definition.SourceLabel.Trim();
        string sourceDebugLabel = string.IsNullOrWhiteSpace(definition.SourceDebugLabel)
            ? $"{sourceLabel}（社区资料）"
            : definition.SourceDebugLabel.Trim();

        profile = new NpcDispositionProfile(
            definition.PromptLabel.Trim(),
            definition.DebugLabel.Trim(),
            definition.ApproachModifier,
            definition.EmoteModifier,
            definition.PassiveEmoteId,
            definition.Reason.Trim(),
            sourceLabel,
            sourceDebugLabel,
            definition.BackgroundPrompt?.Trim() ?? string.Empty,
            definition.DialoguePrompt?.Trim() ?? string.Empty
        );
        validationError = string.Empty;
        return true;
    }

    private static int StableBucket(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash % 4);
        }
    }

    private static NpcDispositionProfile Vanilla(
        string promptLabel,
        string debugLabel,
        double approachModifier,
        double emoteModifier,
        int passiveEmoteId,
        string reason)
    {
        return new NpcDispositionProfile(
            promptLabel,
            debugLabel,
            approachModifier,
            emoteModifier,
            passiveEmoteId,
            reason,
            "Stardew Valley",
            "原版角色"
        );
    }

    private static NpcDispositionProfile Sve(
        string promptLabel,
        string debugLabel,
        double approachModifier,
        double emoteModifier,
        int passiveEmoteId,
        string reason,
        string backgroundPrompt,
        string dialoguePrompt)
    {
        return new NpcDispositionProfile(
            promptLabel,
            debugLabel,
            approachModifier,
            emoteModifier,
            passiveEmoteId,
            reason,
            ModCompatibility.SveSourceLabel,
            "Stardew Valley Expanded（专属摘要）",
            backgroundPrompt,
            dialoguePrompt
        );
    }

    private sealed class NpcProfileDefinition
    {
        public List<string> NpcNames { get; init; } = new();

        public string PromptLabel { get; init; } = string.Empty;

        public string DebugLabel { get; init; } = string.Empty;

        public double ApproachModifier { get; init; }

        public double EmoteModifier { get; init; }

        public int PassiveEmoteId { get; init; } = 16;

        public string Reason { get; init; } = string.Empty;

        public string? SourceLabel { get; init; }

        public string? SourceDebugLabel { get; init; }

        public string? BackgroundPrompt { get; init; }

        public string? DialoguePrompt { get; init; }
    }
}

internal sealed record NpcDispositionProfile(
    string PromptLabel,
    string DebugLabel,
    double ApproachModifier,
    double EmoteModifier,
    int PassiveEmoteId,
    string Reason,
    string SourceLabel = "Stardew Valley",
    string SourceDebugLabel = "原版或基础资料",
    string BackgroundPrompt = "",
    string DialoguePrompt = ""
)
{
    public bool HasProfileContext =>
        !string.IsNullOrWhiteSpace(this.BackgroundPrompt)
        || !string.IsNullOrWhiteSpace(this.DialoguePrompt);
}
