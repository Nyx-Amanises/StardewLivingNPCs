using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Microsoft.Xna.Framework;
namespace ValleyTalk
{
    public partial class ModEntry : Mod
    {
        public static IMonitor SMonitor;
        public static IModHelper SHelper { get; private set; }
        public static ModConfig Config;
        public static Dictionary<string, Type> LlmMap
        {
            get
            {
                if (_llmMap == null)
                {
                // Build dictionary of LLM types (things that inherit from the LLM class)
                _llmMap = new Dictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase)
                {
#if DEBUG
                    {"Dummy", typeof(LlmDummy)},
#endif
                    {"LlamaCpp", typeof(LlmLlamaCpp)},
                    {"Google", typeof(LlmGemini)},
                    {"Anthropic", typeof(LlmClaude)},
                    {"OpenAI", typeof(LlmOpenAi)},
                    {"Mistral", typeof(LlmMistral)},
                    {"DeepSeek", typeof(LlmDeepSeek)},
                    {"VolcEngine", typeof(LlmVolcEngine)},
                    {"OpenAiCompatible", typeof(LlmOAICompatible)}
                };
                }
                return _llmMap;
            }
        }
        public static bool BlockModdedContent { get; private set; } = false;
        private static CultureInfo _locale;
        public static string Language 
        { 
            get
            {
                GetLocale();
                return _locale.EnglishName;
            }
        }

        public static IEnumerable<string> LanguageFileSuffixes
        {
            get
            {
                GetLocale();
                if (_locale != null && _locale.Name != "en-US")
                {
                    var workingLocal = _locale;
                    while (!string.IsNullOrEmpty(workingLocal?.Name))
                    {
                        yield return $".{workingLocal.Name}";
                        workingLocal = workingLocal.Parent;
                    }
                }
                yield return string.Empty;
            }
        }

        private static string _localeCache = string.Empty;
        private static void GetLocale()
        {
            if (_locale != null && SHelper.Translation.Locale == _localeCache) return;
            
            try
            {
                _locale = CultureInfo.GetCultureInfo(SHelper.Translation.Locale);
                _localeCache = SHelper.Translation.Locale;
            }
            catch (Exception)
            {
                _locale = null;
                _localeCache = string.Empty;
            }
            if (_locale == null)
            {
                _locale = CultureInfo.GetCultureInfo("en-US");
                _localeCache = SHelper.Translation.Locale;
            }   
        }

        private static bool? _fixPunctuation = null;
        private static string _localeCacheFixPunctuation = string.Empty;
        private static Dictionary<string, Type> _llmMap;

        public static bool FixPunctuation
        {
            get
            {
                if (_fixPunctuation == null || _localeCacheFixPunctuation != SHelper.Translation.Locale)
                {
                    var suffixes = LanguageFileSuffixes.ToList();
                    _fixPunctuation = suffixes.Count == 1 || suffixes.Any(x => x == ".en" || x == ".fr" || x == ".de" || x == ".es" || x == ".tr" || x == ".pt" || x == ".it" || x == ".nl" || x == ".pl" || x == ".id");
                    _localeCacheFixPunctuation = SHelper.Translation.Locale;
                }
                return _fixPunctuation.Value;
            }
        }


        public override object GetApi()
        {
            return new ValleyTalkInterface();
        }

        public override void Entry(IModHelper helper)
        {
            SHelper = helper;

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            Config = Helper.ReadConfig<ModConfig>();

            SMonitor = Monitor;

            if (!Config.EnableMod)
            {
                return;
            }

            // Initialize the text input manager
            TextInputManager.Initialize();

            // Initialize cross-platform compatible logging
            Log.Initialize(Monitor);
            TokenUsageTracker.Instance.RegisterEvents();
            helper.ConsoleCommands.Add(
                "valleytalk_tokens",
                "Show ValleyTalk token usage for this session and the current save. Usage: valleytalk_tokens [export|reset]",
                this.OnTokenUsageCommand
            );
            helper.ConsoleCommands.Add(
                "valleytalk_forget",
                "Clear ValleyTalk conversation history. Usage: valleytalk_forget [near|NPC name|all confirm]",
                this.OnForgetCommand
            );

#if DEBUG
            if (Config.Debug)
            {
                Log.Debug("###############################################");
                Log.Debug("###############################################");
                Log.Debug("###############################################");
            }
#endif

            if (!LlmMap.TryGetValue(Config.Provider, out var llmType))
            {
                Log.Error($"Invalid LLM type: {Config.Provider}");
                return;
            }

            Llm.SetLlm(llmType, modelName: Config.ModelName, apiKey: Config.ApiKey, url: Config.ServerAddress, promptFormat: Config.PromptFormat);

            DialogueBuilder.Instance.Config = Config;

            CheckContentPacks();

            var harmony = new Harmony(ModManifest.UniqueID);
            harmony.PatchAll();

            Log.Debug($"[{DateTime.Now}] Mod loaded");
        }

        private void OnTokenUsageCommand(string command, string[] args)
        {
            string action = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (action)
            {
                case "":
                    Monitor.Log(TokenUsageTracker.Instance.BuildConsoleSummary(), LogLevel.Info);
                    return;
                case "export":
                    string path = TokenUsageTracker.Instance.ExportCurrentSave();
                    Monitor.Log($"ValleyTalk token usage exported to: {path}", LogLevel.Info);
                    return;
                case "reset":
                    TokenUsageTracker.Instance.ResetCurrentSave();
                    Monitor.Log("ValleyTalk token usage for the current save has been reset.", LogLevel.Info);
                    return;
                default:
                    Monitor.Log("Usage: valleytalk_tokens [export|reset]", LogLevel.Info);
                    return;
            }
        }

        private void OnForgetCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                Monitor.Log(T("commandValleyTalkForgetNeedSave", null, "ValleyTalk: load a save before clearing conversation history."), LogLevel.Info);
                return;
            }

            string target = string.Join(" ", args ?? Array.Empty<string>()).Trim();
            if (string.IsNullOrWhiteSpace(target) || target.Equals("near", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryFindNearestNpc(out NPC npc) || npc == null)
                {
                    Monitor.Log(T("commandValleyTalkForgetNoNearby", null, "ValleyTalk: no nearby NPC conversation history to clear."), LogLevel.Info);
                    return;
                }

                ClearValleyTalkHistory(npc.Name, npc.displayName);
                return;
            }

            if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Monitor.Log(T("commandValleyTalkForgetConfirmAll", null, "ValleyTalk: this clears all ValleyTalk conversation history for the current save. Run: valleytalk_forget all confirm"), LogLevel.Info);
                return;
            }

            if (target.Equals("all confirm", StringComparison.OrdinalIgnoreCase))
            {
                int count = DialogueBuilder.Instance.ClearAllConversationHistory();
                Monitor.Log(T("commandValleyTalkForgetAllDone", new { count }, "ValleyTalk: cleared conversation history for {{count}} NPC(s)."), LogLevel.Info);
                return;
            }

            if (!TryResolveNpcName(target, out string npcName, out string displayName))
            {
                Monitor.Log(T("commandValleyTalkForgetNpcNotFound", new { query = target }, "ValleyTalk: no NPC named \"{{query}}\" was found."), LogLevel.Info);
                return;
            }

            ClearValleyTalkHistory(npcName, displayName);
        }

        private void ClearValleyTalkHistory(string npcName, string displayName)
        {
            if (!DialogueBuilder.Instance.ClearConversationHistory(npcName))
            {
                Monitor.Log(T("commandValleyTalkForgetNpcNoHistory", new { npc = displayName }, "ValleyTalk: {{npc}} has no saved conversation history."), LogLevel.Info);
                return;
            }

            Monitor.Log(T("commandValleyTalkForgetNpcDone", new { npc = displayName }, "ValleyTalk: cleared conversation history for {{npc}}."), LogLevel.Info);
        }

        private static bool TryResolveNpcName(string query, out string npcName, out string displayName)
        {
            npcName = string.Empty;
            displayName = query;

            NPC npc = Game1.currentLocation?.characters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.displayName, query, StringComparison.OrdinalIgnoreCase));
            npc ??= Game1.getCharacterFromName(query);
            if (npc != null)
            {
                npcName = npc.Name;
                displayName = npc.displayName;
                return true;
            }

            string key = Game1.characterData.Keys.FirstOrDefault(candidate => string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(key))
            {
                npcName = key;
                displayName = key;
                return true;
            }

            return false;
        }

        private static bool TryFindNearestNpc(out NPC npc)
        {
            npc = null;
            if (Game1.currentLocation == null || Game1.player == null)
            {
                return false;
            }

            npc = Game1.currentLocation.characters
                .Where(candidate => candidate.currentLocation == Game1.currentLocation && !string.IsNullOrWhiteSpace(candidate.Name))
                .Select(candidate => new
                {
                    Npc = candidate,
                    Distance = Vector2.Distance(candidate.Tile, Game1.player.Tile)
                })
                .Where(pair => pair.Distance <= 4.5f)
                .OrderBy(pair => pair.Distance)
                .Select(pair => pair.Npc)
                .FirstOrDefault();
            return npc != null;
        }

        private static string T(string key, object tokens, string fallback)
        {
            string localized = Util.GetConsoleString(key, tokens, fallback);
            return string.IsNullOrWhiteSpace(localized) ? fallback : localized;
        }

        private void CheckContentPacks()
        {
            BlockModdedContent = false;
            var contentPacks = SHelper.ModRegistry.GetAll().Where(p => p.IsContentPack).ToList();
            var blockedContentPacks = contentPacks
                .Where(p => !SldConstants.PermitListContentPacks.Contains(p.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase))
                .Where(p => !ContentPackPermitsAiUse(p.Manifest));
            if (blockedContentPacks.Any())
            {
                Monitor.Log("Note: Content packs have been found that don't have mod author approval for use with AI.", LogLevel.Warn);
                Monitor.Log("While content from content packs will be displayed in-game, it will not be used for AI dialogue generation.", LogLevel.Warn);
                Monitor.Log($"Content packs without author approval: {string.Join(", ", blockedContentPacks.Select(p => p.Manifest.Name))}", LogLevel.Info);
                Monitor.Log("Mod authors can permit their content to be used in dialogue generation by adding \"permitAiUse\":true to their mod's manifest.", LogLevel.Warn);
                BlockModdedContent = true;
            }
        }

        private static bool ContentPackPermitsAiUse(IManifest manifest)
        {
            foreach (var field in manifest.ExtraFields)
            {
                if (!string.Equals(field.Key, "PermitAiUse", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (field.Value is bool boolValue)
                {
                    return boolValue;
                }

                string textValue = Convert.ToString(field.Value, CultureInfo.InvariantCulture);
                return bool.TryParse(textValue, out bool parsed) && parsed;
            }

            return false;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            ModConfigMenu.Register(this);
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            ResetSaveScopedState();
            ConversationTranscriptExporter.ExportAllKnownHistories();
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            ResetSaveScopedState();
        }

        private static void ResetSaveScopedState()
        {
            DialogueBuilder.Instance.ResetForSaveChange();
            EventHistoryReader.Instance.ClearSessionCache();
        }
    }
}
