using LivingNPCs.Dialogue.Content;

namespace LivingNPCs.Dialogue;

/// <summary>NPC 性别三态（传记派生，见 WP15 §3.4）。</summary>
internal enum PromptGender
{
    None,
    Male,
    Female
}

/// <summary>
/// 提示词骨架查找变体（01 §2 裁决）：携带 NPC 性别（驱动 <c>&lt;key&gt;.MaleNpc/.FemaleNpc</c>
/// 变体查找）与 optimized 开关（世界摘要精简版选择）。default 即无性别、非精简。
/// </summary>
internal readonly record struct PromptVariant(PromptGender NpcGender, bool Optimized);

/// <summary>
/// 跨包契约（01 §2）：WP15 实现，WP10 消费。成员由 01 钉死，不得私自增删。
/// </summary>
internal interface IDialogueContent
{
    /// <summary>传记（可被内容包 EditData 覆盖）。走完整加载链：名字规整 → 资产 → 回退 → 缓存。</summary>
    NpcBio GetBio(string npcName);

    /// <summary>按当前配置（SVE 合并 × 精简开关）选好的世界摘要数据。</summary>
    WorldSummary GetWorldSummary();

    /// <summary>提示词骨架查找（WP15 §4.2 三层规则）。查不到返回空串并每键警告一次。</summary>
    string GetPromptSkeleton(string key, PromptVariant variant = default);
}
