# 20 · 提示词清单与创作指南（WP20）

> 读者：负责**重新创作**全部上游提示词与角色资料文本的 AI（"创作方"）。
> 创作方**不得阅读** `ValleyTalk/`、`ValleyTalk.Tests/`、`upstream-ValleyTalk/` 下任何文件
> （洁净室纪律见 00 §2）。本文档给出功能等价所需的全部信息：结构、键名、字段名、
> 输入变量按原样精确记录（格式与标识符不受版权保护）；"这段文本要传达什么"
> 全部为撰写方的中文概括，**不含一句上游原文**。
> 创作方可自由查阅：Stardew Valley Wiki（中英）、SVE Wiki、本说明书包、`LivingNPCs/` 源码。

## 0. 旧系统全景（功能级，供理解各件的位置）

一次对话生成的最终请求由五段拼成，按顺序发给模型（分段是为了各家 Prompt Caching 的
缓存边界，前三段跨轮稳定、后两段每轮变化）：

1. **System**：系统提示（角色扮演总纲 + 可选的目标语言声明）。
2. **GameConstantContext**：游戏背景导语 + 世界观综述（GameSummary 渲染结果）。
3. **NpcConstantContext**：NPC 介绍导语 + 传记正文 + 人际关系列表 + 性格特质列表 + 传记收尾。
4. **CorePrompt + Instructions + Command**：当轮情境（约 25 个可路由小节）+ 输出格式教学 + 本轮任务指令。
5. **ResponseStart**：预填的助手回复开头（引导模型直接进入台词）。

文案来源分三层：

- **提示词骨架**：约 950 个 i18n 键（英文在 `ContentPack/i18n/default.json`，中文在
  `ContentPack/i18n/zh.json`；`ContentPack/assets/Prompts.json` 只是把每个键重定向到
  i18n 的 CP 资产壳）。其中约 560 键是发给模型的文本，其余是 GMCM/控制台/导出等
  UI 文案（归 WP15，不在本包）。
- **内容资产**：`GameSummary*.json`（世界观）、`bio/*.json`（传记）、SVE 扩展集。
- **代码内嵌**：Yuki 原创的生成器/决策器提示词与 `PromptFragments.cs`——**保留，勿重写**（见 §2）。

骨架键的运行时机制（新引擎 WP10/WP15 会复刻同等能力，创作方只需知道要交什么）：

- 变量占位符 `{{TokenName}}`（各键可用变量在 §1.3 表中列出）。
- **NPC 性别变体**：任一键可配 `<key>.MaleNpc` / `<key>.FemaleNpc` 变体，按说话 NPC
  性别优先选用，缺失则回退无后缀键。旧库约 280 对变体，多数与主键同文，仅人称代词不同。
- **玩家性别分支**：文本内可用游戏原生语法 `${男性文案^女性文案}$` 按农夫性别二选一。
- **精简变体**：教学类键可配 `<key>Optimized` 变体；开启"优化提示词"配置时优先用之，
  缺失回退完整版（绝不能因缺失而整节消失）。
- **按角色覆盖**：传记 JSON 的 `PromptOverrides` 字典可按键名覆盖任一骨架键；
  第三方 mod 也可经 interop 按节名（如 `ThirdPartyContext`、`ReplaceSchedule`）注入/覆盖。
- **语义路由**：每轮由路由决策（保留件）给出各上下文模块 `none/brief/full` 三档，
  CorePrompt 各小节据此裁剪。模块枚举：`World, NpcProfile, GameState, SampleDialogue,
  EventHistory, DateTime, Weather, NearbyNpcs, Relationship, Farm, Location, Trinkets,
  RecentEvents, SpecialDates, Gift, LivingNpc, SpouseAction, Preoccupation, CurrentConversation`。

---

# 第一部分 · 重创作清单（上游文本，全部重写）

## 1.1 世界观综述（GameSummary）

**旧位置**：`ContentPack/assets/GameSummary.json`（完整版，正文约 18K 字符）与
`GameSummaryOptimized.json`（精简版，约 12K 字符）。新交付位置按 01 §1：
`LivingNPCs/assets/dialogue/world/`（容器细节以 WP15 为准，分节数据模型如下，字段名沿用）。

**数据模型**（渲染器按此拼 Markdown）：

- 顶层 `SectionOrder`：`Dictionary<string,bool>`，**键的顺序 = 分节输出顺序**；
  布尔值 = 是否在该节前打印 `### 节名 :` 标题（正文始终输出）。两版现值均为：
  Intro=false, FarmerBackground=false, Seasons=true, Locations=true, Festivals=true,
  Villagers=true, Outro=false。缺某节属性时记错误日志并跳过该节。
- 每节 = `{ "Text": 引导句, "Entries": { id → 条目 } }`。
  - 普通条目：`{ id, Name, Description }`，渲染为 `- **Name** - Description`。
  - `Seasons` 条目扩展 `Crops: string[]`、`Forage: string[]`，渲染时在描述后接
    "作物有：…"、"可采集：…"（引子用骨架键 `seasonCrops` / `seasonForage`）。
  - `Locations` 条目扩展 `Region` 字段，渲染按 Region 分组。

**各分节要传达的内容与建议长度**（素材：Stardew Valley Wiki）：

| 节 | 用途与必须涵盖的信息点 | 建议长度 | Wiki 页面 |
|---|---|---|---|
| Intro | 一句话定调：这是星露谷、鹈鹕镇的乡村生活世界，供模型建立世界观框架 | 完整 100–200 字符 / 精简 80–140 | 首页、"Stardew Valley" 概述 |
| FarmerBackground | 玩家角色设定：从城市辞职继承爷爷的农场、务农+探矿+钓鱼+社交的多面生活、与镇民建立关系是核心玩法 | 完整 500–800 / 精简 300–500 | Getting Started、The Player |
| Seasons | 四季各一条：气候感受、该季节标志性事件；`Crops`/`Forage` 列表填该季主要作物与采集物（英文物品名与游戏一致） | 每季描述 60–120 字符；列表各 4–8 项 | Crops、Foraging、四季页 |
| Locations | 23 个条目（键名照抄）：`TheFarm, PelicanTown_SeedShop, PelicanTown_JojaMart, PelicanTown_StardropSaloon, PelicanTown_HarveysClinic, PelicanTown_Blacksmith, PelicanTown_LibraryMuseum, PelicanTown_CommunityCenter, CindersapForest, CindersapForest_MarniesRanch, CindersapForest_WizardsTower, Mountain, Mountain_AdventurersGuild, Mountain_TheMines, Mountain_Spa, Mountain_TrainStation, Mountain_Quarry, Beach_FishShop, GingerIsland, Beach_TidePools, Desert, Desert_Oasis, Desert_SkullCavern`；每条写清这是什么地方、谁在此经营/出没、NPC 谈及它时的常识 | 每条 60–150 字符；Region 取所在大区名 | Pelican Town 及各地点页 |
| Festivals | 8 个条目，键=日期：`spring13`(复活节彩蛋节), `spring24`(花舞节), `summer11`(夏威夷宴会), `summer28`(月光水母节), `fall16`(星露谷展览会), `fall27`(万灵节), `winter8`(冰雪节), `winter25`(冬星盛宴)；Name 用节日官方英文名，描述写活动内容与镇民心态 | 每条 60–120 字符 | Festivals 总页 |
| Villagers | 32 个条目（= §1.2 名单去掉 Dwarf——矮人属于隐秘角色，旧版有意不进公共综述），每条一两句：身份、住处、显著性格标签，供"谈论他人"时使用 | 每条 60–150 字符 | 各 NPC 页首段 |
| Outro | 收尾提醒：以上是背景常识，NPC 只应自然引用与自己生活相关的部分 | 100–200 字符 | — |

**完整版 vs 精简版**：同一结构两份文件；精简版压缩 Text 与 Description（约 65% 篇幅），
Locations 可减至 17 条（砍掉次要子地点），列表类字段可精简。两版都要交。

**SVE 附加集**（检测到 SVE 时合并，见 01 §4）：
- 追加 Villagers 条目 23 个：`Alesia, Andy, Apples, Camilla, Claire, Gunther, Hank,
  Henchman, Isaac, Jadu, Jolyne, Lance, Marlon, Martin, Morgan, Morris, Olivia, Peaches,
  Scarlett, Sophia, Susan, Treyvon, Victor`（每条 100–200 字符；素材：SVE Wiki 各角色页）。
- 追加/覆盖 Locations 条目 6 个：`BlueMoonVineyard, CindersapForest`(覆盖为含 SVE 内容的版本),
  `FairhavenFarm, AuroraVineyard, Grampleton, Highlands`。
- 同样交完整+精简两版。旧位置：`Extensions/ValleyTalk for SVE/assets/GameSummary*.json`、
  `Locations*.json`（经 CP `TargetField` 打进对应节的 Entries；新架构由 WP15 决定合并方式）。

## 1.2 NPC 传记（33 原版 + SVE 集）

**旧位置**：`ContentPack/assets/bio/<Name>.json`（每 NPC 一个 CP 补丁文件，目标资产
`ValleyTalk/Bios/<Name>`）；SVE 在 `Extensions/ValleyTalk for SVE/assets/bio/`。
新交付：`assets/dialogue/bios/<NPCName>.json` 与 `bios-sve/`（容器按 WP15）。

**原版 33 人名单**（文件名=内部名）：
Abigail, Alex, Caroline, Clint, Demetrius, Dwarf, Elliott, Emily, Evelyn, George, Gus,
Haley, Harvey, Jas, Jodi, Kent, Krobus, Leah, Lewis, Linus, Marnie, Maru, Pam, Penny,
Pierre, Robin, Sam, Sandy, Sebastian, Shane, Vincent, Willy, Wizard。

**SVE 名单**（23 个传记文件）：
Alesia, Andy, Apples, Camilla, Claire, Gunther, Hank, Henchman, Isaac, Jadu, Jolyne,
Lance, Marlon, Martin, Morgan, Morris, Olivia, Peaches, Scarlett, Sophia, Susan,
Treyvon, Victor。
另外，多数 SVE 传记文件还附带**对原版 NPC 关系表的追加补丁**（把新角色写进老角色的
`Relationships`），旧世界的映射为：Alesia→Marlon；Andy→Lewis,Pierre；Claire→Shane；
Gunther→Penny,Willy,Robin；Marlon→Wizard,Marnie,Krobus；Morris→Lewis,Pierre,Shane；
Olivia→Caroline,Jodi,Pam,Leah；Sophia→Emily,Haley；Susan→Lewis,Marnie。
新版需重写这些追加关系条目（每条一句话，说明两人如何认识/关系性质）。

**传记 JSON 字段结构**（字段名精确保留，对齐 WP15 的 `NpcBio`）：

| 字段 | 类型 | 作用 |
|---|---|---|
| `Biography` | string | 传记正文（多段，`\n` 分段；渲染时连续空行会被折叠） |
| `Relationships` | `{ 键 → {id, Heading, Description} }` | 人际关系列表；Heading=对方名/称谓，Description=关系一句话 |
| `Traits` | `{ 键 → {id, Heading, Description} }` | 性格特质列表；Heading=特质词，Description=一句展开。精简路由档只取前 4 条，**故最重要的特质排前面** |
| `BiographyEnd` | string | 收尾段：概括该 NPC 的说话风格/语气要点（紧跟在关系与特质之后输出） |
| `Unique` | string | 该 NPC 独有立绘（索引 `u`）的表情描述短语（如"显得暴躁"这类）；会自动并入 ExtraPortraits |
| `ExtraPortraits` | `{ 立绘索引 → 表情描述 }` | 超出通用集($h/$s/$0/$l/$a)的额外表情立绘；索引是数字或字母；特殊键 `"!"` 表示"此 NPC 禁用表情指令教学" |
| `Preoccupations` | `string[]` | 3 个左右"近日挂心的话题"（地点、爱好、喜好物品等名词短语）；无对话历史时按日随机选一个注入 |
| `Dialogue` | `{ 日程键 → 台词 }` | 可选：补充进采样池的手写风格台词（旧库仅 Abigail 有一条 Mon 键）；一般留空对象 |
| `HomeLocationBed` | bool | 是否住集体/店铺建筑内有私人床位（影响"在家"措辞的功能开关，照实填） |
| `UsePatchedDialogue` | bool | 是否允许其它 mod 改写过的原版台词进入采样池（SVE 角色多为 true，原版角色不写=false） |
| `PromptOverrides` | `{ 骨架键 → 覆盖文本 }` | 按角色覆盖骨架键。已知须保留的实例：**Jas 与 Shane** 各覆盖 4 键：`nonSpouseFriendshipFirstConversation`、`nonSpouseFreindshipStrangers`、`nonSpouseFriendshipAcquaintances`、`instructionsBreaks`（Jas 是幼儿需监护人式距离感、Shane 初期冷淡拒人，低好感措辞需角色特化） |

**每份传记必须覆盖的内容维度**（写进 Biography/Traits/Relationships/BiographyEnd）：

1. 基本身份：年龄段、职业/生计、住所、日常活动圈。
2. 性格核心与内在矛盾（Traits 4–6 条，最重要的在前）。
3. 背景故事：家庭、过去经历、当前处境与烦恼。
4. 人际关系（Relationships 3–6 条）：家人必写；密友/雇主/恋敌等按 Wiki 事实。
5. 日程习惯：一周节奏、常去地点、爱好（供模型自然谈及"今天要去哪"）。
6. 喜恶速写：最爱/最恨的礼物类型倾向（不必列全清单，写成性格化偏好）。
7. **对玩家态度随好感的演变**：低好感如何待人、熟络后展露什么、（可恋爱角色）
   恋爱/婚后的相处样貌——写成"随关系加深的性格展开"，不写数值。
8. 说话风格（BiographyEnd）：语速、口头禅倾向、幽默/严肃、用词档次。

**剧透政策**：心事件剧情写到"性格揭示"层面——可写事件揭示的**性格事实与人物弧光**
（如某人酗酒抑郁并逐步好转、某人与家人矛盾的根源），不逐场复述心事件情节、不写
一次性桥段台词，不写玩家选择分支的结局。社区中心/Joja 等世界线进度**不进传记**
（由运行时上下文提供）。神秘角色（Wizard、Krobus、Dwarf、Sandy）保留其自我认知内的
秘密：只写本人知道且可能谈及的内容。

**长度指引**：Biography 正文 800–2500 字符（英文计），关键主角（可恋爱 12 人 + 剧情
浓角色如 Shane/Kent/Linus）取上限，功能性配角（Gus、Sandy 等）可取下限。旧库中位数
约 1600 字符、最长约 4500——新版控制在 2500 内，把细节让给 Traits/Relationships 结构化字段。

**双语交付**：每份传记交**英文版 + 简体中文版**两份全量文件（中文版所有 Description/
Biography 均为中文，物品/地名用游戏官方中文译名）。目录组织按 WP15
（预期为 `bios/` 与本地化后缀或平行目录；创作方按键值内容负责，装配归 WP15）。
素材来源：中英 Wiki 各 NPC 页（性格段、日程表、心事件、礼物喜好表）。

## 1.3 提示词骨架（逐节登记）

以下按最终提示词中的出现顺序登记全部需要重写的骨架键。
"变量"列是文本内可用的 `{{Token}}`。**每键的旧英文原文一律不看不引**；
创作方按"要传达什么"自拟新文案（更凝练、更少歧义即为质量改进）。
除注明外每键 1–3 句。带 ♦ 的键需要 `.MaleNpc/.FemaleNpc` 变体
（凡文本含 NPC 第三人称代词的键都应配对写）。

### A. System 段
| 键 | 变量 | 要传达什么 |
|---|---|---|
| `systemPrompt` | — | 设定身份：资深游戏对话作家，擅长完全贴合角色情境与个性写台词 |
| `systemPromptTranslation` | Language | 目标语言声明：指令是英文但一切可见台词与选项只能用 {{Language}} 写，不得混语种（仅开启翻译模式时附加） |

### B. GameConstantContext 段
| 键 | 变量 | 要传达什么 |
|---|---|---|
| `gameContext` | — | 任务定调：为星露谷玩家生成增强体验的对话；面向成年玩家，可在合适时增加深度与多样性，但须忠于角色 |
| `gameSummaryHeading` | — | 世界观小节标题（渲染为 `##标题`） |
| `gameSummaryTranslations` | — | 世界观后的附注钩子，默认留空字符串（保留键位） |

### C. NpcConstantContext 段
| 键 | 变量 | 要传达什么 |
|---|---|---|
| `npcContextIntro` | Name | 引出本段：以下是 {{Name}} 的资料，台词要符合此人 |
| `npcContextBiographyHeading` | Name | 传记小节标题 |
| `biographyRelationships` | — | 关系列表标题词 |
| `biographyPersonality` | — | 性格列表标题词 |

### D. CorePrompt 段（小节名 = 语义路由记账名，顺序如下）

**GameState**（`gameStateHeading` + 是/否成对键）：世界进度事实句——
`gameStateCommunityCenterYes/No`、`gameStateBusYes/No`、`gameStateQuarryBridgeYes/No`、
`gameStateMinecartYes/No`、`gameStateBoulderYes/No`（社区中心修复、沙漠巴士、采石场桥、
矿车、山间巨石）与 `gameStateKentYes/No`（Kent 是否已从战场回镇，按年份）。
每句陈述现状 + 镇民对此的普遍感受方向。

**SampleDialogue**：`sampleDialogueHeading`(Name)、`sampleDialogueIntro`(Name)——
引出原版台词采样列表：这是该角色在当前好感级别的官方语气样本，用于模仿风格。

**EventHistory**：`eventHistoryHeading`、`eventHistoryIntro`(Name)、`eventHistorySubheading`——
引出历史事件/对话列表并教引用规则：刚发生的必须回应；一两天内的宜提及；更早的仅在
相关或无话可说时引用；除非符合人设否则不要复读旧台词。这节 intro 较长（400–700 字符）。
配套**历史行格式键**（列表内每行的模板）：`dialogueHistoryFormat`(npcName,totalDialogue)、
`historyConversationFormat`(builder)、`historyOverheardFormat`(name,totalDialogue)、
`historyThirdPartyFormat`(Name,npcName,festivalNameString,totalDialogue)、
`historyDialogueFormat`(npcName,allListeners,festivalNameString,totalDialogue)、
`historyThirdPartyFestival`(festivalName)——分别描述"与农夫的对话/被偷听的台词/
本 NPC 旁观他人对话/节日场合台词"如何一行化；以及**成就短句**（进历史列表的一次性
事件描述）：`cc_Bus_Repaired`♦、`cc_Boulder_Removed`♦、`cc_Bridge`♦、`cc_Complete`♦、
`cc_Greenhouse`♦、`cc_Minecart`♦、`wonIceFishing`、`wonGrange`、`wonEggHunt`
（农夫修复了某设施/在某比赛夺冠的事实句）。

**CoreHeader**（不可路由，恒出现）：`coreInstructionHeading`（指令大标题）、
`coreContextHeading`（情境小标题）、`coreFarmerGender`——用 `${…^…}$` 语法声明农夫
性别及其兴趣气质随性别的倾向。

**DateAndTime**：`dateTimeDayOfSeason`(DayOfSeason,Season)、`dateTimeTimeOfDay`(TimeOfDay)、
`dateTimeEarlyMorningNormal`（清晨对镇民是正常时段，别当成稀奇事）、
`dateTimeNewThisYear`（农夫今年才搬来）、`dateTimeResidencyToday`（今天是农夫抵达首日）、
`dateTimeResidencyProgress`(ElapsedDays,CompletedSeasons)（农夫已住了多久，供"认识多久"措辞）。

**Weather**：`weatherLightning` / `weatherGreenRain` / `weatherSnow` / `weatherRain`——
当前天气一句（雷暴/绿雨/雪/雨），绿雨句要点出这是罕见怪象。

**OtherNpcs**：`openNpcsHeading`、`otherNpcsIntro`(Name)、`otherNpcsOutro`——附近还有哪些
NPC 在场的列表引子与收尾（对话可意识到旁人在场、可自然提及，但主对象仍是农夫）。

**婚姻/室友/子女**（已婚或室友时出现）：`coreRoommates`♦(Name)（Krobus 型室友关系说明）、
`coreMarried`(Name,Pronoun)（与农夫已婚、住在农舍）、`coreMarriedSince`(Name,RelativeDate)、
`childrenNone/Single/Multiple`(Name,count)、`childrenDescriptionBoy/Girl`♦(Name,Age)
（Age 为阶段数字：婴儿/爬行/学步等，文本按年龄段描述）、
`childrenPregnant.npcMale`/`.npcFemale`(Name,daysUntilBirth)——**两个变体必须都写**：
npcMale=农夫怀孕/领养倒计时、npcFemale=NPC 配偶是怀有身孕的一方。
`marriageSentimentGood/Neutral/Bad`(Name,marriageOrRoommate)（按心数>12/10–12/<10 的婚姻
满意度基调）、`generalTheMarriage`、`generalBeingRoommates`（满意度句里的填充词）。

**Spouse 网络**（多配偶/未婚状态感知）：`spousesMarriedToOne/ToMany/ToOthers`、
`spouseRoommateWithOne/WithMany/RoommatesWithOthers`、`spousesNOtherPeople`(nSpouses)、
`spousesAllTheOthers`、`spouseRoommatesAllTheOthers`、`spouseEngaged`(engagedTo,weddingDays)、
`spousePoly`(Name)、`spousePolyView`(Name)（变量见 §1.3 表内括号）——向模型说明农夫
当前婚姻/订婚全景：正与本人对话的配偶如何看待其他配偶（开放式婚姻的自洽态度）、
本 NPC 未婚时如何知晓农夫已婚/订婚。此组共 11 键，是最容易写拧的一组，
必须两两区分"对话对象是配偶之一"与"对话对象是旁人"两个视角。

**Farm**（已婚时出现）：`farmBuildingsIntro/None`、`farmBuildingsRuinedGreenhouse/
RepairedGreenhouse`、`farmBuildingsConstruction`(buildingType,daysOfConstructionLeft)、
`farmAnimalsIntro/None`、`farmCropsIntro/None`、`farmCropsReadyForHarvest`(ripe)、
`farmCropsNotReady`、`farmContentsPet`(petType,Name)、`farmContentsNoPets`——农场资产
清单的各段引子/空态句（配偶对自家农场了如指掌的口吻）。
**Wealth**：`wealthPoor/Middle/Rich/VeryRich`(wealth,Name)——家底档位与配偶对家庭财务的感受基调。

**Location**（最大的一族，91 键）：
- 特定地点句（NPC 此刻在哪，含该地点的行为暗示）：`locationAtHome`(Name,inShopString)、
  `locationAtHomeOrShop`、`locationTown/Beach/Desert/BusStop/Railroad/Saloon/Pierres/
  JojaMart/FarmHouse/Farm`(Name)、度假村系列 `locationResortChair/Towel/Umbrella/Bar/
  Entering/Leaving/Shore/Wander/Resort`(Name)、`locationSaloonDrunk`（成人在酒馆可能微醺
  的附注——**儿童与 Emily 不适用**，新文案保持"可拼接的短语"形态）、
  `locationGeneric`(Name,Location)、`locationOutro`（地点句收尾：对话应扎根在此场景）。
- 行程句：`locationTravelling`(Name,destination)、`locationCurrentlyStationary`(Name)、
  `locationFuturePlans`(Name,Locations)、`locationNextScheduleSoon`(Name,Minutes,Destination)
  （30 分钟内要动身，台词可自然带出要走了）、`locationScheduleWindow`(Name,Minutes,
  Destination)（还有一段时间才走，别急着告辞）、`locationNoUpcomingSchedule`(Name)。
- 当前状态小节（精简路由档也输出）：`locationCurrentStateHeading`、
  `locationCurrentStatePlace`(Name,Location,TileX,TileY)、`locationCurrentStateActivity`(Activity)、
  `locationCurrentScheduleStop`(Name,Time,Location)、`locationCurrentStateGrounding`(Name)
  （**重要**：台词必须与此时此地此活动一致，不得凭空描述别处场景——约 300 字符的扎根指令）。
- `locationBed`(Name)♦ 保留键位（就寝场景，现版停用）。`timeInTheFuture`（地点未知时的占位词）。

**Trinkets**：`trinketsFairyBox`(Name)、`trinketsCompanionFrog`(Name)、
`trinketsCompanionParrot`(Name)——农夫携带的奇特饰品/同伴生物，NPC 可能好奇注意到。

**RecentEvents**：`recentEventsHeading`、`recentEventsIntro`（引子：以下是最近 7 天内
发生的镇上大事）+ 事件句族 `recentEventsBoulder/QuarryBridge/Bus/Greenhouse/Minecarts/
CommunityCenter/MovieTheatre/PamHouse/PamHouseAnonymous/JojaLightning/BabyBoy/BabyGirl/
Married/LuauBest/LuauShorts/LuauPoisoned/MovieInvited(Name)/DumpsterDive(Name)/GreenRain`
——每句陈述一件近事（设施修复/影院开业/Pam 新居（署名与匿名两种）/Joja 遭雷击/
农夫添丁/婚礼/宴会汤的三种结局/被邀看电影/翻垃圾桶被撞见/绿雨结束），
写明镇民视角的谈资口吻。

**ThirdPartyContext**：无默认文案（纯 interop 注入口），登记即可。

**SpecialDatesAndBirthday**：固定日期句 `specialDatesSpring1/Spring12/Spring23/Summer1/
Summer10/Summer27/Summer28/Fall1/Fall15/Fall26/WInter1/Winter7/Winter24/Winter28`
（**注意键名 `WInter1` 的大小写异常按原样保留**，除非 WP15 决定改键并记录映射）——
各季首日=换季感想；12/23/10/15/26/7/24 各是次日节日的前夕预告（彩蛋节/花舞节/宴会/
展览会/万灵节/冰雪节/冬星）；Summer27=后天月光水母节、Summer28=今晚月光水母节；
Winter28=年末感想。`specialDatesBirthday`(Name)：今天是本 NPC 生日。

**Gift**（收礼当轮出现）：`giftIntro`(Name,giftName)、口味五档 `giftLoved/giftLiked/
giftNeutral/giftDislike/giftHate`(Name)（对应游戏口味常量 0/2/8-其他/4/6 档——注意代码
按 0,2,4,6,默认 分支）、`giftMustIncludeReaction`(Name)（回应必须包含对礼物的反应）、
`giftBirthday`(Name)（生日收礼加倍感动，约 240 字符）、`giftOutro`（反应要符合人设与
好感）、`giftGiving`(Name,GiftName)（反向：配偶 NPC 今天主动送农夫东西，台词要自然
交出礼物）、求助物资代收两键 `giftHelpRequestIntro`(Name,giftName)、
`giftHelpRequestReaction`(Name)（这是任务物资不是日常礼物，按感谢完成委托来回应，
禁用口味评价——由 LivingNPCs 上下文含 `## LivingNPCs Help Request Gift Response`
标题时触发，见 §3.2 交叉约束）。

**LivingNpcExtraPrompt / SpouseAction**：前者无文案（注入 LivingNPCs 隐藏上下文原文，
精简档经压缩器摘要——压缩器是保留件）；后者 `spouseActionFunLeave/JobLeave/Patio/
FunReturn/JobReturn/SpouseRoom`(Name)（配偶今天外出玩耍/上班/在院子里/刚回来/在自己
房间等六种日程动作说明，其中 SpouseRoom 备 `.npcFemale` 变体）。

**NonSpouseFriendshipLevel**（未婚时的好感阶梯，全部 ♦，均带 Name）：
`nonSpouseFriendshipFirstConversation`（心值 -1：素未谋面，陌生礼貌、无既往）、
`nonSpouseFreindshipStrangers`（**键名拼写错误按原样保留**，<2 心：点头之交）、
`nonSpouseFriendshipAcquaintances`（<4：认识但不深）、`nonSpouseFriendshipFriends`
（<6：朋友，可闲话家常）、`nonSpouseFriendshipCloseFriends`（<8：密友，可谈心事）、
`nonSpouseFriendshipWantToDate`（8–10 可恋爱对象：有暧昧张力，期待表白）、
`nonSpouseFriendshipIntimate`（10–14 后备档）、`nonSpouseFriendshipNonSingleAdult8`
（不可恋爱成人 6–8 心：挚友但无浪漫）、`nonSpouseFriendshipNonSingleAdult10`（>8：
情同家人仍无浪漫）、`nonSpouseFriendshipChild8Plus`（儿童高好感：亲近的大朋友，
绝无浪漫元素）。每档 120–280 字符，写清语气、话题深度、称呼变化。

**SpecialRelationshipStatus**：`specialRelationshipDating`(Name,relationshipPublic,
relationshipWord)、`specialRelationshipDatingPublic`/`DatingDiscrete`（恋情公开/低调两种
填充词）、`specialRelationshipEngaged`(Name,daysToWedding)、`specialRelationshipDivorced`(Name)♦
（离异后的冷淡戒备）、`specialRelationshipProposalRejected`(Name)♦（求婚被拒的尴尬）、
恋称词 `generalHeterosexual`、`generalGayMale`、`generalLesbian`（按双方性别选用的
恋人关系词）。

**coreGenderReferences**：用 `${…^…}$` 说明台词可按农夫性别使用相应称谓与生活细节。

**Preoccupation**：`preoccupation`(Name,preoccupation)——今天心里惦记某话题（来自传记
`Preoccupations`+喜恶物品名，50% 概率注入），台词可围绕它起话头。

**CurrentConversation**：`currentConversationHeading`、`currentConversationIntro`(Name)
（以下是正在进行的对话，新台词是下一句回应，不要重复已说内容——约 300 字符）、
`currentConversationJustSpoke`(Name)（刚说完一句话、农夫又立刻搭话的衔接情境）、
`generalFarmerLabel`（对话记录里农夫侧的标签词）。

### E. Command 段
| 键 | 变量 | 要传达什么 |
|---|---|---|
| `commandHeading` | — | 任务小节标题 |
| `commandIntro` | Name | 本轮任务：为 {{Name}} 写一句符合情境与个性的台词 |
| `commandReplaceSchedule` | ScheduleLine | 改写模式：给出原版日程台词，新句要主题相近而表达不同（节名 `ReplaceSchedule`，可被覆盖） |
| `instructionsTranslate` | Language | 输出语言强制：台词与选项只用 {{Language}}（翻译模式时追加） |

### F. Instructions 段（输出格式教学；顺序即拼接顺序）
| 键 | 要传达什么 |
|---|---|
| `instructionsHeading` | 小节标题 |
| `instructionsIntro` | 总则：为星露谷村民写对农夫说的台词，贴合游戏文风与当前熟悉度 |
| `instructionsGrounding` | **防幻觉铁律**：不得发明上下文/历史/当前对话之外的共同经历、稀有物品、礼物、任务或世界事实；初见与低熟悉度时保持平淡日常 |
| `instructionsSampleDialogue` | 采样台词的用法：模仿语气与风格 |
| `instructionsFarmersName` | 用 `@` 符号指代农夫名 |
| `instructionsBreaks` | 分屏标记教学：`#$b#` 换屏、`#$e#` 强分隔；两个分隔间不超过 24 词；首尾不放标记；不用真实换行代替 |
| `instructionsSingleLine` | 输出为单行、以 `- ` 开头、标点大小写规范 |
| `instructionsResponses` | 农夫回应选项教学：台词若邀请回应，给 2–4 个覆盖不同态度的选项；好感越高选项越常出现；每行以 `% ` 开头、≤12 词、农夫口吻第一人称；选项内不得有 @/表情记号/特殊符号。需自拟 2–3 个**示例**（有选项、无选项、冷淡拒绝三种形态）——示例台词必须原创，约 1000 字符 |
| `instructionsLivingNpcMetadata` | **最重的一键（完整版约 6.5K 字符，精简版约 2.8K）**：教模型在可见台词与选项之后追加一行以 `!LIVINGNPCS_META` 开头的紧凑 JSON。schema 字段按 §3.2 精确照抄。必须讲清：`rapportDelta` 0–30 的分档标准（敌意 0 / 普通愉快 10–15 / 有意义的温暖 16–24 / 罕见默契 25–30）；`endConversation` 何时置 true（道别、达成约定、回去干活）且此时不给选项；`ambientFollowUp` 仅在双方仍在附近且有自然后续时给一句旁白台词，否则空串，不得用于叙述出行；`emotionImpact` 仅当对话真实改变情绪时填（枚举见 schema），apology 仅农夫真诚道歉、repairDelta 仅实质修复矛盾；`behaviorInfluences` 是"事后倾向"不是即时世界编辑，至多 2 条、短时效，不得改日程/传送；`conflicts` 仅明确伤害才建（严重度 1–100 分档：轻微摩擦 10–25 / 真实伤害 30–60 / 严重破裂 >60）；`actions` 每轮至多 1 个且可见台词必须明确承诺（六种动作各自的门槛：小礼物需熟络、贵重礼物需深交或特殊时机、给钱 25–250、同行出游需双方同意且目的地受支持（20 分钟护送/60 分钟同游）、节日互动限节日、助攻任务不代完成）；不得请求列表外动作，系统仍会二次校验并可能拒绝；`memories` 只存对话中明说或明确达成的耐久信息，`playerPreference` 仅描述农夫本人时为 true，subject 要短且稳定、tags 只用 schema 内的规范标签；若后续送礼是因为记得农夫偏好，可见台词要自然点出"记得你喜欢"；有已接受昵称时用昵称称呼且不与本名混用；隐藏行本身绝不能在可见台词中被提及 |
| `instructionsLivingNpcGiftIds` | 礼物 ID 纪律：只能用当前上下文表里给出的共享/个性化 itemId，不得编造或借用他人礼物；隐藏 gift 动作必须先有可见的送礼台词；台词点名礼物则 itemId/itemLabel 必须匹配；未点名则两者留空由系统挑选 |
| `instructionsLivingNpcImmediateTravel` | 立即同行判定：可见台词接受"现在一起去"才请求 companion_outing 并填 targetLocation；护送/带路/顺路去日程地=20 分钟、真正同游=60 分钟；无目的地散步不请求移动；短暂准备用 delayMinutes 1–10；普通日程是软约束（顺路更应答应）；只因关系边界/活动/睡眠/危险/要务才婉拒；台词不得叙述路线与机制 |
| `instructionsLivingNpcTravelConsent` | travelConsent 枚举教学：`accepted_now / accepted_later / declined / tentative / none` 五值的判定标准（稍作准备仍算 now；改天/晚点=later；拒绝/时机不对=declined；含糊=tentative）；非 accepted_now 不得请求 companion_outing；枚举值恒为英文 |
| `instructionsLivingNpcHelpRequests` | 求助请求教学（完整版约 2.4K）：`helpRequests`/`helpRequestUpdates` 字段结构照 §3.2；只在上下文明说"今天可以开口求助"且可见台词真的求了才建；只允许 `item_request` 且 requestedItemId 只能取上下文"当前合理物品"表；`dueInDays` 1–7；不得求跑腿/送信/改日程/问答类；新请求默认 requiresAcceptance=true（仅农夫主动提出帮忙且 NPC 应允时为 false 直接进任务日志）；状态流转 accepted/declined/advanced/fulfilled 的判定；steps 至多 3 步且每步都是物品步；followUpPotential 取 none 或 deeper_relationship |
| `instructionsLivingNpcEmotionDepth` | 情绪深度与信任边界：jealous/worried/grateful/disappointed 四情绪的适用情形；尊重上下文给的信任档与秘密分享深度（低信任不得突然掏心）；严重矛盾的 repairDelta 必须对应真正的修复性对话，一句道歉不能抹平长链修复 |
| `instructionsExtraPortraitLine`(Key,Value) | 单条额外表情的列举格式（拼进下一键） |
| `instructionsEmotion`(extraPortraits) | 表情记号教学：段末缀 `$h`(大喜)/`$0`(中性)/`$s`(伤心)/传记额外表情记号；记号放在其适用的分段内、前面不加 `#`；禁用 emoji、星号动作等其它情绪表达 |

以上 6 个 `instructionsLivingNpc*` 键各配 `Optimized` 精简变体（要点不删、示例与解释压缩；
旧库 HelpRequests/EmotionDepth 的 Optimized 曾缺失依赖回退——新版**六个都要写全**）。

### G. ResponseStart 段
| 键 | 变量 | 要传达什么 |
|---|---|---|
| `responseStart` | Name | 预填的助手答复开头：接下来给出符合 {{Name}} 人设与情境的台词（让模型直接续写台词本体） |

### H. 骨架配套零件（同表交付）
- 相对时间词族（历史行的时间前缀）：`timeJustNow, timeInTheLastHour, timeEarlierToday,
  timeYesterday, timeDaysAgo(days), timeDaysAgoSeasonDay(days,season,day),
  timeEarlierThisYear(season,day), timeLastYear(season,day), timeALongTimeAgo(year,season,day),
  timeInTheFuture`。
- 通用小词：`generalMale/Female/He/She/Him/Her/His/Hers`（传记性别机制消费）、
  `generalBoy/Girl`、`generalAnd`、时段词 `generalEarlyMorning/LateMorning/Midday/
  Afternoon/Evening/LateNight`。
- 玩家可选按钮词：`outputRespond`（打开输入框回应）、`outputStaySilent`（保持沉默）——
  出现在游戏对话框选项里，属骨架依赖，一并交付。
- `seasonCrops` / `seasonForage`（世界观 Seasons 渲染引子，见 §1.1）。

**不属于 WP20** 的键（WP15 重写 UI 文案时处理，勿混入）：`config*`(131)、`modelCheck*`、
`ui*`、`transcript*`、`commandValleyTalkForget*`、`warning*`、`log*`、`dialogueInstructions` 类。

## 1.4 其他上游文本（一并登记）

| 项 | 旧位置 | 处置 |
|---|---|---|
| 上游法语译文包 | `ValleyTalk/translations/fr-FR/`（i18n 全表 + 33 份 `bio/<Name>.fr-FR.txt`，txt 内容是字段同构的本地化 JSON） | 上游文本的衍生，**不搬运**。新版语言矩阵：中文+英文必备（00 §3）；法语骨架 i18n 若要保留由 WP15 决定，传记法语版列为可选后续，不阻塞 |
| 上游中文译文 | `ValleyTalk/translations/zh-CN/i18n/zh-CN.json` | 上游衍生，不搬运（Yuki 的 `ContentPack/i18n/zh.json` 才是保留件，两者勿混淆） |
| 英文 UI 文案 | `ContentPack/i18n/default.json` 中的 config/ui/modelCheck/transcript 等键 | 归 WP15 重写 |
| 上游文档 | `ValleyTalk/docs/*`（作者指南、Nexus 页文案、安装说明、模型清单） | 全部废弃不迁移；新 mod 文档另写 |
| CP 壳文件 | `ContentPack/assets/Prompts.json`（全部 942 条均为 `{{i18n:键}}` 重定向）、`content.json`、两个内容包 manifest | 新架构无 CP 依赖，消亡；键集合以本文档 §1.3 为准 |
| 婚姻/节日专用模板 | 无独立文件——婚姻文案即 §1.3 D 组婚姻各键；节日文案即 SpecialDates/Festivals/`historyThirdPartyFestival`/`luau*` 各键 | 已含在上表 |
| 错误回退台词 | 引擎失败时回退到原版台词池，无 AI 台词兜底文案；语言重试指令内嵌在搬运件 `ConversationTextPostProcessor` 中（保留件） | 无需创作 |

---

# 第二部分 · 保留清单（Yuki 原创，勿重写，仅防重复创作）

创作方**不要**为以下文案面另写内容；它们随代码搬运原样保留（03 §2/§3）。

## 2.1 `LivingNPCs/Behavior/Prompts/PromptFragments.cs`（行为系统全部英文文案的唯一家）
覆盖面（按内部类）：
- `State`/`Facts`/`Recall`：NPC 持久状态（情绪、熟悉度、舒适档、互动节奏、礼物/事件
  记忆、长期记忆、农夫偏好、社区印象、共同经历、行为倾向、求助、信任、秘密分享、
  矛盾、昵称）的一行化描述。
- `Context`：`## LivingNPCs Context: <名>` 隐藏上下文整节（规则、对话姿态、当前状态行、
  高优先级连续性提示、近期时刻列表、下一句指导；含 concise 变体与标签词）。
- `GiftOpportunity` / `GiftResponseMail` / `HelpRequestOpportunity` / `HelpRequestHandIn` /
  `HelpRequestDelivery`：五种情境注入节（`## LivingNPCs Gift Opportunity`、
  `## LivingNPCs Birthday/Reciprocal Gift Mail`、`## LivingNPCs Help Request Opportunity`、
  `## LivingNPCs Help Request Gift Response`、`## LivingNPCs Immediate Help Request Delivery`）。
- `Outing`：`## Active Companion Outing` 出游进行时节。
- `Planner`：微行为规划器的 system+user 提示（JSON intent 输出）。

## 2.2 搬运件内嵌提示词（随代码保留）
| 生成器 | 旧位置 | 提示词功能 |
|---|---|---|
| `MemoryImpressionGenerator` | `ValleyTalk/src/Generation/MemoryImpressionGenerator.cs` | 把被挤出的长期记忆压缩成"关系印象"段（中英双语 system/user 内嵌，输出经语言校验） |
| `GiftMailGenerator` | `…/GiftMailGenerator.cs` | 回礼/生日感谢信正文生成（中英双语内嵌；信件换行用 `^`；失败回退模板信在 LivingNPCs 侧，同为 Yuki 件） |
| `ContextRoutingDecisionPass` | `…/ContextRoutingDecisionPass.cs` | 语义路由决策：输出各模块 none/brief/full 的 JSON（键 world,npcProfile,gameState,sampleDialogue,eventHistory,recentEvents,location,livingNpc,gift,action,confidence） |
| `LivingNpcActionDecisionPass` | `…/LivingNpcActionDecisionPass.cs` | 第二遍动作判定：从可见回复反推 travelDecision/giftDecision/actions/helpRequests 的 `!LIVINGNPCS_META` JSON 分类器 |
| `LivingNpcContextCompressor` | `…/LivingNpcContextCompressor.cs` | 隐藏上下文的 brief 压缩文案 |
| `ConversationTextPostProcessor` | `…/ConversationTextPostProcessor.cs` | 语言重试追加指令、错语言判定 |

## 2.3 `ContentPack/i18n/zh.json`
Yuki 原创的全量中文骨架（950 键，与 default 同键集）。**保留**并作为新中文骨架的
基础语料；新英文骨架写好后，中文版以 zh.json 为底本对齐新键名/新措辞修订，
而不是从零重写。

---

# 第三部分 · 创作规范（总则）

## 3.1 语气与人设
- 骨架文案的读者是模型：用清晰、指令式、无修饰的说明文；一条指令一件事；
  优先短句与列表。禁止在骨架里预设具体 NPC 的性格（性格属于传记）。
- 传记的读者也是模型：白描 + 事实，避免文学化堆砌；所有能结构化的信息
  （关系、特质）不要塞进正文散文里重复。
- 全部文本使用与游戏一致的专有名词（英文版用官方英文名，中文版用官方中文译名）。

## 3.2 与 LivingNPCs 隐藏标记格式的兼容（最重要的功能契约，交叉引用 WP16）

`ValleyTalkExchangeParser`（Yuki 件，原样保留）解析模型输出中的结构化标记；
**骨架必须教会模型输出它们**，负责教学的节与要教的内容：

| 标记/格式 | 由哪节教 | 精确形态 |
|---|---|---|
| 台词行 | `instructionsSingleLine` | 单行、`- ` 前缀 |
| 农夫名占位 | `instructionsFarmersName` | `@` |
| 分屏 | `instructionsBreaks` | `#$b#`、`#$e#`，间隔 ≤24 词 |
| 表情 | `instructionsEmotion`(+`instructionsExtraPortraitLine`) | 段尾 `$h/$0/$s/$l/$a` + 传记 `ExtraPortraits` 键；`ExtraPortraits` 含 `"!"` 键的 NPC 整节不教 |
| 回应选项 | `instructionsResponses` | 每行 `% ` 前缀、2–4 项、≤12 词 |
| 元数据行 | `instructionsLivingNpcMetadata` 族 | 末尾一行 `!LIVINGNPCS_META{…}`，JSON 顶层键：`rapportDelta`(int 0–30), `endConversation`(bool), `ambientFollowUp{text,delayMinutes}`, `emotionImpact{emotion∈happy\|calm\|jealous\|worried\|grateful\|disappointed\|uneasy\|upset\|angry\|sad\|none, intensityDelta, apology, repairDelta, reason}`, `behaviorInfluences[{type∈visit_location\|comforted\|offended\|give_space\|stay_near\|pause_to_talk, summary, targetLocation, targetLocationLabel, durationDays, intensity, maxTriggers}]`, `actions[{type∈give_small_gift\|give_meaningful_gift\|give_money\|companion_outing\|festival_interaction\|assist_quest, amount, durationMinutes, delayMinutes, targetLocation, travelConsent, questHint, itemId, itemLabel, reason}]`, `conflicts[{causeKind∈dialogue\|gift\|boundary\|promise, summary, severity}]`, `memories[{kind∈fact\|preference\|promise\|boundary\|relationship, summary, importance, playerPreference, playerPreferenceKind∈liked_item_category\|disliked_item\|habit\|value\|goal\|none, subject, tags[…canonical]}]`, `helpRequests[{type:"item_request", summary, requiresAcceptance, requestedItemId, requestedItemLabel, questionTopic, dueInDays, reason, steps[≤3], followUpPotential∈none\|deeper_relationship}]`, `helpRequestUpdates[{summary, status∈accepted\|declined\|advanced\|fulfilled, resolution}]` |
| targetLocation 枚举 | Metadata/Travel 两节 | `Farm, Town, Mountain, Beach, Forest, BusStop, Saloon, SeedShop, ArchaeologyHouse, Hospital` |
| travelConsent 枚举 | `instructionsLivingNpcTravelConsent` | `accepted_now, accepted_later, declined, tentative, none`（恒英文） |
| memories tags 规范表 | Metadata 节 | `food, drink, flower, mineral, forage, nature, sweet, comfort, practical, scholarly, adventurous, magical, artistic, refined, work, active, fishing, mining, farming, morning, night` |

**双向一致性约束**：`PromptFragments`（保留件）产出的节标题
`## LivingNPCs Context / ## LivingNPCs Gift Opportunity / ## LivingNPCs Help Request
Gift Response / ## LivingNPCs Immediate Help Request Delivery / ## Active Companion Outing`
是引擎的识别锚点（如收礼节靠检测 `## LivingNPCs Help Request Gift Response` 切换到
求助代收文案）。骨架新文案提到这些机制时，措辞必须与这些标题所代表的机制吻合，
且不得建议模型自己输出这类 `## LivingNPCs` 标题。

## 3.3 游戏语言适配
- 交付语言：**英文骨架 + 中文骨架**各一套全量（中文以 zh.json 为底本修订，见 §2.3）。
- 其他语言不交骨架：运行时走"英文骨架 + `systemPromptTranslation` + `instructionsTranslate`
  的 {{Language}} 强制"模式；这两键的新文案必须把"指令语言≠输出语言"讲死，
  并明确"非目标语言不得混入"（现状痛点是中文模型混中文）。
- 语义路由现状边界（沿用）：游戏语言非中/英时路由不用会话缓存、每轮重路由——
  骨架无需为此写文案，但精简变体的措辞不得假设"输出必为英文"。

## 3.4 禁止事项（新文案必须保留的功能性约束，逐条落到对应键）
1. 不发明游戏不存在的事实/物品/事件/共同经历（`instructionsGrounding`）。
2. AI 文本不得单独发放奖励：一切给物/给钱/出游必须走隐藏 action 且系统二次校验；
   台词承诺 ≠ 系统执行（Metadata/GiftIds 节；参见 memory：AI 文本不得单独发奖）。
3. 不承诺游戏内不存在的物件（书签、手写便条一类"非游戏物品"禁止具名赠送）。
4. 礼物 itemId 只能取上下文白名单；无名礼物一律泛称。
5. 儿童 NPC：无浪漫、无酒精（酒馆微醺短语不适用于儿童与 Emily 的拼接规则要保留）；
   高好感儿童文案是"亲近的大朋友"。
6. 情绪与自我披露受信任档约束；低信任不得突然掏心或深度告白（EmotionDepth）。
7. 隐藏行/元数据/模组机制绝不在可见台词中出现；不提 AI、prompt、JSON。
8. 婚姻多配偶场景按开放式婚姻自洽处理，不写出轨冲突（Spouse 网络组）。
9. 台词必须扎根当前地点/时间/活动（CurrentState grounding）。
10. 面向成年玩家但保持游戏分级：无露骨性内容；"成熟"指话题深度而非尺度（`gameContext`）。

## 3.5 交付格式与自校验（对齐 WP15）
- 世界观与传记：JSON 交到 `LivingNPCs/assets/dialogue/`（`world/GameSummary.json`、
  `world/GameSummaryOptimized.json`、`bios/<Name>.json`、`bios-sve/<Name>.json`；
  中文版位置按 WP15 的本地化约定）。骨架键值表交到 WP15 指定的资产/i18n 容器
  （旧键名如需更名，随交付附"旧键→新键"映射表，供 WP15/WP16 对账）。
- **自校验清单**（创作方交付前自跑）：① 所有 JSON 可被严格解析（UTF-8、无注释、无尾逗号）；
  ② 骨架键集合与本文档 §1.3 清单 diff 为空（含 ♦ 性别对、6 个 Optimized 对）；
  ③ 全部 `{{Token}}` 拼写与 §1.3 括号内变量表一致（脚本正则抽取比对）；
  ④ 传记 33+23 个文件齐全、必填 8 字段齐全、Jas/Shane 的 PromptOverrides 存在；
  ⑤ 中英两版键集一致；⑥ §3.2 表中每个标记至少被一个键教到（关键词扫描
  `!LIVINGNPCS_META`、`% `、`#$b#`、`$h`、`@`、`travelConsent` 等）。
  可参考仓库现成脚本风格：`tools/audit_prompt_literals.py`、`tools/verify_anchors.py`。

---

# 第四部分 · 验收抽查表（创作完成后，用户/审校 AI 执行）

| # | 抽查项 | 方法 | 通过标准 |
|---|---|---|---|
| 1 | 传记事实核查 | 随机抽 5 个原版 NPC + 2 个 SVE NPC，逐条对照 Wiki（家庭关系、职业、住处、日程、生日、爱憎礼物倾向） | 无一条与 Wiki 矛盾；秘密角色无越界剧透 |
| 2 | 骨架齐全性 | 跑 §3.5 自校验脚本 | 键集 diff 为空、变量表全对 |
| 3 | 标记教学完备 | 人工读 `instructionsResponses/Breaks/Emotion/SingleLine/LivingNpc*` 六族 | §3.2 每行契约都被教到且形态精确；示例为原创 |
| 4 | 阶梯连贯性 | 通读好感 7 档 + 婚姻 3 档 + 特殊关系 4 键 | 档间语气单调递进、无跳档矛盾；儿童档无浪漫 |
| 5 | 长度预算 | 统计各件字符数 | GameSummary 完整/精简 ≈ 18K/12K ±30%；Metadata 完整/精简 ≈ 6K/3K ±30%；传记在 §1.2 区间 |
| 6 | 双语一致 | 抽 3 个 NPC 传记 + 30 个骨架键中英对读 | 语义等价、术语用官方译名、无机翻腔 |
| 7 | 上游残留 | 任取 20 句新文案在旧库全文检索（由**脏屋侧**执行，创作方不可执行） | 无 ≥8 词连续重合 |
| 8 | 烟测 | 部署后开日志导出器实跑 5 轮对话（含送礼、已婚、出游邀请各 1 轮） | 拼装完整无缺节告警；元数据行可被解析；表情/选项渲染正常 |

---

# 附 · 审计索引（登记项 → 旧文件位置；仅脏屋侧与审计使用，创作方勿读旧路径内容）

| 登记项 | 旧位置 |
|---|---|
| 五段拼装顺序与缓存边界 | `ValleyTalk/src/Character.cs`（推理调用处）、`ValleyTalk/src/Prompts.cs`（属性 System/GameConstantContext/NpcConstantContext/CorePrompt/Command/ResponseStart/Instructions） |
| CorePrompt 小节顺序/路由开关 | `ValleyTalk/src/Prompts.cs` `GetCorePrompt()`；模块枚举 `src/Generation/ContextRoutingPlan.cs` |
| 世界观容器/渲染规则/SectionOrder 语义 | `ValleyTalk/src/GameSummaryBuilder.cs`；`ContentPack/assets/GameSummary.json`、`GameSummaryOptimized.json` |
| SVE 世界观/地点/村民附加 | `Extensions/ValleyTalk for SVE/assets/GameSummary*.json`、`Locations*.json` |
| 传记字段结构 | `ValleyTalk/src/models/BioData.cs`；样例 `ContentPack/assets/bio/*.json`（33 文件） |
| SVE 传记与关系补丁 | `Extensions/ValleyTalk for SVE/assets/bio/*.json`（23 文件，含 TargetField=Relationships 的补丁段） |
| 骨架键全集/变量/性别变体机制 | `ContentPack/i18n/default.json`（950 键）、`ContentPack/assets/Prompts.json`（i18n 重定向壳）、`ValleyTalk/src/Util.cs` GetString（变体选择与 {{Token}} 替换） |
| 好感阶梯/婚姻/配偶网络/礼物/地点/近事各键的触发逻辑 | `ValleyTalk/src/Prompts.cs` 对应 Get* 方法 |
| Optimized 探测回退 | `ValleyTalk/src/Prompts.cs` `GetLivingNpcInstruction()` |
| 求助代收礼物的标题锚点 | `ValleyTalk/src/Prompts.cs` `IsLivingNpcHelpRequestGift()`（检测 `## LivingNPCs Help Request Gift Response`） |
| 第三方覆盖/注入口 | `ValleyTalk/src/Prompts.cs` `DefaultOrOverride`/`AppendThirdPartyContext`；bio 级 `PromptOverrides` 消费在 `Util.GetString` |
| 保留件提示词 | `LivingNPCs/Behavior/Prompts/PromptFragments.cs`；`ValleyTalk/src/Generation/{MemoryImpressionGenerator,GiftMailGenerator,ContextRoutingDecisionPass,LivingNpcActionDecisionPass,LivingNpcContextCompressor,ConversationTextPostProcessor}.cs` |
| 中文骨架底本 | `ContentPack/i18n/zh.json` |
| 上游译文（不搬运） | `ValleyTalk/translations/fr-FR/`、`translations/zh-CN/` |
| 玩家选项词 outputRespond/StaySilent 的消费点 | `ValleyTalk/src/Generation/DialogueBuilder.cs`、`src/Patches/Dialogue_ChooseResponse_Patch.cs` |

## 开放问题（待用户/对应 WP 裁决）
1. 骨架键名是否沿用旧名（含 `WInter1`、`nonSpouseFreindshipStrangers` 两处拼写瑕疵）
   还是借机更名——建议更名并附映射表，由 WP15 定容器后一并落。
2. 法语支持范围（仅 i18n 骨架 vs 含传记）——影响 01 §1 中 `i18n/fr.json` 的承诺。
3. 传记中文版的容器形式（平行目录 or 后缀文件）——WP15 未定稿前创作方先按平行目录交。
