using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Content;
using StardewValley;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// NPC 对话样本的选取与净化（WP15 §4.5）。存在未授权内容、或该角色扩展兼容判 false 时
/// （且传记未设 UsePatchedDialogue），不用 NPC 当前对话，改由独立 ContentManager 从原始
/// 游戏资产加载净版；婚后对话键加 M_ 前缀合并。最后无条件用传记 Dialogue 字典覆盖同键条目。
/// </summary>
internal static class DialogueSampleLoader
{
    public static Dictionary<string, string> GetSamples(NPC? npc, NpcBio bio, bool expansionCompatible)
    {
        bool useCleanLoad = (ThirdPartyContentPolicy.HasUnauthorizedContent || !expansionCompatible)
            && !bio.UsePatchedDialogue;

        Dictionary<string, string> samples;
        if (useCleanLoad)
        {
            samples = LoadClean(npc?.Name ?? string.Empty);
        }
        else
        {
            samples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (npc?.Dialogue != null)
                {
                    foreach (var pair in npc.Dialogue)
                    {
                        samples[pair.Key] = pair.Value;
                    }
                }
            }
            catch
            {
                // NPC 对话字典不可用（极端时序）：保持已收集的部分。
            }
        }

        foreach (var pair in bio.Dialogue)
        {
            samples[pair.Key] = pair.Value;
        }

        return samples;
    }

    /// <summary>独立 ContentManager 加载净版对话（绕开一切运行时补丁）。失败静默返回已得部分。</summary>
    private static Dictionary<string, string> LoadClean(string npcName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return result;
        }

        ContentManager? content = null;
        try
        {
            content = new ContentManager(Game1.content.ServiceProvider, Game1.content.RootDirectory);
            MergeFirstAvailable(content, $"Characters\\Dialogue\\{npcName}", result, keyPrefix: null);
            MergeFirstAvailable(content, $"Characters\\Dialogue\\MarriageDialogue{npcName}", result, keyPrefix: "M_");
        }
        catch
        {
            // 建管线本身失败：无净版样本可用。
        }
        finally
        {
            content?.Dispose();
        }

        return result;
    }

    /// <summary>按语言后缀序列逐个尝试（当前 locale 及其父级，如 .zh-CN → .zh → 空），取第一个能加载的。</summary>
    private static void MergeFirstAvailable(ContentManager content, string baseAssetName, Dictionary<string, string> target, string? keyPrefix)
    {
        foreach (string suffix in LanguageSuffixes())
        {
            try
            {
                var loaded = content.Load<Dictionary<string, string>>(baseAssetName + suffix);
                foreach (var pair in loaded)
                {
                    target[keyPrefix == null ? pair.Key : keyPrefix + pair.Key] = pair.Value;
                }

                return;
            }
            catch
            {
                // 该语言变体不存在，试下一个。
            }
        }
    }

    internal static IEnumerable<string> LanguageSuffixes()
    {
        string locale = DialogueServices.Helper?.GameContent.CurrentLocale ?? string.Empty;
        return LanguageSuffixesFor(locale);
    }

    /// <summary>en-US 只有空后缀；其余 locale 依次给 ".{locale}"、".{语言码}"、""。</summary>
    internal static IEnumerable<string> LanguageSuffixesFor(string locale)
    {
        if (!string.IsNullOrWhiteSpace(locale) && !locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            yield return "." + locale;
            int dash = locale.IndexOf('-');
            if (dash > 0)
            {
                yield return "." + locale[..dash];
            }
        }

        yield return string.Empty;
    }
}
