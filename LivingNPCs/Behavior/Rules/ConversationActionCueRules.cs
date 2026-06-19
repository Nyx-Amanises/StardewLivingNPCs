using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class ConversationActionCueRules
{
    private static readonly string[] ImmediateTravelAcceptanceFragments =
    [
        "一起去吧",
        "一起走吧",
        "我陪你去",
        "我跟你去",
        "我和你去",
        "我带你去",
        "我们走吧",
        "咱们走吧",
        "赶紧走",
        "赶紧去",
        "赶紧溜",
        "咱们赶紧溜",
        "那我们走",
        "那我们去",
        "我们现在去",
        "咱们现在去",
        "现在走",
        "现在去",
        "马上去",
        "这就去",
        "出发吧",
        "走吧",
        "let's go",
        "let us go",
        "let's head",
        "i'll go with you",
        "i can go with you",
        "i'll come with you",
        "i can come with you",
        "i'll take you",
        "i can take you",
        "we can go now",
        "sure, let's",
        "sure, i'll"
    ];

    private static readonly string[] TravelMotionFragments =
    [
        "现在",
        "马上",
        "这就",
        "去",
        "走",
        "出发",
        "陪你",
        "跟你",
        "带你",
        "go",
        "come",
        "head",
        "walk",
        "with you",
        "let's"
    ];

    private static readonly string[] DirectTravelAcceptanceFragments =
    [
        "当然可以",
        "可以啊",
        "可以呀",
        "好啊",
        "好呀",
        "好吧",
        "当然",
        "愿意",
        "行啊",
        "没问题",
        "okay",
        "ok",
        "sure",
        "yes"
    ];

    private static readonly string[] TravelRejectionFragments =
    [
        "下次",
        "改天",
        "晚点",
        "以后",
        "一会儿再",
        "忙完",
        "有点忙",
        "走不开",
        "今天不行",
        "今天不可以",
        "今天不太行",
        "今天不合适",
        "今天不适合",
        "现在不行",
        "现在不可以",
        "现在不太行",
        "现在不合适",
        "现在不适合",
        "不太合适",
        "不太适合",
        "不合适",
        "不适合",
        "不方便",
        "不太方便",
        "不行",
        "不可以",
        "不能",
        "没法",
        "得先把",
        "要先把",
        "先把",
        "课本",
        "碰见你",
        "later",
        "another time",
        "another day",
        "not now",
        "not today",
        "next time",
        "can't",
        "cannot",
        "too busy",
        "busy today",
        "after i finish"
    ];

    private static readonly string[] FutureTravelPlanFragments =
    [
        "明天再",
        "明天吧",
        "明天去",
        "明天见",
        "明天来",
        "明日再",
        "明日去",
        "tomorrow",
        "next time",
        "another day",
        "another time"
    ];

    private static readonly string[] UncertainTravelFragments =
    [
        "也许",
        "或许",
        "可能",
        "说不定",
        "maybe",
        "perhaps",
        "possibly"
    ];

    public static bool LooksLikeImmediateGiftOffer(string npcResponse)
    {
        return ContainsAny(
            npcResponse,
            "给你",
            "送你",
            "拿着",
            "收下",
            "带给你",
            "留给你",
            "给你一个",
            "送你一个",
            "一点心意",
            "小礼物",
            "回礼",
            "谢礼",
            "这个给",
            "这个是给",
            "这点东西",
            "这份",
            "this is for you",
            "this one's for you",
            "here's something",
            "here is something",
            "i have something for you",
            "take this",
            "you can have this",
            "i brought you",
            "i saved this for you",
            "small gift",
            "return the favor",
            "to thank you"
        );
    }

    public static IReadOnlyList<ValleyTalkWorldActionRequest> FilterActionsContradictedByVisibleDialogue(
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        if (actions.Count == 0)
        {
            return actions;
        }

        var filtered = actions
            .Where(action => ShouldKeepVisibleWorldAction(action, playerText, npcResponse))
            .ToArray();
        return filtered.Length == actions.Count ? actions : filtered;
    }

    public static bool LooksLikeGiftOfferRejection(string npcResponse)
    {
        return ContainsAny(
            npcResponse,
            "不能给你",
            "没法给你",
            "下次再给",
            "以后再给",
            "改天再给",
            "没有什么能给",
            "没什么能给",
            "not today",
            "next time",
            "another day",
            "can't give",
            "cannot give",
            "nothing to give"
        );
    }

    public static bool LooksLikeMeaningfulGiftOffer(string playerText, string npcResponse)
    {
        string combined = $"{playerText} {npcResponse}";
        return ContainsAny(
            combined,
            "有意义",
            "特别",
            "重要",
            "珍藏",
            "用心",
            "记得你喜欢",
            "special",
            "meaningful",
            "important",
            "saved this",
            "remembered you like"
        );
    }

    public static IReadOnlyList<ValleyTalkWorldActionRequest> FilterTravelActionsContradictedByVisibleDialogue(
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        if (actions.Count == 0)
        {
            return actions;
        }

        var filtered = actions
            .Where(action => action.Type != "companion_outing" || ShouldKeepTravelAction(action, playerText, npcResponse))
            .ToArray();
        return filtered.Length == actions.Count ? actions : filtered;
    }

    public static void TryCorrectTravelActionTargetFromVisibleDialogue(
        NPC npc,
        IReadOnlyList<ValleyTalkWorldActionRequest> actions,
        string playerText,
        string npcResponse
    )
    {
        var action = actions.FirstOrDefault(candidate => candidate.Type == "companion_outing");
        if (action == null)
        {
            return;
        }

        string visibleTarget = TryDetectTravelTargetLocation(npc, $"{playerText} {npcResponse}");
        if (string.IsNullOrWhiteSpace(visibleTarget))
        {
            return;
        }

        string currentTarget = BehaviorMemory.NormalizeTravelLocation(action.TargetLocation, string.Empty);
        bool currentTargetIsGeneric = string.IsNullOrWhiteSpace(currentTarget)
            || currentTarget is "Town" or "BusStop";
        if (!currentTargetIsGeneric && string.Equals(currentTarget, visibleTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (currentTargetIsGeneric || visibleTarget == ResolveNpcHomeEscortTarget(npc))
        {
            action.Type = "companion_outing";
            action.TargetLocation = visibleTarget;
            action.Reason = BuildWorldActionReason(
                action.Reason,
                $"visible dialogue clarified the destination as {visibleTarget}"
            );
        }
    }

    public static bool TryBuildFallbackTravelAction(
        NPC npc,
        string playerText,
        string npcResponse,
        out ValleyTalkWorldActionRequest? action
    )
    {
        return TryBuildFallbackTravelActionCore(npc, playerText, npcResponse, out action);
    }

    internal static bool TryBuildFallbackTravelActionForTesting(
        string playerText,
        string npcResponse,
        out ValleyTalkWorldActionRequest? action
    )
    {
        return TryBuildFallbackTravelActionCore(null, playerText, npcResponse, out action);
    }

    private static bool TryBuildFallbackTravelActionCore(
        NPC? npc,
        string playerText,
        string npcResponse,
        out ValleyTalkWorldActionRequest? action
    )
    {
        action = null;
        string combinedText = $"{playerText} {npcResponse}";
        if (!LooksLikeImmediateTravelInvitation(playerText, npcResponse)
            || LooksLikeDeferredRejectedOrUncertainTravel(playerText, npcResponse))
        {
            return false;
        }

        string targetLocation = TryDetectTravelTargetLocation(npc, combinedText);
        if (string.IsNullOrWhiteSpace(targetLocation))
        {
            return false;
        }

        action = new ValleyTalkWorldActionRequest
        {
            Type = "companion_outing",
            TargetLocation = targetLocation,
            TravelConsent = "accepted_now",
            DurationMinutes = LooksLikeBriefEscortRequest(playerText, npcResponse)
                ? CompanionOutingRules.DefaultShortVisitMinutes
                : CompanionOutingRules.MinimumStayMinutes,
            DelayMinutes = DetectPreparationDelayMinutes(npcResponse),
            Reason = "the visible conversation ended with an immediate shared outing plan"
        };
        return true;
    }

    public static int DetectPreparationDelayMinutes(string npcResponse)
    {
        return ContainsAny(
            npcResponse,
            "等我一下",
            "稍等",
            "一会儿",
            "准备一下",
            "换件衣服",
            "拿件衣服",
            "拿衣服",
            "雨衣",
            "带把伞",
            "拿把伞",
            "wait a moment",
            "get my coat",
            "grab my coat",
            "umbrella"
        )
            ? 10
            : 0;
    }

    public static bool ContainsAny(string text, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldKeepTravelAction(
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse
    )
    {
        string consent = BehaviorValueNormalizer.NormalizeTravelConsent(action.TravelConsent);
        if (!string.IsNullOrWhiteSpace(consent))
        {
            return consent == "accepted_now" && !LooksLikeDeferredRejectedOrUncertainTravel(playerText, npcResponse);
        }

        return LooksLikeImmediateTravelInvitation(playerText, npcResponse)
            && !LooksLikeDeferredRejectedOrUncertainTravel(playerText, npcResponse);
    }

    private static bool ShouldKeepVisibleWorldAction(
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse
    )
    {
        return action.Type switch
        {
            "companion_outing" => ShouldKeepTravelAction(action, playerText, npcResponse),
            "give_small_gift" or "give_meaningful_gift" => ShouldKeepGiftAction(action, npcResponse),
            "give_money" => LooksLikeImmediateMoneyOffer(npcResponse),
            _ => true
        };
    }

    private static bool ShouldKeepGiftAction(ValleyTalkWorldActionRequest action, string npcResponse)
    {
        if (LooksLikeGiftOfferRejection(npcResponse))
        {
            return false;
        }

        if (LooksLikeImmediateGiftOffer(npcResponse))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(action.ItemLabel)
            && VisibleDialogueMentionsLabel(npcResponse, action.ItemLabel)
            && ContainsAny(
                npcResponse,
                "给",
                "送",
                "拿",
                "收下",
                "for you",
                "take",
                "have this",
                "brought you",
                "saved this"
            );
    }

    private static bool LooksLikeImmediateMoneyOffer(string npcResponse)
    {
        if (!ContainsAny(npcResponse, "钱", "金币", "gold", "money", "coin", "coins")
            && !ContainsGoldAmount(npcResponse))
        {
            return false;
        }

        return ContainsAny(
            npcResponse,
            "给你",
            "拿着",
            "收下",
            "贴补",
            "补贴",
            "这点钱",
            "这些钱",
            "this is for you",
            "take this",
            "here's",
            "here is",
            "i can spare",
            "this should cover",
            "some gold",
            "some money"
        );
    }

    private static bool ContainsGoldAmount(string text)
    {
        for (int index = 1; index < text.Length; index++)
        {
            if ((text[index] == 'g' || text[index] == 'G') && char.IsDigit(text[index - 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool VisibleDialogueMentionsLabel(string npcResponse, string itemLabel)
    {
        string label = itemLabel.Trim();
        return label.Length > 0 && npcResponse.Contains(label, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeImmediateTravelInvitation(string playerText, string npcResponse)
    {
        bool farmerInvited = ContainsAny(
            playerText,
            "一起去",
            "要不要去",
            "陪我去",
            "陪我去看海",
            "去我农场",
            "来我农场",
            "去农场看看",
            "去看海",
            "一起看海",
            "去海边",
            "去海滩",
            "一起走",
            "我们去",
            "咱们去",
            "带你去",
            "带我去",
            "go with me",
            "come to my farm",
            "visit my farm",
            "walk with me"
        ) || LooksLikeTargetedFarmerTravelRequest(playerText);
        bool npcAccepted = ContainsAny(npcResponse, ImmediateTravelAcceptanceFragments)
            || ContainsAny(npcResponse, DirectTravelAcceptanceFragments)
            || (ContainsAny(npcResponse, "可以") && ContainsAny(npcResponse, TravelMotionFragments));
        // The NPC may also be the one who proposes the outing, with the farmer simply agreeing.
        bool npcProposedOuting = ContainsAny(
            npcResponse,
            "我陪你",
            "我跟你去",
            "我带你去",
            "一起去吧",
            "一起走吧",
            "一起看海",
            "我们现在去",
            "咱们现在去",
            "那我们去",
            "那我们走",
            "去看海",
            "去海边",
            "去海滩",
            "要不要一起去",
            "要不要现在去",
            "带你去",
            "let's go",
            "let's head",
            "shall we",
            "why don't we go",
            "want to go now",
            "wanna go now"
        );
        bool farmerAgreed = ContainsAny(
            playerText,
            "好啊",
            "好呀",
            "好的",
            "好吧",
            "可以呀",
            "当然",
            "没问题",
            "走吧",
            "一起去",
            "愿意",
            "行啊",
            "okay",
            "ok",
            "sure",
            "yes",
            "let's",
            "sounds good"
        );
        return (farmerInvited && npcAccepted) || (npcProposedOuting && farmerAgreed);
    }

    private static bool LooksLikeTargetedFarmerTravelRequest(string playerText)
    {
        if (string.IsNullOrWhiteSpace(TryDetectTravelTargetLocation(null, playerText)))
        {
            return false;
        }

        bool mentionsTravelMotion = ContainsAny(
            playerText,
            "去",
            "到",
            "逛",
            "看看",
            "走走",
            "go",
            "visit",
            "head",
            "walk",
            "来"
        );
        if (!mentionsTravelMotion)
        {
            return false;
        }

        return ContainsAny(
            playerText,
            "一起",
            "陪我",
            "陪你",
            "带我",
            "带你",
            "要不要",
            "愿意",
            "可以",
            "能不能",
            "想不想",
            "现在",
            "马上",
            "走吧",
            "去吗",
            "去么",
            "去嘛",
            "吗",
            "？",
            "?",
            "with me",
            "together",
            "want to",
            "would you",
            "could you",
            "can you",
            "shall we",
            "let's"
        );
    }

    private static bool LooksLikeBriefEscortRequest(string playerText, string npcResponse)
    {
        string combinedText = $"{playerText} {npcResponse}";
        return ContainsAny(
            combinedText,
            "带我去",
            "给我带路",
            "领我去",
            "带我过去",
            "陪我过去",
            "认路",
            "顺路",
            "take me to",
            "show me to",
            "lead me to",
            "walk me to",
            "on the way"
        );
    }

    public static bool LooksLikeDeferredOrRejectedTravel(string playerText, string npcResponse)
    {
        string combinedText = $"{playerText} {npcResponse}";
        string rejectionNpcResponse = RemoveNonTravelFarewells(npcResponse);
        string rejectionCombinedText = RemoveNonTravelFarewells(combinedText);
        if (ContainsAny(rejectionNpcResponse, TravelRejectionFragments)
            || ContainsAny(rejectionCombinedText, FutureTravelPlanFragments))
        {
            return true;
        }

        if (DetectPreparationDelayMinutes(npcResponse) > 0)
        {
            return false;
        }

        return false;
    }

    private static string RemoveNonTravelFarewells(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Replace("改天再聊", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("下次再聊", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("以后再聊", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("改天聊", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("下次聊", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("talk later", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("chat later", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("see you later", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDeferredRejectedOrUncertainTravel(string playerText, string npcResponse)
    {
        return LooksLikeDeferredOrRejectedTravel(playerText, npcResponse)
            || ContainsAny(npcResponse, UncertainTravelFragments);
    }

    private static string TryDetectTravelTargetLocation(NPC? npc, string text)
    {
        string npcHome = ResolveNpcHomeEscortTarget(npc);
        if (!string.IsNullOrWhiteSpace(npcHome)
            && ContainsAny(
                text,
                "你家",
                "你的家",
                "你家里",
                "你住的地方",
                "你住处",
                "你房间",
                "她家",
                "他家",
                "家里看看",
                "回你家",
                "去家里",
                "your home",
                "your house",
                "where you live"
            ))
        {
            return npcHome;
        }

        if (ContainsAny(text, "潘妮家", "潘妮的家", "潘妮家里", "帕姆家", "帕姆的家", "拖车", "penny's home", "penny's house", "trailer"))
        {
            return "Trailer";
        }

        if (ContainsAny(text, "农场", "farm"))
        {
            return "Farm";
        }

        if (ContainsAny(text, "海边", "海滩", "看海", "大海", "海浪", "浪花", "贝壳", "去海", "beach", "shore", "waves"))
        {
            return "Beach";
        }

        if (ContainsAny(text, "格兰普顿海岸", "sve 海岸", "grampleton coast", "sve coast"))
        {
            return "Custom_GrampletonCoast";
        }

        if (ContainsAny(text, "蓝月葡萄园", "索菲亚的葡萄园", "blue moon vineyard", "sophia's vineyard"))
        {
            return "Custom_BlueMoonVineyard";
        }

        if (ContainsAny(text, "极光葡萄园", "aurora vineyard"))
        {
            return "Custom_AuroraVineyard";
        }

        if (ContainsAny(text, "祝尼魔森林", "junimo woods"))
        {
            return "Custom_JunimoWoods";
        }

        if (ContainsAny(text, "魔法林地", "enchanted grove"))
        {
            return "Custom_EnchantedGrove";
        }

        if (ContainsAny(text, "爷爷的棚屋", "grandpa's shed"))
        {
            return "Custom_GrandpasShedOutside";
        }

        if (ContainsAny(text, "博物馆", "图书馆", "museum", "library"))
        {
            return "ArchaeologyHouse";
        }

        if (ContainsAny(text, "西部森林", "forest west", "west forest"))
        {
            return "Custom_ForestWest";
        }

        if (ContainsAny(text, "sve 山顶", "山顶", "summit", "sve summit"))
        {
            return "Custom_SVESummit";
        }

        if (ContainsAny(text, "森林", "煤矿森林", "forest"))
        {
            return "Forest";
        }

        if (ContainsAny(text, "矿井", "矿洞", "矿山", "mine", "mines"))
        {
            return "Mine";
        }

        if (ContainsAny(text, "山上", "山地", "mountain"))
        {
            return "Mountain";
        }

        if (ContainsAny(text, "酒吧", "沙龙", "saloon"))
        {
            return "Saloon";
        }

        if (ContainsAny(text, "医院", "诊所", "clinic", "hospital"))
        {
            return "Hospital";
        }

        if (ContainsAny(text, "花舞节", "花田", "flower dance", "flower festival"))
        {
            return "FlowerDance";
        }

        if (ContainsAny(text, "皮埃尔", "杂货店", "general store", "pierre"))
        {
            return "SeedShop";
        }

        if (ContainsAny(text, "巴士站", "bus stop"))
        {
            return "BusStop";
        }

        if (ContainsAny(text, "镇上", "鹈鹕镇", "town"))
        {
            return "Town";
        }

        return string.Empty;
    }

    private static string ResolveNpcHomeEscortTarget(NPC? npc)
    {
        if (npc == null)
        {
            return string.Empty;
        }

        return npc.Name switch
        {
            "Penny" or "Pam" => "Trailer",
            "Alex" or "Evelyn" or "George" => "JoshHouse",
            "Haley" or "Emily" => "HaleyHouse",
            "Sam" or "Jodi" or "Vincent" or "Kent" => "SamHouse",
            "Abigail" or "Pierre" or "Caroline" => "SeedShop",
            "Sebastian" or "Maru" or "Demetrius" or "Robin" => "ScienceHouse",
            "Leah" => "LeahHouse",
            "Marnie" or "Jas" or "Shane" => "AnimalShop",
            "Elliott" => "ElliottHouse",
            "Gus" => "Saloon",
            "Clint" => "Blacksmith",
            "Willy" => "FishShop",
            "Wizard" => "WizardHouse",
            "Linus" => "Tent",
            _ => string.Empty
        };
    }

    private static string BuildWorldActionReason(string requestedReason, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedReason)
            ? fallback
            : $"{requestedReason.Trim()}; {fallback}";
    }
}
