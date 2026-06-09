using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class GiftCatalog
{
    private static readonly IReadOnlyList<GiftCandidate> Candidates =
    [
        new("(O)194", "Fried Egg", "煎蛋", GiftTier.Small, ["food", "comfort", "practical"]),
        new("(O)195", "Omelet", "煎蛋卷", GiftTier.Small, ["food", "comfort", "practical"], Aliases: ["omelette", "菜肉蛋卷"]),
        new("(O)196", "Salad", "沙拉", GiftTier.Small, ["food", "nature", "refined"]),
        new("(O)197", "Cheese Cauliflower", "奶酪花椰菜", GiftTier.Small, ["food", "comfort", "homestyle"]),
        new("(O)198", "Baked Fish", "烤鱼", GiftTier.Small, ["food", "fish", "practical"]),
        new("(O)199", "Parsnip Soup", "防风草汤", GiftTier.Small, ["food", "comfort", "homestyle"]),
        new("(O)200", "Vegetable Medley", "蔬菜杂烩", GiftTier.Small, ["food", "nature", "homestyle"]),
        new("(O)201", "Complete Breakfast", "完全早餐", GiftTier.Small, ["food", "active", "comfort"]),
        new("(O)202", "Fried Calamari", "炸鱿鱼", GiftTier.Small, ["food", "fish", "refined"]),
        new("(O)203", "Strange Bun", "奇怪的小面包", GiftTier.Small, ["food", "adventurous", "magical"]),
        new("(O)205", "Fried Mushroom", "炒蘑菇", GiftTier.Small, ["food", "nature", "comfort"]),
        new("(O)206", "Pizza", "披萨", GiftTier.Small, ["food", "comfort", "youthful"]),
        new("(O)207", "Bean Hotpot", "豆类火锅", GiftTier.Small, ["food", "comfort", "nature"]),
        new("(O)208", "Glazed Yams", "琉璃山药", GiftTier.Small, ["food", "comfort", "sweet"], "fall"),
        new("(O)210", "Hashbrowns", "薯饼", GiftTier.Small, ["food", "comfort", "practical"]),
        new("(O)211", "Pancakes", "薄煎饼", GiftTier.Small, ["food", "comfort", "sweet"]),
        new("(O)212", "Salmon Dinner", "鲑鱼晚餐", GiftTier.Small, ["food", "fish", "refined"]),
        new("(O)213", "Fish Taco", "鱼肉卷", GiftTier.Small, ["food", "fish", "adventurous"]),
        new("(O)214", "Crispy Bass", "香酥鲈鱼", GiftTier.Small, ["food", "fish", "comfort"]),
        new("(O)215", "Pepper Poppers", "爆炒青椒", GiftTier.Small, ["food", "active", "adventurous"]),
        new("(O)216", "Bread", "面包", GiftTier.Small, ["food", "comfort", "practical"]),
        new("(O)218", "Tom Kha Soup", "椰汁汤", GiftTier.Small, ["food", "refined", "adventurous"]),
        new("(O)219", "Trout Soup", "鳟鱼汤", GiftTier.Small, ["food", "fish", "comfort"]),
        new("(O)222", "Rhubarb Pie", "大黄派", GiftTier.Small, ["food", "sweet", "comfort"], "spring"),
        new("(O)223", "Cookie", "曲奇", GiftTier.Small, ["food", "comfort", "sweet", "youthful"], Aliases: ["cookies", "饼干"]),
        new("(O)224", "Spaghetti", "意大利面", GiftTier.Small, ["food", "comfort"], Aliases: ["意面"]),
        new("(O)225", "Fried Eel", "炸鳗鱼", GiftTier.Small, ["food", "fish", "adventurous"]),
        new("(O)227", "Sashimi", "生鱼片", GiftTier.Small, ["food", "fish", "refined"]),
        new("(O)228", "Maki Roll", "生鱼寿司", GiftTier.Small, ["food", "fish", "refined"]),
        new("(O)229", "Tortilla", "墨西哥薄饼", GiftTier.Small, ["food", "practical", "comfort"]),
        new("(O)232", "Rice Pudding", "大米布丁", GiftTier.Small, ["food", "comfort", "sweet"]),
        new("(O)233", "Ice Cream", "冰淇淋", GiftTier.Small, ["food", "sweet", "youthful"]),
        new("(O)234", "Blueberry Tart", "蓝莓千层酥", GiftTier.Small, ["food", "sweet", "comfort"], Aliases: ["蓝莓挞"]),
        new("(O)238", "Cranberry Sauce", "蔓越莓酱", GiftTier.Small, ["food", "comfort", "sweet"], "fall"),
        new("(O)239", "Stuffing", "填料", GiftTier.Small, ["food", "comfort", "homestyle"], "fall"),
        new("(O)241", "Survival Burger", "救生汉堡", GiftTier.Small, ["food", "nature", "adventurous"]),
        new("(O)243", "Miner's Treat", "矿工特供", GiftTier.Small, ["food", "mineral", "adventurous", "sweet"]),
        new("(O)253", "Triple Shot Espresso", "三倍浓缩咖啡", GiftTier.Small, ["drink", "active", "work", "practical"]),
        new("(O)340", "Honey", "蜂蜜", GiftTier.Small, ["food", "sweet", "nature"]),
        new("(O)342", "Pickles", "腌菜", GiftTier.Small, ["food", "practical", "homestyle"]),
        new("(O)344", "Jelly", "果酱", GiftTier.Small, ["food", "sweet", "homestyle"]),
        new("(O)346", "Beer", "啤酒", GiftTier.Small, ["drink", "comfort"]),
        new("(O)350", "Juice", "果汁", GiftTier.Small, ["drink", "food", "refined"]),
        new("(O)376", "Poppy", "虞美人", GiftTier.Small, ["flower", "artistic", "special"], "summer"),
        new("(O)395", "Coffee", "咖啡", GiftTier.Small, ["drink", "practical", "scholarly", "work"]),
        new("(O)400", "Strawberry", "草莓", GiftTier.Small, ["food", "sweet", "nature"], "spring"),
        new("(O)403", "Field Snack", "工作小食", GiftTier.Small, ["food", "nature", "practical"]),
        new("(O)421", "Sunflower", "向日葵", GiftTier.Small, ["flower", "artistic", "comfort"], "summer"),
        new("(O)591", "Tulip", "郁金香", GiftTier.Small, ["flower", "artistic", "comfort"], "spring"),
        new("(O)593", "Summer Spangle", "夏季亮片", GiftTier.Small, ["flower", "artistic", "bright"], "summer"),
        new("(O)595", "Fairy Rose", "玫瑰仙子", GiftTier.Small, ["flower", "artistic", "magical"], "fall"),
        new("(O)597", "Blue Jazz", "蓝爵", GiftTier.Small, ["flower", "artistic", "comfort"], "spring"),
        new("(O)604", "Plum Pudding", "李子布丁", GiftTier.Small, ["food", "sweet", "comfort"], "winter"),
        new("(O)605", "Artichoke Dip", "洋蓟蘸酱", GiftTier.Small, ["food", "refined", "comfort"]),
        new("(O)606", "Stir Fry", "炒蔬菜", GiftTier.Small, ["food", "nature", "practical"]),
        new("(O)607", "Roasted Hazelnuts", "烤榛子", GiftTier.Small, ["food", "nature", "comfort"], "fall"),
        new("(O)609", "Radish Salad", "萝卜沙拉", GiftTier.Small, ["food", "nature", "refined"]),
        new("(O)610", "Fruit Salad", "水果沙拉", GiftTier.Small, ["food", "sweet", "refined"]),
        new("(O)611", "Blackberry Cobbler", "黑莓脆皮饼", GiftTier.Small, ["food", "sweet", "comfort"], "fall"),
        new("(O)612", "Cranberry Candy", "蔓越莓糖果", GiftTier.Small, ["food", "sweet", "youthful"], "fall"),
        new("(O)614", "Green Tea", "绿茶", GiftTier.Small, ["drink", "comfort", "scholarly", "refined"]),
        new("(O)618", "Bruschetta", "意式烤面包", GiftTier.Small, ["food", "refined", "artistic"]),
        new("(O)731", "Maple Bar", "枫糖棒", GiftTier.Small, ["food", "sweet", "comfort"]),
        new("(O)732", "Crab Cakes", "蟹饼", GiftTier.Small, ["food", "fish", "refined"]),

        new("(O)60", "Emerald", "绿宝石", GiftTier.Meaningful, ["mineral", "artistic", "special"]),
        new("(O)62", "Aquamarine", "海蓝宝石", GiftTier.Meaningful, ["mineral", "artistic", "refined", "special"]),
        new("(O)64", "Ruby", "红宝石", GiftTier.Meaningful, ["mineral", "adventurous", "special"]),
        new("(O)66", "Amethyst", "紫水晶", GiftTier.Meaningful, ["mineral", "adventurous", "magical", "artistic"]),
        new("(O)68", "Topaz", "黄水晶", GiftTier.Meaningful, ["mineral", "practical", "special"]),
        new("(O)70", "Jade", "翡翠", GiftTier.Meaningful, ["mineral", "artistic", "magical"]),
        new("(O)72", "Diamond", "钻石", GiftTier.Meaningful, ["mineral", "artistic", "refined", "special"]),
        new("(O)82", "Fire Quartz", "火水晶", GiftTier.Meaningful, ["mineral", "adventurous", "magical"]),
        new("(O)84", "Frozen Tear", "冰封泪晶", GiftTier.Meaningful, ["mineral", "comfort", "magical", "special"], Aliases: ["冰泪", "泪晶"]),
        new("(O)86", "Earth Crystal", "地晶", GiftTier.Meaningful, ["mineral", "nature", "magical"]),
        new("(O)204", "Lucky Lunch", "幸运午餐", GiftTier.Meaningful, ["food", "active", "special"]),
        new("(O)220", "Chocolate Cake", "巧克力蛋糕", GiftTier.Meaningful, ["food", "comfort", "sweet", "special"]),
        new("(O)221", "Pink Cake", "粉红蛋糕", GiftTier.Meaningful, ["food", "comfort", "sweet", "flower", "special"]),
        new("(O)226", "Spicy Eel", "香辣鳗鱼", GiftTier.Meaningful, ["food", "fish", "adventurous", "special"]),
        new("(O)230", "Red Plate", "红之盛宴", GiftTier.Meaningful, ["food", "active", "refined", "special"]),
        new("(O)231", "Eggplant Parmesan", "帕尔玛奶酪茄子", GiftTier.Meaningful, ["food", "comfort", "refined"]),
        new("(O)235", "Autumn's Bounty", "秋日恩赐", GiftTier.Meaningful, ["food", "nature", "comfort", "special"], "fall"),
        new("(O)236", "Pumpkin Soup", "南瓜汤", GiftTier.Meaningful, ["food", "comfort", "special"], "fall"),
        new("(O)237", "Super Meal", "巨无霸餐", GiftTier.Meaningful, ["food", "active", "special"]),
        new("(O)240", "Farmer's Lunch", "农夫午餐", GiftTier.Meaningful, ["food", "practical", "special"]),
        new("(O)242", "Dish O' The Sea", "海之菜肴", GiftTier.Meaningful, ["food", "fish", "special"]),
        new("(O)265", "Seafoam Pudding", "海泡布丁", GiftTier.Meaningful, ["food", "fish", "refined", "special"]),
        new("(O)394", "Rainbow Shell", "彩虹贝壳", GiftTier.Meaningful, ["nature", "artistic", "magical", "special"], "summer"),
        new("(O)424", "Cheese", "奶酪", GiftTier.Meaningful, ["food", "comfort", "artisan"]),
        new("(O)426", "Goat Cheese", "山羊奶酪", GiftTier.Meaningful, ["food", "comfort", "artisan", "refined"]),
        new("(O)445", "Caviar", "鱼子酱", GiftTier.Meaningful, ["food", "fish", "refined", "special"]),
        new("(O)446", "Rabbit's Foot", "兔子的脚", GiftTier.Meaningful, ["nature", "magical", "special"]),
        new("(O)608", "Pumpkin Pie", "南瓜派", GiftTier.Meaningful, ["food", "comfort", "sweet", "special"], "fall"),
        new("(O)649", "Fiddlehead Risotto", "意式蕨菜炖饭", GiftTier.Meaningful, ["food", "nature", "refined", "special"]),
        new("(O)730", "Lobster Bisque", "龙虾浓汤", GiftTier.Meaningful, ["food", "fish", "refined", "special"]),
        new("(O)733", "Shrimp Cocktail", "虾鸡尾酒", GiftTier.Meaningful, ["food", "fish", "refined", "special"]),
        new("(O)907", "Tropical Curry", "热带咖喱", GiftTier.Meaningful, ["food", "adventurous", "refined", "special"])
    ];

    private static readonly IReadOnlyDictionary<string, GiftCandidate> CandidatesById =
        Candidates.ToDictionary(candidate => candidate.ItemId, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> CommonSmallItemIds = new HashSet<string>(
        [
            "(O)194", "(O)195", "(O)196", "(O)198", "(O)200", "(O)206",
            "(O)210", "(O)211", "(O)216", "(O)219", "(O)223", "(O)224",
            "(O)227", "(O)228", "(O)229", "(O)232", "(O)233", "(O)234",
            "(O)239", "(O)241", "(O)253", "(O)340", "(O)344", "(O)395",
            "(O)400", "(O)403", "(O)421", "(O)591", "(O)593", "(O)595",
            "(O)597", "(O)610", "(O)612", "(O)614", "(O)618", "(O)731"
        ],
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly IReadOnlySet<string> CommonMeaningfulItemIds = new HashSet<string>(
        [
            "(O)72", "(O)220", "(O)221", "(O)230", "(O)235", "(O)236",
            "(O)237", "(O)240", "(O)242", "(O)424", "(O)426", "(O)608",
            "(O)649", "(O)730", "(O)733", "(O)907"
        ],
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly IReadOnlyDictionary<string, NpcGiftPool> VanillaNpcPools =
        new Dictionary<string, NpcGiftPool>(StringComparer.OrdinalIgnoreCase)
        {
            ["Abigail"] = new("阿比盖尔", ["(O)203"], ["(O)66", "(O)84", "(O)226"]),
            ["Alex"] = new("亚历克斯", ["(O)201", "(O)215", "(O)350"], ["(O)204", "(O)226"]),
            ["Caroline"] = new("卡洛琳", ["(O)207", "(O)376", "(O)606", "(O)609"], ["(O)60"]),
            ["Clint"] = new("克林特", ["(O)205", "(O)243"], ["(O)68", "(O)82", "(O)86"]),
            ["Demetrius"] = new("德米特里厄斯", ["(O)205", "(O)350", "(O)606"], ["(O)60", "(O)394"]),
            ["Dwarf"] = new("矮人", ["(O)203", "(O)243"], ["(O)68", "(O)82", "(O)86"]),
            ["Elliott"] = new("艾利欧特", ["(O)202", "(O)218", "(O)350"], ["(O)62", "(O)394", "(O)445"]),
            ["Emily"] = new("艾米丽", ["(O)350", "(O)376", "(O)606"], ["(O)60", "(O)66", "(O)70"]),
            ["Evelyn"] = new("艾芙琳", ["(O)222", "(O)604", "(O)611"], ["(O)446"]),
            ["George"] = new("乔治", ["(O)199", "(O)205", "(O)208"], ["(O)231"]),
            ["Gus"] = new("格斯", ["(O)197", "(O)605", "(O)732"], ["(O)231", "(O)445"]),
            ["Haley"] = new("海莉", ["(O)222", "(O)350", "(O)376"], ["(O)62", "(O)394", "(O)446"]),
            ["Harvey"] = new("哈维", ["(O)342", "(O)350", "(O)609"], ["(O)62", "(O)204"]),
            ["Jas"] = new("贾斯", ["(O)222", "(O)604", "(O)611"], ["(O)446"]),
            ["Jodi"] = new("乔迪", ["(O)197", "(O)208", "(O)605"], ["(O)231"]),
            ["Kent"] = new("肯特", ["(O)199", "(O)208", "(O)238"], ["(O)204"]),
            ["Krobus"] = new("科罗布斯", ["(O)203", "(O)205", "(O)342"], ["(O)84", "(O)86"]),
            ["Leah"] = new("莉亚", ["(O)605", "(O)606", "(O)609"], ["(O)60", "(O)231", "(O)394"]),
            ["Leo"] = new("雷欧", ["(O)202", "(O)213", "(O)350"], ["(O)265", "(O)394"]),
            ["Lewis"] = new("刘易斯", ["(O)199", "(O)208", "(O)607"], ["(O)204", "(O)445"]),
            ["Linus"] = new("莱纳斯", ["(O)205", "(O)342", "(O)607"], ["(O)86", "(O)394"]),
            ["Marnie"] = new("玛妮", ["(O)197", "(O)201", "(O)607"], ["(O)231", "(O)446"]),
            ["Maru"] = new("玛鲁", ["(O)201", "(O)243", "(O)350"], ["(O)60", "(O)68"]),
            ["Pam"] = new("潘姆", ["(O)208", "(O)215", "(O)346"], ["(O)204", "(O)226"]),
            ["Penny"] = new("潘妮", ["(O)222", "(O)376", "(O)611"], ["(O)60", "(O)62"]),
            ["Pierre"] = new("皮埃尔", ["(O)342", "(O)605", "(O)607"], ["(O)204", "(O)445"]),
            ["Robin"] = new("罗宾", ["(O)197", "(O)201", "(O)215"], ["(O)68", "(O)86"]),
            ["Sam"] = new("山姆", ["(O)215", "(O)350", "(O)732"], ["(O)64", "(O)226"]),
            ["Sandy"] = new("桑迪", ["(O)222", "(O)350", "(O)376"], ["(O)62", "(O)394"]),
            ["Sebastian"] = new("塞巴斯蒂安", ["(O)203", "(O)205", "(O)346"], ["(O)64", "(O)82", "(O)84"]),
            ["Shane"] = new("谢恩", ["(O)201", "(O)215", "(O)346"], ["(O)204", "(O)226"]),
            ["Vincent"] = new("文森特", ["(O)350", "(O)604", "(O)611"], ["(O)446"]),
            ["Willy"] = new("威利", ["(O)202", "(O)212", "(O)213", "(O)214", "(O)225", "(O)732"], ["(O)265", "(O)394", "(O)445"]),
            ["Wizard"] = new("法师", ["(O)203", "(O)205", "(O)243"], ["(O)66", "(O)82", "(O)84", "(O)86"])
        };

    static GiftCatalog()
    {
        ValidateItemIds(CommonSmallItemIds, GiftTier.Small, "common small");
        ValidateItemIds(CommonMeaningfulItemIds, GiftTier.Meaningful, "common meaningful");
        foreach ((string npcName, NpcGiftPool pool) in VanillaNpcPools)
        {
            ValidateItemIds(pool.SmallItemIds, GiftTier.Small, $"{npcName} small");
            ValidateItemIds(pool.MeaningfulItemIds, GiftTier.Meaningful, $"{npcName} meaningful");
        }
    }

    public static int CandidateCount => Candidates.Count;

    public static IReadOnlyDictionary<string, NpcGiftPool> VanillaPersonalizedPools => VanillaNpcPools;

    public static IReadOnlyList<GiftCandidate> GetCommonCandidates(GiftTier tier)
    {
        IReadOnlySet<string> itemIds = tier == GiftTier.Meaningful
            ? CommonMeaningfulItemIds
            : CommonSmallItemIds;
        return Candidates
            .Where(candidate => candidate.Tier == tier && itemIds.Contains(candidate.ItemId))
            .ToList();
    }

    public static IReadOnlyList<GiftCandidate> GetPersonalizedCandidates(string npcName, GiftTier tier)
    {
        if (!VanillaNpcPools.TryGetValue(npcName, out NpcGiftPool? pool))
        {
            return Array.Empty<GiftCandidate>();
        }

        IReadOnlyList<string> itemIds = tier == GiftTier.Meaningful
            ? pool.MeaningfulItemIds
            : pool.SmallItemIds;
        return itemIds.Select(itemId => CandidatesById[itemId]).ToList();
    }

    public static IReadOnlyList<GiftCandidate> GetAvailableCandidates(string npcName, GiftTier tier)
    {
        return GetCommonCandidates(tier)
            .Concat(GetPersonalizedCandidates(npcName, tier))
            .DistinctBy(candidate => candidate.ItemId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static GiftCandidate? FindAvailableCandidate(string npcName, GiftTier tier, string itemId)
    {
        return GetAvailableCandidates(npcName, tier).FirstOrDefault(candidate =>
            candidate.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static GiftCandidate? FindCandidate(string itemId)
    {
        return CandidatesById.TryGetValue(itemId, out GiftCandidate? candidate)
            ? candidate
            : null;
    }

    public static bool IsPersonalizedFor(string npcName, GiftTier tier, string itemId)
    {
        return GetPersonalizedCandidates(npcName, tier).Any(candidate =>
            candidate.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static bool TextMentionsCandidate(string text, GiftCandidate candidate)
    {
        if (text.Contains(candidate.DebugName, StringComparison.OrdinalIgnoreCase)
            || text.Contains(candidate.ChineseName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.Aliases?.Any(alias =>
            text.Contains(alias, StringComparison.OrdinalIgnoreCase)
        ) == true;
    }

    public static string BuildPromptList(string npcName, GiftTier tier, bool personalized)
    {
        IReadOnlyList<GiftCandidate> candidates = personalized
            ? GetPersonalizedCandidates(npcName, tier)
            : GetCommonCandidates(tier);
        return candidates.Count == 0
            ? "none"
            : string.Join(", ", candidates.Select(candidate => $"{candidate.DebugName} {candidate.ItemId}"));
    }

    private static void ValidateItemIds(IEnumerable<string> itemIds, GiftTier tier, string source)
    {
        foreach (string itemId in itemIds)
        {
            if (!CandidatesById.TryGetValue(itemId, out GiftCandidate? candidate))
            {
                throw new InvalidOperationException($"Unknown gift item ID {itemId} in {source} pool.");
            }

            if (candidate.Tier != tier)
            {
                throw new InvalidOperationException($"Gift item ID {itemId} has tier {candidate.Tier}, but {source} expects {tier}.");
            }
        }
    }
}

internal sealed record GiftCandidate(
    string ItemId,
    string DebugName,
    string ChineseName,
    GiftTier Tier,
    IReadOnlyList<string> Tags,
    string Season = "",
    IReadOnlyList<string>? Aliases = null
);

internal sealed record NpcGiftPool(
    string ChineseName,
    IReadOnlyList<string> SmallItemIds,
    IReadOnlyList<string> MeaningfulItemIds
);

internal enum GiftTier
{
    Small,
    Meaningful
}
