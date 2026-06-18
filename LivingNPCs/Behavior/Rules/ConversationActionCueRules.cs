using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class ConversationActionCueRules
{
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
            "一点心意",
            "小礼物",
            "回礼",
            "谢礼",
            "这个给",
            "这个是给",
            "this is for you",
            "this one's for you",
            "take this",
            "take it",
            "i brought you",
            "i saved this for you",
            "small gift",
            "return the favor",
            "to thank you"
        );
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
            "not today",
            "next time",
            "another day",
            "can't give"
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
        action = null;
        string combinedText = $"{playerText} {npcResponse}";
        if (!LooksLikeImmediateTravelInvitation(playerText, npcResponse)
            || LooksLikeDeferredOrRejectedTravel(npcResponse))
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
            DurationMinutes = CompanionOutingRules.MinimumStayMinutes,
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

    private static bool LooksLikeImmediateTravelInvitation(string playerText, string npcResponse)
    {
        bool farmerInvited = ContainsAny(
            playerText,
            "一起去",
            "要不要去",
            "陪我去",
            "去我农场",
            "来我农场",
            "去农场看看",
            "一起走",
            "我们去",
            "咱们去",
            "带你去",
            "带我去",
            "go with me",
            "come to my farm",
            "visit my farm",
            "walk with me"
        );
        bool npcAccepted = ContainsAny(
            npcResponse,
            "一起去",
            "我陪你",
            "那我们",
            "走吧",
            "可以",
            "当然",
            "好啊",
            "好呀",
            "愿意",
            "let's go",
            "i'll go",
            "i can go",
            "sure"
        );
        // The NPC may also be the one who proposes the outing, with the farmer simply agreeing.
        bool npcProposedOuting = ContainsAny(
            npcResponse,
            "一起去",
            "我们去",
            "咱们去",
            "我陪你",
            "那我们",
            "一起走",
            "要不要一起",
            "要不要去",
            "带你去",
            "let's go",
            "shall we",
            "why don't we",
            "want to go",
            "wanna go"
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

    private static bool LooksLikeDeferredOrRejectedTravel(string npcResponse)
    {
        if (DetectPreparationDelayMinutes(npcResponse) > 0)
        {
            return false;
        }

        return ContainsAny(
            npcResponse,
            "下次",
            "改天",
            "晚点",
            "以后",
            "今天不行",
            "不行",
            "不可以",
            "不能",
            "没法",
            "抱歉",
            "later",
            "another time",
            "not now",
            "can't"
        );
    }

    private static string TryDetectTravelTargetLocation(NPC npc, string text)
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

        if (ContainsAny(text, "海边", "海滩", "beach"))
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

    private static string ResolveNpcHomeEscortTarget(NPC npc)
    {
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
