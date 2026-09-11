using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingNPCs;

/// <summary>Bounded lexical search shared by local memory and world-knowledge retrieval.</summary>
internal static class LocalTextSearch
{
    private const int MaxInputCharacters = 8192;
    private const int MaxUniqueTokens = 1024;
    private const int MaxWordCharacters = 96;
    private const int MaxPhraseCharacters = 256;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "than", "so", "as", "at", "by",
        "for", "from", "in", "into", "of", "on", "onto", "to", "with", "without", "about",
        "above", "below", "over", "under", "between", "through", "during", "before", "after",
        "is", "am", "are", "was", "were", "be", "been", "being", "do", "does", "did", "done",
        "have", "has", "had", "having", "can", "could", "may", "might", "must", "shall",
        "should", "will", "would", "not", "no", "nor", "yes", "it", "its", "this", "that",
        "these", "those", "there", "here", "i", "me", "my", "myself", "you", "your",
        "yours", "yourself", "he", "him", "his", "himself", "she", "her", "hers", "herself",
        "we", "us", "our", "ours", "ourselves", "they", "them", "their", "theirs", "themselves",
        "who", "whom", "whose", "what", "which", "when", "where", "why", "how", "all", "any",
        "both", "each", "few", "more", "most", "other", "some", "such", "only", "own", "same",
        "very", "too", "just", "also", "again", "really", "please", "s", "t", "d", "ll", "re", "ve",
        "一个", "一下", "一些", "一点", "什么", "怎么", "怎样", "为什么", "这个", "那个",
        "这些", "那些", "我们", "你们", "他们", "她们", "它们", "自己", "可以", "能不能",
        "是不是", "有没有", "还是", "或者", "但是", "然后", "因为", "所以", "觉得", "知道",
        "记得", "今天", "现在", "最近", "之前", "上次", "已经", "正在", "真的", "请问",
        "我想", "想要", "你好", "谢谢", "是否", "的话", "了吗", "我的", "你的", "他的",
        "她的", "它的", "的是", "就是", "不是", "呢", "吗", "啊", "嗯", "了",
        "的", "我", "你", "他", "她", "它", "也", "和", "与", "在", "是", "就", "都", "很", "还", "吧"
    };

    /// <summary>
    /// Returns unique lower-case words and CJK bigrams. Short CJK runs also retain their whole
    /// phrase; no model, dictionary download, or persistent index is required.
    /// </summary>
    public static IReadOnlySet<string> Tokenize(string? text, int maxTokens = 256)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int tokenLimit = Math.Clamp(maxTokens, 0, MaxUniqueTokens);
        if (string.IsNullOrEmpty(text) || tokenLimit == 0)
        {
            return tokens;
        }

        int characterLimit = Math.Min(text.Length, MaxInputCharacters);
        int index = 0;
        while (index < characterLimit && tokens.Count < tokenLimit)
        {
            if (IsCjk(text[index]))
            {
                int start = index++;
                while (index < characterLimit && IsCjk(text[index]))
                {
                    index++;
                }

                int length = index - start;
                for (int offset = start; offset + 1 < index && tokens.Count < tokenLimit; offset++)
                {
                    AddToken(text.Substring(offset, 2), tokens);
                }

                if (length <= 4 && tokens.Count < tokenLimit)
                {
                    AddToken(text.Substring(start, length), tokens);
                }
            }
            else if (char.IsLetterOrDigit(text[index]))
            {
                int start = index++;
                while (index < characterLimit && IsWordCharacter(text[index]))
                {
                    index++;
                }

                int length = index - start;
                // A cut-off word must not turn a long identifier into a false exact match.
                bool complete = index < text.Length ? !IsWordCharacter(text[index]) : true;
                if (complete && length >= 2 && length <= MaxWordCharacters)
                {
                    AddToken(text.Substring(start, length).ToLowerInvariant(), tokens);
                }
            }
            else
            {
                index++;
            }
        }

        return tokens;
    }

    /// <summary>Matches a literal phrase, preserving Latin word boundaries and CJK substrings.</summary>
    public static bool ContainsPhrase(string? text, string? phrase)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(phrase) || phrase.Length > MaxPhraseCharacters)
        {
            return false;
        }

        phrase = phrase.Trim();
        if (phrase.Length == 0)
        {
            return false;
        }

        int characterLimit = Math.Min(text.Length, MaxInputCharacters);
        int offset = 0;
        while (offset <= characterLimit - phrase.Length)
        {
            int index = text.IndexOf(phrase, offset, characterLimit - offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            int end = index + phrase.Length;
            bool startBoundary = !IsWordCharacter(phrase[0]) || index == 0 || !IsWordCharacter(text[index - 1]);
            bool endBoundary = !IsWordCharacter(phrase[^1]) || end == text.Length || !IsWordCharacter(text[end]);
            if (startBoundary && endBoundary)
            {
                return true;
            }

            offset = index + 1;
        }

        return false;
    }

    private static void AddToken(string token, ISet<string> tokens)
    {
        if (!StopWords.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static bool IsWordCharacter(char value)
    {
        return !IsCjk(value)
            && (char.IsLetterOrDigit(value)
                || value == '_'
                || char.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark);
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\u3040' and <= '\u30FF'
            or >= '\u31F0' and <= '\u31FF'
            or >= '\uAC00' and <= '\uD7AF';
    }
}
