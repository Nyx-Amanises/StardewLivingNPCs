# 14 · WP14 持久化与数据迁移说明书

> 实现目录：`LivingNPCs/Dialogue/Persistence/`（迁移、历史、用量账本）与 `Diagnostics/`（导出器，搬运件）。
> 开工前必读：00、01、03。本文档中的**键名、路径、JSON 字段名是兼容契约，必须逐字使用**；
> 行为描述均为功能级，不含旧代码表达。

## 1. 目的与范围

本工作包负责两件事：

1. **新引擎的全部持久化**：每 NPC 对话历史、token 用量账本、farmhand 备用文件、
   各类导出文件（转录、token 报表、AI 响应/提示词/路由日志）。
2. **从旧 mod 迁移数据**：旧对话引擎 UniqueID 为 `dandm1.ValleyTalk`，新 mod 为
   `Yuki.LivingNPCs`。老用户升级后，存档内数据与旧 mod 文件夹内的配置/数据必须自动搬迁，
   做到"无缝升级"（00 §1 目标 3）。

不在范围内：LivingNPCs 行为系统自己的存档键 `behavior-memory`（物理键
`smapi/mod-data/yuki.livingnpcs/behavior-memory`）已经属于新 mod，不需要迁移，本包不得触碰；
历史修剪的**策略**（保留多少条、多少天）属 WP10，本包只提供物理护栏与存取通道；
config.json 字段的语义与新旧字段映射表属 WP15，本包只负责"找到旧 config 并交给 WP15 的映射器"。

## 2. 权属与搬运边界

- `TokenUsageTracker.cs`（含账本/条目数据模型）是 MINE 搬运件（03 §2），阶段 A 已复制到
  `Dialogue/Persistence/`，本包可直接改写；**其存档键与 JSON 结构仍必须按 §3.1.2 记录的契约保持兼容**。
- `ConversationTranscriptExporter.cs`、`AiResponseLogExporter.cs`、`PromptLogExporter.cs`、
  `ContextRoutingLogExporter.cs` 是搬运件（03 §2/§3），归 `Diagnostics/`；本文档只钉死它们的
  **文件路径与命名约定**（§3.3），内部格式随搬运件走。
- `EventHistoryReader.cs`、`models/history/StardewEventHistory.cs` 及各 history 模型**属重写范围**
  （03 §5）；本文档给出它们持久化格式的完整功能级契约（§3.1.1），新实现照契约重做。
- `PromptCache`（提示词资产缓存）**没有任何持久化**，纯内存 + 游戏资产管线，与本包无关（归 WP15）。

## 3. 外部契约：持久化数据全量清单

### 3.0 SMAPI 存档数据的物理格式（已对 SMAPI 源码与真实存档双重验证）

- SMAPI 的 `IDataHelper.WriteSaveData(key, model)` 把值用 Newtonsoft JSON 序列化成**单个 JSON 字符串**，
  存入游戏存档 XML 的 `<CustomData>` 字典（运行时同时写 `Game1.CustomData` 与
  `SaveGame.loaded.CustomData`）。
- 物理键格式：`smapi/mod-data/<modID>/<key>`，**整体转小写**。
  依据：SMAPI 源码 `src/SMAPI/Framework/ModHelpers/DataHelper.cs` 的 GetSaveFileKey
  （`$"smapi/mod-data/{this.ModID}/{key}".ToLower()`，develop 分支，2026-07 查证；
  该格式自 SMAPI 3.x 起未变，并已在本机 5 个真实存档中逐键验证）。
- 逻辑键必须是 slug：只允许字母、数字、下划线、句点、连字符，否则 SMAPI 抛
  ArgumentException（"The data key is invalid (keys must only contain letters, numbers,
  underscores, periods, or hyphens)."）。**斜杠不允许**——新键命名（§5.1）不得含 `/`。
- `WriteSaveData(key, null)` 的语义是**删除该键**。
- 读写限制：远程 farmhand（`Context.IsOnHostComputer == false`）调用 Read/WriteSaveData 会抛
  InvalidOperationException；**分屏玩家在主机电脑上，不受此限制**。旧代码用 `Context.IsMainPlayer`
  分流，与 SMAPI 的实际限制（IsOnHostComputer）不完全重合，新实现建议统一按 `IsMainPlayer` 分流
  （主机写存档，其余走文件），并把这点写进注释。
- SMAPI JsonHelper 的序列化设置（读旧数据必须兼容）：`Formatting.Indented`（存档内实际是紧凑单行，
  由 WriteSaveData 显式用 `Formatting.None`）、`ObjectCreationHandling.Replace`、转换器
  `StringEnumConverter` + `SemanticVersionConverter`。对本包最关键的是 **StringEnumConverter：
  枚举一律序列化为字符串**（如季节 `"Spring"`、按键 `"LeftAlt"`）。

### 3.1 存档内 SaveData（仅主机持久化）

旧 mod 在存档里只有两类键，均在 modID `dandm1.ValleyTalk` 之下。

#### 3.1.1 每 NPC 对话历史：逻辑键 `EventHistory_{saveName}`

- **saveName 构造规则**（逐字精确，迁移时要反向匹配）：取 NPC 内部名（`NPC.Name`），
  删除所有不在集合 `abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.`
  中的字符；若删完为空，改用原名每个 char 强转 byte 后的大写十六进制串（无分隔符）；
  超过 50 字符则截断到 50。
- **物理键**：`smapi/mod-data/dandm1.valleytalk/eventhistory_<saveName小写>`。
  例（真实存档实测）：`smapi/mod-data/dandm1.valleytalk/eventhistory_abigail`、
  `…/eventhistory_marlonfay`。注意 SMAPI 转小写后**丢失了 NPC 名的大小写**，这是迁移的
  主要难点（§4.2.3）。
- **值 JSON 结构**（顶层四个数组，字段名逐字精确；已对真实存档核对）：
  - `"EventHistory"`：元素 `{"Item1": <StardewTime>, "Item2": {"Dialogues": [<DialogueLine>…], "Listeners": [<完整 NPC 对象>…], "EventName": "<节日/事件名>"}}`。
    ⚠️ `Listeners` 序列化的是游戏 NPC 实体，正常情况下会因引用环序列化失败——
    实测 5 个存档（含 0.1.x 早期数据）中该数组**全部为空**。迁移与新实现读取时必须
    容忍任意内容并允许整条丢弃（§4.4）。
  - `"OverheardHistory"`：元素 `{"Item1": <StardewTime>, "Item2": {"name": "<说话者 NPC 内部名>", "dialogues": [<DialogueLine>…]}}`（注意这两个字段是小写开头）。
  - `"DialogueHistory"`：元素 `{"Item1": <StardewTime>, "Item2": {"Dialogues": [<DialogueLine>…]}}`。
  - `"ConversationHistory"`：元素 `{"Item1": <StardewTime>, "Item2": {"ConversationElements": [<ConversationElement>…]}}`。
    另有一个更老的替代形态 `"chatHistory"`（字符串数组，偶数下标为 NPC 台词、奇数下标为
    玩家台词）：旧代码只读不写，本机 5 个存档均未出现，但迁移解析必须兼容（§4.4）。
- **子结构 JSON**（逐字精确）：
  - StardewTime：`{"season": "Spring"|"Summer"|"Fall"|"Winter", "dayOfMonth": <int>, "timeOfDay": <int 如 1300>, "year": <int>}`（字段名小写开头；季节是字符串）。
  - DialogueLine（游戏类型 StardewValley.DialogueLine 直接序列化的产物）：
    `{"Text": "<原始台词，可含 #$b#、#$q、$h 等星露谷对话代码>", "SideEffects": null, "HasText": true}`。
    **只有 `Text` 是承载数据的字段**；读取时必须忽略未知字段、忽略 `SideEffects`/`HasText`。
  - ConversationElement：`{"Text": "<台词>", "IsPlayerLine": <bool>, "Id": "<GUID>"}`。
    `Id` 写出但读入时不还原（每次反序列化重新生成）；读取方不得依赖它。
- **写入时机**（旧行为，功能级）：主机侧写操作先进内存缓存，仅在 SMAPI `Saving` 事件时逐键
  WriteSaveData（玩家睡觉/存档时落盘）；"让 NPC 遗忘"操作是例外，立即写一份空历史。
- **读取时机**：每次需要某 NPC 历史时按需 ReadSaveData 反序列化（无载入期全量预读），
  读取后会把"晚于当前游戏时间"的条目删掉（回档保护），失败时记 Error 日志并当作空历史。
- **主机/farmhand 差异**：只有主机读写存档键；远程 farmhand 用 §3.2.1 的文件替代。

#### 3.1.2 token 用量账本：逻辑键 `TokenUsageLedger`

- **物理键**：`smapi/mod-data/dandm1.valleytalk/tokenusageledger`。
- **值 JSON 结构**（逐字精确；已对真实存档核对）：
  `{"Totals": <Totals>, "ByModel": {"<Provider>/<ModelName>": <Totals>…}, "ByNpc": {"<NPC名>": <Totals>…}, "RecentEntries": [<Entry>…最多 20 条，旧的先淘汰]}`
  - Totals：`{"PromptTokens","CompletionTokens","TotalTokens","CachedPromptTokens","CacheWritePromptTokens","ReasoningTokens","OfficialTokens","EstimatedTokens"}`（全 long）。
    ⚠️ 多版本：`CacheWritePromptTokens` 是 0.1.5 期新增字段，老存档（实测 4/5 个）没有该字段，
    反序列化时缺省为 0 即可。
  - Entry：`{"NpcName","Provider","ModelName","Outcome"}`（string；NpcName 空时写 `(system)`，
    Provider 空写 `unknown`，ModelName 空写 `(default)`，Outcome 空写 `unknown`）、
    `{"PromptTokens","CompletionTokens","TotalTokens","CachedPromptTokens","CacheWritePromptTokens","ReasoningTokens"}`（int）、
    `"IsEstimated"`（bool）、`"Source"`（string）、`"Year"`（int）、`"Season"`（**小写**游戏季节字符串，
    如 `"spring"`，来自 Game1.currentSeason，与 StardewTime 的首字母大写不同）、
    `"DayOfMonth"`、`"TimeOfDay"`（int）、`"RecordedAtUtc"`（ISO 8601 UTC，如
    `"2026-06-20T05:36:16.7516098Z"`）。
- **写入时机**：`Saving` 事件写整本账 + 顺手导出 §3.3.2 的 md 报表；控制台 reset 命令立即写空账本。
- **读取时机**：`SaveLoaded` 后首次用到时懒加载；`ReturnedToTitle` 清空内存副本。
- **主机/farmhand 差异**：旧代码没有分流，远程 farmhand 上会因 SMAPI 限制抛异常（被 SMAPI
  捕获记 error）。新实现必须显式仅主机读写，farmhand 只留会话内统计（§6 WP11 的接口不变）。

### 3.2 mod 文件夹内 data 文件（IDataHelper.ReadJsonFile/WriteJsonFile，相对 mod 根目录）

#### 3.2.1 farmhand 历史备用文件：`multiplayer/<SaveFolderName>.json`

- 仅**远程 farmhand** 使用（无法写主机存档），路径相对 mod 文件夹；旧部署下的绝对位置形如
  `Mods\ValleyTalk\ValleyTalk\multiplayer\<存档文件夹名>.json`。
- 内容：`{"<NPC 内部名（未净化、保留原大小写）>": <StardewEventHistory，同 §3.1.1 值结构>}` 的字典。
- 载入后首次使用时读；`Saving` 事件整体重写。
- ⚠️ 旧实现的已知怪癖（新实现必须修正，不是契约）：文件名在单例首次构造时用当时的
  SaveFolderName 定死，同一游戏进程内换存档不会换文件名。新实现按当前存档每次计算。

#### 3.2.2 config.json

- 位置：mod 文件夹根 `config.json`。**旧部署是嵌套结构**：`Mods\ValleyTalk\ValleyTalk\config.json`
  （外层 `Mods\ValleyTalk\` 下还有 `[CP] ValleyTalk Base`、`ValleyTalk for SVE` 两个内容包文件夹，
  它们的 UniqueID 分别是 `dandm1.CPValleyTalk`、`dandm1.ValleyTalkSVE`，不含 config）。
- 旧 config 字段全集（逐字，供 §4.3 搬迁与 WP15 映射；枚举/按键序列化为字符串）：
  `EnableMod`(bool)、`Debug`(bool)、`ExportAiResponseLogs`(bool)、`Provider`(string)、
  `ModelName`(string)、`ServerAddress`(string)、`PromptFormat`(string)、`QueryTimeout`(int)、
  `ApiKey`(string，**敏感**)、`ApplyTranslation`(bool)、`GeneralFrequency`/`MarriageFrequency`/
  `GiftFrequency`(int)、`GenerateAiForNormalRightClick`(bool)、`TypedResponses`(string)、
  `EnableSveCompatibility`(bool)、`UseOptimizedPrompts`(bool)、`EnableSemanticContextRouting`(bool)、
  `SemanticContextRoutingTimeoutSeconds`(int)、`RoutingThinkingLevel`(string)、
  `ChatThinkingLevel`(string)、`DisableCharacters`(string，逗号/空格分隔)、
  `InitiateTypedDialogueKey`(string，SButton 名)、`SuppressConnectionCheck`(bool)。
  旧 Provider 合法值（迁移时校验用）：`LlamaCpp`、`Google`、`Anthropic`、`OpenAI`、`Mistral`、
  `DeepSeek`、`VolcEngine`、`OpenAiCompatible`（大小写不敏感）。
- 同目录可能存在 `config.json.bak-<yyyyMMdd-HHmmss>` 备份文件，扫描时忽略。

### 3.3 导出类文件（人读的，只有目录/命名是契约）

全部位于 mod 文件夹下，按存档分目录；NPC 文件名把 `Path.GetInvalidFileNameChars()` 中的字符
替换为 `_`，全无效则用 `unknown-npc`；存档名不可用时目录用 `unknown-save`。

1. **对话转录**：`conversation_logs/<SaveFolderName>/<NPC>.md`。文件内含两个 HTML 注释标记行，
   是**程序回读的机器契约**（用于把被修剪历史归档进文件、以及尾部快速重写）：
   归档头 `<!-- valleytalk:archive count=<n> lastDay=<绝对天数> lastTime=<hhmm> -->`、
   归档尾 `<!-- valleytalk:archive-end -->`。绝对天数 = year×112 + 季节序号(春0夏1秋2冬3)×28 +
   dayOfMonth。无标记的文件按"无归档"处理，不丢数据。该导出器是搬运件；若新 mod 决定
   沿用旧转录文件（§4.3.2），标记字符串**必须原样保留**，否则旧文件的归档段视为无标记全量重建。
2. **token 报表**：`token_usage/<SaveFolderName>.md`（未载入存档时 `no_save_loaded.md`）。
3. **AI 响应日志**：`ai_response_logs/<SaveFolderName>/<NPC>.md`（追加式）。
4. **提示词日志**：`prompt_logs/<SaveFolderName>/<NPC>.md`（追加式）。
5. **语义路由日志**：`context_routing_logs/<SaveFolderName>/<NPC>.md`（追加式）。
   3–5 由 config `ExportAiResponseLogs` 总开关控制。

## 4. 迁移规范

### 4.1 总则

- 迁移器是 `Persistence/` 下的独立服务，入口两处：
  **存档数据迁移**在每次 `SaveLoaded` 检查并执行；**文件夹迁移**（config/data 文件）在
  `GameLaunched` 执行一次（不依赖存档）。
- **只主机执行**存档迁移（`Context.IsMainPlayer`）；farmhand 侧只做 §4.3.3 的 multiplayer 文件搬迁。
- **幂等**：迁移完成后写标记键（§5.1 的 `migration.v1`），下次载入看到标记直接跳过。
  标记值建议记录：迁移时间(UTC)、迁移条数、旧 mod 版本（若可知）、失败键列表。
- **失败不破坏旧数据**：整个迁移过程绝不写 `dandm1.valleytalk` 前缀的任何键、绝不删除旧
  mod 文件夹内的任何文件。单键失败记入标记的失败列表并继续，不中止整体。
- 若启动时检测到 `dandm1.ValleyTalk` 仍在加载（01 §5），**跳过全部迁移**（对话引擎也保持关闭），
  等用户删除旧文件夹后的下一次启动再做。

### 4.2 存档数据迁移（SaveLoaded，主机）

1. **枚举**：新 mod 的 IDataHelper 只能读自己 modID 下的键，因此直接遍历游戏侧字典
   `SaveGame.loaded.CustomData`（及 `Game1.CustomData`，两者内容一致，读其一即可），
   取所有以 `smapi/mod-data/dandm1.valleytalk/` 为前缀的键值对。值是 JSON 字符串，
   用 Newtonsoft + StringEnumConverter 反序列化（§3.0 设置）。
2. **tokenusageledger**：按 §3.1.2 结构读出，转写为新账本模型后经 IDataHelper.WriteSaveData
   写到新键（§5.1）。缺失字段取 0/空。
3. **eventhistory_<name>**：物理键里的 NPC 名已被小写化，需要反查原名：
   对 `Game1.characterData` 的每个键（NPC 内部名）套用 §3.1.1 的 saveName 规则再转小写，
   建立"小写净化名 → 原名"映射后逐一匹配。匹配不上的残留键（多为已卸载的 NPC mod，
   实测存档里有大量 SVE/RSV 角色）**保留原样不迁移、不删除**，数量记入迁移标记，
   日志列出键名——将来玩家装回该 NPC mod 后重跑迁移仍可拾起（迁移标记按"仍存在未迁移
   旧键"判断是否需要补迁，或干脆在标记里存已迁移键集合）。
4. **转写**：反序列化成功的历史按 §5.1 键名用新 mod 身份写回。写之前跑一遍与读取路径相同的
   容错清洗（§4.4）。
5. **旧键处置（用户决策点，建议已给）**：建议 0.2.x 全程**保留旧键**（先写后不删），
   代价是每存档若干十 KB 量级的冗余（实测最大单存档约 400KB）；提供控制台命令
   `livingnpcs_purge_valleytalk`（需 confirm 参数）供用户手动清除——清除实现即对每个旧物理键
   从 CustomData 字典移除。待某个后续版本再默认删除。**是否接受"永不自动删除"请用户拍板**（§8）。

### 4.3 旧文件夹数据迁移（GameLaunched）

1. **定位旧文件夹**：从游戏 `Mods/` 目录（新 mod 文件夹的上级，即
   `helper.DirectoryPath` 的父目录）开始，扫描**第一层与第二层**子目录中的 `manifest.json`，
   找 `UniqueID == "dandm1.ValleyTalk"`（大小写不敏感）的目录——必须扫两层，因为旧部署是
   `Mods\ValleyTalk\ValleyTalk\`（§3.2.2）。内容包 ID（`dandm1.CPValleyTalk`、
   `dandm1.ValleyTalkSVE`）不匹配、不处理。找不到则静默结束（全新用户）。
2. **搬什么**：
   - `config.json`：读出 §3.2.2 全部字段，交给 WP15 的字段映射器合入新 config（**字段名映射表
     与语义以 WP15 文档为准**，本包只负责读旧文件、调用映射、WriteConfig 落盘）。`ApiKey` 等
     敏感值不得写入日志。仅当新 config 仍是"从未配置过"状态（如 ApiKey 为空且无迁移标记）才合入，
     避免覆盖用户在新 mod 里已改的设置。
   - `multiplayer/*.json`：整目录复制到新 mod 文件夹同名子目录（farmhand 历史，§3.2.1）。
   - `conversation_logs/` 整目录：**建议复制**到新 mod 文件夹（它是玩家可读的回忆录，且归档段
     含已从存档修剪掉的对话，不搬就永久丢失）。归档标记按 §3.3.1 保持兼容。
   - `token_usage/`、`ai_response_logs/`、`prompt_logs/`、`context_routing_logs/`：纯诊断，
     **默认不搬**（体积大、无游戏价值）。
   - 其余（DLL、manifest、内容包）一律不动。
3. **farmhand 场景**：farmhand 机器上同样跑这一步（它只涉及本机文件），multiplayer 文件搬过去后
   新 mod 即可无缝续接 farmhand 历史。
4. **文件夹迁移的幂等**：以新 mod 文件夹内的标记文件 `data/migration-state.json`（内容含
   completedAtUtc、来源路径、各项结果）判断是否已做过。
5. **不删除旧文件夹**：只提示。删除交给用户手动完成（01 §5 的"请删除 ValleyTalk 文件夹"提示
   本就要求用户动手）。

### 4.4 反序列化容错（迁移与日常读取共用）

- 任何键值整体反序列化失败：记 Warn 日志（含键名与异常摘要），该键跳过；迁移时保留旧键
  并计入失败列表；日常读取时按空数据继续，**绝不抛出打断存档载入/保存**。
- 历史条目级容错：四个数组独立解析，坏元素丢弃不连坐；`EventHistory` 数组内容（含 `Listeners`）
  允许整条丢弃（§3.1.1 警告）；`ConversationHistory` 条目优先读 `ConversationElements`，
  没有时回退读 `chatHistory`（偶数下标 NPC、奇数下标玩家）；`DialogueLine`/`ConversationElement`
  只取 `Text`/`IsPlayerLine`，未知字段忽略（`MissingMemberHandling` 保持默认 Ignore）。
- 时间字段容错：季节字符串大小写不敏感解析；非法季节/负数日期的条目丢弃。
- 读到"晚于当前游戏时间"的条目照旧删除（回档保护，见 §3.1.1）。

### 4.5 用户可见反馈

- 迁移执行时：SMAPI Info 日志（i18n，中英），格式含迁移的 NPC 数、账本是否迁移、跳过/失败键数。
- 迁移完成的当次载入：一次性游戏内 HUD 提示（`Game1.addHUDMessage` 级别的普通提示），文案走
  i18n，例如"已从 ValleyTalk 迁移 N 位 NPC 的对话记忆"。之后永不再弹。
- 检测到旧 mod 仍在加载：Error 日志 + HUD 警告（01 §5 已定，由 WP12 接线，本包提供检测结果）。
- 失败：Warn 日志逐键列出，HUD 只提示"部分数据未能迁移，详见 SMAPI 日志"。

## 5. 新世界的持久化布局

### 5.1 存档键命名（新契约，从 0.2.0 起）

全部键**带版本段**，前缀 `smapi/mod-data/yuki.livingnpcs/` 由 SMAPI 自动加：

| 逻辑键 | 用途 |
|---|---|
| `dialogue.history.v1_<saveName>` | 每 NPC 对话历史（saveName 沿用 §3.1.1 的净化规则，规则本身是格式契约可复用） |
| `dialogue.tokens.v1` | token 用量账本 |
| `dialogue.migration.v1` | 迁移标记（§4.1） |

注意：键必须是 slug（§3.0），不能用 `/` 分隔；用 `.` 与 `_` 组合。物理键会整体转小写，
因此**逻辑键约定直接全小写**，避免再次踩大小写陷阱；saveName 段在构造时即转小写，
并把"小写净化名 → 原名"的原名冗余存进值内（新历史 JSON 顶层加 `"npcName"` 字段），
使未来的枚举式处理不再依赖反查。

### 5.2 值结构与序列化设置

- 新模型字段由 WP10（ExchangeRecord 等）定；本包要求：顶层带 `"schemaVersion": 1`（int）与
  `"npcName"`（string）；时间戳沿用 `{year, season(字符串), dayOfMonth, timeOfDay}` 形态以便复用
  迁移解析器；台词只存纯文本字段，不再序列化游戏对象（杜绝 §3.1.1 的 Listeners 事故）。
- 序列化统一走 SMAPI IDataHelper（它自带 StringEnumConverter），mod 内不得自建不同设置的
  serializer 处理存档数据；迁移读取旧数据时的独立 serializer 也用相同转换器集合。

### 5.3 物理体积护栏（策略归 WP10，护栏归本包）

- 单 NPC 历史序列化后 **> 256 KB** 时：拒绝写入更大的新版本，改为触发一次强制修剪回调
  （WP10 提供），修剪后仍超限则丢最老条目直至达标，并记 Warn。实测旧存档单 NPC 最大约
  130 KB，256 KB 是 2 倍裕量。
- 全 mod 键总体积 **> 4 MB** 时记 Warn 日志提示玩家（存档变慢的预警线，不阻断）。
- 账本 RecentEntries 上限维持 20 条（搬运件自带）。

### 5.4 写入/读取时机与多人语义（新世界）

- 沿用旧节奏：写操作进内存缓存，`Saving` 时统一落盘；"遗忘"立即落盘；`ReturnedToTitle`/
  `SaveLoaded` 清空会话缓存。**禁止在 LLM 回调线程直接写存档结构**——统一入队，主线程
  Saving 时消费（00 §3 线程模型）。
- 主机：读写 SaveData。远程 farmhand：读写 `multiplayer/<SaveFolderName>.json`（结构同新历史
  模型的字典，键为 NPC 内部名原大小写）；token 账本 farmhand 不持久化，只保留会话内统计，
  控制台命令在 farmhand 上明确提示"用量账本仅主机保存"。
- 导出文件（§3.3 五类）路径约定原样保留（搬运件），根目录换成新 mod 文件夹
  `Mods/LivingNPCs/` 之下；转录归档标记沿用 `valleytalk:archive` 字面量以兼容搬来的旧文件
  （改名与否见 §8）。

## 6. 与其他工作包的接口

- **WP10**：本包实现 01 §2 的 `IDialogueHistory`（Append/Recent）；修剪策略回调、被修剪对话
  的归档钩子（转给转录导出器）由 WP10 调用本包。护栏（§5.3）对 WP10 透明。
- **WP11**：token 用量的记录接口（Record(npc, usage, provider, model, outcome)）保持搬运件
  签名，WP11 在每次请求完成时调用。
- **WP12**：SaveLoaded/Saving/ReturnedToTitle/GameLaunched 事件由 WP12 统一接线到本包入口；
  HUD 提示的显示通道由 WP12 提供。
- **WP15**：旧 config 字段映射表、新 config 模型、GMCM。本包只做"读旧文件 → 调映射 → 写新
  config"。i18n 键（迁移日志/HUD 文案）登记到 WP15 的 i18n 清单。
- **WP16**：`behavior-memory`（行为系统记忆，含记忆印象）完全归 WP16，本包不迁移不读写。

## 7. 验收要点

1. 用本机真实旧存档（含 `dandm1.valleytalk` 键的存档）载入新 mod：日志报迁移条数；
   与 NPC 对话时能引用旧历史；再次载入不重复迁移；旧键仍在存档中。
2. 旧嵌套文件夹 `Mods\ValleyTalk\ValleyTalk\` 存在（无 DLL 亦可）时：config 的 ApiKey/Provider
   等被搬入新 config；conversation_logs 被复制；再次启动不重复搬。
3. 构造含 `chatHistory` 老形态、含非空 `EventHistory`、含非法季节字符串的坏 JSON 注入旧键：
   迁移不崩溃，坏条目丢弃，坏键计入失败列表且旧键保留。
4. 老账本（无 `CacheWritePromptTokens` 字段）迁移后统计正确，该项为 0。
5. NPC 名净化规则单测：中文名、全非法字符名（走十六进制分支）、>50 字符名、含 `.`/`-`/`_` 名。
6. 多人：远程 farmhand 不触碰 SaveData（无异常日志）；multiplayer 文件按当前存档命名，
   换存档后文件名跟着换。
7. 体积护栏：注入超 256 KB 历史，验证强制修剪与 Warn 日志。
8. 新键均为 slug 且全小写；`dandm1.ValleyTalk` 在加载列表时迁移与引擎均不启动。

## 8. 开放问题（请用户裁决）

1. 旧存档键是否永不自动删除（仅提供 purge 命令）？——建议是（§4.2.5）。
2. 转录归档标记 `valleytalk:archive` 字面量：保留（零成本兼容搬来的旧转录），还是改为
   `livingnpcs:archive` 并对旧标记做双读？——建议保留，字符串仅是机器标记不含品牌露出风险。
3. `conversation_logs` 之外的诊断日志目录确认不搬？（§4.3.2 默认不搬）
4. 未匹配到已知 NPC 的旧历史键（卸载的 NPC mod）：按 §4.2.3 留待补迁，还是提供"按小写名
   强行迁移"的命令？——建议前者。
5. 旧 config 合入条件"新 config 从未配置过"用什么判据（ApiKey 为空？专门的 firstRun 标志？）
   ——与 WP15 联合裁决。

### 裁决（2026-07-06，Yuki + 架构侧，全部落定）

1. **旧存档键永不自动删除**（Yuki 裁决，支持回滚），仅提供 purge 控制台命令
   （命令名按 WP12 裁决 3 用 `livingnpcs_` 前缀）。
2. `valleytalk:archive` 归档标记**保留**（机器标记，零成本兼容旧转录）。
3. `conversation_logs` 之外的诊断日志**不搬**。
4. 未匹配 NPC 的旧历史键**留待补迁**，不做强行迁移命令。
5. 判据裁决：新 config 增加 `LegacyConfigImported`（bool，默认 false，不进 GMCM）。
   首次启动若为 false：尝试导入旧 config（仅填充仍等于默认值的字段），无论是否
   找到旧文件均置 true 写回。避免以 ApiKey 为空做判据（用户可能故意留空）。
   WP15 落表。

## 9. 审计索引（撰写本文档时核对过的位置）

- ValleyTalk/src/EventHistoryReader.cs:20-44（主机缓存/farmhand 文件分流、Saving 落盘）、
  :98/:137/:161-169（`EventHistory_` 键构造与遗忘写空）、:104-126（读取+回档删除+容错）、
  :177-209（saveName 净化规则：合法字符集、十六进制回退、50 截断）
- ValleyTalk/src/TokenUsageTracker.cs:14-16（键 `TokenUsageLedger`、目录 `token_usage`、上限 20）、
  :129-166（reset/SaveLoaded/Saving/ReturnedToTitle 时机）、:210-216（报表路径与 `no_save_loaded`）、
  :232-344（Ledger/Totals/Entry 全字段）
- ValleyTalk/src/models/history/StardewEventHistory.cs:16-26（保留上限与 112 天）、:28-74（四数组
  属性名）、:112-117（不可持久化类型丢弃）、:148-233（RemoveAfter/Prune）
- ValleyTalk/src/models/history/ConversationHistory.cs:14-31（chatHistory 旧形态只读不写、
  ConversationElements、Id 不序列化）；DialogueHistory.cs:28、DialogueEventOverheard.cs:10-11、
  DialogueEventHistory.cs:25-27（各条目字段名与 Listeners 隐患）；ThirdPartyHistory.cs:7-18 与
  ActivityHistory.cs:3-15（无可序列化桶，只入内存）
- ValleyTalk/src/StardewTime.cs:8-11（season/dayOfMonth/timeOfDay/year 字段）、:61-64（绝对天数公式）
- ValleyTalk/src/Generation/ConversationElement.cs:5-16（Text/IsPlayerLine/Id）
- ValleyTalk/src/ConversationTranscriptExporter.cs:13-34（目录 `conversation_logs`、unknown-save）、
  :19-21/:366-418（归档标记格式与水位）、:662-672（文件名净化）
- ValleyTalk/src/AiResponseLogExporter.cs:15-45、PromptLogExporter.cs:13-44、
  ContextRoutingLogExporter.cs:12-43（目录名、ExportAiResponseLogs 开关、追加式）
- ValleyTalk/src/PromptCache.cs:9-67（确认无持久化）
- ValleyTalk/src/config/ModConfig.cs:10-58（config 字段全集）；ModEntry.cs:17-41（Provider 合法值）、
  :125（ReadConfig）、:352-367（SaveLoaded/ReturnedToTitle 清缓存）
- ValleyTalk/src/manifest.json:6（`dandm1.ValleyTalk`）；ContentPack/manifest.json:6 与
  Extensions/ValleyTalk for SVE/manifest.json:6（内容包 ID）
- LivingNPCs/Behavior/BehaviorEngine.cs:31（`behavior-memory`，不在本包范围）
- SMAPI 源码 src/SMAPI/Framework/ModHelpers/DataHelper.cs（develop，2026-07）：物理键格式、
  slug 校验、null 即删除、IsOnHostComputer 限制；src/SMAPI.Toolkit/Serialization/JsonHelper.cs：
  序列化设置
- 实测存档（本机 `%APPDATA%\StardewValley\Saves\`，5 个含旧键的存档）：物理键小写形态、
  四数组 JSON、DialogueLine 的 `Text/SideEffects/HasText`、ConversationElement 的
  `Text/IsPlayerLine/Id`、账本新旧字段差异（CacheWritePromptTokens 仅最新存档有）、
  EventHistory 全空、chatHistory 旧形态未出现

## 10. 实现记录（2026-07-07，WP14）

### 落位

`LivingNPCs/Dialogue/Persistence/` 新增 9 个文件 + 改写搬运件 `TokenUsageTracker.cs`；
跨包契约 `LivingNPCs/Dialogue/IDialogueHistory.cs` 与 `StardewTime.cs`；
测试在 `LivingNPCs.Tests/Dialogue/Persistence/`（5 个文件、42 项）。

- **历史模型**（`HistoryModels.cs`，按 §3.1.1 契约重做）：四桶 `StardewEventHistory`
  （Conversation/Dialogue/Event/Overheard，元素形态沿用 `{"Item1","Item2"}` 的 Tuple 序列化产物，
  使迁移解析与新数据读写共用一套代码）+ 顶层 `schemaVersion`/`npcName`（§5.2）。台词行只有
  `Text`；事件在场者只存内部名字符串（杜绝 Listeners 序列化 NPC 对象事故）；第三方目击记录
  `[JsonIgnore]` 仅内存（WP10 §4.14）。修剪按 WP10 §4.15 契约实现：容量 事件40/旁听40/台词60/会话30、
  112 天年龄上限（事件除外）、当日条目保护、幂等；被修剪会话时间正序返回供归档。
  `RemoveAfter` 承载回档保护。阶段 A 桩里的占位模型（上限 20、无旁听桶）已删除，
  `LegacyStubs` 的 `EventHistoryReader` 桩一并移除。
- **容错解析**（`HistoryJson.cs`，§4.4 迁移与日常读取共用）：JToken 逐条解析，四数组独立、
  坏元素丢弃不连坐；季节大小写不敏感、非法季节/负数日期丢弃；`ConversationElements` 优先、
  `chatHistory` 回退（偶 NPC 奇玩家）；`Listeners` 容忍任意内容（字符串收名、对象取 Name、其余弃）；
  `Id` 不还原、未知字段忽略。
- **名字净化**（`SaveNameSanitizer.cs`，§3.1.1 逐字）：合法集合过滤 → 全非法转 byte 大写十六进制
  （`X2` 定宽；文档未注明宽度，若真实旧存档出现低于 0x10 的字节且反查失败，会落入"留待补迁"而非丢失）
  → 50 截断；新键用 `SanitizeLower`。
- **存取通道**（`DialogueHistoryStore.cs`，实现 01 §2 `IDialogueHistory`）：写进内存缓存、Saving 统一
  落盘、遗忘立即写空历史（`Forget`/`ForgetAll` 返回是否确有清除，供 WP10 §4.14 清除接口）、
  SaveLoaded/ReturnedToTitle 清缓存、读取时回档删除、读失败记 Error 按空历史。主机走 IDataHelper
  存档键 `dialogue.history.v1_<净化名小写>`（斜杠禁用，`.`/`_` 组合，§5.1）；远程 farmhand 走
  `multiplayer/<SaveFolderName>.json`（键为内部名原大小写，文件名按当前存档每次计算，修正了 §3.2.1
  记载的旧怪癖；旧格式文件同一解析器接住）。护栏（§5.3）：单 NPC 序列化 >256KB 先触发
  `ForcePruneCallback`（WP10 注入，缺省契约修剪），仍超限丢最老条目直至达标并 Warn；
  全 mod 键（`smapi/mod-data/yuki.livingnpcs/` 前缀）总体积 >4MB Warn 一次。被修剪会话经
  `ArchiveSink`（缺省接转录导出器 `ArchivePrunedConversations`，归档标记按裁决 2 保留
  `valleytalk:archive` 字面量）。全部操作单锁保护（00 §3 线程模型：LLM 回调线程只进缓存）。
- **契约类型**：`ExchangeRecord`（Kind=Conversation/DialogueLines/EventLines/Overheard + Time/
  ConversationId/SpeakerName/EventName/ListenerNames/Lines）为 WP14 初版定义，字段最终裁定权
  在 WP10（01 §2）；Conversation 按 ConversationId 同 ID 覆盖（upsert），`Recent` 四桶按时间合并
  取最近 N 条、正序返回。`StardewTime` 从阶段 A 桩提升到 `LivingNPCs.Dialogue` 根命名空间
  （JSON 形态即 §3.1.1 契约），绝对天数公式与转录水位共用。
- **存档迁移**（`LegacySaveDataMigrator.cs`，§4.2）：SaveLoaded 主机执行；直接枚举
  `Game1.CustomData` 取 `smapi/mod-data/dandm1.valleytalk/` 前缀；账本 → 新键 `dialogue.tokens.v1`
  并写穿（缺失字段缺省 0，§7.4）；历史键对 `Game1.characterData` 建"净化小写名 → 原名"反查，
  未匹配键保留待补迁（裁决 4，装回 NPC mod 后下次载入自动拾起）；写前跑同一容错清洗 + 回档删除。
  幂等标记 `dialogue.migration.v1` 存已迁移/失败键集合；仅剩 pending 的一轮不算执行过（不写标记
  不出日志）。旧键永不自动删除（裁决 1）；全程零写旧前缀。HUD 一次性提示（迁移数 >0 时）+
  失败时"部分数据未能迁移"提示，走 `LlmHudNotifier` 主线程泵。
- **文件夹迁移**（`LegacyFolderMigrator.cs` + `LegacyValleyTalkConfig.cs`，§4.3）：GameLaunched 执行
  （farmhand 机器同样跑）；Mods 根两层扫描 manifest 认 `dandm1.ValleyTalk`（大小写不敏感；标准解析
  失败退精确正则以兼容 SMAPI 宽松 manifest），内容包 ID 不处理；复制 `multiplayer/*.json` 与
  `conversation_logs/` 全目录（不覆盖已存在文件），诊断日志目录不搬（裁决 3）；幂等标记
  `data/migration-state.json`（completedAtUtc/来源/条数/错误）。config.json 读为 §3.2.2 全字段模型
  （含 Raw JObject 与 Provider 合法值校验）后交 `LegacyFolderMigrator.ConfigImporter` 钩子——
  **WP15-TODO**：由 WP15 注册映射器并落 `LegacyConfigImported`（裁决 5；没找到旧 config 也会以
  null 通知，WP15 仍应置 true）。找不到旧文件夹静默结束且不写标记（用户从备份恢复旧文件夹可再触发，
  作为 §4.3 数据丢失场景的补救通道）。绝不删除旧文件夹任何文件。
- **TokenUsageTracker**（搬运件改写）：存档键 `TokenUsageLedger` → `dialogue.tokens.v1`（§5.1）；
  Read/Write/Reset 仅主机（§3.1.2 farmhand 只留会话统计，控制台摘要在 farmhand 上加"仅主机保存"
  提示行）；新增 `ImportMigratedLedger`（只改内存，落盘由迁移器写穿 + Saving 兜底）。
- **接线**（`DialoguePersistence.cs` 门面）：注册四个 GameLoop 事件与
  `livingnpcs_purge_valleytalk` 命令（无 confirm 参数时只报旧键数量与用法；confirm 后从
  `Game1.CustomData`/`SaveGame.loaded.CustomData` 移除旧键，下次存档生效）；暴露
  `LegacyValleyTalkLoaded` 供 WP12 做 HUD 警告与引擎禁用（检测到时本包记 Error 日志并跳过全部迁移）。
  `ModEntry` 在启用路径调用 `RegisterEvents`（WP12-TODO：最终收编到游戏集成层）。
- **可测性**：`IPersistenceEnvironment` 把 SMAPI/游戏静态收敛为唯一出口
  （`SmapiPersistenceEnvironment` 生产实现）；测试假环境按 SMAPI 真实行为模拟（slug 校验、
  StringEnumConverter 序列化为单字符串、物理键整体小写）。发现并绕开：单测进程里
  `StardewModdingAPI.Context` 类型初始化会抛异常，故迁移路径不直接触碰 Context（tracker 的
  Context 分支仅在游戏进程走到）。

### 实现判断点（规格留白处的裁定，供审计）

- 十六进制回退用 `X2` 定宽（§3.1.1 只写"强转 byte 的大写十六进制串"未注明宽度）。两种写法仅在
  字节 <0x10 时有别且只影响全非法名的反查；反查不中会落入补迁名单而非丢失，风险受控。
- 修剪的"当日保护"应用于全部四桶（规格针对会话表述，扩展到其余桶只会更保守）；会话的 112 天
  年龄修剪结果同样交归档（它们也是被修剪的会话记忆）。
- 迁移标记里的失败键不自动重试（数据不会自愈，重试只会每次载入刷 Warn）；`pendingKeys` 仅在
  标记写入时随手记录，权威判断始终按"旧键 − 已迁移 − 已失败"现算。
- 护栏强制修剪后仍超限的"丢最老条目"跨四桶按时间统一取最老，其中丢掉的会话同样归档。
- `Recent` 未纳入第三方目击记录（不持久化，WP10 经 `GetHistory` 直接采样内存桶）。
- 新键写入走 `WriteSaveData(key, model)` 由 SMAPI 序列化；护栏测量用等价设置
  （Newtonsoft + StringEnumConverter + Formatting.None）自行序列化，两者字段名由 JsonProperty 钉死。

### 供下游工作包对接

- **WP10**：`DialogueHistoryStore.Instance` 实现 `IDialogueHistory`（Append/Recent）；直接变更用
  `GetHistory(npc)` + `MarkDirty(npc)`；修剪入口 `PruneAndArchive(npc)`（契约修剪 + 自动归档）；
  强制修剪回调挂 `ForcePruneCallback`；清除接口 `Forget`/`ForgetAll`（转录档案重置请另调
  `ConversationTranscriptExporter.ResetTranscript`，本包不越俎）。`ExchangeRecord` 字段如需扩展改
  `IDialogueHistory.cs` 一个文件。
- **WP11**：`TokenUsageTracker.Instance.Record(npc, usage, provider, model, outcome)` 签名未动。
- **WP12**：事件已由门面自行注册，收编时把 `DialoguePersistence.RegisterEvents()` 挪走即可；
  旧 mod 检测结果读 `DialoguePersistence.LegacyValleyTalkLoaded`（HUD 警告与引擎禁用由 WP12 接线）。
- **WP15**：注册 `LegacyFolderMigrator.ConfigImporter`（参数 `LegacyValleyTalkConfig?`，含全部
  §3.2.2 字段 + `Raw`；null 表示没找到旧 config，仍须置 `LegacyConfigImported=true`）。i18n 需落表：
  `dialogue.migration.save.summary`（tokens: npcCount/ledger/pendingCount/failedCount）、
  `dialogue.migration.save.hud`（npcCount）、`dialogue.migration.save.hudPartial`、
  `dialogue.migration.folder.summary`（source/multiplayer/transcripts/config）、
  `dialogue.tokens.hostOnly`、`dialogue.purge.hostOnly`、`dialogue.purge.usage`（count/command）、
  `dialogue.purge.done`（count）；英文兜底文案已内置。
- **WP16**：`behavior-memory` 未触碰（§1 范围外）。

### 验证

- `dotnet test LivingNPCs.Tests`：**通过 399，失败 0，跳过 1**（唯一 skip 仍为阶段 A 遗留的
  WP15-TODO）；本包新增 42 项，覆盖 §7 验收 3–8（净化规则中文名/十六进制/50 截断/合法字符、
  四数组容错解析与 chatHistory 回退与非法季节丢弃与 Listeners 任意内容、老账本缺字段为 0、
  新键 slug 全小写（假环境按 SMAPI 规则校验）、farmhand 零 SaveData 调用与按存档命名换档跟随、
  256KB 强制修剪 + Warn、旧 mod 在加载/farmhand 全跳过、迁移幂等/补迁/坏键不重试/旧键保留、
  purge 只删旧前缀、嵌套文件夹定位/内容包忽略/诊断不搬/标记幂等/config 钩子）；修剪契约测试
  更新为 WP10 §4.15 容量（40/40/60/30）并补旁听桶/年龄归档/RemoveAfter 用例。
- 主工程 Debug 与 Release（`-p:EnableModDeploy=false`）双配置 0 错误；`Persistence/` 目录 0 警告
  （顺手修掉搬运件 TokenUsageTracker 的三个事件处理器可空性警告）。
- 验收 1、2、6、7 的真机部分（真实旧存档载入、旧嵌套文件夹、联机 farmhand、游戏内体积护栏）
  留给 30 号验收的用户冒烟；单测已覆盖其可自动化面。

### 洁净室声明

本包实现全程未打开 `ValleyTalk/`、`ValleyTalk.Tests/`、`upstream-ValleyTalk/` 下任何文件
（rewrite worktree 中前两者已物理删除），未以任何方式检索上游源码；仅阅读 RewriteSpec、
`LivingNPCs/`、`LivingNPCs.Tests/`、阶段 A 已落位的搬运件与 WP11 成果。
