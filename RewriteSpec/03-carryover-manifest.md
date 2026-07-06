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

## 7. 实现记录（2026-07-06，Stage A）

- 已按 §2/§3 将允许搬运的对话相关源码落位到 `LivingNPCs/Dialogue/{Engine,Llm,Ui,GameHooks,Persistence,Diagnostics}`，命名空间改为 `LivingNPCs.Dialogue.*`。
- 已复制原创测试到 `LivingNPCs.Tests/Dialogue/` 并改为新命名空间；其中 `ContextRoutingPlanTests.PromptOptimizationAndRoutingConfigKeysAreLocalizedAndInjected` 标记为 `WP15-TODO` skip，因为提示词、配置与 i18n 资产会在 WP15 重写，Stage A 不读取旧内容资产。
- 新增过渡门面 `DialogueServices` 以及 `LegacyStubs`/`LegacyLlmStubs`，用于替代旧静态入口并让后续 WP10/WP11/WP12 接管未实现接口。
- `LivingNPCs/ModEntry.cs` 已初始化 `DialogueServices`；`LivingNPCs/LivingNPCs.csproj` 与测试工程已补齐本阶段编译所需的 SMAPI/Stardew/Harmony/Newtonsoft 引用。
- 为满足 Stage A 命名空间验收，旧 LivingNPCs 互操作接口已从 `namespace ValleyTalk` 移到 `LivingNPCs.Interop`。旧桥接整体删除与进程内直连由 WP16 处理。
- 新搬运模块中的用户可见/日志标题已从 `ValleyTalk` 改为 `LivingNPCs`；测试注释中保留的旧名仅用于标注 WP15 旧资产迁移来源。
- `LlmThinking.DescribeThinkingParameters` 避免依赖运行时缺失的 Newtonsoft.Json `JToken.ToString(Formatting)`/`WriteTo(JsonWriter)` 重载，改用本地紧凑化辅助函数。

验证：

- `dotnet test LivingNPCs.Tests\LivingNPCs.Tests.csproj`：通过 262，跳过 1，失败 0；仍有搬运文件的 nullable 警告，后续工作包可逐步清理。
- `rg -n "namespace ValleyTalk|using ValleyTalk|\[ValleyTalk\]" LivingNPCs LivingNPCs.Tests`：零结果。
- `rg -n "ValleyTalk" LivingNPCs\Dialogue LivingNPCs.Tests\Dialogue`：仅剩测试注释/skip 测试中的旧资产路径说明，无新模块运行时代码命中。

洁净室记录：本阶段实现未打开清单外的 `ValleyTalk/`、`ValleyTalk.Tests/`、`upstream-ValleyTalk/` 源文件；后续实现应继续只读取 RewriteSpec、LivingNPCs、Shared、LivingNPCs.Tests 与已经落位的新目录。