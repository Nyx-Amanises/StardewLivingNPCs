using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 第三方内容授权扫描（WP15 §4.5）：启动时检查全部已装内容包的 manifest 扩展字段
/// <c>PermitAiUse</c>（布尔或可解析字符串；生态契约原样保留）。存在未授权内容时置全局标志，
/// 对话样本改走净版加载（内容照常显示于游戏，只是不进 AI 上下文）。
/// 无硬编码白名单（WP12 裁决 7：纯 manifest 字段驱动）。
/// </summary>
internal static class ThirdPartyContentPolicy
{
    /// <summary>true = 至少一个已装内容包未授权 AI 使用其文本。</summary>
    public static bool HasUnauthorizedContent { get; private set; }

    public static void Scan(IModRegistry registry, IMonitor? monitor)
    {
        List<string> offenders = registry.GetAll()
            .Where(mod => mod.Manifest?.ContentPackFor != null && !PermitsAiUse(mod.Manifest))
            .Select(mod => mod.Manifest!.UniqueID)
            .ToList();

        HasUnauthorizedContent = offenders.Count > 0;
        if (!HasUnauthorizedContent)
        {
            return;
        }

        monitor?.Log(
            $"[LivingNPCs] {offenders.Count} installed content pack(s) do not grant AI use of their text (missing manifest field 'PermitAiUse: true').",
            LogLevel.Warn);
        monitor?.Log(
            $"[LivingNPCs] Affected packs: {string.Join(", ", offenders.Take(10))}{(offenders.Count > 10 ? ", …" : string.Empty)}",
            LogLevel.Warn);
        monitor?.Log(
            "[LivingNPCs] Their content still shows in game as usual, but AI dialogue falls back to unpatched vanilla dialogue samples.",
            LogLevel.Warn);
    }

    /// <summary>测试/会话重置。</summary>
    internal static void ResetForTests(bool hasUnauthorizedContent = false)
    {
        HasUnauthorizedContent = hasUnauthorizedContent;
    }

    internal static bool PermitsAiUse(IManifest manifest)
    {
        if (manifest.ExtraFields == null || !manifest.ExtraFields.TryGetValue("PermitAiUse", out object? raw) || raw == null)
        {
            return false;
        }

        return raw switch
        {
            bool flag => flag,
            _ => bool.TryParse(raw.ToString(), out bool parsed) && parsed
        };
    }
}
