using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// 求助候选池与提示词建议（规则层，不发网络请求）。
/// 目录约 70 件，六类：季节采集/当季易钓鱼（刻意排除全部鱼王）/当季作物/家常烹饪/
/// 矿物矿石/动物产品·工匠货·可购主食。可得性只看"玩家拿得到"：季节、矿井进度、
/// 厨房或好感（烹饪双通道）、农场建筑、年份；单件保持低价值小忙定位。
/// 候选按天稳定轮换（同日同 NPC 确定，跨天变化）；深度允许时附一条同主题两步链建议。
/// </summary>
internal static class HelpRequestAdvisor
{
    private static readonly IReadOnlyDictionary<string, HelpRequestProfile> ExplicitProfiles =
        new Dictionary<string, HelpRequestProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gus"] = new("kitchen and hospitality", ["food", "practical", "comfort", "fish", "staple"]),
            ["Harvey"] = new("care and health", ["drink", "scholarly", "comfort", "crop"]),
            ["Penny"] = new("books, children, and simple teaching examples", ["scholarly", "comfort", "flower", "forage", "mineral"]),
            ["Demetrius"] = new("field study and observation", ["scholarly", "nature", "forage", "fish"]),
            ["Maru"] = new("technical curiosity", ["scholarly", "practical", "mineral"]),
            ["Robin"] = new("building and repairs", ["practical", "work", "forage", "food"]),
            ["Clint"] = new("smithing and materials", ["mineral", "practical", "work"]),
            ["Linus"] = new("foraging and living outdoors", ["forage", "nature", "food", "fish"]),
            ["Leah"] = new("art and nature", ["flower", "nature", "artistic", "forage"]),
            ["Elliott"] = new("writing and aesthetics", ["flower", "artistic", "comfort", "fish"]),
            ["Abigail"] = new("exploration", ["mineral", "adventurous"]),
            ["Wizard"] = new("arcane study", ["mineral", "magical", "scholarly"]),
            ["Willy"] = new("fishing and sea life", ["fish", "practical", "nature"]),
            ["Marnie"] = new("ranch animals and hearty food", ["animal", "food", "comfort", "practical"]),
            ["Pam"] = new("long drives and small pick-me-ups", ["drink", "practical", "comfort", "food"]),
            ["Evelyn"] = new("gardening and home baking", ["flower", "sweet", "comfort", "family", "homestyle"]),
            ["George"] = new("warm meals and quiet routines", ["food", "comfort", "homestyle", "practical"]),
            ["Lewis"] = new("town errands and shared meals", ["practical", "food", "comfort", "work"]),
            ["Caroline"] = new("tea, plants, and light exercise", ["nature", "flower", "drink", "comfort"]),
            ["Pierre"] = new("shop stock and fresh produce", ["crop", "staple", "practical", "work"]),
            ["Jodi"] = new("family cooking", ["food", "family", "homestyle", "comfort", "staple"]),
            ["Kent"] = new("settling back into home life", ["food", "practical", "comfort", "homestyle"]),
            ["Sam"] = new("band snacks and quick energy", ["food", "sweet", "practical", "drink"]),
            ["Sebastian"] = new("late-night work fuel", ["drink", "scholarly", "mineral", "practical"]),
            ["Alex"] = new("training and hearty breakfasts", ["food", "practical", "animal"]),
            ["Haley"] = new("photo props and pretty things", ["flower", "artistic", "sweet"]),
            ["Emily"] = new("cloth dyes and crystals", ["mineral", "artistic", "magical", "flower"]),
            ["Shane"] = new("chickens and cheap comfort", ["animal", "drink", "food", "comfort"]),
            ["Vincent"] = new("bugs, snacks, and small adventures", ["sweet", "nature", "forage", "family"]),
            ["Jas"] = new("quiet crafts and sweets", ["sweet", "flower", "comfort", "family"]),
            ["Sandy"] = new("a touch of green in the desert", ["flower", "comfort", "artistic", "nature"]),
            ["Krobus"] = new("curious surface goods", ["magical", "mineral", "fish", "food"]),
            ["Dwarf"] = new("minerals and mining snacks", ["mineral", "adventurous", "practical"]),
            ["Leo"] = new("island memories and bird-watching", ["nature", "fish", "forage", "food"]),
            ["Claire"] = new("everyday fatigue and small comforts", ["drink", "practical", "comfort"]),
            ["Sophia"] = new("vineyard life and small comforts", ["flower", "comfort", "food", "crop"]),
            ["Susan"] = new("farm work", ["food", "nature", "practical", "forage", "crop"]),
            ["Victor"] = new("engineering and careful planning", ["scholarly", "practical", "mineral"]),
            ["Andy"] = new("orchard work and a stiff drink", ["crop", "drink", "practical", "food"]),
            ["Olivia"] = new("wine and refined comforts", ["drink", "comfort", "artistic", "flower"]),
            ["Lance"] = new("expedition provisions", ["adventurous", "food", "practical", "mineral"]),
            ["Scarlett"] = new("homestead crafts and animals", ["animal", "flower", "homestyle", "food"]),
            ["Gunther"] = new("museum curiosities", ["scholarly", "mineral", "artistic"]),
            ["Martin"] = new("long shifts and quick meals", ["work", "practical", "food", "drink"]),
            ["Morris"] = new("corporate polish", ["work", "practical", "comfort", "drink"]),
            ["MorrisTod"] = new("corporate polish", ["work", "practical", "comfort", "drink"])
        };

    /// <summary>
    /// 全部候选（扁平表）。Seasons = null 表示不限季节；门槛见 <see cref="HelpRequestAvailability"/>。
    /// 鱼类刻意只收常见鱼（无任何鱼王/传说鱼）；单件基础价值保持"低价值小忙"量级。
    /// </summary>
    private static readonly IReadOnlyList<HelpRequestItem> AllItems =
    [
        // —— 季节采集（沿用原池） ——
        new("(O)16", "Wild Horseradish", ["forage", "nature", "food", "practical"], Seasons: ["spring"]),
        new("(O)18", "Daffodil", ["flower", "nature", "artistic"], Seasons: ["spring"]),
        new("(O)20", "Leek", ["forage", "food", "practical"], Seasons: ["spring"]),
        new("(O)22", "Dandelion", ["flower", "nature"], Seasons: ["spring"]),
        new("(O)396", "Spice Berry", ["forage", "food", "sweet"], Seasons: ["summer"]),
        new("(O)398", "Grape", ["forage", "food", "sweet"], Seasons: ["summer"]),
        new("(O)402", "Sweet Pea", ["flower", "artistic"], Seasons: ["summer"]),
        new("(O)404", "Common Mushroom", ["forage", "nature", "practical"], Seasons: ["fall"]),
        new("(O)406", "Wild Plum", ["forage", "food", "sweet"], Seasons: ["fall"]),
        new("(O)408", "Hazelnut", ["forage", "food", "practical"], Seasons: ["fall"]),
        new("(O)410", "Blackberry", ["forage", "food", "sweet"], Seasons: ["fall"]),
        new("(O)412", "Winter Root", ["forage", "food", "practical"], Seasons: ["winter"]),
        new("(O)414", "Crystal Fruit", ["forage", "food", "magical"], Seasons: ["winter"]),
        new("(O)416", "Snow Yam", ["forage", "food", "practical"], Seasons: ["winter"]),
        new("(O)418", "Crocus", ["flower", "artistic"], Seasons: ["winter"]),

        // —— 当季易钓鱼（无鱼王；不看钓鱼等级——它升得最快，玩家拿得到） ——
        new("(O)129", "Anchovy", ["fish", "food", "nature"], Seasons: ["spring", "fall"]),
        new("(O)130", "Tuna", ["fish", "food", "practical"], Seasons: ["summer", "winter"]),
        new("(O)131", "Sardine", ["fish", "food", "practical"], Seasons: ["spring", "fall", "winter"]),
        new("(O)132", "Bream", ["fish", "food", "nature"]),
        new("(O)137", "Smallmouth Bass", ["fish", "food", "nature"], Seasons: ["spring", "fall"]),
        new("(O)138", "Rainbow Trout", ["fish", "food", "nature"], Seasons: ["summer"]),
        new("(O)139", "Salmon", ["fish", "food", "practical"], Seasons: ["fall"]),
        new("(O)141", "Perch", ["fish", "food", "nature"], Seasons: ["winter"]),
        new("(O)142", "Carp", ["fish", "food", "practical"], Seasons: ["spring", "summer", "fall"]),
        new("(O)145", "Sunfish", ["fish", "food", "nature"], Seasons: ["spring", "summer"]),
        new("(O)147", "Herring", ["fish", "food", "practical"], Seasons: ["spring", "winter"]),
        new("(O)151", "Squid", ["fish", "food", "adventurous"], Seasons: ["winter"]),
        new("(O)154", "Sea Cucumber", ["fish", "nature", "adventurous"], Seasons: ["fall", "winter"]),
        new("(O)701", "Tilapia", ["fish", "food", "nature"], Seasons: ["summer", "fall"]),
        new("(O)702", "Chub", ["fish", "food", "practical"]),

        // —— 当季作物（第一年春第一茬收成前不出现） ——
        new("(O)24", "Parsnip", ["crop", "food", "nature", "practical"], HelpRequestAvailability.CropHarvestReady, Seasons: ["spring"]),
        new("(O)192", "Potato", ["crop", "food", "practical", "homestyle"], HelpRequestAvailability.CropHarvestReady, Seasons: ["spring"]),
        new("(O)250", "Kale", ["crop", "food", "nature"], HelpRequestAvailability.CropHarvestReady, Seasons: ["spring"]),
        new("(O)256", "Tomato", ["crop", "food", "homestyle"], HelpRequestAvailability.CropHarvestReady, Seasons: ["summer"]),
        new("(O)258", "Blueberry", ["crop", "food", "sweet"], HelpRequestAvailability.CropHarvestReady, Seasons: ["summer"]),
        new("(O)260", "Hot Pepper", ["crop", "food", "adventurous"], HelpRequestAvailability.CropHarvestReady, Seasons: ["summer"]),
        new("(O)262", "Wheat", ["crop", "practical", "staple"], HelpRequestAvailability.CropHarvestReady, Seasons: ["summer", "fall"]),
        new("(O)270", "Corn", ["crop", "food", "homestyle"], HelpRequestAvailability.CropHarvestReady, Seasons: ["summer", "fall"]),
        new("(O)272", "Eggplant", ["crop", "food", "homestyle"], HelpRequestAvailability.CropHarvestReady, Seasons: ["fall"]),
        new("(O)278", "Bok Choy", ["crop", "food", "nature"], HelpRequestAvailability.CropHarvestReady, Seasons: ["fall"]),
        new("(O)282", "Cranberries", ["crop", "food", "sweet"], HelpRequestAvailability.CropHarvestReady, Seasons: ["fall"]),

        // —— 家常烹饪（有厨房 或 好感≥2 可开口；沙拉/意面酒吧常年有售，另走可购通道） ——
        new("(O)194", "Fried Egg", ["food", "homestyle", "comfort"], HelpRequestAvailability.KitchenOrHearts2),
        new("(O)195", "Omelet", ["food", "homestyle", "comfort"], HelpRequestAvailability.KitchenOrHearts2),
        new("(O)199", "Parsnip Soup", ["food", "homestyle", "comfort"], HelpRequestAvailability.KitchenOrHearts2),
        new("(O)207", "Bean Hotpot", ["food", "homestyle", "nature"], HelpRequestAvailability.KitchenOrHearts2),
        new("(O)210", "Hashbrowns", ["food", "homestyle", "practical"], HelpRequestAvailability.KitchenOrHearts2),
        new("(O)211", "Pancakes", ["food", "homestyle", "sweet"], HelpRequestAvailability.KitchenOrHearts2),
        new("(O)196", "Salad", ["food", "nature", "comfort"]),
        new("(O)224", "Spaghetti", ["food", "homestyle", "comfort"]),

        // —— 矿物与矿石（按矿井进度解锁） ——
        new("(O)80", "Quartz", ["mineral", "scholarly", "magical"], HelpRequestAvailability.MinesOpen),
        new("(O)66", "Amethyst", ["mineral", "adventurous", "magical", "artistic"], HelpRequestAvailability.MineLevel40),
        new("(O)378", "Copper Ore", ["mineral", "practical", "work"], HelpRequestAvailability.MinesOpen),
        new("(O)380", "Iron Ore", ["mineral", "practical", "work"], HelpRequestAvailability.MineLevel40),
        new("(O)68", "Topaz", ["mineral", "practical"], HelpRequestAvailability.MinesOpen),
        new("(O)86", "Earth Crystal", ["mineral", "magical", "nature"], HelpRequestAvailability.MinesOpen),
        new("(O)84", "Frozen Tear", ["mineral", "magical", "comfort"], HelpRequestAvailability.MineLevel40),
        new("(O)82", "Fire Quartz", ["mineral", "magical", "adventurous"], HelpRequestAvailability.MineLevel80),

        // —— 动物产品 / 工匠货（有对应产能才开口） ——
        new("(O)176", "Egg", ["animal", "food", "homestyle", "practical"], HelpRequestAvailability.CoopOwned),
        new("(O)184", "Milk", ["animal", "food", "comfort", "practical"], HelpRequestAvailability.BarnOwned),
        new("(O)340", "Honey", ["artisan", "sweet", "nature", "food"], HelpRequestAvailability.Year2Plus),
        new("(O)344", "Jelly", ["artisan", "sweet", "homestyle", "food"], HelpRequestAvailability.Year2Plus),
        new("(O)342", "Pickles", ["artisan", "practical", "homestyle", "food"], HelpRequestAvailability.Year2Plus),

        // —— 可购主食（皮埃尔常年有售，随时拿得到） ——
        new("(O)245", "Sugar", ["staple", "food", "sweet", "practical"]),
        new("(O)246", "Wheat Flour", ["staple", "food", "practical", "homestyle"]),
        new("(O)423", "Rice", ["staple", "food", "practical", "homestyle"]),
        new("(O)247", "Oil", ["staple", "food", "practical"]),
        new("(O)419", "Vinegar", ["staple", "food", "practical"]),

        // —— 关系物品（沿用原池：好感门槛） ——
        new("(O)216", "Bread", ["food", "comfort", "hospitality", "practical", "homestyle"], MinFriendshipHearts: 2),
        new("(O)395", "Coffee", ["drink", "comfort", "work", "hospitality"], MinFriendshipHearts: 4),
        new("(O)223", "Cookie", ["food", "comfort", "sweet", "family"], MinFriendshipHearts: 6)
    ];

    /// <summary>全目录物品 ID（含未解锁项）：HelpRequestMemoryService 的超集白名单由此派生，单一真源。</summary>
    public static readonly IReadOnlySet<string> AllItemIds = new HashSet<string>(
        AllItems.Select(item => item.ItemId),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 两步链候选对（食材→成品、浅矿→深矿等主题递进）。只作提示词建议：
    /// 双方都必须出现在当轮候选里才会被提出，每步仍逐一过白名单校验。
    /// </summary>
    private static readonly IReadOnlyList<(string First, string Second)> ChainPairs =
    [
        ("(O)176", "(O)211"), // 蛋 → 薄煎饼
        ("(O)176", "(O)195"), // 蛋 → 煎蛋卷
        ("(O)246", "(O)216"), // 面粉 → 面包
        ("(O)245", "(O)223"), // 糖 → 曲奇
        ("(O)24", "(O)199"),  // 防风草 → 防风草汤
        ("(O)16", "(O)196"),  // 野山葵 → 沙拉
        ("(O)378", "(O)380"), // 铜矿石 → 铁矿石
        ("(O)80", "(O)66"),   // 石英 → 紫水晶
        ("(O)86", "(O)84")    // 地晶 → 冰封泪晶
    ];

    internal static IReadOnlyList<(string First, string Second)> ChainPairsForTests => ChainPairs;

    internal static IEnumerable<(string ItemId, string Label, IReadOnlyCollection<string> Tags, IReadOnlyCollection<string>? Seasons)> CatalogForTests =>
        AllItems.Select(item => (item.ItemId, item.Label, item.Tags, item.Seasons));

    public static string BuildPromptLabel(NPC npc, WorldProgressSnapshot progression)
    {
        var profile = GetProfile(npc);
        var world = WorldContext.For(npc);
        var preferredItems = GetPreferredItems(npc, progression);
        string itemText = preferredItems.Count == 0
            ? "no currently reasonable item request; do not open a help request now"
            : string.Join(", ", preferredItems.Select(item => $"{item.Label} {item.ItemId}"));
        string stageText = BuildRequestDepthGuidance(progression);
        string routeText = BuildRouteGuidance(progression);
        string relationshipText = BuildRelationshipGuidance(world.FriendshipHearts);
        string chainText = BuildChainSuggestionText(preferredItems, progression);

        return $"theme {profile.Theme}; currently reasonable item requests: {itemText}; allowed help request type: item_request only; never create question_request; request relationship tier: {relationshipText}; request depth: {stageText}; world-stage constraint: {routeText}{chainText}; whenever the visible reply has this NPC ask the farmer to bring or find one of the listed items — whether the farmer offered first or the NPC raised it — you MUST also include exactly one hidden helpRequests entry with a concrete itemId from this list, and never leave the favor only in the spoken text; when the farmer agrees to such a favor, include a hidden helpRequestUpdates entry with status accepted; if no listed item naturally fits, keep the visible reply as ordinary conversation and do not open a hidden request.";
    }

    public static string BuildDebugLabel(NPC npc, WorldProgressSnapshot progression)
    {
        var profile = GetProfile(npc);
        var world = WorldContext.For(npc);
        var preferredItems = GetPreferredItems(npc, progression);
        string itemText = preferredItems.Count == 0
            ? "当前不建议物品求助"
            : string.Join("、", preferredItems.Select(item => $"{item.Label} {item.ItemId}"));
        string depthText = progression.ResidentStage switch
        {
            "first_spring_newcomer" => "第一年春，新人阶段：只适合低负担一步物品求助",
            "first_year_settling_in" => "第一年安顿中：以一步物品求助为主",
            "second_year_established" => "第二年已融入：可出现适度个人化物品求助",
            _ => "老住户阶段：高信任时可出现更贴角色的物品求助"
        };
        string routeText = progression.Route switch
        {
            "community_center" => "社区中心路线已完成",
            "joja" => "Joja 路线已完成",
            _ => "路线未定"
        };
        string relationshipText = world.FriendshipHearts switch
        {
            < 2 => "0-1 心：只允许很轻的当季物品",
            < 4 => "2-3 心：可加入便宜日常物品",
            < 6 => "4-5 心：可加入更贴生活习惯的物品",
            _ => "6 心以上：可加入更有人情味的物品"
        };
        (HelpRequestItem First, HelpRequestItem Second)? chain = FindChainPair(preferredItems, progression);
        string chainText = chain == null
            ? "无"
            : $"{chain.Value.First.Label} → {chain.Value.Second.Label}";

        return $"主题：{profile.Theme}；候选物品：{itemText}；类型：仅物品求助；关系：{relationshipText}；深度：{depthText}；路线：{routeText}；链建议：{chainText}";
    }

    public static bool IsCurrentlyRequestableItem(string itemId)
    {
        var progression = WorldProgression.Current();
        return AllItems.Any(item =>
            item.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)
            && IsAvailable(item, friendshipHearts: int.MaxValue, progression));
    }

    public static bool IsCurrentlyRequestableItem(string itemId, NPC npc)
    {
        return GetPreferredItems(npc, WorldProgression.Current())
            .Any(item => item.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Currently reasonable item requests for this NPC, as (itemId, English label) pairs.</summary>
    public static IReadOnlyList<(string ItemId, string Label)> GetRequestableItems(NPC npc)
    {
        return GetPreferredItems(npc, WorldProgression.Current())
            .Select(item => (item.ItemId, item.Label))
            .ToList();
    }

    private static List<HelpRequestItem> GetPreferredItems(NPC npc, WorldProgressSnapshot progression)
    {
        var profile = GetProfile(npc);
        var tags = new HashSet<string>(profile.Tags, StringComparer.OrdinalIgnoreCase);
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;

        var available = AllItems
            .Where(item => IsAvailable(item, friendshipHearts, progression))
            .OrderBy(item => DailyRotationKey(item.ItemId, npc.Name))
            .ToList();

        int maxCandidates = MaxCandidateCount(friendshipHearts, progression);
        var preferred = new List<HelpRequestItem>();
        var perPrimaryTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in available)
        {
            if (preferred.Count >= maxCandidates)
            {
                break;
            }

            if (!item.Tags.Any(tags.Contains))
            {
                continue;
            }

            // 单一主类别最多 2 件：避免"今天全是鱼"这类同质候选。
            string primaryTag = item.Tags.First();
            if (perPrimaryTag.GetValueOrDefault(primaryTag) >= 2)
            {
                continue;
            }

            perPrimaryTag[primaryTag] = perPrimaryTag.GetValueOrDefault(primaryTag) + 1;
            preferred.Add(item);
        }

        if (preferred.Count < 3)
        {
            foreach (var item in available)
            {
                if (preferred.Count >= Math.Min(3, available.Count))
                {
                    break;
                }

                if (!preferred.Contains(item))
                {
                    preferred.Add(item);
                }
            }
        }

        return preferred;
    }

    /// <summary>深度允许两步链时把候选放宽到 7 件（链对更容易同时在场），否则 4-5 件。</summary>
    private static int MaxCandidateCount(int friendshipHearts, WorldProgressSnapshot progression)
    {
        if (AllowsMultiStep(progression))
        {
            return 7;
        }

        return friendshipHearts >= 6 || progression.Year > 1 ? 5 : 4;
    }

    private static bool AllowsMultiStep(WorldProgressSnapshot progression)
    {
        return progression.ResidentStage != "first_spring_newcomer";
    }

    /// <summary>候选里能配出的第一组链对（无则 null）。只提两步：三步链交给模型自然延伸时也仍逐步校验。</summary>
    private static (HelpRequestItem First, HelpRequestItem Second)? FindChainPair(
        IReadOnlyList<HelpRequestItem> preferred,
        WorldProgressSnapshot progression)
    {
        if (!AllowsMultiStep(progression) || preferred.Count < 2)
        {
            return null;
        }

        var byId = preferred.ToDictionary(item => item.ItemId, StringComparer.OrdinalIgnoreCase);
        foreach ((string first, string second) in ChainPairs)
        {
            if (byId.TryGetValue(first, out var firstItem) && byId.TryGetValue(second, out var secondItem))
            {
                return (firstItem, secondItem);
            }
        }

        // 回退：同主类别的两件（先易后难的因果留给模型演绎）。
        foreach (var group in preferred.GroupBy(item => item.Tags.First(), StringComparer.OrdinalIgnoreCase))
        {
            var pair = group.Take(2).ToList();
            if (pair.Count == 2)
            {
                return (pair[0], pair[1]);
            }
        }

        return null;
    }

    private static string BuildChainSuggestionText(
        IReadOnlyList<HelpRequestItem> preferred,
        WorldProgressSnapshot progression)
    {
        var chain = FindChainPair(preferred, progression);
        if (chain == null)
        {
            return string.Empty;
        }

        return $"; optional two-step chain suggestion (use only when the conversation genuinely supports a bigger favor): step 1 = {chain.Value.First.Label} {chain.Value.First.ItemId}, then step 2 = {chain.Value.Second.Label} {chain.Value.Second.ItemId}; the two steps must stay thematically connected in this NPC's own words, and each step still needs its own hidden helpRequests step entry";
    }

    private static HelpRequestProfile GetProfile(NPC npc)
    {
        if (ExplicitProfiles.TryGetValue(npc.Name, out var explicitProfile))
        {
            return explicitProfile;
        }

        var disposition = NpcDisposition.For(npc);
        string profileText = $"{disposition.PromptLabel} {disposition.BackgroundPrompt} {disposition.DialoguePrompt}".ToLowerInvariant();
        if (ContainsAny(profileText, "magical", "arcane", "adventurer", "danger"))
        {
            return new HelpRequestProfile("exploration and unusual findings", ["mineral", "adventurous", "magical"]);
        }

        if (ContainsAny(profileText, "artist", "creative", "performer", "stylish"))
        {
            return new HelpRequestProfile("aesthetics and expression", ["flower", "artistic", "comfort"]);
        }

        if (ContainsAny(profileText, "scholarly", "educated", "student", "technical", "engineering"))
        {
            return new HelpRequestProfile("study and careful thinking", ["scholarly", "practical", "mineral"]);
        }

        if (ContainsAny(profileText, "fisher", "sailor", "ocean", "sea"))
        {
            return new HelpRequestProfile("water and daily catches", ["fish", "nature", "practical"]);
        }

        if (ContainsAny(profileText, "farm", "rural", "work", "practical"))
        {
            return new HelpRequestProfile("practical daily work", ["practical", "forage", "food", "crop"]);
        }

        if (ContainsAny(profileText, "warm", "family", "gentle", "community"))
        {
            return new HelpRequestProfile("everyday care and comfort", ["comfort", "food", "flower", "homestyle"]);
        }

        return new HelpRequestProfile("small everyday favors", ["practical", "comfort", "forage", "food"]);
    }

    private static bool IsAvailable(HelpRequestItem item, int friendshipHearts, WorldProgressSnapshot progression)
    {
        if (friendshipHearts < item.MinFriendshipHearts)
        {
            return false;
        }

        if (item.Seasons != null && !item.Seasons.Contains(CurrentSeason(), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return item.Availability switch
        {
            HelpRequestAvailability.MinesOpen => AreMinesOpen(),
            HelpRequestAvailability.MineLevel40 => Game1.player?.deepestMineLevel >= 40,
            HelpRequestAvailability.MineLevel80 => Game1.player?.deepestMineLevel >= 80,
            HelpRequestAvailability.KitchenOrHearts2 => HasKitchen() || friendshipHearts >= 2,
            HelpRequestAvailability.CoopOwned => HasFarmBuilding("Coop"),
            HelpRequestAvailability.BarnOwned => HasFarmBuilding("Barn"),
            HelpRequestAvailability.Year2Plus => progression.Year > 1,
            HelpRequestAvailability.CropHarvestReady => IsCropHarvestReady(),
            _ => true
        };
    }

    private static string CurrentSeason()
    {
        try
        {
            return Game1.season.ToString().ToLowerInvariant();
        }
        catch
        {
            return "spring";
        }
    }

    private static bool AreMinesOpen()
    {
        if (Game1.player?.deepestMineLevel > 0)
        {
            return true;
        }

        return Game1.year > 1
            || Game1.currentSeason != "spring"
            || Game1.dayOfMonth >= 5;
    }

    /// <summary>第一年春的第一茬（防风草约第 8 天）收成前，不请求作物。</summary>
    private static bool IsCropHarvestReady()
    {
        return Game1.year > 1
            || Game1.currentSeason != "spring"
            || Game1.dayOfMonth >= 8;
    }

    private static bool HasKitchen()
    {
        try
        {
            return Game1.player?.HouseUpgradeLevel >= 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasFarmBuilding(string buildingType)
    {
        try
        {
            return Game1.getFarm()?.buildings?.Any(building =>
                building?.buildingType?.Value?.Contains(buildingType, StringComparison.OrdinalIgnoreCase) == true) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>每日稳定轮换键：同日同 NPC 顺序固定（提示词缓存与调试可复现），跨天轮换。跨进程确定（FNV，不用 string.GetHashCode）。</summary>
    private static int DailyRotationKey(string itemId, string npcName)
    {
        int day;
        try
        {
            day = (int)(Game1.stats?.DaysPlayed ?? 0u);
        }
        catch
        {
            day = 0;
        }

        unchecked
        {
            return StableHash(itemId) ^ StableHash(npcName ?? string.Empty) ^ (day * 40503);
        }
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char character in value)
            {
                hash = (hash ^ char.ToLowerInvariant(character)) * 16777619;
            }

            return hash;
        }
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildRequestDepthGuidance(WorldProgressSnapshot progression)
    {
        return progression.ResidentStage switch
        {
            "first_spring_newcomer" =>
                "prefer one-step, low-stakes item requests; avoid requests that assume long shared history",
            "first_year_settling_in" =>
                "small one-step item requests fit best; a modest two-step item request is okay only when the current conversation strongly supports it",
            "second_year_established" =>
                "modest personal item requests and occasional two-step item favors can fit when trust and conversation support them",
            _ =>
                "deeper personal item requests or fuller multi-step item favors can fit when trust is high and the conversation earns them"
        };
    }

    private static string BuildRelationshipGuidance(int friendshipHearts)
    {
        return friendshipHearts switch
        {
            < 2 => "distant; choose only very easy seasonal forage or flowers",
            < 4 => "familiar; cheap everyday items may fit if character-specific",
            < 6 => "friendly; modest comfort items can fit",
            _ => "trusted; more personal low-value comfort items can fit"
        };
    }

    private static string BuildRouteGuidance(WorldProgressSnapshot progression)
    {
        string facilities = progression switch
        {
            { BusRepaired: false, GingerIslandUnlocked: false, MovieTheaterOpen: false } =>
                "do not rely on unrepaired travel or late-game facilities",
            _ =>
                "already-unlocked public facilities may be treated as ordinary parts of life"
        };

        string route = progression.Route switch
        {
            "community_center" => "the community center is already restored",
            "joja" => "the town followed the Joja route",
            _ => "the town route is still unresolved"
        };

        return $"{route}; {facilities}";
    }

    private sealed record HelpRequestProfile(
        string Theme,
        IReadOnlyCollection<string> Tags
    );

    private sealed record HelpRequestItem(
        string ItemId,
        string Label,
        IReadOnlyCollection<string> Tags,
        HelpRequestAvailability Availability = HelpRequestAvailability.Always,
        int MinFriendshipHearts = 0,
        IReadOnlyCollection<string>? Seasons = null
    );

    private enum HelpRequestAvailability
    {
        Always,
        MinesOpen,
        MineLevel40,
        MineLevel80,
        KitchenOrHearts2,
        CoopOwned,
        BarnOwned,
        Year2Plus,
        CropHarvestReady
    }
}
