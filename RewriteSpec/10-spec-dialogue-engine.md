# 10 · WP10 对话生成引擎（LivingNPCs.Dialogue.Engine）

> 阅读顺序：README → 00 → 01 → 本文档。本文档描述**行为**，不描述旧实现；
> 实现方按 01 §2 的接口自行设计类结构。文中"WP20 键"指提示词骨架文案的键名，
> 文案本身由 WP20 重新创作；引擎只负责按键取文并按本文档的条件与顺序拼装。

## 1. 目的与范围

WP10 实现对话生成的核心编排：接收游戏侧的生成请求（对话、送礼、日程/事件台词、
婚姻台词），装配上下文，拼装提示词，调用 WP11 的 LLM 客户端，解析/校验/后处理
响应，产出可直接交给游戏的对话数据；并把交换结果回传给行为系统（WP16）与
历史存储（WP14）。

范围内：请求接纳与去重、上下文快照、提示词分节装配、提示词片段缓存、
LLM 调用编排（含重试/超时/流式）、响应解析与校验、多选项响应拼装、
历史采样与 token 预算、每 NPC 启用判定的入口。

范围外（仅消费其接口）：LLM 网络层（WP11）、Harmony 补丁与 UI 窗口（WP12）、
持久化与迁移（WP14）、资产与配置加载（WP15）、行为系统数据处理（WP16）、
提示词文案（WP20）。

## 2. 权属与搬运边界

本包**重写**旧世界的：对话编排器（单例门面）、异步调度器、上下文快照类型、
提示词装配类、角色运行时（传记/对话样本/历史的每 NPC 聚合）、历史记录模型
（四类记录 + 采样）、世界观摘要拼装器、提示词片段缓存。

以下为**搬运件**（03 清单，本文档只写接口关系，内部行为勿再规定）：
`ContextRoutingDecisionPass`、`ContextRoutingPlan`、`ConversationAnalysis`、
`LivingNpcActionDecisionPass`、`LivingNpcContextCompressor`、
`MemoryImpressionGenerator`、`GiftMailGenerator`、`GeneratedResponse`、
`StreamingDialoguePreview`、`StreamingResponseOption`、
`ConversationTextPostProcessor`、`ConversationCues`、`RsvAiPolicy`、
`GiftMailContentValidator`，以及诊断导出器（AiResponseLogExporter、
PromptLogExporter、ContextRoutingLogExporter、ConversationTranscriptExporter）。

## 3. 外部契约（逐字精确，兼容性必需）

### 3.1 LLM 响应文本格式（引擎 ↔ 模型的解析契约）

模型被要求输出（格式约定由 WP20 文案传达，解析由本包实现）：

1. **第一行 NPC 台词**：以 `- ` 开头（短横线前缀是判定台词行的标志）。
2. **玩家回应选项行**（可选，多行）：每行以 `%` 开头。
3. **结构化元数据尾部**（可选）：标记 `!LIVINGNPCS_META` 后跟一个平衡的
   JSON 对象。解析取**最后一次**出现的标记；标记后无 `{` 或括号不平衡则
   视为无元数据。元数据解析与规范化由搬运件 `ConversationAnalysis.Parse`
   完成，字段名（camelCase，解析大小写不敏感）：`rapportDelta`、
   `endConversation`、`memories`、`ambientFollowUp`、`emotionImpact`、
   `actions`、`behaviorInfluences`、`helpRequests`、`helpRequestUpdates`、
   `conflicts`。子字段结构以搬运件类型定义为准。

台词行内允许出现 Stardew 对话控制记号（见 3.2）与肖像情绪记号
`$h $s $l $a`（外加传记 `ExtraPortraits` 定义的自定义键）。

### 3.2 Stardew 对话字符串格式（引擎产出 ↔ 游戏）

- 分页：`#$b#`；结束：`#$e#`；情绪：`$<key>`（紧跟在分段末尾）；
  `@` 会被游戏替换为农夫名。
- 多选项应答菜单（FormattedLine 尾部拼装）：
  - `#$q {index} {SLD_前缀}Default#{outputRespond文案}` —— `index` 为静态
    自增计数器，初值 **20000**；
  - `#$r -999999 0 {SLD_前缀}Silent#{outputStaySilent文案}`（保持沉默）；
  - 每个模型生成的选项一条：`#$r -999998 0 {SLD_前缀}Next#{选项文本}`；
  - 配置 `TypedResponses != "Never"` 时追加
    `#$r -999997 0 {SLD_前缀}TypedResponse#{uiTypeYourResponse文案}`（自行输入）。
- 对话键前缀：`SLD_`。保留键：`SLD_Default`、`SLD_Silent`、`SLD_Next`、
  `SLD_TypedResponse`、`SLD_Conversation`（连续会话）、`SLD_Error`（失败回退）、
  `SLD_Streaming`（流式窗占位）。送礼应答键：`Accept_{礼物内部名}`。
- 会话应答行前缀 `skip#`：`GenerateResponseDetailed` 默认加在 FormattedLine
  前（`dontSkipNext=false` 时），供 WP12 的补丁识别"跳过下一句原版台词"。
  相关常量：`DialogueGenerationTag = "$$$%%%"`、`DialogueSkipTag = "$$%%"`
  （由 WP12 的补丁消费，本包只需在常量类中提供）。
- 主动送礼标记：若本次生成决定配偶送礼（见 4.6.11），在第一行台词末尾追加
  `[{物品ID}]`（方括号包裹的物品 ID），由 WP12 的呈现层解析并发放物品。

### 3.3 回传行为系统的 JSON

生成完成后调用行为系统的交换记录入口（WP16），第四参数为
`ConversationAnalysis` 的默认 Newtonsoft 序列化结果（**PascalCase** 属性名），
由 LivingNPCs 现存的 `ValleyTalkExchangeParser` 消费——该解析器原样保留，
序列化形态不得改变。

### 3.4 资产名与存档键（与 WP14/WP15 的共享契约）

- 旧资产名：`ValleyTalk/Prompts`、`ValleyTalk/GameSummary`、
  `ValleyTalk/GameSummaryOptimized`、`ValleyTalk/Bios/<角色名>`。
  新引擎按 01 §4 改为 `Mods/Yuki.LivingNPCs/` 前缀（具体映射 WP15 定），
  本包一律通过 `IDialogueContent` 取用，不自己 Load 资产。
- 历史存档键（WP14 持有，此处为消费契约）：主机端 SaveData 键
  `EventHistory_{净化后角色名}`；净化规则：仅保留
  `a-z A-Z 0-9 _ - .`，全部无效则用字符字节的十六进制串，超过 50 字符截断。
  分机端存文件 `multiplayer/{存档文件夹名}.json`（角色名 → 历史 的字典）。
- 时间桶文案、分节标题等全部是 WP20 键（4.6 节内联标注）。

### 3.5 消费的配置字段（WP15 持有）

`EnableMod`、`Provider`、`ModelName`、`QueryTimeout`（秒，默认 85）、
`ApplyTranslation`、`TypedResponses`（`"Never"`/`"Always"`/`"With Generated"`，
默认后者）、`UseOptimizedPrompts`（默认 false）、
`GenerateAiForNormalRightClick`（默认 false）、`DisabledCharacters`
（逗号分隔角色名列表）、`EnableSveCompatibility`、`Debug`。
另有运行时标志：`BlockModdedContent`（存在未声明 `permitAiUse:true` 的
内容包时为真，见 WP15）。

## 4. 行为规范（按管线阶段）

### 4.1 触发类型与入口

四类生成请求（对应 `GenerationRequest.Trigger`）：

| 触发 | 旧入口语义 | 输入要点 |
|---|---|---|
| Scheduled（日程/事件台词） | 以对话键 + 原版台词发起 | `dialogueKey`、`originalLine`；键首段若能解析为 RandomAction/SpouseAction 枚举则注入相应上下文 |
| Conversation（玩家继续对话/输入文本） | 以会话元素列表发起 | 既有会话行（含玩家新行） |
| Gift（送礼应答） | 以礼物对象 + 口味发起 | 物品、GiftTaste（0 喜爱/2 喜欢/4 不喜欢/6 讨厌/其他 中性） |
| Marriage（婚姻小动作台词） | 归入 Scheduled，靠键解析 | SpouseAction：`patio`、`funLeave`、`jobLeave`、`funReturn`、`jobReturn`、`spouseRoom`；RandomAction：`Rainy`、`Indoor`、`Outdoor`、`OneKid`、`TwoKids`、`Good`、`Neutral`、`Bad` |

Scheduled 触发且 `originalLine` 为空时允许"配偶主动送礼"抽取（`CanGiveGift`）。

### 4.2 请求接纳、去重、异步时序（非流式路径）

- **单飞行请求**：同一时刻最多一个未完成的生成。已有请求在途时，新请求记
  警告日志并**直接丢弃**（不排队）。
- **延迟启动**：请求先置为"待生成"状态，在游戏 UpdateTicked 且
  `Game1.activeClickableMenu == null` 时才真正启动——保证任何打开的菜单
  （含前一个对话框）关闭后再开始，同时打开"思考中"窗口（WP12 的
  ThinkingDialogueController）。
- **代际号与取消**：每次启动分配自增代际号。玩家按 Esc 取消时：置取消标志、
  代际号 +1、关闭思考窗；**底层 LLM 请求不中断**，靠自身超时结束，迟到结果
  凭代际号校验后丢弃。
- **主线程交接**：LLM 完成后的续体运行在线程池（桌面无同步上下文），
  **必须**注册一次性的 UpdateTicked 处理器，把"关思考窗 + 呈现对话"推迟到
  下一游戏 tick 在主线程执行；异常路径同理（下一 tick 关窗并呈现回退对话）。
- **呈现**：设置 `Game1.currentSpeaker` 后 DrawDialogue；对话为空则不呈现。
- **异常回退**：生成抛异常且仍为当前代际 → 以键 `SLD_Error`、文本 `...`
  呈现回退对话。
- **状态复位**：仅当"仍是当前代际"时清空共享请求状态，防止被取消/被取代的
  旧任务清掉新请求的数据。

### 4.3 上下文快照（GenerationRequest 装配时采集）

在请求发起时对游戏状态做一次快照（新类型建议见 §5.1），内容与规则：

- 季节（枚举）、季节内日期、年份；星期 = `dayOfMonth % 7`（0=Sun…6=Sat）。
- 时间桶（WP20 键 + 24 小时钟表值后缀，格式 `文案 (H:mm)`）：
  ≤800 `generalEarlyMorning`；≤1130 `generalLateMorning`；≤1400
  `generalMidday`；≤1700 `generalAfternoon`；≤2200 `generalEvening`；
  其余 `generalLateNight`。
- 天气标志列表：`rain`、`snow`、`lightning`、`green rain`（按当前位置判定）。
- 好感心数：`好感点 / 250`；**0 点视为 -1**（从未正式认识）。
- 位置：NPC 当前地图内部名；当前日程条目（≤当前时间的最近一条）与下一条
  （目标地点 + 距今分钟数，向下取 0）。
- 当前活动描述：在途 → "正走向下一日程点"；否则取日程条目的
  endOfRouteBehavior/endOfRouteMessage 归一化为活动短语（读书/钓鱼/喝酒/
  工作/静坐/锻炼/音乐/跳舞/休息等关键词映射，不命中则给出带原始提示的
  通用描述）；无日程 → "站在附近无特定活动"。
- 农夫侧：性别、金钱、配偶名、是否已婚、子女列表（名字/性别/年龄）、
  `Inlaw`（配偶名，用于"与配偶亲属对话需收敛"判定）。
- 行为系统上下文（WP16 直调注入）：对话触发取行为对话上下文，送礼触发取
  礼物应答上下文，存入 `BehaviorContext` 字符串。
- 会话触发额外携带：全量会话历史（与上次会话上下文按元素 GUID 去重合并，
  见 4.14）、`LastLineIsPlayerInput`。

### 4.4 每 NPC 启用判定与 RSV 排除（挂载点）

`IDialogueEngine.IsEnabledFor(npc)` 及概率判定入口的规则链：

1. 硬排除：npc 为空，或 **RsvAiPolicy（搬运件）判定为被屏蔽 NPC**
   （Ridgeside 名单/位置前缀）→ 永不生成。
2. 总开关：引擎被禁用标志（如旧版共存检测，01 §5）或 `EnableMod=false`。
3. `DisabledCharacters` 名单命中 → 不生成。
4. `BlockModdedContent=true` 时要求该 NPC 有非空传记，否则不生成。
5. 概率门（供 WP12 补丁调用）：`probability` 取 0–4，4 恒真，0 恒假，
   其余按 `probability/4` 概率放行；`retainResult=true` 时按"NPC×游戏日"
   记忆当日结果（当日重复询问返回同一结论），日期变更时清空记忆表。
6. 被动右键路径（普通右键闲聊）额外要求 `GenerateAiForNormalRightClick=true`。

### 4.5 决策通道挂载点（搬运件）

- **上下文路由**：在构造提示词**之前**调用
  `ContextRoutingDecisionPass.BuildPlanAsync(角色, 上下文)`，得到
  `ContextRoutingPlan`（各上下文模块的 None/Brief/Full 档位 + 路由诊断：
  outcome 字符串、耗时、超时秒数）。计划传入提示词装配器；装配器构造时
  **必须**调用计划的 `ApplyDependencies()`（这是所有到达提示词的计划——
  路由产出、缓存命中或 Full 默认——统一收敛依赖关系的唯一位置）。
- **行动决策补充**：主响应解析成功且有台词后调用
  `LivingNpcActionDecisionPass.TrySupplementAsync(角色, 上下文, 当前分析,
  台词行数组)`，返回补充后的 `ConversationAnalysis` 与诊断对象；用返回的
  分析替换当前分析。失败/跳过时该搬运件自会返回原分析。
- 两个通道内部都自带 LLM 调用与超时，引擎不为其做重试。

### 4.6 提示词装配（分节结构）

提示词由**六段**组成，按序为：`System`、`GameConstantContext`、
`NpcConstantContext`、`CorePrompt`、`Instructions`、`Command`，另有
`ResponseStart`（预填的应答开头，WP20 键 `responseStart`）。前三段是
"稳定段"，供 WP11 做提供商侧 Prompt Caching 的缓存断点；后三段拼为
"本轮段"。每段字符数需可观测（调试日志 + 每小节长度表）。

所有文案 = WP20 键；小节允许被第三方 Prompt Override（见 4.6.13）替换。
各节仅在其路由模块被计划包含时输出（模块名括号标注）：

1. **System**：`systemPrompt`；`ApplyTranslation` 时附 `systemPromptTranslation`
   （带目标语言参数）。
2. **GameConstantContext**（World）：`gameContext` 引言 + `gameSummaryHeading`
   标题 + 世界观摘要文本（Full=完整版，Brief=精简版；见 4.7.2）。
3. **NpcConstantContext**（NpcProfile）：`npcContextIntro`；传记正文长度
   >10 且档位非 None 时输出 `npcContextBiographyHeading` 标题；Full 档输出
   传记正文（连续空行折叠为单换行）+ 人际关系列表（`biographyRelationships`
   标题，逐条 `**标题**: 描述`）+ 全部性格特质（`biographyPersonality`）+
   传记结语；Brief 档只输出**前 4 条**性格特质。
4. **CorePrompt** 各小节，相对顺序固定如下（小节名即长度统计键）：
   1. `GameState`（GameState）：社区中心/巴士/采石场桥/矿车/巨石修复与否、
      Kent 是否已回归（第 1 年未回归）——各有 yes/no 两个 WP20 键。
   2. `SampleDialogue`（SampleDialogue）：样本非空时输出标题 + 逐条原版
      台词样本（选取算法见 4.6.14）。
   3. `EventHistory`（EventHistory）：历史采样非空时输出标题、引言、
      子标题与逐条历史行（预算见 4.15）。
   4. `CoreHeader`（恒出，不可 Override）：`coreInstructionHeading`、
      `coreContextHeading`、`coreFarmerGender`。
   5. `DateAndTime`（DateTime）：日期（`dateTimeDayOfSeason`）、时间桶
      （`dateTimeTimeOfDay`，清晨时附加 `dateTimeEarlyMorningNormal`）、
      第 1 年附 `dateTimeNewThisYear`；农夫入住时长（总天数 =
      (年-1)×112 + 季节序×28 + 日-1；为 0 → `dateTimeResidencyToday`，
      否则 `dateTimeResidencyProgress` 带天数与整季数）。
   6. `Weather`（Weather）：按优先级 lightning > green rain > snow > rain
      输出一条。
   7. `OtherNpcs`（NearbyNpcs）：附近 NPC 非空时标题 + 引言 + 逐名列表 + 结语。
   8. **已婚/室友分支**（好感数据存在且 IsMarried）：室友 →
      `coreRoommates`（Relationship）；配偶 → `MarriageStatus`（婚姻声明 +
      按 DaysMarried 推算的"结婚于…"相对日期，不可 Override）与
      `Children`（子女数分支文案 + 逐名描述；怀孕倒计时按 NPC 性别选
      `childrenPregnant.npcMale` / `childrenPregnant.npcFemale`）；
      随后 `Spouse`、`MarriageFeelings`（Relationship；心数 >12 好 / <10 差 /
      其余中性）；`FarmContents` 与 `Wealth`（Farm；农场建筑/动物/作物/宠物
      清单，财富四档 <1000/<10000/<100000/以上）。
   9. `Location`（Location）：Full 档 → 在家判定（当前位置 == 家地图且非
      拜访配偶亲属；可能在店铺时附加店铺提示）或按位置内部名选专属文案
      （Town/Beach/Desert/BusStop/Railroad/Saloon(成人附微醺)/SeedShop/
      JojaMart/各 Resort 变体/FarmHouse/Farm，否则用本地化显示名的通用文案
      + `locationOutro`）；在途 → `locationTravelling`（目标显示名），否则
      `locationCurrentlyStationary`；随后"当前状态"块（标题、地图显示名 +
      瓦片坐标、当前活动、当前日程停留点、接地提示 `locationCurrentStateGrounding`）；
      未来日程仅当（在途 / 下一条 ≤30 分钟 / 玩家最后一句命中
      `ConversationCues.FutureSchedule` 线索）才输出：剩余不重复地点列表
      `locationFuturePlans` + 下一站（≤30 分钟用 `locationNextScheduleSoon`
      否则 `locationScheduleWindow`）；无后续日程 → `locationNoUpcomingSchedule`。
      Brief 档 → 仅"当前状态"块 + 在途/驻留 + 下一站窗口。
   10. `Trinkets`（Trinkets）：农夫饰品含仙女盒 / 青蛙伙伴 / 飞行伙伴时各一条。
   11. `RecentEvents`（RecentEvents）：`previousActiveDialogueEvents` 中
       天数 <7 的条目映射为文案（键覆盖 `cc_*`、`movieTheater`、
       `pamHouseUpgrade(Anonymous)`、`jojaMartStruckByLightning`、
       `babyBoy/Girl`、`wedding`、`luauBest/Shorts/Poisoned`、
       `Characters_MovieInvite_Invited`、`DumpsterDiveComment`、
       `GreenRainFinished`），非空时加标题与引言。
   12. `ThirdPartyContext`（LivingNpc 档位控制，不可用默认文案）：仅当存在
       第三方 Override 时输出；LivingNpc 档 None 则整体跳过；与
       `BehaviorContext` 重复的 LivingNPCs 系 Override（判定：包含
       `## LivingNPCs` / `## Active Companion Outing` / `LivingNPCs Context` /
       `LivingNPCs Help Request` / `LivingNPCs Gift` 之一）跳过；Brief 档
       经 `LivingNpcContextCompressor.BuildBriefContext` 压缩后输出。
   13. `SpecialDatesAndBirthday`（SpecialDates）：按（季节, 日）命中节日
       文案（spring 1/12/23，summer 1/10/27/28，fall 1/15/26，
       winter 1/7/24/28）；当日为该 NPC 生日 → `specialDatesBirthday`。
   14. `Gift`（Gift）：收礼时 → 求助礼物特判（`BehaviorContext` 含
       `## LivingNPCs Help Request Gift Response` 时用求助礼物引言/反应文案）
       或普通礼物（`giftIntro` + 按口味 0/2/4/6/默认 选
       giftLoved/Liked/Dislike/Hate/Neutral + `giftMustIncludeReaction`）；
       生日附 `giftBirthday`；结尾 `giftOutro`。主动送礼时 → `giftGiving`
       （礼物显示名，物品数据可查则本地化）。
   15. `LivingNpcExtraPrompt`（LivingNpc）：`BehaviorContext` 非空时输出；
       Full 档原文，Brief 档经压缩器压缩。
   16. `SpouseAction`（SpouseAction）：按枚举选六个文案键之一。
   17. **未婚分支**（好感数据不存在或非 IsMarried；Relationship）：
       `NonSpouseFriendshipLevel` —— 可浪漫对象或心数 ≤6 或未知时按心数分档
       （-1 首次交谈 / <2 陌生 / <4 相识 / <6 朋友 / <8 密友 / ≤10 想约会）；
       否则非单身成人 ≤8、儿童 8+、非单身成人 10 各一档。随后 `Spouse`、
       `SpecialRelationshipStatus`（约会中[公开/低调变体+性向词]、已订婚
       [婚期倒数]、已离婚、求婚被拒，各自独立判定可叠加）。
   18. `coreGenderReferences`（恒出）。
   19. `Preoccupation`（Preoccupation）：仅当**无会话历史**且 50% 概率通过；
       从候选池（传记 Preoccupations + 最爱/最恨礼物显示名）抽一个，
       **同一游戏日内粘滞**（当日重复生成用同一个）。
   20. `CurrentConversation`（CurrentConversation）：有会话历史 → 标题 +
       引言 + 逐行转录（NPC 名 / `generalFarmerLabel` 标签；**剔除疑似错误
       语言的行**）；无历史但刚刚说过话（见 4.14）→ 标题 +
       `currentConversationJustSpoke`。
5. **Instructions**：标题、引言、接地要求、（有样本时）样本模仿指示、
   农夫名指代、分段规则、单行规则、回应选项规则；随后 6 条 LivingNPCs 元数据
   指示键：`instructionsLivingNpcMetadata`、`…GiftIds`、`…ImmediateTravel`、
   `…TravelConsent`、`…HelpRequests`、`…EmotionDepth`（优化开关影响见 4.8）；
   传记 `ExtraPortraits` 不含键 `"!"` 时输出情绪肖像指示（逐个额外肖像行
   `instructionsExtraPortraitLine` 内嵌进 `instructionsEmotion`）；最后追加
   WP11 客户端声明的 `ExtraInstructions`（如有）。
6. **Command**：标题 + 引言；`ReplaceSchedule` 小节（可 Override）——仅当
   `originalLine` 非空、无会话历史、且 NPC 并非刚刚说过话时输出"替换这句
   日程台词"的指示（带原句）；`ApplyTranslation` 时附 `instructionsTranslate`。
   **重试时**在 Command 末尾追加语言重试强化指示
   （`ConversationTextPostProcessor.GetLanguageRetryInstruction()`，搬运件）。

### 4.7 缓存语义

**4.7.1 提示词片段缓存（旧 PromptCache 的行为）**
- 缓存内容：提示词骨架资产（键 → 文案字典）加载 + 预处理
  （`Game1.content.PreprocessString`，展开性别开关等游戏记号）后的结果字典。
- 键：单例整字典缓存；**失效条件**（三者任一）：资产被 SMAPI 失效、
  游戏语言变化、**农夫性别变化**（预处理结果依赖性别）。
- 值过滤：原文或预处理结果以 `(no translation` 开头的条目跳过不入缓存。
- 资产加载失败：记错误日志并**禁用整个引擎**（EnableMod=false 等效行为）。
- 新实现归属：字典加载在 WP15（`IDialogueContent.GetPromptSkeleton`），
  但"性别/语言敏感的失效"语义必须保留（谁持有缓存由实现定，语义不变）。

**4.7.2 世界观摘要缓存**
- 摘要文本由分节 JSON 资产拼装（结构见 §5.3），拼装结果按
  （完整/精简 × SVE/基础）四个静态槽位缓存；GameSummary 资产失效时四槽全清。
- `EnableSveCompatibility=false` 用基础版数据源，否则用含 SVE 的版本。

**4.7.3 角色级缓存**
- 台词样本缓存：键 =（季节, 季节日, 心数）三元组，变则重算（见 4.6.14）。
- 历史截断点缓存：按游戏日期缓存（见 4.15）。
- 传记/原版对话缓存：随资产失效与 SVE 兼容开关变化而重载（WP15 交互）。

### 4.8 优化提示词开关（UseOptimizedPrompts）

- **世界观摘要**：开 → 使用"精简版摘要"资产（独立 JSON，内容更短）；
  另外无论开关如何，路由把 World 降为 Brief 档时也用精简版。
- **指令小节**：4.6 第 5 段的每条 LivingNPCs 指示键先探测 `<键名>Optimized`
  变体（探测允许缺失）；存在且非空用之，否则**回退完整版**（绝不整节丢弃，
  否则模型不知道该元数据字段存在）。
- 其余小节不受影响。

### 4.9 LLM 调用、超时与重试

- 调用 WP11：非流式 `CompleteAsync`，流式 `StreamAsync`。请求携带四段
  （System / 游戏稳定段 / NPC 稳定段 / 本轮段）+ `ResponseStart`，
  `allowRetry=false`（**重试由 WP10 统一编排，WP11 不得自行重试**）。
- 每次尝试独立超时 = `QueryTimeout` 秒（CancellationTokenSource），超时按
  失败处理。
- 重试策略：**最多 2 次尝试**（初试 + 1 次重试），重试前固定延迟 2 秒
  （保持思考窗等待时间可控）。触发重试的情形：异常/超时、响应不可解析
  （无有效台词行）、首行疑似错误语言（判定为搬运件
  `LooksLikeWrongLanguage`；此时清空结果并在 Command 加语言强化指示重试）。
- 用量记录：响应携带 usage 则用之，否则按全部输入 + 输出文本估算；
  按（NPC 名, 用量, Provider, ModelName, success/failed）上报用量追踪器
  （WP14 搬运件 TokenUsageTracker）。
- 全部尝试失败或不可解析：结果回退为单行 `...`，并按有无异常分别记
  error/warn 日志。
- 每次生成输出诊断：耗时分解（总/提示词初始化/提示词拼装/推理/路由）、
  各段字符数、各小节字符数、响应字符数、尝试次数、行数（Debug 配置开启时）；
  并向 AiResponseLogExporter / PromptLogExporter（搬运件）各追加一条。

### 4.10 解析与校验（响应 → 台词行数组）

顺序执行：

1. **原始归一化**：去除不可见字符（搬运件）；把文中出现的
   `!LIVINGNPCS_META`（含多叹号变体）规范为独立行；把行中缝的 `%`/`%%`
   选项标记拆为独立行。
2. 按换行拆分、去空行；**剔除疑似元数据行**（去除行首 `{`、`,`、`"` 后以
   3.1 所列元数据字段名开头的行，大小写不敏感）。
3. **首行定位**：找第一个以 `-` 开头的行，其前所有行丢弃。找不到时进入
   **宽容恢复**：取第一个 `%` 行之前、非"客套开场白"（Here is/Sure/以下/当然
   开头）的最后一行作为台词行，人为补 `- ` 前缀（Debug 时记警告）；
   仍无候选或候选以 `%` 开头 → 解析失败（空结果）。
4. **台词行清洗**（依序）：剥离隐藏尾部与选项尾巴（搬运件
   `StripHiddenAndResponseTail`）；去首部 `- " “ %` 等符号、去尾部引号、
   剥首尾 `#$b#`/`#$e#`、删除所有引号；规范 Stardew 命令（搬运件）；删除
   `#$c .5#`；`@@`→`@`；`#$<肖像>` 修为 `$<肖像>`；扫描全部 `$` 记号——
   `$e/$c/$b` 保留，`$<合法肖像键>` 保留，其余 `$片段` 整体删除。
5. **超长拆分**：按 `#` 分段后任一段 >200 字符时，在句号/叹号/问号处回溯
   切分为多段（保持段尾肖像记号粘附）；切分后仍有 >200 的段 → 整行作废
   （宽松校验模式跳过此作废，见 §8 开放问题 3）。
6. **标点修补**（仅特定语言，`FixPunctuation` 为真时）：每段在肖像记号前
   若不以 `.` `!` `?` 结尾则补句号。
7. **选项行**：首行之后的行经归一化——`%` 开头直接收；`!`/`{`/`}` 开头或
   元数据行丢弃；其余剥离"玩家回应/回复/选项/response/reply/option + 序号 +
   分隔符"前缀与列表符号后，若 ≤160 字符且不含 `#$`、不含
   `!LIVINGNPCS_META`、不以 `-`/`###` 开头，则视为选项收下。选项行再清洗：
   删 `#`，删所有 `$` 命令，`@`→农夫名，必要时补标点；**>90 字符的选项丢弃**。
8. **元数据裁决**：`ConversationAnalysis.Parse`（搬运件）在原始文本上独立
   解析；若其 `EndConversation=true` 且行数 >1，则**只保留台词行**（丢弃全部
   选项）。

### 4.11 后处理与结束判定

- `NormalizeImmediateNicknameReply(台词, 玩家最后一句)`（搬运件）应用于
  FormattedLine 与首行。
- **结束会话判定**（三者取或）：分析的 `EndConversation`；
  `PlayerLikelyEndedConversation(玩家末句)`；
  `NpcLikelyEndedConversation(NPC 首行)`（后两者为搬运件启发式）。
  判定为结束 → 不拼装应答菜单（只出首行台词）。
- 单行且 `TypedResponses != "Always"` 且不允许"输入回应兜底"时也不拼菜单。
- 空结果统一回退 `...`。

### 4.12 各触发的收尾动作

- **Conversation**：把 NPC 新台词追加进会话历史并持久化（4.14）；调用行为
  系统 RecordExchange（玩家末句, NPC 可见首行, 分析 JSON）；对话键取请求键，
  缺省 `SLD_Conversation`。
- **Gift**：RecordExchange 的玩家侧文本固定为
  `The farmer offered {礼物显示名}.`；同时把一对会话（玩家行 = WP20 键
  `transcriptGiftPlayerLine`（参数 npcName/giftName，缺失时回退英文句式
  `The farmer gave {NPC显示名} {礼物名}.`）、NPC 行 = 成品台词）写入会话历史；
  对话键 `Accept_{礼物内部名}`。
- **Scheduled/Marriage**：不回传行为系统、不写会话历史（进入 DialogueHistory
  的路径由 WP12 的补丁在台词实际展示后调用 AddDialogueLine，见 4.14）。

### 4.13 流式路径

> 旧代码中流式管线（预览窗 + 流式推理）**已实现但调度器未接线**（无调用点，
> 处于休眠状态）。新引擎按 01 §2 的 `StreamAsync` 正式接入，语义如下：

- 入口与非流式相同的接纳/去重/启用判定；同样在主线程 tick 启动。
- 推理层改用 WP11 流式接口，每收到一个文本增量回调 `IStreamSink`；投喂 UI 前
  由 WP12 的流式窗用 `StreamingDialoguePreview.ExtractVisibleText`（搬运件）
  过滤——**元数据尾部与选项行绝不能上屏**。流式模式下重试仍允许（重试从头
  重新流式）。
- 完成时：走完整解析/后处理（4.10–4.11），以最终首行 + 选项集合调用
  sink 的完成回调。选项集合构造规则（流式专用，不用 `$q/$r` 字符串）：
  行数 ≤1 且 `TypedResponses != "Always"` → 空集；否则依次为
  Silent 选项（`outputStaySilent` 文案，缺省 `Stay silent`）、每个模型选项
  （Generated 类）、`TypedResponses != "Never"` 时 Typed 选项
  （`uiTypeYourResponse` 文案，缺省 `Type your response`）。选项三分类
  枚举为搬运件 `StreamingResponseOptionKind`（Silent/Generated/Typed）。
- 选项被选中后的行为（回调由 WP12 触发，引擎提供语义）：Silent → 向会话
  历史追加一条空玩家行；Typed → 拉起文本输入（携带当前会话历史）；
  Generated → 以所选文本为新玩家行发起下一轮 Conversation 生成。
- 结束判定为真时选项集合为空集。
- 失败：以 `...`、空选项集合完成 sink，关闭窗口的回调仍要触发。
- 历史与 RecordExchange 收尾与非流式 Conversation 一致。

### 4.14 历史记录模型（引擎侧语义；存储在 WP14）

四类历史记录（各自独立列表持久化，均带游戏时间戳）：

| 类型 | 内容 | 写入时机 |
|---|---|---|
| DialogueHistory | 一次展示的原版/生成台词行组 | 台词框实际展示后（WP12 调 AddDialogueLine） |
| ConversationHistory | 完整会话（元素列表，含玩家行标志；以首元素 GUID 为会话 ID） | 每轮会话后整体 upsert（同 ID 覆盖） |
| DialogueEventHistory | 事件/节日中的台词 + 在场 NPC + 事件名 | 事件台词展示后；同时给每个在场者写第三方目击记录 |
| DialogueEventOverheard | 旁听的（说话人, 台词） | 旁听发生时；先删除同说话人重叠文本的旧条目 |

- **去重**：写 DialogueHistory / DialogueEventHistory 前，若与该 NPC 最后
  一条 DialogueHistory 的文本序列完全一致则跳过；并**剔除合成的应答提示行**
  （以 `Respond:` 或本地化 `outputRespond` 文案开头的行）。
- 写 ConversationHistory 时删除文本与之重叠的 DialogueHistory 条目
  （避免同一句话双记）。
- "刚刚说过话"判定：最后一条历史属于三类台词记录之一且时间戳为"刚才"
  （同刻近邻），供 4.6.4.20 与 Command 的 ReplaceSchedule 条件用。
- 第三方目击记录不持久化（含活对象引用），仅当日在内存中参与采样。
- 会话上下文续接：连续对话间用"最近上下文"缓存会话历史，新旧按元素 GUID
  去重合并；存档切换、清除历史、事件结束（WP12 调 ClearContext）时清空。
- 清除接口：按名清除单 NPC（内存 + 持久层 + 转录档案）与全量清除，
  返回是否确有历史被清除（供控制台命令回显）。

### 4.15 token/长度预算与历史修剪

- **提示词内历史采样**：全部四类记录 + 由 `previousActiveDialogueEvents`
  合成的活动条目（键限 `cc_Bus`、`cc_Boulder`、`cc_Bridge`、`cc_Complete`、
  `cc_Greenhouse`、`cc_Minecart`、`wonIceFishing`、`wonGrange`、`wonEggHunt`，
  且天数 <112 或为 112 的整倍数；时间戳 = 今天 − 天数），合并按时间排序后
  **只取最近 20 条**（截断点按游戏日缓存），再按时间正序输出；输出时从最新
  往回累计字符，**总预算 4000 字符**，超出即停（保证最新的先入选）；当前
  进行中的会话（ID 相同）不重复入历史。
- **持久层修剪**（WP14 执行，此处为语义契约）：各列表容量上限——事件 40、
  旁听 40、台词 60、会话 30；除事件列表外应用 112 天年龄上限；**当日条目
  受保护**（进行中的会话每轮以当日时间戳重写，不得被修剪归档）；被修剪的
  会话按时间正序交给转录导出器归档，其余类型直接丢弃；修剪幂等。
- 载入时删除时间戳晚于当前游戏时间的条目（回档保护）。
- 响应侧长度限制见 4.10（200/90/160）。

### 4.16 边界情况汇总

| 情形 | 行为 |
|---|---|
| 网络/API 失败、超时 | 计一次失败尝试；重试用尽 → `...` 回退 + error 日志；思考窗在主线程 tick 关闭 |
| 空响应/不可解析 | 同上但 warn 日志；AiResponseLog 标 `unparseable` |
| 错误语言 | 清空本次结果，追加语言强化指示重试一次 |
| 超长响应 | 段级句界拆分；仍超长整行作废（触发重试/回退） |
| 重复请求 | 在途时新请求丢弃（warn）；同日概率门记忆；同台词组不重复入历史 |
| 玩家取消（Esc） | 代际号失配，迟到结果与异常路径全部静默丢弃；底层请求自然超时 |
| 生成成功但配偶送礼被抽中 | 首行末尾追加 `[物品ID]`；回退行 `...` 同样会追加（保持旧行为） |
| RSV NPC / 禁用名单 | 生成入口直接返回空，不打开思考窗 |
| 提示词资产加载失败 | 禁用引擎并记错误 |

## 5. 新类型与接口建议（对齐 01 §2）

### 5.1 GenerationRequest

| 字段 | 类型 | 语义 |
|---|---|---|
| NpcName | string | 目标 NPC 内部名 |
| Trigger | enum GenerationTrigger { Scheduled, Conversation, Gift } | 触发类型（婚姻台词归 Scheduled，靠 DialogueKey 解析） |
| DialogueKey | string | 原版对话键（Scheduled）或续接键（Conversation，可空） |
| OriginalLine | string | 被替换的原版台词（Scheduled，可空） |
| Conversation | IReadOnlyList\<ConversationTurn\> | 会话元素（文本 + 是否玩家行 + GUID） |
| GiftItemId / GiftTaste | string / int | 礼物与口味（Gift） |
| BehaviorContext | string | 行为系统注入的上下文（WP16） |
| Snapshot | GameStateSnapshot | 4.3 的快照（引擎入口在主线程采集） |

### 5.2 GenerationResult

| 字段 | 类型 | 语义 |
|---|---|---|
| FormattedLine | string | 含 `skip#`/`$q$r` 菜单的成品 Stardew 对话串 |
| ParsedLines | string[] | 首行台词 + 各选项（净文本） |
| AnalysisJson | string | 3.3 契约的序列化分析 |
| EndConversation | bool | 结束判定结果 |
| DialogueKey | string | 呈现用键（4.12） |
| Usage | TokenUsage（搬运件） | 用量 |

`IStreamSink`：`OnToken(string delta)`（原始增量，过滤由消费方用搬运件做）、
`OnCompleted(GenerationResult, IReadOnlyList<StreamingResponseOption>)`、
`OnFailed()`。`LlmRequest/LlmReply` 字段以 WP11 文档为准，但必须容纳
四段稳定/本轮拆分 + ResponseStart + AllowRetry + DisableThinking。

### 5.3 世界观摘要资产结构（供 WP15/WP20；引擎按此拼装）

JSON 字段：`SectionOrder`（节名 → 是否输出 `### 节名 :` 标题的有序字典）；
节：`Intro`、`FarmerBackground`、`Villagers`、`Seasons`、`Locations`、
`Festivals`、`Outro`。每节含 `Text`（引言）与 `Entries`（id → 条目）；
条目字段 `id`、`Name`、`Description`；Locations 条目另有 `Region`
（按区分组输出）；Seasons 条目另有 `Crops`、`Forage`（列表，拼为
`seasonCrops`/`seasonForage` 文案 + 逗号连接）。条目输出格式
`- **名** - 描述`。缺节记错误并跳过。末尾若 `gameSummaryTranslations`
键有文案则追加。

### 5.4 对话键解析（样本选择与触发解析共用的键文法）

原版对话键按 `_` 分段解析为上下文（全部为游戏数据格式，精确保留）：
前缀 `M`（婚后）、`B`（生日）；季节名；GUID 开头 → ChatID（取前两段）；
位置前缀（`Beach`、`Desert`、`Railroad`、`Saloon`、`SeedShop`、`JojaMart`，
后缀数字为心数）；特殊上下文键集合（`cc_Boulder`、`cc_Bridge`、`cc_Bus`、
`cc_Greenhouse`、`cc_Minecart`、`cc_Complete`、`movieTheater`、
`pamHouseUpgrade`、`pamHouseUpgradeAnonymous`、`jojaMartStruckByLightning`、
`babyBoy`、`babyGirl`、`wedding`、`event_postweddingreception`、`luauBest`、
`luauShorts`、`luauPoisoned`、`Characters_MovieInvite_Invited`、
`DumpsterDiveComment`、`SpouseStardrop`、`FlowerDance_Accept_Spouse`、
`FlowerDance_Accept`、`FlowerDance_Decline`、`GreenRain`、
`GreenRainFinished`、`GreenRain_2`、`Rainy`，含 `_Day/_Night` 的除外）；
星期 3 字母前缀 + 可选心数；纯数字 → 季节日；`Accept` + 物品段；
RandomAction / SpouseAction 枚举名（Rainy/Indoor 后可跟时间段）；年份数字；
`inlaw_<名>`。Resort 系标签：`Resort`、`Resort_Entering`、`Resort_Leaving`。

**样本选择**：把 NPC 的原版 + 传记补充台词按键解析入库；婚后台词键加 `M_`
前缀；每次生成按与当前上下文的**差异分**升序取前 20 条 台词做风格样本。
差异分（越小越相似）：心数差 ×100；季节不同 +50；星期不同 +1；季节日不同
+200；仅一方是收礼 +2000；时间段不同 +20；RandomAct 不同 +200/单方 +2000；
SpouseAct 同前；配偶名不同 +10000/单方 +2000；年份不同 +200/单方 +200；
inlaw 不同 +500/单方 +1000；另加 0–9 随机抖动。`BlockModdedContent` 或
SVE 兼容关闭时改从原版内容管线加载台词（跳过内容包补丁），传记声明
`UsePatchedDialogue=true` 的角色除外。

## 6. 与其他工作包的接口

- **WP11**：`ILlmClient.CompleteAsync/StreamAsync`；请求四段拆分即缓存断点
  契约；`allowRetry` 恒 false；`ExtraInstructions`/`IsHighlySensoredModel`
  由客户端声明，前者进 Instructions 段。用量估算兜底在 WP10。
- **WP12**：补丁在合适的 Harmony 挂点调用引擎的请求入口与概率门（4.4.5）；
  台词展示后回调 AddDialogueLine/AddEventLine/AddOverheardLine；Esc 调
  CancelActiveGeneration；事件结束调 ClearContext；思考窗/流式窗/文本输入
  归 WP12。`skip#`、`$$$%%%`、`$$%%`、`SLD_*` 键为双方共享常量。
- **WP14**：`IDialogueHistory` 承载 4.14 的四类记录与 4.15 的修剪契约；
  存档键/多人文件格式见 3.4；TokenUsageTracker、转录导出器为搬运件。
- **WP15**：`IDialogueContent` 提供传记（含 ExtraPortraits、Preoccupations、
  Relationships、Traits、BiographyEnd、UsePatchedDialogue、Missing、回退
  传记合成）、世界观摘要（四变体）、提示词骨架键值（4.7.1 失效语义）；
  配置字段见 3.5；SVE 名单/别名与 RSV 排除的资产侧由 WP15 与搬运件分担。
- **WP16**：BehaviorContext 注入（对话/礼物两种）、RecordExchange 直调
  （3.3 格式）、礼物邮件与记忆印象生成器的 `Request(requestId, npcName,
  payloadJson)` / `TryGet(requestId)` 入口保持（搬运件，改为进程内 Task 由
  WP16 裁决）。
- **WP20**：本文档引用的全部文案键（4.6 内联 + `outputRespond`、
  `outputStaySilent`、`uiTypeYourResponse`、`uiYourResponse`、
  `transcriptGiftPlayerLine`、`generalFarmerLabel`、时间桶键、历史格式键
  `dialogueHistoryFormat`、`historyConversationFormat`、
  `historyDialogueFormat`、`historyOverheardFormat`、
  `historyThirdPartyFormat`、`historyThirdPartyFestival`）需在 WP20 清单
  中逐一落实；键名可在 WP20 统一重命名，但必须与引擎同步（建议 WP20 输出
  键名映射表）。

## 7. 验收要点

1. 四类触发各自产出合法 Stardew 对话串；`$q/$r` 菜单的 id/键与 3.2 完全一致
   （旧存档中的进行中对话可被新引擎的键接住）。
2. 元数据尾部永不出现在玩家可见文本（含流式过程）；分析 JSON 能被现存
   `ValleyTalkExchangeParser` 解析（PascalCase）。
3. 断网/密钥错误：思考窗正常关闭、出 `...` 回退、无主线程卡顿或崩溃；
   Esc 后迟到结果不弹窗。
4. 错误语言响应触发且仅触发一次带强化指示的重试。
5. 超 200 字符段被句界拆分后正常分页显示。
6. 历史：同一句台词不重复入库；会话按 GUID 覆盖更新；修剪后存档体积有界，
   被修剪会话进转录档案；回档后未来条目消失。
7. 路由计划为 Brief 时提示词显著变短（分节长度日志可证），且 LivingNPCs
   元数据指示仍在。
8. RSV 名单 NPC 与 DisabledCharacters 名单 NPC 右键无思考窗、无请求发出。
9. 单飞行约束：连点多个 NPC 只产生一次请求。
10. 每小节字符统计与计时日志在 Debug 开启时输出（诊断回归基线）。

## 8. 开放问题

1. **流式接线**：旧流式路径休眠。新版是否默认启用流式窗（配置项?）由用户
   与 WP12 商定；引擎两条路径都实现。
2. **`skip#` 协议去留**：合并为单 mod 后，"跳过下一句"可否改为进程内标志
   而非字符串前缀？涉及 WP12 补丁，建议保留字符串以减少联动风险，由用户裁决。
3. **宽松校验死代码**：旧逻辑"第 3 次尝试放宽超长校验"在最多 2 次尝试下
   永不触发。新实现建议：要么删除宽松分支，要么把重试上限提为可配置——
   需用户拍板（影响等待时长）。
4. **回退行也附送礼标记**：旧行为在生成失败回退 `...` 时仍附加 `[物品ID]`
   送出礼物。保持还是修正（仅成功时送礼）？建议修正，需用户确认。
5. **概率门 -1 档**：旧代码预留"询问交互类型"的 -1 概率档未实现，新版不实现。
6. **新资产名下的第三方 Prompt Override 注册 API** 命名归 WP16/WP12 的
   interop 设计（旧 `RegisterPromptOverride` 不保留，00 §1 非目标）。

## 9. 审计索引（行为点 → 旧代码；实现方不读）

基准目录 `ValleyTalk/src/`。

- 端到端编排/触发入口/`skip#` 与 `$q$r` 拼装（含 20000 计数器）/流式选项构造：
  Generation/DialogueBuilder.cs:17,91-294；上下文快照（时间桶/星期/天气/心数/
  日程/活动归一）：同文件 296-535；概率门与当日记忆、启用判定链：595-661；
  历史写入/去重/`Respond:` 过滤/清除与 ClearContext：537-593,663-707。
- 异步时序/代际号/Esc 取消/主线程 tick 交接/回退 `...`：
  Generation/AsyncBuilder.cs:33-185；休眠流式路径与收尾：187-297；
  单飞行去重：299-356；会话收尾/RecordExchange/结束判定：376-412。
- 生成主循环/重试/超时/语言重试/用量/诊断日志：Character.cs:464-838；
  响应解析（元数据行剔除/宽容恢复/清洗/超长拆分/选项校验）：840-1203；
  历史采样与活动事件合成：55-66,1267-1291；样本缓存：434-462；
  传记回退/SVE 别名/肖像键：24-433。
- 六段结构与小节顺序/Override/长度统计：Prompts.cs:43-150,266-388；
  各小节内容规则（4.6.4 逐项）：390-1452；优化指令键探测回退：1509-1526；
  摘要四槽缓存：25-41,197-218；配偶送礼抽取：152-170（`[id]` 追加为
  Character.cs:832-836）。
- 提示词片段缓存语义：PromptCache.cs:9-69；摘要 JSON 结构与拼装：
  GameSummaryBuilder.cs:95-237。
- 存档键/名称净化/多人文件/回档删除/修剪归档：EventHistoryReader.cs:17-210；
  容量/112 天/当日保护/幂等修剪：models/history/StardewEventHistory.cs:9-249；
  四类记录与格式化键：models/history/*.cs。
- 对话键文法/差异分权重：Generation/DialogueContext.cs:55-408；
  元数据标记与 JSON 契约：Generation/ConversationAnalysis.cs:88-131；
  路由/行动决策挂载点：Character.cs:475-476,689-696 与 Prompts.cs:83-103；
  常量：enums/SldConstants.cs、VtConstants.cs；配置默认值：
  config/ModConfig.cs:21-56；BlockModdedContent：ModEntry.cs:309-324。
