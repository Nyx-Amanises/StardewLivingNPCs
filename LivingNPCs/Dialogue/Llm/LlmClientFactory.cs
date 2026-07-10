using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>每提供商的连接字段需求与能力声明（WP15 §5：GMCM 动态显隐与模型下拉据此驱动，不再反射）。</summary>
internal sealed class LlmProviderMetadata
{
    public string ProviderId { get; init; } = string.Empty;

    public bool RequiresApiKey { get; init; }

    public bool RequiresModelName { get; init; }

    public bool RequiresServerAddress { get; init; }

    public bool RequiresPromptFormat { get; init; }

    /// <summary>是否实现 IModelNameSource（GMCM 模型列表段落的显示条件）。</summary>
    public bool SupportsModelList { get; init; }

    /// <summary>是否消费聊天/路由思考档位（VolcEngine 仅支持快速通道关思考，不消费档位，记 false）。</summary>
    public bool SupportsThinkingLevels { get; init; }
}

/// <summary>
/// 提供商注册表与工厂（WP11 §4.5）：配置字符串 → 工厂委托，大小写不敏感；
/// 旧世界的反射构造改为显式 switch 委托（行为等价、可读可查错）。Dummy 仅 DEBUG 构建注册。
/// </summary>
internal static class LlmClientFactory
{
    private static readonly Dictionary<string, (LlmProviderMetadata Metadata, Func<LlmConnectionSettings, LlmClientBase> Create)> Registry;
    private static readonly List<LlmProviderMetadata> OrderedMetadata = new();

    static LlmClientFactory()
    {
        Registry = new Dictionary<string, (LlmProviderMetadata, Func<LlmConnectionSettings, LlmClientBase>)>(StringComparer.OrdinalIgnoreCase);
        Register(
            new LlmProviderMetadata { ProviderId = "OpenAI", RequiresApiKey = true, RequiresModelName = true, SupportsModelList = true, SupportsThinkingLevels = true },
            settings => new OpenAiClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "OpenAiCompatible", RequiresApiKey = true, RequiresModelName = true, RequiresServerAddress = true, SupportsModelList = true, SupportsThinkingLevels = true },
            settings => new OpenAiCompatibleClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "Anthropic", RequiresApiKey = true, RequiresModelName = true, SupportsModelList = true },
            settings => new ClaudeClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "Google", RequiresApiKey = true, RequiresModelName = true, SupportsModelList = true, SupportsThinkingLevels = true },
            settings => new GeminiClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "DeepSeek", RequiresApiKey = true, RequiresModelName = true, SupportsModelList = true, SupportsThinkingLevels = true },
            settings => new DeepSeekClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "Mistral", RequiresApiKey = true, RequiresModelName = true, SupportsModelList = true, SupportsThinkingLevels = true },
            settings => new MistralClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "VolcEngine", RequiresApiKey = true, RequiresModelName = true, SupportsModelList = true },
            settings => new VolcEngineClient(settings));
        Register(
            new LlmProviderMetadata { ProviderId = "LlamaCpp", RequiresServerAddress = true, RequiresPromptFormat = true },
            settings => new LlamaCppClient(settings));
#if DEBUG
        Register(
            new LlmProviderMetadata { ProviderId = "Dummy" },
            settings => new DummyClient(settings));
#endif
    }

    /// <summary>注册顺序即 GMCM 下拉顺序（WP15 消费）。</summary>
    public static IReadOnlyList<string> ProviderIds => OrderedMetadata.Select(metadata => metadata.ProviderId).ToList();

    public static IReadOnlyList<LlmProviderMetadata> Providers => OrderedMetadata;

    public static bool TryGetMetadata(string provider, out LlmProviderMetadata metadata)
    {
        if (Registry.TryGetValue(provider ?? string.Empty, out var entry))
        {
            metadata = entry.Metadata;
            return true;
        }

        metadata = null!;
        return false;
    }

    /// <summary>配置的 Provider 不在表内：Error 日志 + 返回 null（引擎保持关闭，不崩游戏）。</summary>
    public static LlmClientBase? TryCreate(LlmConnectionSettings settings)
    {
        if (!Registry.TryGetValue(settings.Provider ?? string.Empty, out var entry))
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.invalidProvider",
                    new { provider = settings.Provider },
                    $"Invalid LLM type: {settings.Provider}"),
                LogLevel.Error);
            return null;
        }

        return entry.Create(settings);
    }

    private static void Register(LlmProviderMetadata metadata, Func<LlmConnectionSettings, LlmClientBase> create)
    {
        Registry.Add(metadata.ProviderId, (metadata, create));
        OrderedMetadata.Add(metadata);
    }
}
