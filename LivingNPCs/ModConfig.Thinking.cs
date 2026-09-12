using System.Runtime.Serialization;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json;

namespace LivingNPCs;

internal sealed partial class ModConfig
{
    private string thinkingLevel = LlmThinking.Auto;
    private bool hasThinkingLevel;
    private bool needsThinkingLevelMigration;
    private string? legacyRoutingThinkingLevel;
    private string? legacyChatThinkingLevel;

    /// <summary>All dialogue and background model requests share this preference.</summary>
    public string ThinkingLevel
    {
        get => this.thinkingLevel;
        set
        {
            this.thinkingLevel = value;
            this.hasThinkingLevel = true;
        }
    }

    // Write-only JSON aliases import old configs without writing the separate settings back.
    [JsonProperty("RoutingThinkingLevel")]
    private string? LegacyRoutingThinkingLevel
    {
        set
        {
            this.legacyRoutingThinkingLevel = value;
            this.needsThinkingLevelMigration = true;
        }
    }

    [JsonProperty("ChatThinkingLevel")]
    private string? LegacyChatThinkingLevel
    {
        set
        {
            this.legacyChatThinkingLevel = value;
            this.needsThinkingLevelMigration = true;
        }
    }

    [OnDeserialized]
    private void ResolveThinkingLevel(StreamingContext context)
    {
        // Resolve after every field has been read so JSON property order cannot change priority.
        if (!this.hasThinkingLevel)
        {
            this.ThinkingLevel = LlmThinking.FromLegacyLevels(this.legacyChatThinkingLevel, this.legacyRoutingThinkingLevel);
            this.needsThinkingLevelMigration = true;
        }

        this.legacyRoutingThinkingLevel = null;
        this.legacyChatThinkingLevel = null;
    }
}
