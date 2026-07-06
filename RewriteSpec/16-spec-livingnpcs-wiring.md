# 16 · WP16 行为系统接线：解散 Interop 桥接层

> 阶段 D 工作包。前置阅读：README、00、01（尤其 §2 跨包接口与 §3）。与 WP12 并行，
> 依赖 WP10 的 `IDialogueEngine` / `GenerationRequest` 落地（可先按 01 §2 打桩开工）。

## 1. 目的与范围

旧世界里 LivingNPCs（行为系统）与 ValleyTalk（对话引擎）是两个 mod，靠 SMAPI 的
`ModRegistry.GetApi` 双向互调。本工作包：

1. **解散桥接层**：删除双向 interop 接口与代理调用，改为同一程序集内的直接方法调用。
2. **反转上下文注入方向**：行为上下文不再"引擎生成时回调拉取 + 行为侧主动推送 override"
   双通道，统一为调用方在 `GenerationRequest.BehaviorContext` 一次性注入。
3. **礼物邮件 / 记忆印象改真异步**：废除 requestId + 轮询缓存模式，改 `Task` 直等。
4. 更新 `LivingNPCs/manifest.json`（删除 `dandm1.ValleyTalk` 依赖）。

**不在本包范围**：新引擎本体（WP10）、Harmony 补丁与 UI（WP12）、
`GiftMailGenerator` / `MemoryImpressionGenerator` / `ConversationAnalysis` 等搬运件的
落位（阶段 A，见 03 §2——它们是 Yuki 原创 MINE 文件）、提示词文案（WP20）。

## 2. 权属与搬运边界

- **`LivingNPCs/` 侧全部代码是 Yuki 原创**：实现方可自由阅读、修改。本文档对这一侧
  写到类型与方法名。
- **旧 `ValleyTalk/src/Interop/` 五个文件全部废弃、一律不搬运**（03 §2/§3 已注明）：
  `IValleyTalkInterface.cs`、`ValleyTalkInterface.cs`、`ModInteropManager.cs` 为重写类
  （新架构中无对应物）；`LivingNpcConversationBridge.cs`（MIXED 21%）功能溶入本包；
  `ILivingNPCsApi.cs` 虽为 MINE 但随桥接层一起消亡。对它们本文档只作功能级描述。
- 桥接两端各自的**业务逻辑类**另有归属：ValleyTalk 侧的 `ConversationAnalysis.cs`、
  `GiftMailGenerator.cs`、`MemoryImpressionGenerator.cs`、`LivingNpcActionDecisionPass.cs`、
  `LivingNpcContextCompressor.cs` 都是 MINE 搬运件，阶段 A 已复制到
  `LivingNPCs/Dialogue/Engine/`，实现方可直接阅读搬运后的副本。
- `Shared/LivingNpcMetadataRules.cs`（Yuki 原创）目前以 `<Compile Include>` 链接方式
  同时编译进两个工程（LivingNPCs.csproj:22）；合并为单程序集后只剩一个消费者工程，
  链接可保留也可把文件移入仓库内 `LivingNPCs/` 目录——实现方任选，勿改内容。

## 3. 现状桥接全景（精确契约）

### 3.1 拓扑

两条方向、两个接口、四份类型副本：

- **LivingNPCs → ValleyTalk**：`IValleyTalkInterface`。ValleyTalk 定义并实现；
  LivingNPCs 在 `LivingNPCs/Interop/IValleyTalkInterface.cs` 保存一份**同名同签名的
  接口副本**（命名空间也叫 `ValleyTalk`），通过
  `helper.ModRegistry.GetApi<ValleyTalk.IValleyTalkInterface>("dandm1.ValleyTalk")`
  获得 SMAPI（Pintail）鸭子类型代理。唯一消费者是
  `LivingNPCs/Behavior/ValleyTalkPromptBridge.cs`（薄包装：判空、RSV 黑名单拦截、
  try/catch + i18n 日志，所有下游只认这个包装类）。
- **ValleyTalk → LivingNPCs**：功能级描述——ValleyTalk 侧有一个静态桥类，懒初始化时
  `GetApi("Yuki.LivingNPCs")` 拿到 LivingNPCs 的公共 API 对象
  （`LivingNPCs/ModEntry.cs` 的 `LivingNPCsApi`，转发到 `BehaviorEngine`），
  含三个方法：`GetConversationContext`、`GetGiftResponseContext`、
  `RecordValleyTalkExchange`。任一方向 API 拿不到时静默降级（返回空串 / 丢弃）。

### 3.2 `IValleyTalkInterface` 逐方法语义

| 方法 | 语义（功能级） | LivingNPCs 侧使用现状 |
|---|---|---|
| `SetModName(string)` | 声明调用方身份，作为 PromptOverride 存储的第三级键（未调用时用随机 GUID 兜底）。 | 连接成功后传 `"LivingNPCs"`（ValleyTalkPromptBridge.cs:38）。 |
| `IsEnabledForCharacter(NPC)` | 引擎是否会为该 NPC 生成对话：总开关、按角色禁用名单、模组内容拦截策略（无传记的模组 NPC 拒绝）、RSV 黑名单等的合取；**确定性判定，无随机成分**。 | **从未调用**（接口副本里有声明，无消费者）。 |
| `RequestGiftDialogue(NPC, Object gift, int taste)` | 请求引擎立刻为"NPC 收到某物品（taste 为原版 0–8 口味码）"生成一次 AI 反应对话，走引擎的异步生成排队。启用性判定同上（确定性，无概率门）；引擎正忙（已有一次生成在途）或网络不可用时拒绝。返回 true 仅表示"已受理排队"，不保证生成成功。 | `ConversationStartRecorder.DeliverHelpRequestItem`（:307）：求助物品递交后强制 NPC 给出 AI 反应；返回 false 时退回本地气泡致谢文案。 |
| `RegisterPromptOverride(characterName, promptElement, overrideText)` | 为角色注册一段按 `(角色, 元素名, mod 名)` 三级键存储的提示词覆盖块；同一 `(角色, 元素)` 下多个 mod 的块共存、依次注入。 | 唯一用法：`PushBehaviorContext(npc, text)` 固定 `promptElement = "ThirdPartyContext"`（ValleyTalkPromptBridge.cs:10, 51）。 |
| `ClearPromptOverride(characterName, promptElement)` | 删除单条（按本 mod 名）。 | 从未调用。 |
| `ClearPromptOverrides(characterName = "")` | 空参：清掉本 mod 注册的**全部**角色的全部覆盖块。 | `ValleyTalkPromptBridge.ClearAll()`；调用时机见 §3.6。 |
| `RequestGiftMailText(requestId, npcName, payloadJson)` | 发起一次后台 LLM 生成"礼物邮件正文"，结果按 requestId 存入引擎侧内存缓存（详见 §3.4）。 | `BehaviorMailService`（:211, :262）。 |
| `TryGetGiftMailText(requestId)` | 轮询：就绪返回校验过的正文，否则空串（**"未好"与"已失败"不可区分**）。 | 同上（:234）。 |
| `RequestMemoryImpression(requestId, npcName, payloadJson)` | 发起一次后台 LLM 调用，把一批被挤出的长期记忆压缩进"关系印象"段落，结果同样按 requestId 缓存。 | `MemoryImpressionService.SendRequest`（:144）。 |
| `TryGetMemoryImpression(requestId)` | 轮询，语义同 TryGetGiftMailText。 | `MemoryImpressionService.PollState`（:152）。 |

ValleyTalk 侧对 `RegisterPromptOverride` / `RequestGiftMailText` / `RequestMemoryImpression`
入口统一做 RSV 黑名单角色拦截（LivingNPCs 侧包装类也各自拦一次，双保险）。

### 3.3 ILivingNPCsApi 方向：上下文拉取与交换回传（时序）

一次**对话生成**的完整时序（现状）：

1. 玩家触发对话 → 引擎在游戏主线程装配生成上下文，**同步调用**
   `GetConversationContext(npcName, npcDisplayName)`；礼物路径改调
   `GetGiftResponseContext(npcName, npcDisplayName, giftItemId, giftName, taste)`
   （giftItemId 为 QualifiedItemId）。拿到的文本作为"LivingNPCs 附加上下文"进入提示词
   （注入位置见 §3.5）。
2. `BehaviorEngine.GetConversationContext`（:587）委托
   `ValleyTalkContextService.BuildPromptContext(npc)`：基础记忆连续性摘要
   （`BehaviorMemory.BuildPromptContext`）+ 礼物机会段 + 求助机会段 + 出游段。
   **前提校验**：`EnableConversationMemory` 开启且 NPC 位于玩家当前地图
   （`TryFindNpcInCurrentLocation`），否则空串。
   `GetGiftResponseContext`（:602）委托 `BuildGiftResponseContext`：求助物品递交
   压过口味反应，或提示"稍后有回礼邮件"。
3. LLM 输出末尾带隐藏元数据块（格式见 §3.4）。引擎解析出分析对象、剥掉隐藏块得到
   可见文本，生成完成后调用 `RecordValleyTalkExchange(npcName, npcDisplayName,
   playerText, visibleNpcResponse, analysisJson)`，其中 `analysisJson` 是分析对象
   **重新序列化**的 JSON（camelCase 字段，schema 同 §3.4）。调用发生在 LLM 响应的
   continuation 上，即**线程池后台线程**（桌面无 SynchronizationContext）。
4. `BehaviorEngine.RecordValleyTalkExchange`（:476）只做可在任意线程执行的参数校验
   （RSV 黑名单、开关、playerText 非空），然后把五元组塞进
   `ConcurrentQueue<PendingValleyTalkExchange>`，返回 true = "已入队"。
   下一次 `UpdateTicked`（主线程）`ProcessPendingValleyTalkExchanges` 出队，
   `ApplyValleyTalkExchange`（:512）：经 `ValleyTalkExchangeParser.Parse` 归一化后
   写记忆/好感/求助/冲突/情绪，执行世界动作（送小礼、给钱、出游、节日互动、协助任务），
   排队环境跟进台词，最后 `PushInteractionContext` 刷新推送上下文（见 §3.5）。
   NPC 已不在当前地图则整条丢弃。

礼物邮件/记忆印象的**轮询时序**（现状）：

- 礼物邮件：`BehaviorMailService.TryStartGiftMailGeneration`（:197）在礼物邮件事实
  （`NpcGiftMailFact`）创建时发起，requestId = `MailKey`（格式
  `LivingNPCs.GiftMail.{npc}.{totalDays}.{timeOfDay}.{5位随机}`），仅限
  `AiMailMotives` 白名单动机。`ResolvePendingGiftMailGenerations`（:219）在
  SaveLoaded 与 DayStarted 轮询所有 `GenerationStatus == "pending"` 的邮件：
  取到文本→本地再校验（无 `%`/`[`/`]`、语言正确）→ "ready"（存 `GeneratedBody`，
  入存档）或 "failed"；取不到→ `GenerationAttempts++`，满 `MaxGenerationAttempts = 3`
  次标 "failed"，否则**重发同 requestId 请求**（防引擎重启丢内存缓存）。失败回落模板正文。
- 记忆印象：`MemoryImpressionService`。DayStarted 主循环 `ProcessDayStart`：有在途
  请求先记一次尝试并轮询；无在途且 `ShouldRequest`（backlog ≥ 8 条，或最老条目积压
  ≥ 28 天；失败后冷却 3 天）则把 backlog 前 `MaxImpressionBatch` 条移入
  `ImpressionInFlight`，requestId = `impression-{npc}-{Guid:N}`，每天最多 2 个新请求。
  TimeChanged 时 `PollPending` 免费轮询（不计尝试）。拿到结果 `ApplyResult` 才丢弃
  in-flight 批次；3 次日始尝试无果 `FailRequest` 把批次原样塞回 backlog 头部——
  **模型失败永不丢数据**。重试时换新 requestId 覆盖同一批次。

### 3.4 结构化隐藏数据往返格式（逐字精确，LivingNPCs 侧定义）

**LLM 输出中的隐藏标记**（引擎侧解析，解析器为 MINE 搬运件，原样保留）：

- 标记字面量：`!LIVINGNPCS_META`（区分大小写，取**最后一次**出现位置）。
- 标记之后找到第一个 `{`，按字符串感知的花括号配平截取一个完整 JSON 对象
  （支持转义与字符串内花括号）；配平失败或标记后无对象 → 记 Trace 日志并按"无元数据"处理。
- 可见文本 = 原文剥去隐藏块之后的部分（剥除逻辑在引擎的文本后处理搬运件中）。

**JSON schema**（camelCase；两侧模型：引擎侧 `ConversationAnalysis`（搬运件），
行为侧 `ValleyTalkExchangeAnalysis`（BehaviorMemoryModels.cs:83），字段一一对应）：

```jsonc
{
  "rapportDelta": 0,               // int，钳制到 [0, 30]
  "endConversation": false,        // bool
  "memories": [{                   // ≤4 条
    "kind": "fact",                // 归一化：长期记忆类别枚举
    "summary": "", "importance": 0,     // importance 钳制 [0,100]
    "playerPreference": false, "playerPreferenceKind": "none",
    "subject": "", "tags": []           // tags ≤6 个，归一化
  }],
  "ambientFollowUp": { "text": "", "delayMinutes": 0 },   // delay ≤120
  "emotionImpact": { "emotion": "none", "intensityDelta": 0,
                     "apology": false, "repairDelta": 0, "reason": "" },
  "actions": [{                    // ≤1 条
    "type": "none",                // give_small_gift | give_meaningful_gift | give_money |
                                   // companion_outing | festival_interaction | assist_quest | none
    "amount": 0,                   // 金额钳制 ≤250
    "durationMinutes": 0,          // 出游 ≤600，其余 ≤20
    "delayMinutes": 0,             // ≤20
    "reason": "", "targetLocation": "", "travelConsent": "",
    "questHint": "", "itemId": "", "itemLabel": ""
  }],
  "behaviorInfluences": [{         // ≤2 条
    "type": "none", "summary": "", "targetLocation": "", "targetLocationLabel": "",
    "durationDays": 0, "intensity": 0, "maxTriggers": 0   // ≤7 / ≤100 / ≤4
  }],
  "helpRequests": [{               // ≤1 条
    "type": "none", "summary": "", "requiresAcceptance": true,
    "steps": [{ "type": "none", "summary": "", "requestedItemId": "",
                "requestedItemLabel": "", "questionTopic": "" }],   // ≤3 步
    "requestedItemId": "", "requestedItemLabel": "", "questionTopic": "",
    "dueInDays": 3, "reason": "", "followUpPotential": "none"
  }],
  "helpRequestUpdates": [{ "summary": "", "status": "none", "resolution": "" }], // ≤2
  "conflicts": [{ "causeKind": "dialogue", "summary": "", "severity": 0 }]       // ≤2
}
```

所有枚举归一化与数值钳制的**单一事实来源**是 `Shared/LivingNpcMetadataRules.cs`
（两侧共同编译）+ 行为侧 `Behavior/Rules/BehaviorValueNormalizer.cs`。**双重归一化是
有意设计**：引擎侧解析时钳一次（防脏数据入提示词日志），行为侧
`ValleyTalkExchangeParser.Parse`（System.Text.Json、大小写不敏感）再钳一次
（防桥接对端版本不齐）。合并后仍保留双侧钳制，成本为零且防御未来第三方注入。

补充：引擎侧还有一个**补充动作判定 pass**（MINE 搬运件 `LivingNpcActionDecisionPass`）：
主回复缺失 actions/helpRequests/helpRequestUpdates 且对话看起来相关时，追加一次小
LLM 调用产出仅含这三个字段的同格式 JSON，`MergeSupplementalActionMetadata` 合并。
此机制随搬运件进入新引擎，接口不变。

**礼物邮件请求 payload**（LivingNPCs → 引擎，`BehaviorMailService.BuildGenerationPayload`:271）：

```jsonc
{ "motive": "reciprocal",      // reciprocal | birthday | help_request_reward（白名单）
  "itemLabel": "", "sourceGift": "", "npcDisplayName": "",
  "tier": "small",             // small | meaningful；引擎侧现状忽略此字段
  "timeoutSeconds": 30 }       // 引擎侧钳制 [5,120]
```

**记忆印象请求 payload**（`MemoryImpressionService.BuildPayload`:206）：

```jsonc
{ "npcDisplayName": "",
  "existingImpression": "",    // 现有印象段落，可空
  "memories": ["[-3d] ……"],   // 每条可选 "[-N d]" 前缀 = 约 N 游戏日前
  "timeoutSeconds": 45 }       // 引擎侧钳制 [10,180]
```

返回值均为纯文本（邮件正文 / 印象段落），引擎侧已做格式校验、语言校验与长度截断
（印象 ≤1200 字符、合并为单段落）。

### 3.5 PromptOverride 机制与"双通道"现状

现状存在**两条内容高度重叠的上下文通道**，这是理解本包设计的关键：

- **拉取通道**：引擎每次生成开始时调 `GetConversationContext` / `GetGiftResponseContext`
  （§3.3 步骤 1），结果注入提示词的"LivingNPC 附加上下文"专属节。该节受引擎的上下文
  路由控制：全文注入或经压缩器（MINE 搬运件 `LivingNpcContextCompressor`）压成简报。
- **推送通道**：LivingNPCs 在行为事件发生时主动
  `RegisterPromptOverride(npc, "ThirdPartyContext", text)` 推一份几乎相同的上下文
  （`ValleyTalkContextService.PushInteractionContext`:43）。引擎在提示词中另有一个
  固定追加的"第三方上下文"节，把该角色名下**所有 mod** 注册的 override 依次注入
  （同样受路由的全文/简报分级）；当同一次生成已通过拉取通道拿到 LivingNPCs 上下文时，
  内容上被识别为 LivingNPCs 风格的 override 会被跳过以免重复。

推送时机（谁在调 `PushInteractionContext` / `PushBehaviorContext`）：
小行为执行成功后（BehaviorEngine.cs:826）、对话/事件/送礼被记录时
（ConversationStartRecorder.cs:147, 164, 250, 254, 301, 397）、交换应用完成后
（BehaviorEngine.cs:568）、AI 礼物/出游等 runtime 通过 `BehaviorEngineServices` 注入的
委托（:114, :139, :153, :165）。

**推送通道存在的唯一实质理由**：`PushInteractionContext(npc, msg, immediatePromptContext)`
的第三参"即时线索"（如"玩家刚递交了求助物品，正在等你反应"，
ConversationStartRecorder.cs:301-305）只进推送文本，`BuildPromptContext(npc)` 的
拉取路径**不含**这个块。其余部分推与拉完全同源。

清理时机：`ClearAll()`（= `ClearPromptOverrides("")`）在 DayStarted、ReturnedToTitle、
手动清记忆后调用（BehaviorEngine.cs:161, 188, 242）——override 是"当日态"，每天重建。

### 3.6 线程假设（现状小结）

- `IValleyTalkInterface` 全部方法：LivingNPCs 只在游戏主线程调用（SMAPI 事件处理器内）。
  引擎侧 `Request*` 方法约定"在调用线程捕获全部游戏状态，仅网络调用进后台
  `Task.Run`"，后台并发受信号量限制（邮件 2 并发、印象 1 并发），结果缓存为
  `ConcurrentDictionary`，`TryGet*` 任意线程安全。
- `GetConversationContext` / `GetGiftResponseContext`：引擎在生成装配阶段调用，实际
  发生在主线程（生成任务从 UpdateTicked 启动，装配在首个 await 之前）。**但行为侧未
  设防**——它直接读 `Game1.Date`、遍历 NPC 状态。
- `RecordValleyTalkExchange`：**后台线程进入**，行为侧以 ConcurrentQueue + 主线程
  UpdateTicked 消费解决（§3.3 步骤 4）。这是全桥接唯一明确跨线程的调用。

## 4. 目标设计与映射表

### 4.1 逐能力映射

| 旧桥接能力 | 新直调路径 |
|---|---|
| 双向 `GetApi` + 接口副本 + Pintail 代理 | 删除。`BehaviorEngine` 与新引擎同程序集，直接持有 `LivingNPCs.Dialogue.IDialogueEngine` 引用（由 `ModEntry` 组装时注入 `BehaviorEngineServices`）。 |
| `GetConversationContext` / `GetGiftResponseContext`（引擎回调拉取） | **方向反转**：WP12 在触发生成、构造 `GenerationRequest` 时，调用行为系统的 `ValleyTalkContextService.BuildPromptContext(npc)` / `BuildGiftResponseContext(npc, itemId, name, taste)` 填入 `GenerationRequest.BehaviorContext`（string）。行为系统不再暴露跨 mod API。 |
| `RegisterPromptOverride("ThirdPartyContext")` 推送通道 | **废除**。唯一不可替代的"即时线索"改为行为侧新增 per-NPC 暂存：`ValleyTalkContextService` 增加 `SetImmediateContext(npc, text)`（一次性/当日有效，下一次 `BuildPromptContext` 合并后清除或按天过期），原 `PushInteractionContext` 调用点改写暂存。见开放问题 8.1。 |
| `ClearPromptOverrides` 每日清理 | 随推送通道消亡；即时暂存在 DayStarted/ReturnedToTitle 清空（沿用 BehaviorEngine 现有清理点）。 |
| `RecordValleyTalkExchange`（跨 mod、后台线程、轮询队列应用） | 引擎生成完成后**直接调用** `BehaviorEngine.RecordValleyTalkExchange(...)`（签名不变，改 internal 直调）。**保留 ConcurrentQueue + UpdateTicked 主线程应用模型**：流式路径的完成回调仍可能在后台线程（见 §4.3）。`analysisJson` 参数可保留 string（最小改动），或直接传搬运件 `ConversationAnalysis` 对象——见开放问题 8.2。 |
| `RequestGiftDialogue` | `ConversationStartRecorder` 改调新引擎的礼物生成入口（`IDialogueEngine.GenerateAsync`，`GenerationRequest` 带礼物字段；具体请求形状以 WP10 为准）。保留"引擎忙/网络不可用→false→本地气泡兜底"语义：新入口需同步返回是否受理（或 `IsEnabledFor` + 受理检查组合）。 |
| `IsEnabledForCharacter` | 对应 `IDialogueEngine.IsEnabledFor(NPC)`（01 §2 已钉死）。行为侧现状无调用点，无需接线，仅供 WP12 使用。 |
| `SetModName` | 随 override 存储消亡，无对应物。 |
| `RequestGiftMailText` / `TryGetGiftMailText` 轮询 | 搬运件 `GiftMailGenerator` 改造为真异步 API：`Task<string?> GenerateAsync(GiftMailRequest, CancellationToken)`（参数即 §3.4 payload 的强类型化，废除 requestId 与 JSON 序列化往返；null/空 = 失败）。`BehaviorMailService` 发起后在 continuation 回主线程写 `NpcGiftMailFact.GeneratedBody/GenerationStatus`。**持久化语义保留**：`GenerationStatus == "pending"` 仍入存档，重启后 SaveLoaded/DayStarted 的 `ResolvePendingGiftMailGenerations` 改为"对 pending 且无在途 Task 的邮件重新发起"，尝试计数与 3 次上限、失败回落模板全部保留。 |
| `RequestMemoryImpression` / `TryGetMemoryImpression` 轮询 | 同理，搬运件 `MemoryImpressionGenerator` 改 `Task<string?>`。`MemoryImpressionService` 保留 backlog / ImpressionInFlight / attempts / 冷却的**存档结构与状态机不变**（防丢数据设计原样保留，WP14 契约），仅把"日始计数轮询 + 换 requestId 重发"替换为"发起 Task → 完成回调回主线程 ApplyResult / 失败走 FailRequest；游戏退出丢失在途 Task 时，重启后日始重新发起同一批次"。`ImpressionRequestId` 字段保留用于存档兼容（可仅作日志标识）。 |

### 4.2 回主线程的统一约定

新引擎完成回调（含 RecordExchange、邮件/印象 Task 的 continuation）一律视为
**后台线程**。行为侧写游戏状态（好感、背包、金钱、任务日志）与共享内存前必须回主线程。
可复用两种现成模式：BehaviorEngine 的 ConcurrentQueue + UpdateTicked（交换记录用），
或新增一个轻量 main-thread dispatcher（若 WP10 已提供"结果经 callback 回主线程"的
保证——01 §2 注释——则邮件/印象 continuation 可以简化，但**不得假设**，以引擎实际
文档为准；防御性入队的成本可忽略）。

### 4.3 生成期间上下文的一致性

现状"生成装配在主线程同步拉上下文"这一性质必须保留：`BehaviorContext` 必须在
**触发生成的主线程时刻**装配完成（WP12 责任），不得让引擎在后台线程回头调用行为系统
——这是删除拉取回调后的自然结果，此处仅为验收断言：新引擎持有的是不可变字符串。

## 5. LivingNPCs 侧改动文件清单

| 文件 | 改动性质 |
|---|---|
| `LivingNPCs/Interop/IValleyTalkInterface.cs` | **删除**（接口副本失去意义）。 |
| `LivingNPCs/Behavior/ValleyTalkPromptBridge.cs` | **删除或改写为薄适配器**。建议改写为 `DialogueEngineLink`（同名方法转发到 `IDialogueEngine` + 邮件/印象生成器），保住全部现有调用点的形状与测试可桩性（§6 相关：WP12/16 未就绪时可注入 null 引擎，各方法降级为现状"未连接"行为）。 |
| `LivingNPCs/Behavior/BehaviorEngineServices.cs` | 构造注入 `IDialogueEngine`（替换 `new ValleyTalkPromptBridge(...)`）。 |
| `LivingNPCs/Behavior/BehaviorEngine.cs` | `TryInitialize`/`ClearAll` 调用点清理；`RecordValleyTalkExchange` 保持签名、改由引擎直调；`GetConversationContext`/`GetGiftResponseContext` 保留逻辑但降为 internal（供 WP12 装配 `BehaviorContext`）。 |
| `LivingNPCs/ModEntry.cs` | `GetApi`/`LivingNPCsApi`：三个方法失去唯一消费者。建议**保留类但清空为版本信息类 API 或直接删除**——见开放问题 8.3。引擎构造与注入接线。 |
| `LivingNPCs/Behavior/Runtime/ValleyTalkContextService.cs` | 新增即时上下文暂存（§4.1）；`PushInteractionContext` 改写。 |
| `LivingNPCs/Behavior/Runtime/ConversationStartRecorder.cs` | `TryRequestGiftDialogue` 调用点改新引擎入口。 |
| `LivingNPCs/Behavior/Runtime/BehaviorMailService.cs` | 邮件生成 Task 化（§4.1），轮询函数改重发起语义。 |
| `LivingNPCs/Behavior/Memory/MemoryImpressionService.cs` | 印象生成 Task 化（§4.1），状态机不变。 |
| `LivingNPCs/ModConfig.cs` + `ModConfigMenu.cs` | `EnableValleyTalkPromptBridge` 改名（如 `EnableBehaviorContextInDialogue`），`Migrate()` 里做旧键值搬移（WP14/15 的配置迁移机制）。 |
| `LivingNPCs/i18n/default.json`、`zh.json` | `log.bridge.*`、`gmcm.bridge.*`、`bridge.pushed/notPushed` 文案改写为直连语义（键名可换，走 WP15）。 |
| `LivingNPCs/manifest.json` | 见 §6。 |
| **保留不动** | `Behavior/Parsing/ValleyTalkExchangeParser.cs`、`Behavior/Models/BehaviorMemoryModels.cs`（`ValleyTalk*` 类型名不改，见开放问题 8.4）、`Shared/LivingNpcMetadataRules.cs`（内容）。 |

## 6. 与其他工作包的接口

- **WP10（引擎）**：本包消费 `IDialogueEngine.IsEnabledFor/GenerateAsync`；要求
  `GenerationRequest` 含 `BehaviorContext`（string，可空）字段与礼物字段；要求引擎在
  每次生成完成（含礼物路径、随机搭话路径）后回调本包注册的交换记录钩子
  （建议形状：引擎构造时接收 `Action<NPC,string,string,ConversationAnalysis>` 或
  等价接口）。搬运件 `ConversationAnalysis`/`GiftMailGenerator`/
  `MemoryImpressionGenerator`/`LivingNpcActionDecisionPass`/`LivingNpcContextCompressor`
  归 WP10 目录，其 Task 化改造（§4.1）由本包与 WP10 协商归属——建议本包执行、WP10 评审。
- **WP12（游戏集成）**：对话/礼物触发点负责装配 `BehaviorContext`（调用本包提供的
  `ValleyTalkContextService` 两个 Build 方法）。**共存检测归 WP12**（01 §5：检测到
  `dandm1.ValleyTalk` 仍加载→错误日志 + HUD + 引擎关闭）；本包提供判定所需保证：
  行为系统在引擎为 null/关闭时完全可运行（现状即如此——桥接未连接即全降级，改造后
  `DialogueEngineLink` 保持同一降级语义）。
- **WP14（持久化）**：`NpcGiftMailFact.GenerationStatus/GenerationAttempts/GeneratedBody`
  与 `LivingNpcState.ImpressionBacklog/ImpressionInFlight/ImpressionRequestId/
  ImpressionRequestAttempts/LastImpressionFailureTotalDays` 的存档 schema **不变**。
- **WP15（配置/i18n）**：配置键改名迁移、bridge 文案键替换。
- **WP20（提示词）**：`!LIVINGNPCS_META` 标记与 §3.4 schema 是提示词必须让模型输出的
  格式（指令文案重新创作，格式逐字保留）；邮件/印象两个独立小提示词的需求已在搬运件中
  （中文原文属 Yuki，可参考）。

**manifest 变更**（`LivingNPCs/manifest.json`）：删除 `Dependencies` 中
`dandm1.ValleyTalk` 条目（现状 IsRequired: false）；保留 GMCM 可选依赖；
`Description` 里的 "optional ValleyTalk prompt integration" 字样同步改写；
版本号升 0.2.0（01 §5，由发版流程统一执行）。

## 7. 验收要点

1. 仓库内 `GetApi<`、`"dandm1.ValleyTalk"`（共存检测点除外）、`IValleyTalkInterface`、
   `ILivingNPCsApi` 零残留；`LivingNPCs/Interop/` 目录删除。
2. 对话生成的提示词中出现行为上下文（连续性摘要/礼物机会/求助机会/出游段），且
   仅出现一次（双通道去重问题随推送通道消亡自动消除）。
3. 求助物品递交 → 即时线索出现在**紧随其后的那次**生成里；隔天/回标题后不再出现。
4. 交换元数据端到端：模型输出 `!LIVINGNPCS_META {…}` → 好感/记忆/世界动作在**主线程**
   应用（可用调试日志断言线程）；玩家可见文本无隐藏块残留。
5. AI 礼物邮件：断网/无模型时回落模板；生成中途退出游戏，重启后重新发起并最终送达或
   3 次后回落模板。
6. 记忆印象：模型三连失败后 backlog 无损、冷却生效；成功后 in-flight 清空、印象更新。
7. `LivingNPCs.Tests` 全绿（解析器与规则测试不依赖桥接，理应零改动通过）；
   新增 `DialogueEngineLink` 降级行为（引擎 null）单元测试。
8. manifest 无 `dandm1.ValleyTalk`；与残留 ValleyTalk 同装时行为系统照常、引擎关闭
   （联合 WP12 验收）。

## 8. 开放问题（由用户裁决）

1. **即时线索暂存的生命周期**：建议"下一次生成消费后即清 + 当日结束兜底清"；
   备选"仅当日有效可重复消费"。影响：求助递交后连续多次对话是否都强调该事件。
2. **RecordExchange 的参数形状**：维持 `analysisJson` 字符串（改动最小、双侧钳制
   模式不变）还是直传 `ConversationAnalysis` 对象（省一次序列化，但行为侧
   `ValleyTalkExchangeParser` 需加对象重载）。建议前者，0.2.0 后再简化。
3. **是否保留面向第三方的公共 API**：旧 `RegisterPromptOverride` 生态上是给其他 mod
   注入角色上下文的入口（override 存储按 mod 名分键即为此设计），但据知无第三方消费者。
   00 已声明不保留 interop 兼容。**建议**：0.2.0 内化（不发布公共 API），在
   `LivingNPCsApi` 留空壳或删除；若日后有需求再按新命名（如
   `RegisterDialogueContext(modId, npcName, text)`）设计——记录于此以免遗失设计意图。
4. **`ValleyTalk*` 类型名与 `ValleyTalkContextService`/`ValleyTalkExchangeParser`
   文件名是否更名**（如 `Dialogue*`）：01 §3 说解析器"原样保留"。**建议**：0.2.0 不改
   （避免大面积 churn 干扰审计），发布后做一次纯重命名提交。
5. `EnableValleyTalkPromptBridge` 关闭时的新语义：现状=不连桥（无上下文注入、无 AI
   邮件/印象）。直连后建议拆为"行为上下文注入"单开关，AI 邮件/印象已各有独立开关
   （`EnableAiGiftMail`、`EnableMemoryImpressions`），只需把它们对桥连接的判定改为对
   引擎可用性的判定。

## 9. 审计索引（file:line，撰写时点）

- ValleyTalk/src/Interop/IValleyTalkInterface.cs:3-15（接口全貌）
- ValleyTalk/src/Interop/ValleyTalkInterface.cs:17-101（各方法实现语义、RSV 拦截）
- ValleyTalk/src/Interop/ModInteropManager.cs:11-89（override 三级字典、GetPromptOverrides）
- ValleyTalk/src/Interop/LivingNpcConversationBridge.cs:13-106（反向 GetApi、异常降级）
- ValleyTalk/src/Interop/ILivingNPCsApi.cs:3-25（反向接口签名）
- ValleyTalk/src/Generation/DialogueBuilder.cs:110,139,144-149,190（上下文拉取与礼物路径
  RecordExchange 时序）、595-655（PatchNpc 判定、probability=4 即无随机门、CanGenerateForNpc）
- ValleyTalk/src/Generation/AsyncBuilder.cs:33-44（生成从 UpdateTicked 启动）、
  205-223（流式完成后后台线程 RecordExchange）、318-333（RequestNpcGiftResponse 忙拒绝）、404
- ValleyTalk/src/Generation/ConversationAnalysis.cs:88-124（`!LIVINGNPCS_META` 配平解析、
  ToJson）、328-516（schema 全字段）
- ValleyTalk/src/Prompts.cs:102,350,360-363,672-718（两个注入节、去重、全文/简报分级）
- ValleyTalk/src/Generation/GiftMailGenerator.cs:47-105（payload 字段、requestId 缓存、
  超时钳制）、107-156（后台生成、校验、Fail）
- ValleyTalk/src/Generation/MemoryImpressionGenerator.cs:48-112（payload 字段）、179-198
  （归一化、1200 字符截断）
- ValleyTalk/src/manifest.json:6（UniqueID dandm1.ValleyTalk）
- LivingNPCs/Interop/IValleyTalkInterface.cs:1-17（接口副本）
- LivingNPCs/Behavior/ValleyTalkPromptBridge.cs:9-10,24-40,42-59,61-176（全部包装方法）
- LivingNPCs/ModEntry.cs:13-15,52-75（GetApi 与 LivingNPCsApi）
- LivingNPCs/Behavior/BehaviorEngine.cs:59（ConcurrentQueue）、132,161,188,242
  （TryInitialize/ClearAll 调用点）、362-382（UpdateTicked 消费）、476-502
  （RecordValleyTalkExchange 线程注释与入队）、504-585（主线程应用与世界动作分发）、
  587-615（两个上下文出口）、640-646（世界动作类型枚举）、819-845（行为后推送）
- LivingNPCs/Behavior/BehaviorEngineServices.cs:47,53-54,71-165（服务装配与推送委托）
- LivingNPCs/Behavior/Runtime/ValleyTalkContextService.cs:43-66（PushInteractionContext
  与 immediate 参数）、68-101（BuildPromptContext 组装）、155-185（BuildGiftResponseContext）
- LivingNPCs/Behavior/Runtime/ConversationStartRecorder.cs:301-314（即时线索推送 +
  TryRequestGiftDialogue 兜底）
- LivingNPCs/Behavior/Runtime/BehaviorMailService.cs:14-18（MailKey 前缀、3 次上限、
  动机白名单）、176（MailKey 格式）、197-282（发起/轮询/重发/payload）
- LivingNPCs/Behavior/Memory/MemoryImpressionService.cs:21-25（阈值常数）、123-216
  （状态机全流程、requestId 格式、payload、[-Nd] 前缀）
- LivingNPCs/Behavior/Parsing/ValleyTalkExchangeParser.cs:15-157（行为侧二次归一化）
- LivingNPCs/Behavior/Models/BehaviorMemoryModels.cs:83-105（ValleyTalkExchangeAnalysis）
- LivingNPCs/LivingNPCs.csproj:20-23（Shared 链接编译）
- LivingNPCs/manifest.json:10-14（依赖声明）
- LivingNPCs/ModConfig.cs:60（EnableValleyTalkPromptBridge）
- LivingNPCs.Tests/MetadataParsingTests.cs:85,121、CompanionOutingRulesTests.cs:19-45
  （测试仅依赖解析器与模型，不依赖桥接）
