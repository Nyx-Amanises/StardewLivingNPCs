using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

/// <summary>
/// Small, bidirectional search-only equivalence groups. Unlike world discovery, these never
/// expand an activity into associated people, places, gifts, or personality traits.
/// </summary>
internal static class MemoryRecallVocabulary
{
    private static readonly Concept[] Concepts =
    {
        Topic("quiet|quietness|peace and quiet|安静|清静|宁静"),
        Topic("crowd|crowds|crowded|人群|人多|拥挤"),
        Topic("noise|noisy|loud noises|喧闹|嘈杂|吵闹"),
        Topic("anxious|anxiety|nervous|紧张|焦虑"),
        Topic("tired|fatigue|fatigued|疲惫|疲劳|累了"),
        Topic("morning|mornings|早晨|早上|清晨"),
        Topic("evening|evenings|傍晚|晚间"),
        Topic("night|nights|夜晚|夜间|晚上"),

        Topic("coffee|咖啡"),
        Topic("beer|啤酒"),
        Topic("wine|葡萄酒"),
        Topic("quartz|石英"),
        Topic("amethyst|紫水晶"),
        Topic("diamond|diamonds|钻石"),
        Topic("emerald|emeralds|绿宝石"),
        Topic("ruby|rubies|红宝石"),
        Topic("topaz|黄水晶"),
        Topic("jade|翡翠"),
        Topic("aquamarine|海蓝宝石"),
        Topic("strawberry|strawberries|草莓"),
        Topic("blueberry|blueberries|蓝莓"),
        Topic("pumpkin|pumpkins|南瓜"),
        Topic("parsnip|parsnips|防风草"),
        Topic("potato|potatoes|土豆|马铃薯"),
        Topic("cauliflower|花椰菜"),
        Topic("melon|melons|甜瓜|甜葫芦"),
        Topic("starfruit|杨桃"),
        Topic("cranberry|cranberries|蔓越莓"),

        Topic("fishing|angling|钓鱼|垂钓"),
        Topic("farming|种田|种地|耕种|务农"),
        Topic("mining|采矿|挖矿"),
        Topic("reading|read|reads|读书|阅读|看书"),
        Topic("book|books|书本|书籍"),
        Topic("cooking|cook|cooks|烹饪|做饭"),
        Topic("woodworking|carpentry|木工"),
        Topic("painting|paintings|绘画|画画"),
        Topic("sculpture|sculptures|sculpting|雕塑"),
        Topic("music|音乐"),
        Topic("guitar|吉他"),
        Topic("piano|钢琴"),
        Topic("dancing|跳舞|舞蹈"),
        Topic("swimming|游泳"),
        Topic("gardening|园艺"),
        Topic("programming|编程"),
        Topic("video games|videogames|电子游戏"),
        Topic("rain|rainy|下雨|雨天"),
        Topic("snow|snowy|下雪|雪天"),
        Topic("birthday|birthdays|生日"),
        Topic("wedding|weddings|婚礼"),
        Topic("marriage|结婚|婚姻"),
        Topic("divorce|离婚"),

        Topic("library|libraries|图书馆"),
        Topic("museum|museums|博物馆"),
        Topic("saloon|tavern|酒吧|酒馆"),
        Topic("mines|矿井|矿洞"),
        Topic("beach|beaches|海边|海滩"),
        Topic("farm|farms|农场"),
        Topic("forest|forests|森林"),
        Topic("Ginger Island|姜岛|生姜岛|姜之岛"),
        Topic("Calico Desert|卡利科沙漠|卡利可沙漠"),
        Topic("Skull Cavern|骷髅洞穴|骷髅矿洞|骷髅洞"),
        Topic("Community Center|Community Centre|社区中心"),

        // NPC aliases require an identity in the saved Subject. A passing mention in a summary
        // does not change who a memory is about. Ambiguous fruit names (Apples/Peaches) and broad
        // role words (wizard/巫师) deliberately remain ordinary lexical queries.
        Person("Abigail|阿比盖尔"), Person("Alex|亚历克斯"), Person("Caroline|卡罗琳"),
        Person("Clint|克林特"), Person("Demetrius|德米特里厄斯|德米特里乌斯"),
        Person("Elliott|艾利欧特|艾略特"), Person("Emily|艾米丽"),
        Person("Evelyn|艾芙琳|伊芙琳"), Person("George|乔治"), Person("Gus|格斯"),
        Person("Haley|海莉"), Person("Harvey|哈维"), Person("Jas|贾斯"), Person("Jodi|乔迪"),
        Person("Kent|肯特"), Person("Krobus|科罗布斯|克罗布斯"), Person("Leah|莉亚"),
        Person("Lewis|刘易斯"), Person("Linus|莱纳斯"), Person("Marnie|玛妮"), Person("Maru|玛鲁"),
        Person("Pam|帕姆"), Person("Penny|潘妮|潘尼"), Person("Pierre|皮埃尔"), Person("Robin|罗宾"),
        Person("Sam|山姆"), Person("Sandy|桑迪"), Person("Sebastian|塞巴斯蒂安"),
        Person("Shane|谢恩"), Person("Vincent|文森特"), Person("Willy|威利"),
        Person("Claire|克莱尔"), Person("Gunther|GuntherSilvian|冈瑟"),
        Person("Lance|兰斯"), Person("Marlon|MarlonFay|马龙"), Person("Martin|马丁"),
        Person("Morris|MorrisTod|莫里斯"), Person("Olivia|奥莉维亚|奥利维亚"),
        Person("Sophia|索菲亚|索菲娅"), Person("Susan|苏珊"), Person("Victor|维克多"), Person("Leo|雷欧")
    };

    public static IReadOnlyList<Concept> FindConcepts(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<Concept>();
        }

        // Match the query once, then check only these groups for each candidate. The shared
        // phrase matcher bounds input length and preserves Latin word/identifier boundaries.
        return Concepts.Where(concept => concept.Phrases.Any(phrase => LocalTextSearch.ContainsPhrase(text, phrase)))
            .ToArray();
    }

    private static Concept Topic(string phrases) => new(phrases.Split('|'), false);

    private static Concept Person(string phrases) => new(phrases.Split('|'), true);

    internal sealed class Concept
    {
        public string[] Phrases { get; }

        private readonly bool identitySubjectOnly;

        public Concept(string[] phrases, bool identitySubjectOnly)
        {
            this.Phrases = phrases;
            this.identitySubjectOnly = identitySubjectOnly;
        }

        public bool MatchesSubject(string? subject)
        {
            if (!this.identitySubjectOnly)
            {
                return this.Phrases.Any(phrase => LocalTextSearch.ContainsPhrase(subject, phrase));
            }

            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 256)
            {
                return false;
            }

            // Community recall passes "internal-name display-name". Accept that identity pair
            // as well as a single stored name, but not prose such as "Sam and Penny".
            string[] names = subject.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return names.Length is > 0 and <= 2
                && names.All(name => this.Phrases.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        public bool MatchesSummary(string? summary)
        {
            return !this.identitySubjectOnly
                && this.Phrases.Any(phrase => LocalTextSearch.ContainsPhrase(summary, phrase));
        }
    }
}
