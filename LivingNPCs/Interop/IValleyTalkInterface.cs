using StardewValley;

namespace ValleyTalk;

public interface IValleyTalkInterface
{
    void SetModName(string modName);
    bool IsEnabledForCharacter(NPC character);
    bool RequestGiftDialogue(NPC character, StardewValley.Object gift, int taste);
    void RegisterPromptOverride(string characterName, string promptElement, string overrideText);
    void ClearPromptOverride(string characterName, string promptElement);
    void ClearPromptOverrides(string characterName = "");
    void RequestGiftMailText(string requestId, string npcName, string payloadJson);
    string TryGetGiftMailText(string requestId);
}
