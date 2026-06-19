namespace ValleyTalk;

public interface ILivingNPCsApi
{
    string GetConversationContext(
        string npcName,
        string npcDisplayName
    );

    string GetGiftResponseContext(
        string npcName,
        string npcDisplayName,
        string giftItemId,
        string giftName,
        int taste
    );

    bool RecordValleyTalkExchange(
        string npcName,
        string npcDisplayName,
        string playerText,
        string npcResponse,
        string analysisJson
    );
}
