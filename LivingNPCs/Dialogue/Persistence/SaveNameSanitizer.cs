using System.Linq;
using System.Text;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// NPC 内部名 → 存档键片段的净化规则（WP14 §3.1.1，逐字精确的格式契约）：
/// 删除所有不在合法集合中的字符；删完为空则改用原名每个 char 强转 byte 的大写十六进制串
/// （无分隔符）；超过 50 字符截断到 50。迁移反查与新键构造共用同一规则。
/// </summary>
internal static class SaveNameSanitizer
{
    private const string LegalChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.";
    private const int MaxLength = 50;

    public static string Sanitize(string npcName)
    {
        npcName ??= string.Empty;
        var builder = new StringBuilder(npcName.Length);
        foreach (char ch in npcName)
        {
            if (LegalChars.Contains(ch))
            {
                builder.Append(ch);
            }
        }

        string sanitized = builder.ToString();
        if (sanitized.Length == 0)
        {
            var hex = new StringBuilder(npcName.Length * 2);
            foreach (char ch in npcName)
            {
                hex.Append(((byte)ch).ToString("X2"));
            }

            sanitized = hex.ToString();
        }

        return sanitized.Length > MaxLength ? sanitized[..MaxLength] : sanitized;
    }

    /// <summary>新存档键的名字段：净化后再转小写（§5.1，物理键本就整体小写，避免大小写陷阱）。
    /// 注意：本方法保持旧格式逐字契约不变（迁移反查旧 ValleyTalk 键依赖它），有碰撞风险的
    /// 场景由 <see cref="SanitizeUniqueLower"/> 承接。</summary>
    public static string SanitizeLower(string npcName)
    {
        return Sanitize(npcName).ToLowerInvariant();
    }

    private const int UniqueHeadLength = 24;

    /// <summary>
    /// 无碰撞净化（F8，v2 键格式）：名字全部由合法字符组成且不超长时与
    /// <see cref="SanitizeLower"/> 完全一致（绝大多数 NPC 的键不变、零迁移）；
    /// 一旦有字符被删除、为空或超长（v1 的三类碰撞源：删字符合并、低字节十六进制截断、
    /// 50 字符截断），改用"合法字符头（≤24）+ '-u' + 全名逐 UTF-16 码元 4 位十六进制"，
    /// 编码对原名单射，不同名字必得不同键。读取端对 v1 旧键做回退迁移（DialogueHistoryStore）。
    /// </summary>
    public static string SanitizeUniqueLower(string npcName)
    {
        npcName ??= string.Empty;
        var kept = new StringBuilder(npcName.Length);
        foreach (char ch in npcName)
        {
            if (LegalChars.Contains(ch))
            {
                kept.Append(ch);
            }
        }

        bool lossless = kept.Length == npcName.Length && kept.Length <= MaxLength;
        if (lossless)
        {
            return kept.ToString().ToLowerInvariant();
        }

        string head = kept.Length > UniqueHeadLength ? kept.ToString(0, UniqueHeadLength) : kept.ToString();
        var hex = new StringBuilder(npcName.Length * 4);
        foreach (char ch in npcName)
        {
            hex.Append(((ushort)ch).ToString("x4"));
        }

        return (head + "-u" + hex).ToLowerInvariant();
    }
}
