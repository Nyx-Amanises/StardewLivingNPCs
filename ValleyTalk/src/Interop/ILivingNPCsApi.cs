namespace ValleyTalk;

public interface ILivingNPCsApi
{
    bool RecordValleyTalkExchange(
        string npcName,
        string npcDisplayName,
        string playerText,
        string npcResponse,
        string analysisJson
    );
}
