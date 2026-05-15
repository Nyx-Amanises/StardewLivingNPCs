using System;
using System.Reflection;
using StardewValley;

namespace ValleyTalk;

internal static class LivingNpcConversationBridge
{
    private const string LivingNpcUniqueId = "Codex.LivingNPCs";

    private static bool initialized;
    private static object api;
    private static MethodInfo recordMethod;

    public static void RecordExchange(NPC npc, string playerText, string npcResponse)
    {
        if (npc == null || string.IsNullOrWhiteSpace(playerText))
        {
            return;
        }

        TryInitialize();
        if (api == null || recordMethod == null)
        {
            return;
        }

        try
        {
            recordMethod.Invoke(api, new object[]
            {
                npc.Name,
                npc.displayName,
                playerText,
                npcResponse ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor?.Log($"LivingNPCs exchange bridge failed for {npc.Name}: {ex.Message}", StardewModdingAPI.LogLevel.Debug);
        }
    }

    private static void TryInitialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        api = ModEntry.SHelper.ModRegistry.GetApi<object>(LivingNpcUniqueId);
        recordMethod = api?.GetType().GetMethod("RecordValleyTalkExchange", BindingFlags.Instance | BindingFlags.Public);
    }
}
