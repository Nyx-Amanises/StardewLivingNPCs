using System;
using StardewValley;

namespace ValleyTalk;

internal static class LivingNpcConversationBridge
{
    private const string LivingNpcUniqueId = "Yuki.LivingNPCs";

    private static bool initialized;
    private static ILivingNPCsApi api;

    public static string GetGiftResponseContext(NPC npc, StardewValley.Object gift, int taste)
    {
        if (npc == null || gift == null)
        {
            return string.Empty;
        }

        TryInitialize();
        if (api == null)
        {
            return string.Empty;
        }

        try
        {
            string giftName = string.IsNullOrWhiteSpace(gift.DisplayName) ? gift.Name : gift.DisplayName;
            return api.GetGiftResponseContext(
                npc.Name,
                npc.displayName,
                gift.QualifiedItemId,
                giftName,
                taste
            ) ?? string.Empty;
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor?.Log($"LivingNPCs gift context bridge failed for {npc.Name}: {ex.Message}", StardewModdingAPI.LogLevel.Debug);
            return string.Empty;
        }
    }

    public static void RecordExchange(NPC npc, string playerText, string npcResponse, string analysisJson)
    {
        if (npc == null || string.IsNullOrWhiteSpace(playerText))
        {
            return;
        }

        TryInitialize();
        if (api == null)
        {
            return;
        }

        try
        {
            api.RecordValleyTalkExchange(
                npc.Name,
                npc.displayName,
                playerText,
                npcResponse ?? string.Empty,
                analysisJson ?? string.Empty
            );
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
        api = ModEntry.SHelper.ModRegistry.GetApi<ILivingNPCsApi>(LivingNpcUniqueId);
    }
}
