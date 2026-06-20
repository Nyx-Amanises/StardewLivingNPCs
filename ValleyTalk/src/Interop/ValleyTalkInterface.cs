using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace ValleyTalk;

public class ValleyTalkInterface : IValleyTalkInterface
{
    private string _modName = string.Empty;

    public ValleyTalkInterface()
    {
        _modName = Guid.NewGuid().ToString();
    }

    public void SetModName(string modName)
    {
        _modName = modName;
    }

    public bool IsEnabledForCharacter(NPC character)
    {
        return DialogueBuilder.CanGenerateForNpc(character) && DialogueBuilder.Instance.PatchNpc(character);
    }

    public bool RequestGiftDialogue(NPC character, StardewValley.Object gift, int taste)
    {
        if (character == null || gift == null || !DialogueBuilder.CanGenerateForNpc(character))
        {
            return false;
        }

        if (!DialogueBuilder.Instance.PatchNpc(character, probability: 4))
        {
            return false;
        }

        if (AsyncBuilder.Instance.AwaitingGeneration)
        {
            return false;
        }

        if (!NetworkAvailabilityChecker.IsNetworkAvailableWithRetry())
        {
            return false;
        }

        AsyncBuilder.Instance.RequestNpcGiftResponse(character, gift, taste);
        return true;
    }

    public void RegisterPromptOverride(string characterName, string promptElement, string overrideText)
    {
        if (RsvAiPolicy.IsBlockedNpcName(characterName))
        {
            return;
        }

        ModInteropManager.Instance.RegisterPromptOverride(_modName, characterName, promptElement, overrideText);
    }

    public void ClearPromptOverride(string characterName, string promptElement)
    {
        ModInteropManager.Instance.ClearPromptOverride(_modName, characterName, promptElement);
    }

    public void ClearPromptOverrides(string characterName = "")
    {
        ModInteropManager.Instance.ClearPromptOverrides(_modName, characterName);
    }

    public void RequestGiftMailText(string requestId, string npcName, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(npcName) || RsvAiPolicy.IsBlockedNpcName(npcName))
        {
            return;
        }

        GiftMailGenerator.Instance.Request(requestId, npcName, payloadJson);
    }

    public string TryGetGiftMailText(string requestId)
    {
        return GiftMailGenerator.Instance.TryGet(requestId);
    }
}
