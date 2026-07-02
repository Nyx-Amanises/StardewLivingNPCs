using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ValleyTalk
{
    public class Util
    {
        private static StardewModdingAPI.ITranslationHelper _translationHelper => ModEntry.SHelper?.Translation;

        private static readonly HashSet<string> _warnedMissingPromptKeys = new();

        /// <summary>
        /// Logs (once per key) when a prompt key resolves to nothing. Missing keys fail silently
        /// otherwise — a typo'd key or a translation without an assets/Prompts.json mapping just
        /// blanks out that prompt section, which has hidden whole context blocks in the past.
        /// </summary>
        private static void WarnMissingPromptKey(string key)
        {
            if (_warnedMissingPromptKeys.Add(key))
            {
                ModEntry.SMonitor?.Log(
                    $"Prompt key '{key}' has no value in the ValleyTalk/Prompts asset (check assets/Prompts.json and i18n); that prompt section will be blank.",
                    StardewModdingAPI.LogLevel.Warn
                );
            }
        }

        public static IEnumerable<NPC> GetNearbyNpcs(NPC npc)
        {
            // Check for any other NPCs within 3 squares
            var speakerLocation = npc.Tile;
            var speakerName = npc.Name;
            var npcs = Game1.currentLocation.characters.Where(x => x.CanReceiveGifts() && x.Name != speakerName);
            List<NPC> nearbyNpcs = new List<NPC>();
            foreach (var otherNpc in npcs)
            {
                var npcLocation = otherNpc.Tile;
                if (Microsoft.Xna.Framework.Vector2.Distance(speakerLocation, npcLocation) < 4.5)
                {
                    nearbyNpcs.Add(otherNpc);
                }
            }
            return nearbyNpcs;
        }

        internal static string ConcatAnd(List<string> strings)
        {
            if (strings.Count == 0)
            {
                return string.Empty;
            }
            if (strings.Count == 1)
            {
                return strings[0];
            }
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < strings.Count; i++)
            {
                if (i == strings.Count - 1)
                {
                    builder.Append($" {GetString("generalAnd")} ");
                }
                else if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(strings[i]);
            }
            return builder.ToString();
        }

        internal static string GetString(ValleyTalk.Character npc,string key,object tokens = null,bool returnNull = false)
        {
            if (npc == null) return string.Empty;

            string FallbackToDefault(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (npc.Bio.PromptOverrides.ContainsKey(key))
            {
                var overrideValue = FallbackToDefault(npc.Bio.PromptOverrides[key]);
                if (overrideValue != null)
                {
                    return overrideValue;
                }
            }
            string result = null;
            if (npc.Bio.IsMale ?? false)
            {
                PromptCache.Instance.Cache.TryGetValue($"{key}.MaleNpc", out result);
                result = FallbackToDefault(result);
            }
            else if (!(npc.Bio.IsMale ?? true))
            {
                PromptCache.Instance.Cache.TryGetValue($"{key}.FemaleNpc", out result);
                result = FallbackToDefault(result);
            }
            if (result == null)
            {
                PromptCache.Instance.Cache.TryGetValue(key, out result);
                result = FallbackToDefault(result);
            }
            
            if (returnNull && result == null)
            {
                return null;
            }

            if (result == null)
            {
                WarnMissingPromptKey(key);
            }

            // Replace tokens
            if (tokens != null && result != null)
            {
                foreach (var token in tokens.GetType().GetProperties())
                {
                    var tokenName = "{{" + token.Name + "}}";
                    result = result.Replace(tokenName, token.GetValue(tokens).ToString());
                }
            }
            return result;
        }

        internal static string GetString(string key, object tokens = null, bool returnNull = false)
        {
            string result = string.Empty;
            if (!PromptCache.Instance.Cache.TryGetValue(key, out result))
            {
                if (returnNull)
                {
                    return null;
                }

                WarnMissingPromptKey(key);
            }

            return ApplyTokens(result, tokens);
        }

        internal static string GetConsoleString(string key, object tokens, string englishFallback)
        {
            string result = IsChineseLocale()
                ? GetString(key, returnNull: true)
                : null;

            if (string.IsNullOrWhiteSpace(result))
            {
                result = englishFallback;
            }

            return ApplyTokens(result, tokens);
        }

        internal static bool IsChineseLocale()
        {
            return ModEntry.SHelper?.Translation.Locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static string ApplyTokens(string text, object tokens)
        {
            if (tokens == null || text == null)
            {
                return text;
            }

            foreach (var token in tokens.GetType().GetProperties())
            {
                var tokenName = "{{" + token.Name + "}}";
                text = text.Replace(tokenName, Convert.ToString(token.GetValue(tokens)) ?? string.Empty);
            }

            return text;
        }

        internal static T ReadLocalisedJson<T>(string basePath, string extension = "json") where T : class
        {
            foreach(var langSuffix in ModEntry.LanguageFileSuffixes)
            {
                var path = $"{basePath}{langSuffix}.{extension}";
                var result = ModEntry.SHelper.Data.ReadJsonFile<T>(path);
                if (result != null)
                {
                    return result;
                }
            }

            return default;
        }
    }
}
