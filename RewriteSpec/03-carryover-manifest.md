# 03 · 搬运清单（Carry-over Manifest）

> 执行者：阶段 A 的"搬运对话"。本清单里的文件是 Yuki 的原创（权属见 02），
> 复制与改写不受洁净室限制。**清单之外的 ValleyTalk 文件一律不得复制**。

## 1. 通用改写规则（每个搬运文件都要做）

1. 命名空间 `ValleyTalk` → 按新目录改为 `LivingNPCs.Dialogue.<模块>`（见 01 §1）。
2. `ModEntry.SHelper` / `ModEntry.Config` 等旧静态入口 → LivingNPCs 的对应服务
   （由搬运对话建一个 `DialogueServices` 静态门面过渡，后续工作包接管）。
3. 对**将被重写的旧类型**（如 `DialogueBuilder`、`Llm`、`SldConstants`）的引用：
   改为引用 01 §2 的新接口；接口尚未实现的部分先打 `NotImplementedException` 桩，
   保证编译通过，由对应工作包补齐。每处桩加 `// WP1x-TODO` 注释。
4. 保留原有中文注释与日志文案；日志前缀统一 `[LivingNPCs]`。

## 2. 搬运文件（MINE，≤10% 且甄别通过）

| 旧路径（ValleyTalk/src/…） | 新位置（LivingNPCs/Dialogue/…） |
|---|---|
| Generation/ContextRoutingDecisionPass.cs | Engine/ |
| Generation/ContextRoutingPlan.cs | Engine/ |
| Generation/ConversationAnalysis.cs | Engine/ |
| Generation/LivingNpcActionDecisionPass.cs | Engine/ |
| Generation/LivingNpcContextCompressor.cs | Engine/ |
| Generation/MemoryImpressionGenerator.cs | Engine/ |
| Generation/GiftMailGenerator.cs | Engine/ |
| Generation/GeneratedResponse.cs | Engine/ |
| Generation/StreamingDialoguePreview.cs | Engine/ |
| Generation/StreamingResponseOption.cs | Engine/ |
| Generation/TokenUsage.cs | Llm/ |
| llms/LlmThinking.cs | Llm/ |
| llms/IStreamingLlm.cs | Llm/（并入/让位于 `ILlmClient.StreamAsync`，由 WP11 裁决） |
| RsvAiPolicy.cs | Engine/ |
| UI/StreamingDialogueWindow.cs | Ui/ |
| UI/DialogueUiStateGuard.cs | Ui/ |
| TokenUsageTracker.cs | Persistence/ |
| AiResponseLogExporter.cs | Diagnostics/ |
| PromptLogExporter.cs | Diagnostics/ |
| ContextRoutingLogExporter.cs | Diagnostics/ |

不搬运的 MINE 文件：`AssemblyInfo.cs`（并入 LivingNPCs 工程属性）、
`Interop/ILivingNPCsApi.cs`（桥接层解散，见 WP16）。

## 3. 搬运文件（MIXED 但甄别为误报）

以下文件权属占比 10–24%，命中行经人工核对全部为 `return false;`、
`catch (Exception ex)`、`/// <summary>`、游戏 API 惯用调用等**通用惯用句**，
不构成上游表达（甄别记录见本仓库对话档案，2026-07-06）：

| 旧路径 | 新位置 | 备注 |
|---|---|---|
| Generation/ConversationTextPostProcessor.cs | Engine/ | |
| Generation/GiftMailContentValidator.cs | Engine/ | |
| ConversationCues.cs | Engine/ | |
| ConversationTranscriptExporter.cs | Diagnostics/ | |
| UI/ThinkingDialogueController.cs | Ui/ | |
| UI/NativeDialogueTextInputController.cs | Ui/ | |
| Patches/DialogueBox_ThinkingDialogue_Patch.cs | GameHooks/ | |
| Patches/Event_CheckAction_Patch.cs | GameHooks/ | 含一行对旧 `DialogueBuilder.Instance.ClearContext()` 的调用，按 §1.3 换新接口 |

**不搬运**：`Interop/LivingNpcConversationBridge.cs`（21%，其功能溶入 WP16 的
进程内直调，文件本身废弃）。

## 4. 其余搬运件

- **`ValleyTalk.Tests/` 全部**（上游无测试工程，测试均为 Yuki 原创）→
  并入 `LivingNPCs.Tests/Dialogue/`，命名空间同步改。测试引用了被重写类型的，
  按 §1.3 打桩或改写到新接口；跑不绿的测试标 `[Fact(Skip="WP1x-TODO")]` 留给对应工作包。
- `ContentPack/i18n/zh.json`（0%，Yuki 原创中文文案）→ 合入 `LivingNPCs/i18n/zh.json`
  （键名迁移方案由 WP15 定）。
- `README-FORK.txt` → 不搬（历史文档，留 git 历史）。

## 5. 明确重写（禁止搬运，对应工作包重新实现）

**.cs（UPSTREAM ≥60% 全部 + 以下 MIXED）**：
`Generation/DialogueBuilder.cs`、`Generation/AsyncBuilder.cs`、`Character.cs`、
`ModEntry.cs`、`Prompts.cs`、`llms/Llm.cs`、`llms/LlmOpenAiBase.cs`（24%，含真实
上游血统：请求体组装、平台网络检查等）、`llms/LlmGemini.cs`、全部 UPSTREAM 类
patches、`UI/DialogueTextInputMenu.cs`、`config/ModConfigMenu.cs`、
`models/history/StardewEventHistory.cs`、`Platform/NetworkHelper.cs`、
`Interop/*ValleyTalkInterface.cs`（且新架构中无对应物）。
完整逐文件清单以 `ownership_map.json` 中 `class != MINE` 为准。

**内容资产（上游创作文本，一律重新创作，见 WP20）**：
`ContentPack/assets/bio/*`（33 个 NPC 传记）、`ContentPack/assets/GameSummary*.json`
（世界观综述）、`ContentPack/assets/Prompts.json`（提示词骨架）、
`Extensions/ValleyTalk for SVE/*`（SVE 传记集）、`translations/*`（上游随发的
fr-FR/zh-CN 译文，属上游文本的衍生）、`ContentPack/i18n/default.json`（27%，
英文 UI 文案随 WP15 重写，成本极低）、`docs/*`。

## 6. 完成标准（阶段 A 验收）

- 上述搬运文件全部落位、编译通过（桩允许存在）；
- `grep -r "namespace ValleyTalk" LivingNPCs/` 零结果；
- `LivingNPCs.Tests` 可运行（Skip 的测试有 WP 归属注释）；
- 全程未打开清单外的 ValleyTalk 文件（对话记录可审计）。
