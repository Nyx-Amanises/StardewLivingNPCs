using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// Search vocabulary only, never additional world facts. Each line names one entity across asset IDs,
/// game IDs and English/Chinese display names. Content packs can add their own entry.Aliases.
/// </summary>
internal static class WorldEntryAliases
{
    private static readonly Dictionary<string, string[]> Entities = BuildEntities();

    private static readonly (string[] Phrases, IReadOnlySet<string> Terms)[] Topics =
    {
        Topic("钓鱼|钓竿|鱼饵|鱼钩|垂钓|fish|fishing|angling", "fish fishing angler bait tackle rod beach ocean river lake"),
        Topic("借书|还书|读书|阅读|看书|书籍|read|reading|book|books", "book books library reading reader literature museum"),
        Topic("教书|上课|学习|学校|学生|孩子|小孩|children|teaching", "teach teacher teaching tutor tutoring education children child school lessons students"),
        Topic("挖矿|采矿|矿石|宝石|矿物|mining|ores", "mine mines mining ore ores gem gems minerals cavern quarry"),
        Topic("打怪|怪物|冒险|战斗|武器|adventure|monsters", "adventure adventurer guild monsters danger combat sword mines cavern"),
        Topic("种地|种植|庄稼|作物|农作物|种子|耕种|farming|crops", "farm farming crops crop seeds planting harvest"),
        Topic("采集|野菜|野果|采摘|foraging", "forage foraging forest berries mushroom wild"),
        Topic("养殖|牧场|动物|奶牛|鸡舍|animal husbandry", "animals animal ranch livestock cow cows chicken chickens barn coop"),
        Topic("喝酒|饮酒|啤酒|酒馆|酒吧|喝一杯|drinking", "saloon drink drinks beer alcohol tavern"),
        Topic("吃饭|做饭|烹饪|料理|餐厅|cooking", "food cooking cook meals meal saloon kitchen"),
        Topic("看病|生病|健康|医生|医疗|health", "clinic doctor medical health medicine hospital"),
        Topic("木工|盖房|建房|建造|升级房子|木匠|carpentry", "carpenter carpentry robin building buildings construction house"),
        Topic("锻造|打铁|升级工具|铁匠|blacksmithing", "blacksmith clint forge tools tool upgrades ore"),
        Topic("魔法|巫师|女巫|祝尼魔|朱尼魔|magic", "magic magical wizard witch junimo junimos spirits"),
        Topic("送礼|礼物|生日|gifts", "gift gifts birthday friendship friends"),
        Topic("恋爱|约会|结婚|婚姻|浪漫|romance", "romance romantic dating marriage wedding love"),
        Topic("节日|节庆|庆典|festival|festivals", "festival festivals celebration dance fair feast"),
        Topic("季节|四季|seasons", "spring summer fall autumn winter seasons seasonal"),
        Topic("春天|春季|春日", "spring"),
        Topic("夏天|夏季|盛夏", "summer"),
        Topic("秋天|秋季|秋收", "fall autumn harvest"),
        Topic("冬天|冬季|下雪|雪天", "winter snow snowy ice"),
        Topic("下雨|雨天|暴雨|雷雨|天气|weather", "weather rain rainy storms storm snow sunny"),
        Topic("音乐|乐队|吉他|钢琴|music", "music musical musician band guitar piano"),
        Topic("画画|绘画|雕塑|艺术|art", "art artist painting sculptor sculpture"),
        Topic("运动|橄榄球|健身|sports", "sport sports athletic athlete gridball exercise"),
        Topic("电脑|编程|游戏|电子游戏|video games", "computer computers programmer programming games arcade"),
        Topic("海边|海滩|海洋|大海|beach", "beach ocean sea shore fishing tide"),
        Topic("葡萄园|酿酒|葡萄酒|vineyard", "vineyard wine grapes kegs"),
        Topic("博物馆|考古|古物|文物|捐赠|archaeology", "museum archaeology artifact artifacts donate donation library"),
        Topic("社区中心|献祭|收集包|community bundles", "community center bundles junimos restoration"),
        Topic("鹈鹕镇|小镇|镇上", "pelican town"),
        Topic("草莓", "strawberry strawberries"),
        Topic("蓝莓", "blueberry blueberries"),
        Topic("甜瓜|甜葫芦", "melon melons"),
        Topic("南瓜", "pumpkin pumpkins"),
        Topic("杨桃", "starfruit"),
        Topic("防风草", "parsnip parsnips"),
        Topic("土豆|马铃薯", "potato potatoes"),
        Topic("花椰菜", "cauliflower"),
        Topic("蔓越莓", "cranberry cranberries"),
        Topic("玉米", "corn"),
        Topic("蘑菇", "mushroom mushrooms morel chanterelle"),
        Topic("葡萄", "grape grapes vineyard"),
        Topic("鲑鱼莓|美洲大树莓", "salmonberry salmonberries"),
        Topic("黑莓", "blackberry blackberries")
    };

    internal static string[] ForEntry(string section, string key, WorldSummaryEntry entry)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? identity in new[] { key, entry.Id, entry.Name })
        {
            if (string.IsNullOrWhiteSpace(identity))
                continue;

            string trimmed = identity.Trim();
            aliases.Add(trimmed);
            if (Entities.TryGetValue(section + ":" + trimmed, out string[]? known))
                aliases.UnionWith(known);
        }

        if (entry.Aliases != null)
        {
            foreach (string? alias in entry.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    aliases.Add(alias.Trim());
            }
        }

        return aliases.Where(alias => alias.Length <= 256).ToArray();
    }

    internal static IReadOnlySet<string> QueryTerms(string? text, int maxTokens = 256)
    {
        var terms = new HashSet<string>(LocalTextSearch.Tokenize(text, maxTokens), StringComparer.Ordinal);
        foreach (var topic in Topics)
        {
            if (topic.Phrases.Any(phrase => LocalTextSearch.ContainsPhrase(text, phrase)))
                terms.UnionWith(topic.Terms);
        }

        return terms;
    }

    internal static string[] ForRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return Array.Empty<string>();
        string name = region.Trim();
        return Entities.TryGetValue("Locations:" + name, out string[]? aliases)
            ? aliases.ToArray()
            : new[] { name };
    }

    private static (string[] Phrases, IReadOnlySet<string> Terms) Topic(string phrases, string terms)
        => (phrases.Split('|'), LocalTextSearch.Tokenize(terms));

    private static Dictionary<string, string[]> BuildEntities()
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string section, params string[] rows)
        {
            foreach (string row in rows)
            {
                string[] aliases = row.Split('|');
                foreach (string alias in aliases)
                    result[section + ":" + alias] = aliases;
            }
        }

        Add("Seasons",
            "Spring|春天|春季|春日", "Summer|夏天|夏季|盛夏",
            "Fall|Autumn|秋天|秋季|秋日", "Winter|冬天|冬季|冬日");

        Add("Villagers",
            "Abigail|阿比盖尔", "Alex|亚历克斯", "Caroline|卡罗琳", "Clint|克林特",
            "Demetrius|德米特里厄斯|德米特里乌斯", "Elliott|艾利欧特|艾略特", "Emily|艾米丽",
            "Evelyn|艾芙琳|伊芙琳", "George|乔治", "Gus|格斯", "Haley|海莉", "Harvey|哈维",
            "Jas|贾斯", "Jodi|乔迪", "Kent|肯特", "Krobus|科罗布斯|克罗布斯", "Leah|莉亚",
            "Lewis|刘易斯", "Linus|莱纳斯", "Marnie|玛妮", "Maru|玛鲁", "Pam|帕姆", "Penny|潘妮|潘尼",
            "Pierre|皮埃尔", "Robin|罗宾", "Sam|山姆", "Sandy|桑迪", "Sebastian|塞巴斯蒂安",
            "Shane|谢恩", "Vincent|文森特", "Willy|威利", "Wizard|Magnus|Rasmodius|法师|巫师|拉斯莫迪斯|马格努斯",
            "Alesia|艾蕾西亚|阿莱西亚|阿莱西娅", "Andy|安迪", "Apples|苹果|苹果小精灵",
            "Camilla|卡米拉", "Claire|克莱尔", "Gunther|GuntherSilvian|冈瑟", "Hank|HankSVE|汉克",
            "Henchman|哥布林守卫|女巫仆从", "Isaac|艾萨克", "Jadu|贾杜|贾都", "Jolyne|乔琳",
            "Lance|兰斯", "Marlon|MarlonFay|马龙", "Martin|马丁", "Morgan|摩根", "Morris|MorrisTod|莫里斯",
            "Olivia|奥莉维亚|奥利维亚", "Peaches|桃子", "Scarlett|斯嘉丽", "Sophia|索菲亚|索菲娅",
            "Susan|苏珊", "Treyvon|特雷冯", "Victor|维克多", "Leo|雷欧", "Dwarf|矮人", "Gil|吉尔", "MrQi|Qi|齐先生");

        Add("Locations",
            "TheFarm|The Farm|Farm|农场",
            "PelicanTown_SeedShop|SeedShop|Pierre's General Store|Pierre's|杂货店|皮埃尔的杂货店|皮埃尔商店",
            "PelicanTown_JojaMart|JojaMart|Joja Mart|Joja超市|Joja 超市|乔家超市|乔贾超市",
            "PelicanTown_StardropSaloon|Saloon|The Stardrop Saloon|Stardrop Saloon|星之果实酒吧|酒吧|酒馆",
            "PelicanTown_HarveysClinic|Hospital|Clinic|Harvey's Clinic|哈维的诊所|诊所|医院",
            "PelicanTown_Blacksmith|Blacksmith|铁匠铺|铁匠店",
            "PelicanTown_LibraryMuseum|ArchaeologyHouse|Museum and Library|Museum|Library|博物馆|图书馆",
            "PelicanTown_CommunityCenter|CommunityCenter|Community Center|Community Centre|社区中心",
            "CindersapForest|Cindersap Forest|Forest|煤矿森林|森林",
            "CindersapForest_MarniesRanch|AnimalShop|Marnie's Ranch|玛妮牧场|玛妮的牧场",
            "CindersapForest_WizardsTower|WizardHouse|Wizard's Tower|法师塔|巫师塔",
            "Mountain|The Mountain|山地|山上",
            "Mountain_AdventurersGuild|AdventureGuild|Adventurer's Guild|Adventurers Guild|冒险者公会",
            "Mountain_TheMines|Mine|Mines|The Mines|矿井|矿洞|矿山",
            "Mountain_Spa|BathHouse_Entry|BathHouse|Spa|Bath House|温泉|澡堂|浴场",
            "Mountain_TrainStation|Railroad|Train Station|火车站|铁路",
            "Mountain_Quarry|Quarry|采石场",
            "Beach_FishShop|FishShop|Willy's Fish Shop|Fish Shop|威利的鱼店|鱼店",
            "GingerIsland|Ginger Island|IslandSouth|姜岛|生姜岛|姜之岛",
            "Beach_TidePools|Tide Pools|潮汐池|潮池",
            "Desert|Calico Desert|The Desert|沙漠|卡利科沙漠|卡利可沙漠",
            "Desert_Oasis|SandyHouse|Oasis|绿洲|绿洲商店",
            "Desert_SkullCavern|SkullCave|Skull Cavern|骷髅洞穴|骷髅矿洞|骷髅洞",
            "BlueMoonVineyard|Custom_BlueMoonVineyard|Blue Moon Vineyard|Sophia's Vineyard|蓝月葡萄园|索菲亚的葡萄园",
            "FairhavenFarm|Custom_FairhavenFarm|Fairhaven Farm|费尔黑文农场|安迪的农场",
            "AuroraVineyard|Custom_AuroraVineyard|Aurora Vineyard|极光葡萄园",
            "Grampleton|Custom_GrampletonCoast|Grampleton Coast|格兰普顿|格兰普顿海岸",
            "Highlands|Custom_Highlands|高地",
            "Town|Pelican Town|鹈鹕镇", "Beach|海滩|海边", "BusStop|Bus Stop|巴士站",
            "MovieTheater|Movie Theater|电影院", "Trailer|Trailer_Big|拖车|潘妮家|帕姆家",
            "Custom_ForestWest|West Cindersap|西部森林", "Custom_SVESummit|Summit|山顶",
            "Custom_GrandpasShedOutside|Grandpa's Shed|爷爷的棚屋", "Custom_JunimoWoods|Junimo Woods|祝尼魔森林",
            "Custom_EnchantedGrove|Enchanted Grove|魔法林地");

        Add("Festivals",
            "spring13|Egg Festival|复活节|彩蛋节|蛋节",
            "spring24|FlowerDance|Flower Dance|花舞节",
            "summer11|Luau|夏威夷宴会|夏威夷节|州长宴会",
            "summer28|Dance of the Moonlight Jellies|Moonlight Jellies|月光水母起舞|月光水母节|月光水母",
            "fall16|Stardew Valley Fair|星露谷展览会|星露谷博览会|展览会",
            "fall27|Spirit's Eve|Spirits Eve|万灵节|灵魂之夜",
            "winter8|Festival of Ice|冰雪节|冰雪庆典",
            "winter25|Feast of the Winter Star|Winter Star|冬日星盛宴|冬日星宴会",
            "DesertFestival|Desert Festival|沙漠节", "TroutDerby|Trout Derby|鳟鱼大赛",
            "SquidFest|Squid Fest|鱿鱼节", "NightMarket|Night Market|夜市");

        return result;
    }
}
