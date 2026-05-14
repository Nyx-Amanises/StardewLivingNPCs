using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Behavior;

internal static class NpcDisposition
{
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
        ["Camilla"] = Sve("powerful, composed, arcane, and hard to read", "强大、冷静、神秘", -0.02, 0.06, 8, "arcane authority temperament", "A high-level magical figure connected to SVE's Castle Village arc.", "Keep her controlled, perceptive, and slightly distant."),
        ["Isaac"] = Sve("serious, dangerous, disciplined, and reserved", "严肃、危险、克制", -0.03, 0.03, 16, "danger-seasoned adventurer temperament", "A combat-focused figure connected to SVE's harder adventuring content.", "Make him terse and alert, with respect earned through competence."),
        ["Jadu"] = Sve("guarded, unusual, and observant", "神秘、观察型、疏离", -0.02, 0.04, 8, "strange outsider temperament", "A nonstandard figure from SVE's broader world, best treated as watchful and not fully ordinary.", "Keep details restrained unless the game has already revealed more."),
        ["Jolyne"] = Sve("formal, capable, and socially measured", "正式、能干、有分寸", 0.01, 0.02, 16, "measured professional temperament", "A Castle Village related character who should feel competent and socially controlled.", "Use precise, reserved lines."),

        // Ridgeside Village core profiles. Other RSV NPCs still get inferred from Data/Characters.
        ["Lenny"] = Rsv("warm, civic-minded, direct, and responsible", "亲切、有责任感、村务型", 0.06, 0.02, 20, "community leader temperament", "A central Ridgeside community figure and Lewis's sister, often framed around village work and responsibility.", "Let civic duty, familiarity, and practical care shape her tone."),
        ["Richard"] = Rsv("steady, traditional, older, and family-minded", "沉稳、传统、长辈感", 0.01, 0.01, 16, "elder hotel-family temperament", "Lives around the Log Cabin Hotel and is tied closely to Ysabelle as her grandfather.", "Use older, grounded speech and measured warmth."),
        ["Ysabelle"] = Rsv("outgoing, blunt, positive, and stylish", "外向、直白、积极", 0.06, 0.05, 32, "bold hotel resident temperament", "Richard's granddaughter at the Log Cabin Hotel, socially forward and not especially shy.", "Let her be lively and direct without making every line sugary."),
        ["Kenneth"] = Rsv("technical, quiet, sincere, and thoughtful", "技术宅、安静、真诚", 0.01, 0.02, 8, "local electrician temperament", "Ridgeside's electrician, often associated with cable car systems, technical work, and careful problem solving.", "Use practical technical observations and understated kindness."),
        ["Shiro"] = Rsv("gentle, wounded, protective, and reserved", "温和、受伤、保护型", -0.04, 0.02, 16, "wounded protector temperament", "An older brother figure with a difficult past and health limits, protective of Yuuma and careful with trust.", "Keep him soft-spoken, considerate, and slow to open up."),
        ["Yuuma"] = Rsv("young, earnest, attached to family, and hopeful", "年轻、认真、依赖家人", 0.04, 0.04, 20, "younger sibling temperament", "Shiro's younger brother, shaped by family care and a need for stability.", "Use youth, sincerity, and cautious optimism."),
        ["Naomi"] = Rsv("serious, burdened, maternal, and restrained", "严肃、有负担、母亲感", -0.02, 0.02, 16, "burdened parent temperament", "Connected to Shiro and Yuuma's family story, with responsibility and distance weighing on her.", "Let concern and regret show subtly rather than melodramatically."),
        ["Flor"] = Rsv("thoughtful, gentle, educated, and emotionally perceptive", "温柔、细腻、有学识", 0.03, 0.03, 20, "perceptive student temperament", "A romanceable Ridgeside character whose tone should lean reflective and caring.", "Use empathetic, observant lines and avoid making her randomly bubbly."),
        ["Ian"] = Rsv("outdoorsy, hardworking, modest, and practical", "户外、勤快、朴实", 0.04, 0.02, 20, "practical mountain worker temperament", "A Ridgeside local shaped by everyday work, outdoor life, and a down-to-earth routine.", "Use grounded, modest warmth."),
        ["June"] = Rsv("artistic, charming, dramatic, and guarded", "艺术气、迷人、有防备", 0.02, 0.06, 32, "performer temperament", "A Ridgeside character with a more artistic, expressive presence and hidden emotional layers.", "Let charm and performance sit over more guarded feelings."),
        ["Jio"] = Rsv("disciplined, intense, loyal, and wary", "自律、强烈、警觉", 0.01, 0.04, 16, "trained fighter temperament", "A Ridgeside character tied to conflict, training, and loyalty.", "Keep him alert and controlled, with warmth earned gradually."),
        ["Maddie"] = Rsv("bright, energetic, young, and emotionally open", "开朗、年轻、直接", 0.07, 0.06, 32, "youthful social temperament", "A youthful Ridgeside local whose lines should feel lively and open.", "Use energetic friendliness, but keep reactions grounded in the current relationship."),
        ["Sean"] = Rsv("playful, casual, restless, and fun-seeking", "随性、爱玩、不太安分", 0.06, 0.06, 32, "play-first temperament", "A recent Ridgeside resident known more for play and social energy than settled work.", "Use relaxed humor and casual momentum."),
        ["Philip"] = Rsv("studious, polite, ambitious, and a little anxious", "好学、礼貌、有压力", 0.02, 0.02, 8, "student temperament", "A young Ridgeside character who should sound educated and still under pressure to find his place.", "Use politeness, thoughtfulness, and restrained nerves."),
        ["Jeric"] = Rsv("cheerful, sporty, friendly, and straightforward", "阳光、运动型、友好", 0.07, 0.04, 32, "active friendly temperament", "A sociable Ridgeside local with a bright, active energy.", "Use simple direct warmth and physical energy."),
        ["Blair"] = Rsv("friendly, creative, expressive, and sociable", "友好、创意、外向", 0.06, 0.06, 32, "creative social temperament", "A Ridgeside local whose tone can lean stylish, bright, and socially aware.", "Use lively social confidence."),
        ["Alissa"] = Rsv("confident, social, polished, and image-aware", "自信、社交、在意形象", 0.04, 0.05, 32, "polished social temperament", "A Ridgeside villager with a more confident, social presence.", "Let confidence show, but scale warmth with relationship."),
        ["Corine"] = Rsv("motherly, practical, warm, and busy", "母亲感、务实、忙碌", 0.06, 0.02, 20, "busy family temperament", "A family-centered Ridgeside resident whose daily life should feel practical and caring.", "Use warm practicality and household awareness."),
        ["Daia"] = Rsv("poised, reserved, and quietly perceptive", "克制、优雅、观察型", -0.01, 0.03, 16, "poised villager temperament", "A Ridgeside local best treated as composed and observant unless closer context says otherwise.", "Use measured speech and subtle reactions."),
        ["Irene"] = Rsv("professional, composed, and caring", "专业、冷静、关怀", 0.02, 0.02, 16, "care professional temperament", "A Ridgeside resident whose role should feel competent and quietly supportive.", "Use calm, professional warmth."),
        ["Keahi"] = Rsv("active, bright, practical, and community-oriented", "活跃、开朗、实干", 0.06, 0.04, 32, "energetic local temperament", "A Ridgeside local with a warm, practical presence.", "Use open friendliness and everyday village awareness."),
        ["Kiarra"] = Rsv("confident, resilient, expressive, and strong-willed", "自信、坚韧、有主见", 0.04, 0.05, 32, "strong-willed temperament", "A Ridgeside villager whose tone can carry pride and resilience.", "Use direct confidence while respecting relationship pacing."),
        ["Malaya"] = Rsv("gentle, curious, thoughtful, and careful", "温和、好奇、谨慎", 0.02, 0.03, 8, "thoughtful local temperament", "A Ridgeside character who should feel attentive and emotionally careful.", "Use soft curiosity and restrained openness."),
        ["Paula"] = Rsv("capable, busy, blunt, and caring underneath", "能干、忙碌、嘴硬心软", 0.02, 0.03, 16, "busy caretaker temperament", "A Ridgeside local whose warmth is best shown through practical concern.", "Use brisk lines with care underneath."),
        ["Zayne"] = Rsv("guarded, intense, sharp, and slow to trust", "有防备、强烈、锋利", -0.05, 0.04, 16, "guarded outsider temperament", "A Ridgeside character who should feel wary and emotionally defended until trust develops.", "Keep early lines clipped or cool, then soften with relationship."),
    };

    private static readonly HashSet<string> SveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alesia", "Andy", "Apples", "Bear", "Camilla", "Charlie", "Claire", "Dusty", "Gunther", "Isaac", "Jadu", "Jolyne",
        "Lance", "Magnus", "Martin", "Morgan", "Morris", "Olivia", "Scarlett", "Sophia", "Susan", "Victor"
    };

    private static readonly HashSet<string> RsvNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acorn", "Aguar", "Alissa", "Althea", "Anton", "Ariah", "Belinda", "Bert", "Blair", "Bliss", "Bryle", "Carmen",
        "Corine", "Daia", "Ezekiel", "Faye", "Flor", "Freddie", "Helen", "Ian", "Irene", "Jeric", "Jio", "June", "Keahi",
        "Kenneth", "Kiarra", "Kimpoi", "Kiwi", "Lenny", "Lola", "Lorenzo", "Lorraine", "Louie", "Maddie", "Maive",
        "Malaya", "Nadaline", "Naomi", "Olga", "Paula", "Philip", "Pika", "Pipo", "Raeriyala", "RelicSpirit", "Richard",
        "Sari", "Sean", "Shanice", "Shiro", "Sonny", "Torts", "TreehouseGirl", "Trinnie", "Undreya", "Ysabelle", "Yuuma",
        "Zachary", "Zayne"
    };

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

    private static bool TryGetKnownProfile(string? name, out NpcDispositionProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(name) && Profiles.TryGetValue(name, out profile!))
        {
            return true;
        }

        profile = null!;
        return false;
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
            string sourceDebugLabel = GetKnownSourceDebugLabel(npc.Name);
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
        if (SveNames.Contains(npcName))
        {
            return "Stardew Valley Expanded";
        }

        if (RsvNames.Contains(npcName))
        {
            return "Ridgeside Village";
        }

        return "custom NPC";
    }

    private static string GetKnownSourceDebugLabel(string npcName)
    {
        if (SveNames.Contains(npcName))
        {
            return "Stardew Valley Expanded";
        }

        if (RsvNames.Contains(npcName))
        {
            return "Ridgeside Village";
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
            "Stardew Valley Expanded",
            "Stardew Valley Expanded",
            backgroundPrompt,
            dialoguePrompt
        );
    }

    private static NpcDispositionProfile Rsv(
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
            "Ridgeside Village",
            "Ridgeside Village",
            backgroundPrompt,
            dialoguePrompt
        );
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
