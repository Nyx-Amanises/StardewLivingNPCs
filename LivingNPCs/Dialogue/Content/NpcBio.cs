using System.Collections.Generic;
using Newtonsoft.Json;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// Bios/&lt;Name&gt; 资产的反序列化目标（WP15 §3.4，JSON 字段名为兼容契约按原样保留）。
/// 运行时派生字段（性别、话题池、有效肖像集）由加载链填充，不参与序列化。
/// </summary>
internal sealed class NpcBio
{
    /// <summary>传记正文（多段 \n 分段）。空白视为"无传记内容"，触发回退链（§4.3）。</summary>
    public string Biography { get; set; } = string.Empty;

    public Dictionary<string, BioListEntry> Relationships { get; set; } = new();

    public Dictionary<string, BioListEntry> Traits { get; set; } = new();

    /// <summary>传记收尾段（拼在人际/特质之后）。</summary>
    public string BiographyEnd { get; set; } = string.Empty;

    /// <summary>
    /// 性别覆盖口：仅当值等于提示词键 generalMale/generalFemale 的本地化值（忽略大小写）才生效；
    /// 否则忽略（正常情况下性别由游戏 Data/Characters 按 NPC 名推导，裁决 4：此口在新实现真正生效）。
    /// </summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>NPC 独有的描述短语；不会自动注册为肖像标记。</summary>
    public string Unique { get; set; } = string.Empty;

    /// <summary>额外肖像帧：键为明确配置的肖像代号（如 "7"、"u"），值为该表情的英文短描述。</summary>
    public Dictionary<string, string> ExtraPortraits { get; set; } = new();

    /// <summary>可选"近期心事"话题池（加载后与最爱/最恨礼物名合并为 TopicPool）。</summary>
    public List<string> Preoccupations { get; set; } = new();

    /// <summary>补充对话样本：键为原版对话键（如 Mon），值为原版对话格式字符串；覆盖同键的游戏对话样本。</summary>
    public Dictionary<string, string> Dialogue { get; set; } = new();

    /// <summary>标记 NPC 家中卧床位置可用于就寝语境（旧世界消费点已注释，纯保留字段）。</summary>
    public bool HomeLocationBed { get; set; }

    /// <summary>按提示词元素名覆盖默认骨架（资产口；interop 运行时口归 WP16）。</summary>
    public Dictionary<string, string> PromptOverrides { get; set; } = new();

    /// <summary>true 时即使内容包未授权 AI（§4.5）也允许采用打过补丁的对话样本。</summary>
    public bool UsePatchedDialogue { get; set; }

    /// <summary>加载器标记"无传记"的哨兵，不出现在 JSON。</summary>
    [JsonIgnore]
    public bool Missing { get; set; }

    /// <summary>派生：三态性别（游戏数据推导 + Gender 字段覆盖口）。</summary>
    [JsonIgnore]
    public PromptGender ResolvedGender { get; set; } = PromptGender.None;

    /// <summary>派生：Preoccupations ∪ 最爱/最恨礼物显示名（§4.3 步骤 4）。</summary>
    [JsonIgnore]
    public List<string> TopicPool { get; set; } = new();

    /// <summary>派生：有效肖像集 = {0,h,s,l,a}，再加明确配置的 u 或数字 ExtraPortraits 键。</summary>
    [JsonIgnore]
    public HashSet<string> ValidPortraits { get; set; } = new();

    /// <summary>
    /// 反序列化后归一：第三方传记 JSON 里显式写 null 的字段会被 Json.NET 覆盖掉属性初始化器
    /// （NRT 管不到反序列化），一律回退空实例，保证加载链与消费端拿到的集合/字符串永不为 null。
    /// 幂等，可重复调用。
    /// </summary>
    public void NormalizeNullFields()
    {
        this.Biography ??= string.Empty;
        this.Relationships ??= new Dictionary<string, BioListEntry>();
        this.Traits ??= new Dictionary<string, BioListEntry>();
        this.BiographyEnd ??= string.Empty;
        this.Gender ??= string.Empty;
        this.Unique ??= string.Empty;
        this.ExtraPortraits ??= new Dictionary<string, string>();
        this.Preoccupations ??= new List<string>();
        this.Dialogue ??= new Dictionary<string, string>();
        this.PromptOverrides ??= new Dictionary<string, string>();
    }
}

/// <summary>人际关系/性格特质条目（旧名 ListEntry，JSON 字段名为兼容契约）。</summary>
internal sealed class BioListEntry
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    public string Heading { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 农夫子女的纯数据描述（WP15 §5）：WP16 从游戏 Child 转换、WP10 拼 children* 提示词时消费。
/// </summary>
internal sealed record ChildDescription(string Name, bool IsMale, int Age);
