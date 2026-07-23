using System;
using System.Globalization;

namespace LivingNPCs.Dialogue.Content;

/// <summary>Stardew Valley 原生对话可消费的肖像标记规则。</summary>
internal static class PortraitMarkerRules
{
    // Portrait sheets in normal content packs are tiny. This cap only rejects malformed/model-made
    // integers before they reach Stardew's dialogue parser; physical bounds are checked separately.
    public const int MaxSupportedFrameIndex = 4095;

    /// <summary>将帧索引转换为游戏的规范肖像标记。</summary>
    public static string? MarkerForIndex(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex > MaxSupportedFrameIndex)
        {
            return null;
        }

        return frameIndex switch
        {
            0 => "0",
            1 => "h",
            2 => "s",
            3 => "u",
            4 => "l",
            5 => "a",
            _ => frameIndex.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>解析游戏肖像标记；拒绝前导零、溢出和不支持的字母别名。</summary>
    public static bool TryGetFrameIndex(string? marker, out int frameIndex)
    {
        frameIndex = -1;
        if (string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        marker = marker.Trim();
        if (marker.Length == 1)
        {
            frameIndex = char.ToLowerInvariant(marker[0]) switch
            {
                'h' => 1,
                's' => 2,
                'u' => 3,
                'l' => 4,
                'a' => 5,
                _ => -1
            };
            if (frameIndex >= 0)
            {
                return true;
            }
        }

        if (marker.Length > 1 && marker[0] == '0')
        {
            return false;
        }

        foreach (char character in marker)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(marker, NumberStyles.None, CultureInfo.InvariantCulture, out frameIndex)
            && frameIndex <= MaxSupportedFrameIndex;
    }

    /// <summary>规范化游戏能消费的标记；标准索引统一为 0/h/s/u/l/a。</summary>
    public static string? NormalizeGameMarker(string? marker)
    {
        string trimmed = (marker ?? string.Empty).Trim();
        if (!TryGetFrameIndex(trimmed, out int frameIndex))
        {
            return null;
        }

        // The model must use the canonical letters for standard expressions. Treating a price-like
        // `$5` as `$a` was one direct route to accidental angry portraits.
        bool isCanonicalStandard = trimmed.Length == 1
            && (trimmed[0] == '0' || char.ToLowerInvariant(trimmed[0]) is 'h' or 's' or 'u' or 'l' or 'a');
        return isCanonicalStandard || frameIndex >= 6
            ? MarkerForIndex(frameIndex)
            : null;
    }

    /// <summary>传记额外肖像只允许显式 u 帧或索引 6 以上的数字帧。</summary>
    public static string? NormalizeExtraMarker(string? marker)
    {
        string trimmed = (marker ?? string.Empty).Trim();
        if (!TryGetFrameIndex(trimmed, out int frameIndex))
        {
            return null;
        }

        string? normalized = MarkerForIndex(frameIndex);
        bool explicitUnique = trimmed.Length == 1 && char.ToLowerInvariant(trimmed[0]) == 'u';
        return explicitUnique || frameIndex >= 6 ? normalized : null;
    }
}
