# 15 · WP15 内容资产、配置系统、GMCM 与 i18n 功能说明书

> 读者：从未见过旧代码的实现 AI。开工前先读 00、01、03。
> 本文只描述**行为与格式**。配置字段名、资产名、JSON 键名为兼容契约，按原样精确记录；
> 上游创作文本（传记/世界观/提示词正文）零引用——文本重创作归 WP20，本文只钉容器格式
> 与加载行为。禁止去旧代码目录核对——缺了就记开放问题。

## 1. 目的与范围

WP15 负责 `LivingNPCs/Dialogue/Content/` 下的全部代码：

- **资产管线**：角色传记（Bios）、世界观综述（GameSummary / GameSummaryOptimized）、
  提示词骨架表（Prompts）三类游戏资产的提供、加载、本地化、失效与第三方覆盖；
- **配置系统**：对话引擎的全部配置字段（并入 LivingNPCs 现有 `ModConfig`）、
  config.json 读写、字段迁移契约（执行归 WP14）；
- **GMCM 菜单**：对话引擎配置段在 Generic Mod Config Menu 中的注册、
  按提供商动态显隐字段、菜单刷新，以及无 GMCM 时的降级；
- **i18n**：引擎玩家可见文案的键位规划与迁移（zh 整体搬运、en 重写）。

**不在本包**：提示词的拼装与生成流程（WP10）、LLM 客户端（WP11）、
Harmony 补丁与 UI（WP12）、历史/用量持久化与旧数据搬迁的执行（WP14）、
传记/世界观/提示词文本的创作（WP20）。

## 2. 权属与搬运边界

- **重写**（本包产出）：旧世界 `config/ModConfig.cs`、`config/ModConfigMenu.cs`、
  `Character.cs` 中的传记加载与回退逻辑、`GameSummaryBuilder.cs`、`PromptCache.cs`、
  `Util.cs` 中的字符串查找（GetString 家族）、`VtConstants.cs` 的对应物。
  这些文件均有上游血统，一律按本文行为重新实现。
- **搬运件**（阶段 A 已复制，MINE，可直接阅读调用）：`RsvAiPolicy.cs`
  （Ridgeside 屏蔽策略，本包只提供两个挂载点，见 §4.6）。
- **公开接口文件**：GMCM 的 `IGenericModConfigMenuApi.cs` 是 GMCM 作者公开发布的
  接入接口（spacechase0 提供、供所有 mod 复制使用），直接从 GMCM 官方文档/仓库取用，
  不受洁室限制。
- **内容资产一律重新创作**（WP20）：33 个原版传记、约 24 个 SVE 传记、
  GameSummary 两套、Prompts 全部键值、`ContentPack/i18n/default.json`（英文 UI 文案）、
  `translations/`（上游随发的 fr-FR/zh-CN 译文，属上游文本衍生，禁止搬运）。
- **整体搬运**：`ContentPack/i18n/zh.json`（950 键，Yuki 原创中文文案，03 §4），
  键名迁移方案由本文 §4.8 给出。
- **格式与标识符不受版权保护**：本文记录的字段名/键名/资产名可原样复用。

## 3. 外部契约

### 3.1 配置 schema 全量表（核心契约）

旧世界配置持久化在 `Mods/ValleyTalk/config.json`（WP14 迁移的读取源）。
新世界这些字段并入 LivingNPCs 现有 `ModConfig` 类（`Mods/LivingNPCs/config.json`）。
下表为对话引擎字段全量；"生效"列：**即时**=每次使用时读取；**保存时**=GMCM 保存
回调里重建 LLM 客户端后生效；**重启**=需重启游戏。

| 旧字段名 | 类型 | 默认值 | 语义 | GMCM 控件 | 生效 |
|---|---|---|---|---|---|
| `EnableMod` | bool | `true` | 总开关；启动时为 false 则引擎完全不初始化（不打补丁、不挂事件） | Bool | 重启（关→开） |
| `Debug` | bool | `false` | 调试日志开关 | Bool（旧世界仅 DEBUG 构建显示） | 即时 |
| `ExportAiResponseLogs` | bool | `true` | 是否导出 AI 响应/提示词/路由日志文件（Diagnostics 搬运件读取） | 无（仅 config.json） | 即时 |
| `Provider` | string | `"Mistral"` | LLM 提供商 ID，合法值见 WP11 §3（`OpenAI`/`OpenAiCompatible`/`Google`/`Anthropic`/`Mistral`/`DeepSeek`/`VolcEngine`/`LlamaCpp`，DEBUG 构建另有 `Dummy`）；大小写不敏感匹配；非法值回退列表首项 | 下拉（allowedValues），显示名走 i18n | 保存时 |
| `ApiKey` | string | `""` | **敏感**：API 密钥。不得写入任何日志/导出文件/git；迁移时原样保留 | 文本框（仅当所选提供商构造需要 apiKey 时显示） | 保存时 |
| `ModelName` | string | `""` | 模型名；空串用提供商默认（见 WP11） | 文本框（提供商需要 modelName 时显示） | 保存时 |
| `ServerAddress` | string | `"https://openrouter.ai/api"` | 自定义端点地址（LlamaCpp 与 OpenAiCompatible 用） | 文本框（提供商需要 url 时显示） | 保存时 |
| `PromptFormat` | string | `"[INST] {system}\n{prompt}[/INST]\n{response_start}"` | LlamaCpp 原始补全的提示词模板 | 无（仅 config.json） | 保存时 |
| `QueryTimeout` | int | `85` | LLM 请求超时秒数 | 数字（min 5, max 180, 步长 5） | 即时 |
| `ApplyTranslation` | bool | `true` | 要求模型用游戏语言输出（在系统提示词尾追加语言指令，见 §4.7） | Bool | 即时 |
| `GeneralFrequency` | int | `4` | 普通台词 AI 生成频率，0–4 档对应 0/25/50/75/100% 概率门 | 下拉（值 "0"–"4"，显示"从不(0%)…总是(100%)"） | 即时 |
| `MarriageFrequency` | int | `4` | 婚后台词频率，同上档位 | 下拉同上 | 即时 |
| `GiftFrequency` | int | `4` | 送礼反应频率，同上档位 | 下拉同上 | 即时 |
| `GenerateAiForNormalRightClick` | bool | `false` | 关闭时普通右键保持原版对话，按住热键点击才进 AI 对话 | Bool | 即时 |
| `TypedResponses` | string | `"With Generated"` | 玩家自由输入回复的时机，合法值 `Always`/`With Generated`/`Never` | 下拉（三值，显示名走 i18n） | 即时 |
| `InitiateTypedDialogueKey` | SButton | `LeftAlt` | 按住并点击 NPC 发起打字对话的键 | Keybind | 即时 |
| `EnableSveCompatibility` | bool | `true` | 使用 SVE 专属世界观/传记/对话样本（见 §4.4） | Bool | 即时（传记缓存自动按此开关重载） |
| `UseOptimizedPrompts` | bool | `false` | 世界摘要用精简版（GameSummaryOptimized） | Bool | 即时 |
| `EnableSemanticContextRouting` | bool | `true` | 主提示词前先跑一次语义路由决定各上下文模块详略（WP10） | Bool | 即时 |
| `SemanticContextRoutingTimeoutSeconds` | int | `8` | 语义路由超时秒数，读取与写入均钳制到 [2,30] | 数字（min 2, max 30, 步长 1） | 即时 |
| `RoutingThinkingLevel` | string | `"Off"` | 路由/分类小请求的思考档位；经 `LlmThinking.Normalize` 归一（默认 Off） | 下拉（`LlmThinking.Options`；Gemini 系提供商隐藏 XHigh） | 即时 |
| `ChatThinkingLevel` | string | `"Auto"` | 正式对话回复的思考档位（默认 Auto） | 下拉同上 | 即时 |
| `DisableCharacters` | string | `""` | 禁用 AI 对话的 NPC 名单，逗号/空格分隔；setter 同步解析成 Title Case 内部列表 | 文本框 | 即时 |
| `SuppressConnectionCheck` | bool | `false` | 跳过启动/换提供商时的连通性自检（WP11） | 无（仅 config.json） | 即时 |
| `EnableLivingNpcActionDecisionPass` | bool | `true` | 行为决策附加 pass 开关。**旧世界标了 JsonIgnore（两套序列化器），从不持久化，恒为默认值** | 无 | — |
| `LivingNpcActionDecisionTimeoutSeconds` | int | `12` | 上述 pass 超时（运行时钳到 [2, QueryTimeout]）。同样 JsonIgnore 不持久化 | 无 | — |

**旧字段名 → 新字段名映射**（供 WP14 迁移；原则：无充分理由不改名，
下面三项因与 LivingNPCs 现有 `ModConfig` 同名冲突必须处理）：

| 旧（ValleyTalk config.json） | 新（LivingNPCs config.json） | 说明 |
|---|---|---|
| `EnableMod` | `EnableDialogueEngine` | LivingNPCs 已有 `EnableMod`（全 mod 总开关）；引擎开关独立成新字段，语义不变 |
| `Debug` | `Debug`（合并） | 与 LivingNPCs 现有 `Debug` 合一；迁移取两者逻辑或 |
| `EnableSveCompatibility` | `EnableSveCompatibility`（合并） | 两侧语义一致（是否使用 SVE 内容），合一；迁移取逻辑与（任一侧关即关） |
| 其余全部字段 | 同名 | 原样迁移，含 `ApiKey`（敏感，见上） |
| `EnableLivingNpcActionDecisionPass`、`LivingNpcActionDecisionTimeoutSeconds` | 不迁移 | 旧世界从不持久化；新世界保持为非持久化内部常量（开放问题 §8.3） |

### 3.2 资产名契约

旧世界资产由 Content Patcher 内容包 `dandm1.CPValleyTalk`（文件夹
`[CP] ValleyTalk Base`，Format `2.3.0`）以 `EditData` + `Priority: Early` 填充，
主 mod 只在 `AssetRequested` 注册空默认。资产名全集：

| 旧资产名 | 新资产名（01 §4 前缀） | 内容 |
|---|---|---|
| `ValleyTalk/GameSummary` | `Mods/Yuki.LivingNPCs/GameSummary` | 世界观综述（完整版） |
| `ValleyTalk/GameSummaryOptimized` | `Mods/Yuki.LivingNPCs/GameSummaryOptimized` | 世界观综述（精简版，结构同上） |
| `ValleyTalk/Bios/<Name>` | `Mods/Yuki.LivingNPCs/Bios/<Name>` | 每 NPC 一份传记，`<Name>` 为 NPC 内部名 |
| `ValleyTalk/Prompts` | `Mods/Yuki.LivingNPCs/Prompts` | 提示词骨架表，`Dictionary<string,string>` |

新世界默认内容不再走 CP：主 mod 在 `AssetRequested` 里 `LoadFrom` 直接给出
**完整**默认数据（从 `assets/dialogue/` 读文件），第三方（未来的 SVE 增强包、
其他 NPC mod）仍可用 CP `EditData` 以上述新资产名覆盖/扩充——EditData 在加载
之后套用，与加载优先级无关，生态扩展点保留。旧 CP 包、
SVE 扩展包（`dandm1.ValleyTalkSVE`，文件夹 `ValleyTalk for SVE`）全部退役。

磁盘布局（01 §1）：`assets/dialogue/world/GameSummary.json`、
`world/GameSummaryOptimized.json`、`bios/<Name>.json`（原版 33 个）、
`bios-sve/<Name>.json`（SVE 集）、`prompts/default.json` + `prompts/zh.json`
（提示词骨架，键组织见 §3.5）。磁盘文件即资产的纯数据形式，
**不再带 CP 的 `Changes/Action/Target` 包裹层**。

### 3.3 GameSummary 资产形状（字段级）

反序列化目标是一个固定形状对象（旧名 `GameSummary`，新实现自定命名）：

- `SectionOrder`：`Dictionary<string,bool>`（**保持插入序**）。键为分节名，
  枚举固定为 `Intro`、`FarmerBackground`、`Seasons`、`Locations`、`Festivals`、
  `Villagers`、`Outro` 七节；字典的**键序决定输出顺序**；布尔值语义是
  "是否为该节输出 `### {节名} :` 标题行"（旧默认：Intro/FarmerBackground/Outro
  为 false，其余 true）。`SectionOrder` 里列出但对象上不存在的节名记错误日志跳过；
  缺 `SectionOrder` 整体报错并返回空摘要。
- 七个分节属性各为 `{ "Text": string, "Entries": {…} }`：
  - 通用条目（Intro/FarmerBackground/Festivals/Villagers/Outro）：
    `Entries` 值形如 `{ "id": string, "Name": string, "Description": string }`；
  - `Seasons`：条目在通用之上加 `"Crops": [string]`、`"Forage": [string]`；
  - `Locations`：条目在通用之上加 `"Region": string`（渲染时按 Region 分组）。
- 渲染格式（WP10 消费的最终字符串）：每节先标题（若开）、再 `Text`（非空白才输出）、
  再条目。通用条目 `- **{Name}** - {Description}`；季节条目为
  `- **{Name}** - {Description} ` 后接提示词键 `seasonCrops`/`seasonForage`
  的连接文案与顿号连接的列表（"A, B 和 C" 的连接词取键 `generalAnd`）；
  末尾若键 `gameSummaryTranslations` 非空则追加一行。
- 旧默认数据里 `Villagers.Entries` 键为 32 个原版村民名、`Locations.Entries`
  为 23 个地点、`Festivals.Entries` 为 8 个 `season+日` 键（如 `spring13`）、
  `Seasons.Entries` 为 `Spring/Summer/Fall/Winter`——键集合本身可复用，
  Description 文本全部由 WP20 重写。

### 3.4 Bios/<Name> 资产形状（字段级）

反序列化目标即 01 §2 的 `NpcBio`（旧名 `BioData`）。JSON 字段：

| 字段 | 类型 | 语义 |
|---|---|---|
| `Biography` | string | 传记正文（多段，`\n` 分段）。空白视为"无传记内容"触发回退（§4.3） |
| `Relationships` | `Dictionary<string, ListEntry>` | 人际关系；`ListEntry = { "id": string, "Heading": string, "Description": string }` |
| `Traits` | `Dictionary<string, ListEntry>` | 性格特质，同 ListEntry 形状 |
| `BiographyEnd` | string | 传记收尾段（拼在人际/特质之后） |
| `Gender` | string（可缺省） | 仅当值等于本地化的"男/女"词（提示词键 `generalMale`/`generalFemale` 的值，忽略大小写）才覆盖性别；否则忽略。正常情况下性别由游戏 `Gender` 数据按 NPC 名自动推导，资产字段只是覆盖口 |
| `Unique` | string | NPC 独有的附加肖像描述；非空时自动注册为 `ExtraPortraits["u"]` |
| `ExtraPortraits` | `Dictionary<string,string>` | 额外肖像帧：键为肖像代号（如 `"7"`、`"u"`），值为该表情的英文短描述。有效肖像集 = 固定 `{h,s,l,a}` ∪ 本字典键集 |
| `Preoccupations` | `List<string>` | 可选"近期心事"话题池（拼进提示词键 `preoccupation`；池中另混入该 NPC 最爱/最恨礼物名，见 §4.3） |
| `Dialogue` | `Dictionary<string,string>` | 补充对话样本：键为原版对话键（如 `Mon`），值为原版对话格式字符串（支持 `#` 分行、`^` 性别分支、`$表情` 命令、`[礼物ID列表]`——解析规则归 WP10 的 DialogueFile 对应物）；**覆盖**同键的游戏对话样本 |
| `HomeLocationBed` | bool（默认 false） | 标记 NPC 家中卧床位置可用于就寝语境。旧世界唯一消费点已被注释掉，纯保留字段 |
| `PromptOverrides` | `Dictionary<string,string>` | 按提示词元素名覆盖默认骨架（资产口；另有 interop 运行时口，重设计归 WP16） |
| `UsePatchedDialogue` | bool（默认 false） | true 时即使内容包未授权 AI（§4.5）也允许采用打过补丁的对话样本 |
| `Missing` | bool（内部） | 非资产字段：加载器标记"无传记"的哨兵，不出现在 JSON |

运行时派生（实现于 NpcBio 或其包装）：`IsMale`（三态）、性别代词组
（他/她、他/她宾格、他的/她的——取提示词键 `generalHe/She/Him/Her/His/Hers`）。
**注意**：旧实现从 SMAPI Translation 取这些词，但旧 mod 没有 i18n 文件夹，
实际取到 `(no translation:…)` 占位串——新实现必须改从提示词表（§3.5）取（§8.4）。

### 3.5 Prompts 资产形状与键组织

- 形状：扁平 `Dictionary<string,string>`。值内可含 `{{TokenName}}` 令牌
  （查找时用调用方传入的匿名对象属性替换）与游戏文本预处理语法
  （按**玩家**性别分支，交给 `Game1.content.PreprocessString` 处理）。
- 键组织（三层查找，见 §4.2）：基础键 + 可选 NPC 性别变体
  `<key>.MaleNpc` / `<key>.FemaleNpc`；个别键还有语境子变体
  （如 `spouseActionSpouseRoom.npcFemale`、`childrenPregnant.npcMale`）。
- 键的**清单与文本**归 WP20 创作；本文钉死键的家族前缀与用途，供 WP20 对表：
  `systemPrompt*`（系统提示词）、`gameContext`/`gameSummary*`（世界段引导）、
  `npcContext*`/`biography*`（NPC 段）、`core*`（核心指令/语境标题）、
  `general*`（通用词汇：性别词、代词、时段名、连接词 generalAnd）、
  `special*`/`spouse*`/`nonSpouse*`/`marriage*`（关系状态描述）、
  `gift*`（送礼语境）、`specialDates*`（特殊日期）、`recentEvents*`/`cc_*`/
  `won*`/`gameState*`（世界事件与进度）、`location*`（位置与日程）、
  `farm*`/`wealth*`/`children*`/`weather*`/`dateTime*`/`time*`（农场/家庭/时间）、
  `eventHistory*`/`sampleDialogue*`/`dialogueHistoryFormat`/`history*`
  （历史与样本格式）、`command*`/`instructions*`/`responseStart`/`output*`
  （输出指令）、`preoccupation`、`season*`、`trinkets*`、
  `ui*`（对话 UI 文案）、`config*`（GMCM 文案）、`modelCheck*`（连通自检文案）、
  `warning*`、`transcript*`（转录导出文案）、`log*`。
  旧表共 942 键（含性别变体）。
- 旧世界把 `ui*/config*/modelCheck*/transcript*` 等**玩家 UI 文案也塞在
  Prompts 资产里**（经 CP `{{i18n:}}` 间接到内容包 i18n）。新世界拆开：
  UI/GMCM/控制台文案走 SMAPI 标准 i18n（§4.8），Prompts 资产只留
  提示词骨架键。

### 3.6 与提示词的既有连接键

引擎代码按名字引用的资产键（WP20 不得漏做、实现方不得改名）最小集合即
§3.5 全部家族；其中被**本包代码**直接消费的：`generalAnd`、`seasonCrops`、
`seasonForage`、`gameSummaryTranslations`、`generalMale`、`generalFemale`、
`generalHe/She/Him/Her/His/Hers`。

## 4. 行为规范

### 4.1 资产提供与失效（新世界统一模型）

- `AssetRequested`：对 §3.2 四个资产名，用 `LoadFrom` 返回从 `assets/dialogue/`
  读出的默认数据（优先级取 SMAPI `AssetLoadPriority.Medium` 即可；旧世界用 High
  提供空默认是为了给 CP 让路，新世界主 mod 就是数据源）。Bios 按请求的
  `<Name>` 惰性读单个文件；文件不存在时返回空传记对象（触发 §4.3 回退链）。
- `AssetsInvalidated`：任一资产被失效（语言切换、CP 重载、`patch reload`）时
  丢弃对应内存缓存：Prompts 缓存字典、世界摘要缓存字符串（通知 WP10 的
  `InvalidateWorldSummaries` 对应物）、对应 NPC 的传记缓存。
- 事件只注册**一次**（静态/单例），不得每个 NPC/构建器实例各挂一份
  （旧版曾因此泄漏并漏失效缓存，修复后的行为要保留）。

### 4.2 提示词表加载（PromptCache 对应物）

- 首次访问或（游戏语言变化 / 玩家性别变化 / 缓存为空）时重建：
  `Game1.content.Load<Dictionary<string,string>>(Prompts 资产名)`，逐条
  过滤掉值以 `(no translation` 开头的条目，其余先过
  `Game1.content.PreprocessString`（展开玩家性别分支）再入缓存；
  预处理后为空白或仍以 `(no translation` 开头的丢弃。
- 加载抛异常：记两条错误日志并**运行时关闭对话引擎**（置引擎开关 false，
  不写回 config.json），返回空缓存。
- 查找规则（带 NPC 的重载）：① NPC 传记 `PromptOverrides[key]` 非空白则用之；
  ② 按 NPC 性别试 `<key>.MaleNpc` / `<key>.FemaleNpc`；③ 落到基础键。
  查不到时：`returnNull` 参数为 true 返回 null，否则**每键只警告一次**
  （日志说明该提示词节将为空——键名打错会静默吞掉整块上下文，这条防线必须保留）
  并返回 null/空。命中后做 `{{Token}}` 替换。
- 语言/性别驱动的缓存键：游戏语言用完整 locale（不是仅语言码），
  玩家性别取 `Game1.getPlayerOrEventFarmer()?.Gender`。

### 4.3 传记加载链（每 NPC）

1. **名字规整**：去掉尾部 `·`、`•`、`-`（多配偶类 mod 的克隆后缀）；
   再查 SVE 内部 ID 别名表把 `GuntherSilvian→Gunther`、`MarlonFay→Marlon`、
   `MorrisTod→Morris`、`HankSVE→Hank` 映射到传记注册名。
   传记资产名 = `…/Bios/{规整名}`。
2. **本地化加载**：`Game1.content.LoadLocalized<NpcBio>(资产名)`
   （内容管线自动尝试 locale 后缀变体，这是第三方本地化传记的挂载点）。
3. **回退链**：加载异常或 `Biography` 空白时，从游戏 `Data/Characters`
   生成轻量回退传记：Biography 为几句保守概括（年龄段/家乡/举止/社交/乐观度、
   是否可恋爱、亲友名单前 6 个），`BiographyEnd` 固定声明"本传记为本地生成的
   轻量基线，勿当完整设定"；`Traits` 填 manner/social/outlook 三条；
   `Relationships` 取 `FriendsAndFamily` 前 8 条；`Preoccupations` 取
   家乡/性情枚举词 + 亲友名。`Data/Characters` 里也没有该 NPC（或被 RSV
   策略屏蔽，§4.6）→ 构造 `Missing=true` 的空传记并警告"无传记文件"。
4. **加载后**：有效肖像集 = `{h,s,l,a}` ∪ `ExtraPortraits` 键；话题池 =
   `Preoccupations` ∪ 最爱/最恨礼物显示名（解析 `Game1.NPCGiftTastes` 的
   第 1、7 段，段数 <8 时记 Debug 日志并跳过）。
5. **缓存失效**：传记缓存记录"加载时的扩展兼容开关值"；开关翻转、资产失效、
   或缓存传记既无正文又非 Missing 时重载。

### 4.4 SVE 集选择与合并、expansionCompatibility 语义

- **SVE 检测**（新世界，01 §4）：`ModRegistry.IsLoaded("FlashShifter.StardewValleyExpandedCP")`。
  检测为真且 `EnableSveCompatibility` 开 → Bios 加载对 SVE 名单内的 NPC 改从
  `bios-sve/` 取；世界摘要在默认 GameSummary 之上**合并 SVE 增量**
  （旧 SVE 包的做法：向 `Locations.Entries` 增补 6 个 SVE 地区条目、向
  `Villagers.Entries` 增补约 24 个 SVE 村民条目，用 CP `TargetField` 精准打进
  子字典；新世界等价实现：主 mod 加载默认后把 `world-sve` 增量文件的条目
  并入对应 `Entries`，键冲突以 SVE 侧为准）。
- **每角色扩展兼容判定**（旧名 `IsExpansionCompatibilityEnabledForCharacter`）：
  规整名命中 SVE 兼容名单（`Alesia, Andy, Apples, Bear, Camilla, Charlie,
  CharlieChicken, Claire, Dusty, Gunther, GuntherSilvian, Isaac, Jadu, Jolyne,
  Lance, Magnus, Martin, Morgan, Morris, Olivia, Scarlett, Sophia, Susan, Victor,
  Gil, MarlonFay, MorrisTod, HankSVE, MrQi, Qi`）且开关为关 → 判 false；
  命中 RSV 屏蔽名单（§4.6）→ 判 false；其余 true。
- 判 false 的后果：即使传记资产存在也**强制使用 Data/Characters 轻量回退传记**
  （记 Debug 日志）；对话样本走"绕过补丁"的净版加载（§4.5）。
- **世界摘要与开关**：开关关时旧世界读"未被 SVE 包覆盖的基础版"——因为 CP
  覆盖无法运行时剥离，旧实现直接从磁盘硬编码路径
  `Mods/[CP] ValleyTalk Base/assets/{GameSummary|GameSummaryOptimized}.json`
  解析 `Changes[0].Entries`（失败记错误、给空摘要）。新世界主 mod 自己持有
  默认数据，这个磁盘 hack **废除**：开关关 = 不合并 SVE 增量，直接用默认集。
  四种摘要（完整/精简 × 含 SVE/纯净）各自缓存字符串，资产失效时全部作废。
- `UseOptimizedPrompts` 开 → 摘要一律用精简资产；语义路由（WP10）要求
  简版世界模块时也取精简资产。

### 4.5 对话样本净化与第三方内容授权

- 启动时扫描全部已装内容包：UniqueID 不在许可白名单（当前为空数组）且
  manifest 无 `PermitAiUse: true` 扩展字段（布尔或可解析字符串）→ 记三条警告
  （内容照常显示于游戏，但不进 AI 上下文）并置"存在未授权内容"全局标志。
- 该标志为真（或该角色扩展兼容判 false）且传记未设 `UsePatchedDialogue` 时，
  对话样本不用 NPC 当前对话，而是新建独立 `ContentManager` 从
  `Characters\Dialogue\{Name}` 与 `Characters\Dialogue\MarriageDialogue{Name}`
  加载净版（婚后键加 `M_` 前缀合并；按语言后缀序列逐个尝试，失败静默跳过）。
  否则直接用 `StardewNpc.Dialogue`。最后无条件把传记 `Dialogue` 字典**覆盖**
  同键条目。语言后缀序列 = 当前 locale 及其父级（`.zh-CN`→`.zh`→空），
  en-US 只有空后缀。
- 新世界 manifest 扩展字段 `PermitAiUse` 的约定原样保留（生态契约）。

### 4.6 RsvAiPolicy 挂载点（搬运件）

两个调用点：① 传记回退链入口——被屏蔽的 NPC 不生成回退传记（直接 Missing）；
② 扩展兼容判定——屏蔽名单内判 false。判定接口：按 NPC 对象（名字、显示名、
所在地图名前缀 `Custom_Ridgeside_`）或裸名字（先去尾部 `·•-`）。
名单与逻辑在搬运件内，本包不改写。

### 4.7 语言挂载点（Yuki 近期工作，保留行为）

- **游戏语言值**：来自 SMAPI `Translation.Locale` 解析成 `CultureInfo`，
  失败回退 `en-US`；提示词里嵌入的语言名用 `CultureInfo.EnglishName`。
- `ApplyTranslation` 开 → 系统提示词追加键 `systemPromptTranslation`
  （令牌 `{{Language}}` = 上述英文语言名）。
- **语义路由语言边界**（git 6953f90，逻辑在 WP10 的路由 pass，本包只供语言查询）：
  游戏语言为 `en`/`zh` 时话题漂移守卫覆盖，会话级路由缓存可用；其他语言
  （含自定义语言包）**跳过会话缓存、每轮重新路由**。语言码取
  `LocalizedContentManager.CurrentLanguageCode`（异常回退 en）。
- 中文控制台文案：控制台命令输出在中文 locale 下取 i18n 中文，否则用内置英文
  兜底（键族 `commandValleyTalkForget*`，迁移后归 SMAPI i18n，§4.8）。

### 4.8 i18n 键位规划

- 现状：内容包 `i18n/default.json` 与 `i18n/zh.json` 键集合完全一致（950 键）=
  Prompts 资产 942 键 + 8 个控制台键（`commandValleyTalkForgetNeedSave/
  NoNearby/ConfirmAll/AllDone/NpcNotFound/NpcNoHistory/NpcDone` 等）。
  **已知缺陷**：这 8 个键与 `locationBed.npcFemale` 在 Prompts.json 里没有
  `{{i18n:}}` 映射条目，实际不可达（中文控制台输出静默回退英文）——新世界修复。
- 新世界拆分：
  1. **提示词骨架键**（§3.5 除 `ui*/config*/modelCheck*/transcript*/warning*/
     log*/command*` 外的全部）→ `assets/dialogue/prompts/default.json` +
     `prompts/zh.json`（加载时 locale 版覆盖 default 逐键合并，作为 Prompts
     资产内容；en 文本 WP20 新写）。
  2. **玩家 UI / GMCM / 控制台 / 转录导出文案**（`ui*`、`config*`、
     `modelCheck*`、`transcript*`、`warning*`、`log*`、`command*` 家族）→
     合入 `LivingNPCs/i18n/default.json` 与 `zh.json`（LivingNPCs 现有约 750 行
     文案不动；引擎键为避免撞名加统一前缀 `dialogue.`，如
     `dialogue.configProvider`）。
  3. `zh.json` 的中文**值**整体搬运（Yuki 原创，03 §4），键名按上述规则重排；
     en 值由实现方按本文各键用途新写（不看旧 default.json）；
     fr 见开放问题 §8.5。
- 逐键用途不在此展开：`config*` 键与 §3.1 表逐行对应（每字段一个名键 +
  一个 Tooltip 键 + 枚举值显示键）；`modelCheck*` 为连通自检的分级结论；
  `ui*` 为打字框/思考中/清除历史确认；`transcript*` 为转录导出的时间与段落格式；
  `configNoModels`/`configModels`（令牌 `{{Provider}}`/`{{Models}}`）为模型列表段落。

### 4.9 GMCM 注册时序与降级

- 时机：`GameLoop.GameLaunched` 事件里注册（此时 GMCM 已完成初始化）。
  **即使引擎开关为关也要注册**（否则用户没法从菜单重新打开）。
- 获取 API：`ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu")`；
  为 null（未装 GMCM）→ 记一条 Warn（走 i18n，含英文兜底）后返回，
  配置系统退化为纯 config.json 手编模式，其余功能不受影响。
- 注册参数：`reset` = 引擎字段恢复默认（**不得重置 LivingNPCs 行为系统字段**，
  与现有菜单的整合方式见 §6.4）；`save` = 先按当前 Provider/ApiKey/ModelName/
  ServerAddress/PromptFormat 重建 LLM 客户端（WP11 入口），再 `WriteConfig`。
- 控件次序（引擎段）：引擎开关 →（DEBUG：Debug）→ Provider → [ApiKey] →
  [ModelName] → [ServerAddress] → QueryTimeout → TypedResponses →
  EnableSveCompatibility → UseOptimizedPrompts → EnableSemanticContextRouting →
  SemanticContextRoutingTimeoutSeconds → RoutingThinkingLevel → ChatThinkingLevel →
  GenerateAiForNormalRightClick → InitiateTypedDialogueKey → ApplyTranslation →
  GeneralFrequency → GiftFrequency → MarriageFrequency → DisableCharacters →
  模型列表段落（AddParagraph）。
- **按提供商动态字段**：方括号三项按所选提供商需要的连接参数显隐——
  apiKey/modelName：除 LlamaCpp（与 Dummy）外全部；url：LlamaCpp 与
  OpenAiCompatible。旧实现靠反射构造函数形参名，新实现改为 WP11 提供商元数据
  声明（§5）。
- **切换 Provider 的即时行为**：值变化时清空 `ApiKey`、立即 `WriteConfig`
  持久化、并触发菜单刷新——刷新必须**延迟到下一个 UpdateTicked**（在 GMCM 的
  setValue 回调里同步 Unregister 会崩），流程为 Unregister → 重新注册 →
  `OpenModMenu` 重开本 mod 菜单；用 `_isRefreshingMenu`/`_refreshQueued`
  双标志防重入，异常记 Warn。
- 打开菜单时归一化：`SemanticContextRoutingTimeoutSeconds` 钳 [2,30]，
  两个思考档位过 `LlmThinking.Normalize`（Routing 默认 Off、Chat 默认 Auto）。
  思考档位下拉的选项集：Provider 为 `Google`，或 `OpenAiCompatible` 且
  ModelName 判定为 Gemini 思考系模型（`LlmThinking.IsGeminiThinkingModel`）时，
  从 `LlmThinking.Options` 里剔除 XHigh。
- 频率下拉：内部值为字符串 "0"–"4"；显示 `{档位名} ({百分比})`；解析容错：
  整数钳 [0,4]，否则按本地化显示名匹配，再否则按不变式 `(0%)…(100%)` 子串匹配，
  全失败记 Warn 并保持原值。
- 模型列表段落：所选提供商支持列模型能力（WP11 `IGetModelNames` 对应物）时，
  用当前连接参数临时建客户端拉取列表，排序后逗号换行拼接；拿不到给
  `configNoModels` 提示（提示 ApiKey 可能未填）。

### 4.10 配置读取时序

`Mod.Entry` 内先挂 GameLaunched/SaveLoaded/ReturnedToTitle 事件，再
`ReadConfig`。引擎开关为关 → 不初始化引擎（不建 LLM 客户端、不打引擎补丁），
但 GMCM 注册照常。Provider 非法 → 记 Error 且引擎不启动。
启动即按配置建一次 LLM 客户端（WP11）。

## 5. 新类型与接口建议

命名空间 `LivingNPCs.Dialogue.Content`，建议拆分（实现方可微调，别合成巨类）：

- `DialogueContentService : IDialogueContent`——对 WP10 的唯一门面；
  内聚 §4.1–4.4 全部缓存与失效。
- `ContentAssetNames`（静态常量）——§3.2 四个新资产名与 `assets/dialogue/`
  相对路径，全项目唯一定义点。
- `NpcBio`、`WorldSummary`（含七节与条目类型）、`PromptTable`——资产反序列化
  模型；`ChildDescription`（`Name: string, IsMale: bool, Age: int`）——
  纯数据类型，由 WP16 从游戏 `Child`（名字/性别/Age 阶段值）转换、
  WP10 拼 `children*` 提示词时消费，归本包定义。
- `DialogueEngineConfig`——引擎字段（§3.1）挂进 LivingNPCs `ModConfig` 的方式：
  平铺进现有类（迁移最简单，推荐）；WP14 从旧 config.json 逐键搬。
- `DialogueConfigMenuSection`——向 LivingNPCs 现有 GMCM 注册流程追加引擎段；
  提供商元数据（每提供商需要哪些连接字段、是否支持列模型/思考档位）由 WP11
  的提供商注册表暴露，本包不再反射。
- `PromptKeyAudit`（测试侧）——扫描引擎源码里引用的提示词键与
  `prompts/default.json` 键集求差（键缺失只在运行时警告一次，容易漏；
  测试兜底，旧仓库 `tools/audit_prompt_literals.py` 思路可参考重写）。

## 6. 与其他工作包的接口

1. **实现 01 §2 `IDialogueContent`**：`GetBio(npcName)` 走 §4.3 全链
   （名字规整→本地化资产→回退→缓存）；`GetWorldSummary()` 返回按当前配置
   （SVE 合并 × 精简开关）选好的摘要（渲染成字符串的职责给 WP10 还是本包，
   以 WP10 文档签名为准，冲突时从 WP10）；`GetPromptSkeleton(key)` 即 §4.2
   查找（无 NPC 语境重载）。带 NPC 性别变体的查找需要 NPC 语境，接口只有
   string 入参——见开放问题 §8.1。
2. **WP20 交付格式**：内容作者产出 `assets/dialogue/` 下的纯数据 JSON
   （§3.2 布局、§3.3–3.5 形状，UTF-8 无 BOM，不带 CP 包裹层）。校验：
   本包提供最小校验（反序列化成功 + GameSummary 七节齐全 + SectionOrder 非空 +
   每 bio `Biography` 非空白），跑在单元测试里；提示词键覆盖率由
   `PromptKeyAudit` 保证。
3. **WP14**：旧 `Mods/ValleyTalk/config.json` → 新字段的映射表在 §3.1；
   `ApiKey` 迁移时不落日志。
4. **WP12/WP16**：频率档位、`TypedResponses`、热键、`DisableCharacters` 内部
   列表（Title Case 化）由补丁/UI 侧消费；本包保证 setter 同步解析行为不变。
   LivingNPCs 现有 GMCM 菜单（`LivingNPCs/ModConfigMenu.cs`）保持一次 Register，
   引擎段以 section title 或独立 page 并入，reset/save 回调合并处理。
5. **WP11**：GMCM save 回调调用的"重建客户端"入口、提供商元数据、
   `LlmThinking`、连通自检文案键（`modelCheck*`）。

## 7. 验收要点

- [ ] config.json 含 §3.1 全部持久化字段，默认值逐字一致；GMCM 界面完整可用，
      切 Provider 清 ApiKey 并动态刷新字段；无 GMCM 时仅 Warn 一条、功能正常。
- [ ] 四个 `Mods/Yuki.LivingNPCs/*` 资产可被第三方 CP `EditData` 覆盖生效
      （手工用一个测试 CP 包改 Abigail 传记验证）。
- [ ] 无 SVE 环境：33 个原版 NPC 传记加载成功；未知 NPC（自定义 mod）得到
      Data/Characters 回退传记；RSV 名单 NPC 得到 Missing 传记。
- [ ] 有 SVE：`GuntherSilvian` 等别名命中 SVE 传记；世界摘要含 SVE 地点/村民；
      关掉 `EnableSveCompatibility` 后 SVE 角色回退轻量传记、摘要不含 SVE 条目
      （无需重启）。
- [ ] 语言切换后提示词表与世界摘要缓存失效重建；zh locale 下 GMCM/控制台
      全中文（含旧世界不可达的 8 个 command 键）。
- [ ] 提示词键缺失只警告一次且有键名；`PromptKeyAudit` 测试通过。
- [ ] `EnableDialogueEngine=false` 时引擎不打补丁但 GMCM 可见可改。

## 8. 开放问题

1. `IDialogueContent.GetPromptSkeleton(string key)` 无 NPC 参数，无法表达
   `.MaleNpc/.FemaleNpc` 性别变体查找与 `PromptOverrides`。建议加一个
   `GetPromptSkeleton(string key, NpcBio npc)` 重载——改共享接口需用户裁决。
2. 三个配置字段合并（§3.1 映射表）中 `EnableSveCompatibility` 取"逻辑与"
   是否符合预期（两 mod 旧默认都为 true，正常用户无感），请用户确认。
3. `EnableLivingNpcActionDecisionPass` 两字段旧世界被 JsonIgnore、恒为默认：
   新世界转正为可配置项，还是维持内部常量？本文按"维持"编写。
4. 旧 `BioData` 从 SMAPI Translation 取性别词但旧 mod 无 i18n 文件夹，
   实际是占位串（潜在 bug，`Gender` 资产字段的覆盖口从未真正生效）。

### 裁决（2026-07-06，Yuki + 架构侧，全部落定）

1. `GetPromptSkeleton` 已由 01 §2 裁决：增加 `PromptVariant variant = default`
   参数（携带 NPC 性别与 optimized 开关），不再需要 NpcBio 重载。
2. 三字段合并方案**照 §3.1 执行**（Yuki 授权架构侧拍板）：`EnableMod`→新字段
   `EnableDialogueEngine`；`Debug` 合一取逻辑或；`EnableSveCompatibility` 合一
   取逻辑与。
3. `EnableLivingNpcActionDecisionPass` 两字段**维持内部常量**（按本文编写）。
4. 性别词修复按正文钉死的新行为实现（真 i18n 化，`Gender` 覆盖口生效）。
5. 增补（来自 WP11 裁决 6）：新装默认 `Provider = "OpenAiCompatible"`；
   （来自 WP14 裁决 5）：新增 `LegacyConfigImported` bool 字段，不进 GMCM；
   （来自 WP11 裁决 2）：熔断 i18n 键 `dialogue.breaker.auth`、`dialogue.breaker.rate`。
   新实现按 §3.4 改从提示词表取——行为变化已在文中钉死，请知悉。
5. 01 §1 列了 `i18n/fr.json`，但旧 fr 译文属上游文本衍生不可搬运；
   fr 只能从新 en/zh 重新翻译（可后补），首发是否必须带 fr 请用户裁决。
6. 传记本地化：旧世界靠 `LoadLocalized` + CP 包的 locale 变体，第三方译者
   生态几乎不存在；新世界是否支持 `bios/<Name>.zh.json` 之类磁盘 locale 变体，
   还是只依赖 CP 覆盖？本文按"仅 CP 覆盖 + LoadLocalized 挂载点保留"编写。

## 9. 审计索引（功能 → 旧代码位置，供说明书方复核，实现方勿读）

- 配置字段全表：ValleyTalk/src/config/ModConfig.cs:14-57；默认 config.json：ValleyTalk/src/config.json:1-26
- GMCM 注册/动态字段/刷新/模型列表：ValleyTalk/src/config/ModConfigMenu.cs:19-279, 428-515；频率解析 281-361；思考档位选项 374-410
- GMCM API 接口：ValleyTalk/src/config/IGenericModConfigMenuApi.cs（GMCM 官方公开接口）
- 注册时机与降级：ValleyTalk/src/ModEntry.cs:117-176, 347-350；ModConfigMenu.cs:27-31, 500-504
- 资产名常量：ValleyTalk/src/VtConstants.cs:5-11
- 提示词表加载/过滤/失效：ValleyTalk/src/PromptCache.cs:14-67
- 键查找三层规则与一次性警告：ValleyTalk/src/Util.cs:14-28, 74-144；控制台中文回退 146-163；语言后缀 JSON 探测 181-194
- 传记模型：ValleyTalk/src/models/BioData.cs:20-101（性别词占位串问题 20-31）；ChildDescription.cs:3-15（消费点 Generation/DialogueBuilder.cs:523-535、Prompts.cs:1248-1266）
- 传记加载链/回退/SVE 别名/兼容判定：ValleyTalk/src/Character.cs:25-40, 68-121, 250-329, 331-420
- 对话样本净化与 PermitAiUse：ValleyTalk/src/Character.cs:161-248；ModEntry.cs:309-345；白名单 src/enums/SldConstants.cs:12-13
- 世界摘要结构/渲染/磁盘 hack：ValleyTalk/src/GameSummaryBuilder.cs:15-237；四缓存与选择 src/Prompts.cs:25-41, 197-218
- 语言挂载点：ValleyTalk/src/ModEntry.cs:43-109；Prompts.cs:172-181；路由语言边界 src/Generation/ContextRoutingDecisionPass.cs:241-248, 363-378
- RsvAiPolicy：ValleyTalk/src/RsvAiPolicy.cs:9-42
- 旧 CP 包结构：ValleyTalk/ContentPack/content.json、manifest.json；SVE 包：ValleyTalk/Extensions/ValleyTalk for SVE/content.json（TargetField 用法）、manifest.json
- 资产 JSON 实样（形状核对）：ContentPack/assets/GameSummary.json、Prompts.json（942 键、{{i18n:}} 间接）、assets/bio/Abigail.json
- i18n 键集：ContentPack/i18n/default.json、zh.json（各 950 键；8 个 command 键 + locationBed.npcFemale 无 Prompts 映射）
- LivingNPCs 侧撞名字段：LivingNPCs/ModConfig.cs:11-12, 54
- 上游译文（不可搬）：ValleyTalk/translations/fr-FR/assets/bio/*.txt、translations/zh-CN/i18n/zh-CN.json
