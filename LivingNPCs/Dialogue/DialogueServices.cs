using StardewModdingAPI;

namespace LivingNPCs.Dialogue;

internal static class DialogueServices
{
    public static IModHelper Helper { get; private set; } = null!;
    public static IMonitor Monitor { get; private set; } = null!;
    public static DialogueConfig Config { get; private set; } = new();

    public static void Initialize(IModHelper helper, IMonitor monitor, DialogueConfig? config = null)
    {
        Helper = helper;
        Monitor = monitor;
        if (config != null)
        {
            Config = config;
        }
    }
}

internal sealed class DialogueConfig
{
    public bool Debug { get; set; }
    public bool ExportAiResponseLogs { get; set; }
    public bool EnableSemanticContextRouting { get; set; }
    public bool EnableLivingNpcActionDecisionPass { get; set; }
    public string Provider { get; set; } = "unset";
    public string ModelName { get; set; } = "unset";
    public string ApiKey { get; set; } = "";
    public string ServerAddress { get; set; } = "https://openrouter.ai/api";
    public string PromptFormat { get; set; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";
    public bool SuppressConnectionCheck { get; set; }
    public int QueryTimeout { get; set; } = 85;
    public int SemanticContextRoutingTimeoutSeconds { get; set; } = 8;
    public int LivingNpcActionDecisionTimeoutSeconds { get; set; } = 8;
    public string RoutingThinkingLevel { get; set; } = "off";
    public string ChatThinkingLevel { get; set; } = "auto";
    public SButton InitiateTypedDialogueKey { get; set; } = SButton.None;
}

internal static class Util
{
    public static string GetString(object? context, string key, object? tokens = null, bool returnNull = false)
    {
        return GetString(key, tokens, returnNull);
    }

    public static string GetConsoleString(string key, object? tokens, string fallback)
    {
        string value = GetString(key, tokens, returnNull: true);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string GetString(string key, object? tokens = null, bool returnNull = false)
    {
        if (DialogueServices.Helper == null)
        {
            return returnNull ? string.Empty : key;
        }

        string value = tokens == null
            ? DialogueServices.Helper.Translation.Get(key).ToString()
            : DialogueServices.Helper.Translation.Get(key, tokens).ToString();
        return string.IsNullOrWhiteSpace(value) && !returnNull ? key : value;
    }
}