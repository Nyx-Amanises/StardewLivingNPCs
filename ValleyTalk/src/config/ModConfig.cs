using System.Collections.Generic;
using System;
using System.Linq;
using StardewModdingAPI;

namespace ValleyTalk
{
    public class ModConfig
    {
        private string disableCharacters = string.Empty;

        public bool EnableMod { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string Provider { get; set; } = "Mistral";
        public string ModelName { get; set; } = "";
        public string ServerAddress { get; set; } = "https://openrouter.ai/api";
        public string PromptFormat { get; set; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";
        public int QueryTimeout { get; set; } = 60;
        public string ApiKey { get; set; } = string.Empty;
        public string ActiveModelProfile { get; set; } = "Default";
        public string ModelProfileNameToSave { get; set; } = string.Empty;
        public List<ModelProfileConfig> ModelProfiles { get; set; } = new();
        public bool ApplyTranslation { get; set; } = false;
        public int GeneralFrequency { get; set; } = 4;
        public int MarriageFrequency { get; set; } = 4;
        public int GiftFrequency { get; set; } = 4;
        public bool GenerateAiForNormalRightClick { get; set; } = false;
        public string TypedResponses { get; set; } = "With Generated";
        public bool AllowLocalContentPackDialogueForAi { get; set; } = true;
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

        public void EnsureModelProfiles()
        {
            ModelProfiles ??= new List<ModelProfileConfig>();
            if (ModelProfiles.Count == 0)
            {
                ModelProfiles.Add(ModelProfileConfig.FromCurrent("Default", this));
            }

            ModelProfiles = ModelProfiles
                .Where(profile => profile != null)
                .Select(profile =>
                {
                    profile.Name = NormalizeProfileName(profile.Name);
                    return profile;
                })
                .GroupBy(profile => profile.Name, StringComparer.InvariantCultureIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (ModelProfiles.Count == 0)
            {
                ModelProfiles.Add(ModelProfileConfig.FromCurrent("Default", this));
            }

            if (string.IsNullOrWhiteSpace(ActiveModelProfile) || FindModelProfile(ActiveModelProfile) == null)
            {
                ActiveModelProfile = ModelProfiles[0].Name;
            }
        }

        public void SaveCurrentToActiveModelProfile()
        {
            EnsureModelProfiles();
            var profile = FindModelProfile(ActiveModelProfile);
            if (profile == null)
            {
                profile = ModelProfileConfig.FromCurrent(ActiveModelProfile, this);
                ModelProfiles.Add(profile);
            }

            profile.CopyFrom(this);
        }

        public bool ApplyModelProfile(string profileName)
        {
            EnsureModelProfiles();
            var profile = FindModelProfile(profileName);
            if (profile == null)
            {
                return false;
            }

            ActiveModelProfile = profile.Name;
            profile.CopyTo(this);
            return true;
        }

        public bool SaveCurrentAsModelProfile(string profileName)
        {
            string normalizedName = NormalizeProfileName(profileName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            EnsureModelProfiles();
            var profile = FindModelProfile(normalizedName);
            if (profile == null)
            {
                profile = ModelProfileConfig.FromCurrent(normalizedName, this);
                ModelProfiles.Add(profile);
            }
            else
            {
                profile.CopyFrom(this);
            }

            ActiveModelProfile = profile.Name;
            return true;
        }

        private ModelProfileConfig FindModelProfile(string profileName)
        {
            string normalizedName = NormalizeProfileName(profileName);
            return ModelProfiles?.FirstOrDefault(profile =>
                string.Equals(profile.Name, normalizedName, StringComparison.InvariantCultureIgnoreCase));
        }

        private static string NormalizeProfileName(string profileName)
        {
            return string.IsNullOrWhiteSpace(profileName)
                ? "Default"
                : profileName.Trim();
        }
    }

    public class ModelProfileConfig
    {
        public string Name { get; set; } = "Default";
        public string Provider { get; set; } = "Mistral";
        public string ModelName { get; set; } = "";
        public string ServerAddress { get; set; } = "https://openrouter.ai/api";
        public string PromptFormat { get; set; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";
        public int QueryTimeout { get; set; } = 60;
        public string ApiKey { get; set; } = string.Empty;
        public bool ApplyTranslation { get; set; } = false;

        public static ModelProfileConfig FromCurrent(string name, ModConfig config)
        {
            var profile = new ModelProfileConfig
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim()
            };
            profile.CopyFrom(config);
            return profile;
        }

        public void CopyFrom(ModConfig config)
        {
            Provider = config.Provider;
            ModelName = config.ModelName;
            ServerAddress = config.ServerAddress;
            PromptFormat = config.PromptFormat;
            QueryTimeout = config.QueryTimeout;
            ApiKey = config.ApiKey;
            ApplyTranslation = config.ApplyTranslation;
        }

        public void CopyTo(ModConfig config)
        {
            config.Provider = Provider;
            config.ModelName = ModelName;
            config.ServerAddress = ServerAddress;
            config.PromptFormat = PromptFormat;
            config.QueryTimeout = QueryTimeout;
            config.ApiKey = ApiKey;
            config.ApplyTranslation = ApplyTranslation;
        }
    }
}
