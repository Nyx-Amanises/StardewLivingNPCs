using System.Collections.Generic;
using System;
using System.Linq;
using Newtonsoft.Json;
using StardewModdingAPI;
using SystemJsonIgnoreAttribute = System.Text.Json.Serialization.JsonIgnoreAttribute;

namespace ValleyTalk
{
    public class ModConfig
    {
        private string disableCharacters = string.Empty;

        public bool EnableMod { get; set; } = true;
        public bool Debug { get; set; } = false;
        public bool ExportAiResponseLogs { get; set; } = true;
        public string Provider { get; set; } = "Mistral";
        public string ModelName { get; set; } = "";
        public string ServerAddress { get; set; } = "https://openrouter.ai/api";
        public string PromptFormat { get; set; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";
        public int QueryTimeout { get; set; } = 85;
        public string ApiKey { get; set; } = string.Empty;
        public bool ApplyTranslation { get; set; } = true;
        public int GeneralFrequency { get; set; } = 4;
        public int MarriageFrequency { get; set; } = 4;
        public int GiftFrequency { get; set; } = 4;
        public bool GenerateAiForNormalRightClick { get; set; } = false;
        public string TypedResponses { get; set; } = "With Generated";
        public bool EnableSveCompatibility { get; set; } = true;
        public bool UseOptimizedPrompts { get; set; } = false;
        public bool EnableSemanticContextRouting { get; set; } = true;
        public int SemanticContextRoutingTimeoutSeconds { get; set; } = 8;
        public string RoutingThinkingLevel { get; set; } = "Off";
        public string ChatThinkingLevel { get; set; } = "Auto";
        [JsonIgnore]
        [SystemJsonIgnore]
        public bool EnableLivingNpcActionDecisionPass { get; set; } = true;
        [JsonIgnore]
        [SystemJsonIgnore]
        public int LivingNpcActionDecisionTimeoutSeconds { get; set; } = 12;
        public string DisableCharacters
        {
            get => disableCharacters;
            set
            {
                disableCharacters = value;
                DisabledCharactersList = value
                            .Split(new[] { ',', ' ' })
                            .Select(s => s.Trim().ToTitleCase())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
            }
        }

        public SButton InitiateTypedDialogueKey { get; internal set; } = SButton.LeftAlt;
        internal List<string> DisabledCharactersList { get; private set; } = new List<string>();
        public bool SuppressConnectionCheck { get; set; } = false;
    }
}
