# ValleyTalk 权属地图

对照基线：上游全部 git 历史（所有版本的实质行语料，共 16141 行）。
`upstream_ratio` = 当前文件实质行中能在上游任意版本找到的比例。
分类阈值：>=60% UPSTREAM（按重写处理），<=10% MINE（可直接搬运），其余 MIXED（默认重写，个案甄别）。

## 汇总

| 分类 | 文件数 | 实质行数 |
|---|---|---|
| UPSTREAM | 172 | 10509 |
| MIXED | 26 | 4766 |
| MINE | 24 | 3789 |
| EMPTY | 0 | 0 |

## 逐文件明细

| 文件 | 实质行 | 上游行 | 占比 | 上游有同路径 | 分类 |
|---|---|---|---|---|---|
| .vscode/launch.json | 19 | 19 | 100% | 是 | UPSTREAM |
| .vscode/tasks.json | 17 | 17 | 100% | 是 | UPSTREAM |
| ContentPack/assets/GameSummary.json | 297 | 297 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Abigail.json | 48 | 48 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Alex.json | 50 | 50 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Caroline.json | 43 | 43 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Clint.json | 63 | 63 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Demetrius.json | 41 | 41 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Dwarf.json | 38 | 38 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Elliott.json | 37 | 37 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Emily.json | 57 | 57 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Evelyn.json | 54 | 54 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/George.json | 53 | 53 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Gus.json | 49 | 49 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Haley.json | 53 | 53 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Harvey.json | 28 | 28 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Jas.json | 50 | 50 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Jodi.json | 59 | 59 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Kent.json | 82 | 82 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Krobus.json | 57 | 57 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Leah.json | 60 | 60 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Lewis.json | 71 | 71 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Linus.json | 55 | 55 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Marnie.json | 57 | 57 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Maru.json | 65 | 65 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Pam.json | 42 | 42 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Penny.json | 61 | 61 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Pierre.json | 50 | 50 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Robin.json | 68 | 68 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Sam.json | 70 | 70 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Sandy.json | 53 | 53 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Sebastian.json | 104 | 104 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Shane.json | 68 | 68 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Vincent.json | 61 | 61 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Willy.json | 60 | 60 | 100% | 是 | UPSTREAM |
| ContentPack/assets/bio/Wizard.json | 65 | 65 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/GameSummary.json | 80 | 80 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/Locations.json | 35 | 35 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Alesia.json | 54 | 54 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Andy.json | 56 | 56 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Apples.json | 60 | 60 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Camilla.json | 51 | 51 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Claire.json | 60 | 60 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Gunther.json | 84 | 84 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Hank.json | 55 | 55 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Henchman.json | 52 | 52 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Isaac.json | 54 | 54 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Jadu.json | 49 | 49 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Jolyne.json | 50 | 50 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Lance.json | 59 | 59 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Marlon.json | 94 | 94 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Martin.json | 54 | 54 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Morgan.json | 65 | 65 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Morris.json | 72 | 72 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Olivia.json | 106 | 106 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Peaches.json | 46 | 46 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Scarlett.json | 67 | 67 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Sophia.json | 85 | 85 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Susan.json | 70 | 70 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Treyvon.json | 55 | 55 | 100% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/bio/Victor.json | 55 | 55 | 100% | 是 | UPSTREAM |
| InputTextBox.cs | 87 | 87 | 100% | 是 | UPSTREAM |
| LICENSE.txt | 410 | 410 | 100% | 是 | UPSTREAM |
| docs/AuthorGuide.txt | 5 | 5 | 100% | 是 | UPSTREAM |
| docs/Install.txt | 34 | 34 | 100% | 是 | UPSTREAM |
| docs/ModelsMay25.txt | 95 | 95 | 100% | 是 | UPSTREAM |
| docs/NexusHome.txt | 49 | 49 | 100% | 是 | UPSTREAM |
| docs/OtherMods.txt | 18 | 18 | 100% | 是 | UPSTREAM |
| src/AppLogger.cs | 71 | 71 | 100% | 是 | UPSTREAM |
| src/DialogueFile.cs | 144 | 144 | 100% | 是 | UPSTREAM |
| src/Extensions/StringExtensions.cs | 5 | 5 | 100% | 是 | UPSTREAM |
| src/Generation/ConversationElement.cs | 8 | 8 | 100% | 是 | UPSTREAM |
| src/Interop/ModInteropManager.cs | 37 | 37 | 100% | 是 | UPSTREAM |
| src/Patches/Dialogue_ChooseResponse_Patch.cs | 47 | 47 | 100% | 是 | UPSTREAM |
| src/Patches/NPC_AddMarriageDialogue_Patch.cs | 23 | 23 | 100% | 是 | UPSTREAM |
| src/Patches/NPC_GetGiftReaction_Patch.cs | 18 | 18 | 100% | 是 | UPSTREAM |
| src/Platform/AndroidFileHelper.cs | 55 | 55 | 100% | 是 | UPSTREAM |
| src/Platform/AndroidHelper.cs | 26 | 26 | 100% | 是 | UPSTREAM |
| src/Platform/NetworkAvailabilityChecker.cs | 28 | 28 | 100% | 是 | UPSTREAM |
| src/UI/DialogueTextInputMenuWrapper.cs | 24 | 24 | 100% | 是 | UPSTREAM |
| src/UI/ThinkingWindow.cs | 52 | 52 | 100% | 是 | UPSTREAM |
| src/config/IGenericModConfigMenuApi.cs | 136 | 136 | 100% | 是 | UPSTREAM |
| src/enums/RandomAction.cs | 1 | 1 | 100% | 是 | UPSTREAM |
| src/enums/Season.cs | 1 | 1 | 100% | 是 | UPSTREAM |
| src/enums/SldConstants.cs | 20 | 20 | 100% | 是 | UPSTREAM |
| src/enums/SpouseAction.cs | 1 | 1 | 100% | 是 | UPSTREAM |
| src/enums/Weekday.cs | 1 | 1 | 100% | 是 | UPSTREAM |
| src/llms/IGetModelNames.cs | 2 | 2 | 100% | 是 | UPSTREAM |
| src/llms/LlmMistral.cs | 11 | 11 | 100% | 是 | UPSTREAM |
| src/llms/PromptFormatter.cs | 10 | 10 | 100% | 是 | UPSTREAM |
| src/models/BioData.cs | 58 | 58 | 100% | 是 | UPSTREAM |
| src/models/ChildDescription.cs | 7 | 7 | 100% | 是 | UPSTREAM |
| src/models/history/ActivityHistory.cs | 6 | 6 | 100% | 是 | UPSTREAM |
| src/models/history/ConversationHistory.cs | 24 | 24 | 100% | 是 | UPSTREAM |
| src/models/history/DialogueEventHistory.cs | 13 | 13 | 100% | 是 | UPSTREAM |
| src/models/history/DialogueEventOverheard.cs | 9 | 9 | 100% | 是 | UPSTREAM |
| src/models/history/DialogueHistory.cs | 10 | 10 | 100% | 是 | UPSTREAM |
| src/models/history/IHistory.cs | 2 | 2 | 100% | 是 | UPSTREAM |
| src/models/history/ThirdPartyHistory.cs | 12 | 12 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Abigail.fr-FR.txt | 11 | 11 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Alex.fr-FR.txt | 18 | 18 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Caroline.fr-FR.txt | 10 | 10 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Clint.fr-FR.txt | 11 | 11 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Demetrius.fr-FR.txt | 14 | 14 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Dwarf.fr-FR.txt | 10 | 10 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Elliott.fr-FR.txt | 13 | 13 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Emily.fr-FR.txt | 16 | 16 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Evelyn.fr-FR.txt | 11 | 11 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/George.fr-FR.txt | 15 | 15 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Gus.fr-FR.txt | 13 | 13 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Haley.fr-FR.txt | 21 | 21 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Harvey.fr-FR.txt | 20 | 20 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Jas.fr-FR.txt | 16 | 16 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Jodi.fr-FR.txt | 14 | 14 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Kent.fr-FR.txt | 18 | 18 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Krobus.fr-FR.txt | 11 | 11 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Leah.fr-FR.txt | 18 | 18 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Lewis.fr-FR.txt | 17 | 17 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Linus.fr-FR.txt | 13 | 13 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Marnie.fr-FR.txt | 15 | 15 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Maru.fr-FR.txt | 23 | 23 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Pam.fr-FR.txt | 14 | 14 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Penny.fr-FR.txt | 21 | 21 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Pierre.fr-FR.txt | 19 | 19 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Robin.fr-FR.txt | 26 | 26 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Sam.fr-FR.txt | 28 | 28 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Sandy.fr-FR.txt | 15 | 15 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Sebastian.fr-FR.txt | 24 | 24 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Shane.fr-FR.txt | 22 | 22 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Stardew.fr-FR.txt | 1 | 1 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Vincent.fr-FR.txt | 20 | 20 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Willy.fr-FR.txt | 15 | 15 | 100% | 是 | UPSTREAM |
| translations/fr-FR/assets/bio/Wizard.fr-FR.txt | 17 | 17 | 100% | 是 | UPSTREAM |
| translations/fr-FR/i18n/fr-FR.json | 340 | 340 | 100% | 是 | UPSTREAM |
| ContentPack/content.json | 74 | 73 | 99% | 是 | UPSTREAM |
| translations/zh-CN/i18n/zh-CN.json | 363 | 358 | 99% | 是 | UPSTREAM |
| src/StardewTime.cs | 107 | 105 | 98% | 是 | UPSTREAM |
| src/Patches/MarriageDialogueReference_GetDialogue_Patch.cs | 36 | 35 | 97% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/content.json | 56 | 54 | 96% | 是 | UPSTREAM |
| src/Patches/NPC_PushTemporaryDialogue_Patch.cs | 24 | 23 | 96% | 是 | UPSTREAM |
| src/TextInputHandler.cs | 48 | 46 | 96% | 是 | UPSTREAM |
| src/llms/LlmLlamaCpp.cs | 93 | 89 | 96% | 是 | UPSTREAM |
| src/Generation/DialogueContext.cs | 275 | 259 | 94% | 是 | UPSTREAM |
| src/Patches/NPC_CurrentDialogue_Patch.cs | 43 | 40 | 93% | 是 | UPSTREAM |
| src/Patches/NPC_TryToGetMarriageSpecificDialogue_Patch.cs | 14 | 13 | 93% | 是 | UPSTREAM |
| src/llms/LlmOAICompatible.cs | 13 | 12 | 92% | 是 | UPSTREAM |
| src/Patches/NPC_TryToRetrieveDialogue_Patch.cs | 12 | 11 | 92% | 是 | UPSTREAM |
| src/llms/LlmDummy.cs | 12 | 11 | 92% | 是 | UPSTREAM |
| ContentPack/assets/Prompts.json | 947 | 863 | 91% | 是 | UPSTREAM |
| src/Patches/Dialogue_TryGetDialogue_Patch.cs | 11 | 10 | 91% | 是 | UPSTREAM |
| src/PromptCache.cs | 31 | 27 | 87% | 是 | UPSTREAM |
| README.md | 85 | 72 | 85% | 是 | UPSTREAM |
| src/Patches/NPC_CheckAction_Patch.cs | 27 | 22 | 82% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/LocationsOptimized.json | 35 | 28 | 80% | 否 | UPSTREAM |
| src/llms/LlmDeepseek.cs | 14 | 11 | 79% | 是 | UPSTREAM |
| src/llms/LlmOpenAI.cs | 14 | 11 | 79% | 是 | UPSTREAM |
| src/UI/DialogueTextInputBox.cs | 139 | 109 | 78% | 是 | UPSTREAM |
| src/GameSummaryBuilder.cs | 116 | 84 | 72% | 是 | UPSTREAM |
| src/llms/LlmClaude.cs | 104 | 74 | 71% | 是 | UPSTREAM |
| ContentPack/assets/GameSummaryOptimized.json | 267 | 187 | 70% | 否 | UPSTREAM |
| Extensions/ValleyTalk for SVE/assets/GameSummaryOptimized.json | 80 | 56 | 70% | 否 | UPSTREAM |
| src/Patches/Game1_DrawDialogue_Patch.cs | 13 | 9 | 69% | 是 | UPSTREAM |
| src/Prompts.cs | 833 | 567 | 68% | 是 | UPSTREAM |
| src/llms/LlmVolcEngine.cs | 111 | 75 | 68% | 是 | UPSTREAM |
| src/EventHistoryReader.cs | 105 | 70 | 67% | 是 | UPSTREAM |
| src/Generation/LlmResponse.cs | 15 | 10 | 67% | 是 | UPSTREAM |
| src/config/ModConfig.cs | 39 | 25 | 64% | 是 | UPSTREAM |
| src/Util.cs | 94 | 59 | 63% | 是 | UPSTREAM |
| src/VtConstants.cs | 8 | 5 | 62% | 是 | UPSTREAM |
| src/manifest.json | 8 | 5 | 62% | 是 | UPSTREAM |
| ContentPack/manifest.json | 10 | 6 | 60% | 是 | UPSTREAM |
| Extensions/ValleyTalk for SVE/manifest.json | 10 | 6 | 60% | 是 | UPSTREAM |
| src/Generation/DialogueBuilder.cs | 380 | 224 | 59% | 是 | MIXED |
| src/models/history/StardewEventHistory.cs | 126 | 72 | 57% | 是 | MIXED |
| src/Interop/IValleyTalkInterface.cs | 11 | 6 | 55% | 是 | MIXED |
| src/config.json | 24 | 13 | 54% | 是 | MIXED |
| src/llms/Llm.cs | 141 | 76 | 54% | 是 | MIXED |
| src/Platform/NetworkHelper.cs | 85 | 45 | 53% | 是 | MIXED |
| src/Interop/ValleyTalkInterface.cs | 36 | 19 | 53% | 是 | MIXED |
| src/Character.cs | 762 | 390 | 51% | 是 | MIXED |
| src/Generation/AsyncBuilder.cs | 224 | 112 | 50% | 是 | MIXED |
| src/ModEntry.cs | 180 | 89 | 49% | 是 | MIXED |
| src/UI/DialogueTextInputMenu.cs | 80 | 37 | 46% | 是 | MIXED |
| src/config/ModConfigMenu.cs | 315 | 134 | 42% | 是 | MIXED |
| src/llms/LlmGemini.cs | 134 | 57 | 42% | 是 | MIXED |
| src/Patches/GameLocation_GetLocationOverrideDialogue_Patch.cs | 28 | 11 | 39% | 是 | MIXED |
| ContentPack/i18n/default.json | 950 | 259 | 27% | 是 | MIXED |
| src/Patches/NPC_CheckForNewCurrentDialogue_Patch.cs | 30 | 8 | 27% | 是 | MIXED |
| src/llms/LlmOpenAiBase.cs | 301 | 73 | 24% | 是 | MIXED |
| src/Patches/DialogueBox_ThinkingDialogue_Patch.cs | 33 | 8 | 24% | 否 | MIXED |
| src/Interop/LivingNpcConversationBridge.cs | 42 | 9 | 21% | 否 | MIXED |
| src/Patches/Event_CheckAction_Patch.cs | 26 | 5 | 19% | 否 | MIXED |
| src/Generation/GiftMailContentValidator.cs | 80 | 13 | 16% | 否 | MIXED |
| src/UI/NativeDialogueTextInputController.cs | 198 | 32 | 16% | 否 | MIXED |
| src/UI/ThinkingDialogueController.cs | 84 | 11 | 13% | 否 | MIXED |
| src/ConversationCues.cs | 24 | 3 | 12% | 否 | MIXED |
| src/ConversationTranscriptExporter.cs | 375 | 42 | 11% | 否 | MIXED |
| src/Generation/ConversationTextPostProcessor.cs | 97 | 10 | 10% | 否 | MIXED |
| src/Generation/ContextRoutingDecisionPass.cs | 365 | 32 | 9% | 否 | MINE |
| src/Generation/LivingNpcActionDecisionPass.cs | 488 | 42 | 9% | 否 | MINE |
| src/UI/StreamingDialogueWindow.cs | 240 | 20 | 8% | 否 | MINE |
| src/Generation/GiftMailGenerator.cs | 124 | 9 | 7% | 否 | MINE |
| src/UI/DialogueUiStateGuard.cs | 69 | 5 | 7% | 否 | MINE |
| src/Generation/StreamingDialoguePreview.cs | 63 | 4 | 6% | 否 | MINE |
| src/Generation/MemoryImpressionGenerator.cs | 128 | 8 | 6% | 否 | MINE |
| src/ContextRoutingLogExporter.cs | 83 | 5 | 6% | 否 | MINE |
| src/RsvAiPolicy.cs | 21 | 1 | 5% | 否 | MINE |
| src/AiResponseLogExporter.cs | 119 | 5 | 4% | 否 | MINE |
| src/PromptLogExporter.cs | 123 | 5 | 4% | 否 | MINE |
| src/Generation/LivingNpcContextCompressor.cs | 99 | 4 | 4% | 否 | MINE |
| src/TokenUsageTracker.cs | 184 | 5 | 3% | 否 | MINE |
| src/Generation/ContextRoutingPlan.cs | 86 | 2 | 2% | 否 | MINE |
| src/Generation/ConversationAnalysis.cs | 323 | 6 | 2% | 否 | MINE |
| src/llms/LlmThinking.cs | 174 | 2 | 1% | 否 | MINE |
| ContentPack/i18n/zh.json | 950 | 2 | 0% | 否 | MINE |
| README-FORK.txt | 26 | 0 | 0% | 否 | MINE |
| src/AssemblyInfo.cs | 1 | 0 | 0% | 否 | MINE |
| src/Generation/GeneratedResponse.cs | 3 | 0 | 0% | 否 | MINE |
| src/Generation/StreamingResponseOption.cs | 2 | 0 | 0% | 否 | MINE |
| src/Generation/TokenUsage.cs | 91 | 0 | 0% | 否 | MINE |
| src/Interop/ILivingNPCsApi.cs | 15 | 0 | 0% | 否 | MINE |
| src/llms/IStreamingLlm.cs | 12 | 0 | 0% | 否 | MINE |
