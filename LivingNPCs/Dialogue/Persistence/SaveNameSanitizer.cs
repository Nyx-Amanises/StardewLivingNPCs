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

    /// <summary>新存档键的名字段：净化后再转小写（§5.1，物理键本就整体小写，避免大小写陷阱）。</summary>
    public static string SanitizeLower(string npcName)
    {
        return Sanitize(npcName).ToLowerInvariant();
    }
}
