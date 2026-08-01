# LivingNPCs 0.2.0

[English README](./README.en.md)

> 和鹈鹕镇的居民用自己的话交谈，并让他们真正记住、在意和回应你们共同经历的事。

LivingNPCs 为《星露谷物语》的 NPC 加入一套内置 AI 对话、长期记忆、情绪关系和受控行为系统。它不只是生成一句临时台词：NPC 会结合自己的性格、与你的关系、游戏进度、当前地点与时间，以及过去的谈话来回应；某些合适的对话还会延伸为求助、回礼、短途出游或细微的日常行为。

## 0.2.0：一体化重写

0.2.0 是一次完整重写，也是与 0.1.x 安装方式不同的新版本：

- AI 对话引擎已经独立重写并直接内置进 LivingNPCs。
- 发布包现在只有一个 <code>LivingNPCs</code> Mod 文件夹。
- **不再需要 ValleyTalk，也不再需要 Content Patcher。**
- 原版与 Stardew Valley Expanded（SVE）的中英文人物资料和进度感知已内置，不再需要额外的 SVE 对话内容包。
- 旧版 ValleyTalk 的兼容配置、聊天记录、NPC 对话记忆和 token 账本可以自动迁移。
- AI 对话、记忆、情绪、求助、礼物、出游、日志和用量统计现在由同一个 Mod 统一管理。

- 当前版本：<code>0.2.0</code>
- Mod ID：<code>Yuki.LivingNPCs</code>
- Nexus：<https://www.nexusmods.com/stardewvalley/mods/47704>
- 源码：<https://github.com/Nyx-Amanises/StardewLivingNPCs>

## 运行需求

- Stardew Valley 1.6.x
- [SMAPI 4.1.0 或更高](https://smapi.io/)
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)（可选，但强烈推荐）
- 一个可用的大模型服务/API，或由你自行运行的本地兼容服务

LivingNPCs **不附带模型，也不会自动提供免费 API**。速度、费用、隐私和对话质量取决于你选择的服务商与模型。

## 全新安装

1. 完全退出游戏和 SMAPI。
2. 解压 0.2.0 发布包。
3. 将其中唯一的 <code>LivingNPCs</code> 文件夹放入游戏的 <code>Mods</code> 文件夹。
4. 确认最终路径类似 <code>Stardew Valley/Mods/LivingNPCs/manifest.json</code>，不要多套一层同名文件夹。
5. 通过 SMAPI 启动游戏。
6. 在 Generic Mod Config Menu 中打开 **LivingNPCs → AI 对话引擎**，完成模型连接设置。

启动时，SMAPI 控制台应显示 LivingNPCs 已加载。若没有安装 GMCM，也可以在首次启动后关闭游戏，手动编辑 <code>Mods/LivingNPCs/config.json</code>。

## 从 0.1.x 升级与迁移旧存档

> **请在第一次启动 0.2.0 前完成下面的文件夹改名。** 这样新版才能找到并导入旧配置与文件夹中的聊天数据。

建议先备份存档，以及旧的 <code>LivingNPCs</code>、<code>ValleyTalk</code> 文件夹，然后按顺序操作：

1. 完全退出游戏和 SMAPI。
2. 在游戏的 <code>Mods</code> 文件夹中，将旧版最外层的 <code>ValleyTalk</code> 文件夹改名为 <code>.ValleyTalk</code>：

   ~~~text
   Mods/ValleyTalk/  →  Mods/.ValleyTalk/
   ~~~

   如果旧发布包的结构是 <code>ValleyTalk/ValleyTalk</code>，请改名包含所有旧组件的**最外层文件夹**。不要给旧版 <code>LivingNPCs</code> 加点；它需要由新版替换。

3. 将 0.2.0 的新 <code>LivingNPCs</code> 文件夹解压到 <code>Mods</code>，覆盖或替换旧版同名文件夹。不要把两个版本同时放在不同子目录中加载。
4. 通过 SMAPI 启动游戏，并载入每一个需要迁移的旧存档一次。多人游戏的存档数据迁移必须由主机完成。
5. 如果存档中有可迁移的旧对话数据，画面会显示：

   > LivingNPCs：已从 ValleyTalk 迁移 N 位 NPC 的对话记忆。

6. 看到成功提示后，在游戏内正常保存一次。

迁移会尝试保留：

- NPC 的 AI 对话历史与相关存档记忆；
- 兼容的提供商、API Key、模型、服务器地址和对话设置；
- token 用量账本；
- 联机玩家的本地历史；
- <code>conversation_logs</code> 中可读的聊天记录。

迁移器不会自动删除旧文件或旧存档键。改名后的 <code>.ValleyTalk</code> 不会被 SMAPI 当作 Mod 加载，因此可以继续留作备份；确认 0.2.0 的配置和聊天记录正常后，也可以删除它。

需要特别注意：

- **不要让旧版文件夹继续以 <code>ValleyTalk</code> 原名加载。** 如果 0.2.0 检测到旧版 ValleyTalk 正在运行，为避免两套对话补丁冲突，内置 AI 对话引擎和迁移都会保持停用。
- 没有旧聊天历史或 token 数据的存档可能不会显示“迁移 N 位 NPC”的提示，这不一定代表迁移出错。
- 如果提示“部分数据未能迁移”，请先保留 <code>.ValleyTalk</code> 备份，检查 SMAPI 日志后再反馈。
- 若你已在改名前启动过 0.2.0，旧配置可能不会再自动导入；可以在 LivingNPCs 设置中重新填写连接信息，旧存档对话数据仍会在载入时继续尝试迁移。

## 第一次配置 AI

推荐通过 GMCM 完成设置：

1. 打开 **LivingNPCs → AI 对话引擎**。
2. 选择 LLM 提供商。
3. 如果刚刚切换了提供商，请先保存，再重新打开设置页面。切换提供商会清空旧 API Key，并刷新该提供商需要的字段。
4. 按提供商要求填写 API Key、模型名和服务器地址。
5. 保存设置，观察 SMAPI 控制台中的连接自检结果。
6. 载入存档，靠近 NPC，按住 <code>LeftAlt</code> 再点击或使用交互键，即可打开自由输入框。

支持的提供商：

| 提供商 | 常见必填项 |
| --- | --- |
| OpenAI | API Key、模型名 |
| OpenAI-compatible | API Key、模型名、服务器地址（自定义网关/中转用；填基础地址即可，代码会自动规整 <code>/v1</code> 等后缀） |
| OpenRouter | API Key（端点与默认模型已内置；一个 Key 可以调用多家模型，含免费模型） |
| 智谱（GLM）、月之暗面（Kimi）、阿里云百炼（通义千问）、硅基流动 | API Key（端点已内置；模型名留空使用各家默认，智谱默认 <code>glm-4-flash</code> 为免费模型） |
| Ollama、LM Studio（本地） | 无需 API Key；本机启动服务即可（端点已内置，模型名按本地已装模型填写） |
| Anthropic（Claude） | API Key、模型名 |
| Google（Gemini） | API Key、模型名 |
| DeepSeek | API Key、模型名 |
| Mistral | API Key、模型名 |
| 火山引擎（豆包） | API Key、模型名 |
| llama.cpp（本地） | 你自行运行的完整服务端点（例如 <code>http://127.0.0.1:8080/completion</code>）；「提示词模板」需与所用模型的指令格式匹配（默认为 Mistral <code>[INST]</code> 格式，可在 GMCM 中直接修改） |

LivingNPCs 会在保存连接设置后进行非阻塞自检。自检失败不会永久关闭引擎，下一次真实对话仍会重试。常见错误：

- <code>401/403</code>：API Key 无效、权限不足或填错服务商；
- <code>429</code>：请求过快、余额不足或服务商限流；
- 找不到模型：模型名不正确，或当前 Key 无权访问；
- 连接超时：服务器地址、网络或请求超时设置需要检查。

较强的模型通常更能稳定处理角色扮演、长期记忆和隐藏的结构化行为信息。较小或便宜的模型也可以使用，但更容易出现忘记上下文、角色偏移或动作判断不稳定。

### 5 分钟免费上手

不想先花钱？下面三个方案都可以免费开始（额度与政策以各服务商当前页面为准）：

1. **智谱 GLM（国内直连，有免费模型）**：在 <https://open.bigmodel.cn> 注册并创建 API Key → 提供商选「智谱（GLM）」→ 填入 Key，模型名留空（默认 <code>glm-4-flash</code>，免费）。
2. **Google Gemini（免费额度）**：在 <https://aistudio.google.com> 创建 API Key → 提供商选「Google（Gemini）」→ 填入 Key，模型名留空（默认 <code>gemini-2.5-flash</code>）。需要网络能正常访问 Google。
3. **本地 Ollama（完全免费、离线、无隐私顾虑）**：安装 <https://ollama.com> → 命令行运行 <code>ollama pull qwen3:8b</code> → 提供商选「Ollama（本地）」，其余留空。需要较好的电脑配置；小模型的角色扮演和结构化输出明显弱于云端大模型。

免费方案适合先体验玩法；长期游玩建议换更强的模型，对话质量、记忆稳定性和行为判断会明显更好。

## 如何与 NPC 交谈

- **自由输入 AI 对话：**默认按住 <code>LeftAlt</code>，再点击 NPC 或使用交互键。生成期间显示"正在思考……"，按 <code>Esc</code>（或手柄 B）可取消；回复用原版对话框逐字显示。
- **普通对话：**默认直接右键仍显示原版台词。
- **让普通右键也使用 AI：**在 GMCM 中开启“普通右键也用 AI 对话”。
- **送礼与婚后台词：**可以分别设置 AI 生成频率。
- **关闭某些角色的 AI：**在“禁用 AI 的 NPC”中，用逗号或空格填写内部名字。

如果生成失败、超时或返回内容不可用，Mod 会尽量安全回退，不会为了显示一句 AI 台词而阻断正常存档载入。

## 主要玩法

### 长期对话记忆

NPC 可以保存并在合适时召回：

- 你说过的事实、偏好与讨厌的事；
- 昵称、承诺、边界和重要共同经历；
- 最近的对话走向与长期关系印象；
- 你们之间尚未解决的误会、冲突或修复过程。

当长期记录超过容量时，旧记忆可以被压缩成较稳定的“关系印象”，避免存档无限增长，同时保留人物关系的连续感。

### 情绪、关系节奏与修复

NPC 的反应不只看一句提示词。系统会结合原版好感心数、对话历史、信任、长期情绪和近期冲突控制亲密程度。刚认识的角色不会立刻把玩家当成多年知己；不愉快也不会在下一句话里凭空消失。合适的道歉、礼物和后续交流可以逐渐修复关系。

NPC 还会记住玩家偏好、形成昵称，并根据性格以不同方式表达开心、担心、感激、嫉妒、不悦或平静。

### 社区印象与有限传播

重要互动可以在目击者和 NPC 的亲近圈中形成有限的社区印象。消息会随着时间衰减，转述也可能变得概括，不会让全镇居民瞬间知道一切。这个系统用于让关系产生轻微的社会回声，而不是制造全知 NPC。

### NPC 主动求助

关系合适时，NPC 偶尔会在对话中请求一件小物品：

- 玩家明确接受后，请求会加入原版任务日志并显示提示；
- 只有真正把正确物品交给 NPC 才算完成；
- 完成后可以获得额外好感或小奖励；
- 请求受心数、每日概率、冷却和待完成数量限制；
- 无效、危险或不适合交付的物品会被本地规则拦截。

### 礼物、金钱与个性化信件

在关系和对话都合适时，NPC 可以送出低价值小礼物、少量金钱，或冷却更长的有意义礼物。生日、回礼和求助谢礼可以生成符合角色口吻的信件；模型失败时会回退到稳定模板。

礼物机会、金额、物品有效性、冷却和每日触发次数都在本地校验。NPC 在对白中随口说“送你某物”并不等于一定会执行。每位原版 NPC 的候选礼物池与物品 ID 见 [原版NPC个性礼物池.md](./原版NPC个性礼物池.md)。

### 陪伴出游与节日互动

当回复明确接受邀请时，NPC 可以临时离开日程，经过真实地图边界前往支持的地点并停留一段时间，随后恢复日程。系统会避免在节日、剧情、睡觉、恶劣天气和不安全地图状态下强行开始出游。

原版地图与受支持的 SVE 户外地点有专门的目的地锚点。自定义地图若缺少安全位置，会回退或放弃动作。

### 记忆手册

按 <code>LeftShift + J</code>（可在 GMCM 改键）打开游戏内**记忆手册**：左页是认识的 NPC 名册（头像、心数、最近接触时间），右页分四个标签——

- **关系**：TA 当前的情绪、亲近层级、信任程度、给你起的昵称、尚未解开的疙瘩，以及长期沉淀出的"关系印象"；
- **记忆**：TA 记住的事实、约定、边界与你的喜好，按类别分组，悬停可见重要度与强化次数；
- **对话**：按日期倒序翻阅你们最近的 AI 对话原文；
- **经历**：一起出游的时刻、帮过的小忙、最近的礼物。

支持鼠标滚轮与手柄（B 关闭、LB/RB 切标签、LT/RT 换人）。联机时记忆数据归主机存档所有。

### 游戏进度与现场感

AI 上下文可以感知当前日期、时间、季节、天气、节日、地点、玩家与 NPC 的关系、NPC 当前活动和部分游戏进度。角色因此更容易谈论“现在正在发生什么”，而不是像脱离游戏世界的通用聊天机器人。

内置资料覆盖原版 NPC，并提供 SVE 的中英文人物资料与扩展进度信息。无需安装旧版的 <code>ValleyTalk for SVE</code> 内容包。

### 小型行为与世界动作

对话可能在严格限制下影响 NPC 接下来几分钟到几天的小行为，例如转向玩家、表现情绪、短距离走近、保持距离，或在提到的地点作出更自然的回应。

所有会真正影响游戏世界的动作都经过白名单和二次校验。当前范围主要是礼物、少量金钱、求助、出游、节日互动和轻量 NPC 行为；Mod 不允许模型任意执行命令或随意改写游戏状态。

## SVE 与自定义 NPC

- <code>EnableSveCompatibility</code> 默认开启。安装 SVE 时会使用内置的 SVE 人物资料、关系和进度上下文；未安装 SVE 时不会强制要求它。
- 第三方 NPC 可以使用游戏数据和保守的通用回退，但没有专门资料时，角色深度可能不如原版与 SVE 精修角色。社区可以把 JSON 人物资料直接放进 <code>Mods/LivingNPCs/npc_profiles/</code> 来补充或修正角色，写法见 [npc_profiles/README.md](./LivingNPCs/npc_profiles/README.md)。
- LivingNPCs 尊重内容作者对 AI 使用的许可。未明确允许 AI 使用的内容包文本不会被复制到提示词中；游戏内容本身仍可正常显示。

当前没有宣称对 Ridgeside Village、East Scarp 等其他大型扩展提供专属精修适配。

## 多人联机（v1：主机权威）

0.2.0 起支持局域网/邀请码联机的基础适配，前提是**主机与帮工都安装本 Mod**（各自使用自己的 API Key 与提供商配置）：

- **NPC 心智只有一份，归主机存档**。帮工用自己的 Key 本地生成对话；说完的内容自动上报主机，由主机统一记入记忆/情绪/信任账本，再把每位 NPC 的只读“关系视图”发回帮工——双方都能感知同一段关系历史。帮工对话产生的好感增长会回到帮工自己头上。
- **帮工的对话历史仍存在帮工本机**（`multiplayer/` 目录），记忆手册的"对话"页读取本地历史；其余页（关系卡/记忆/经历）打开时向主机请求最新快照，主机无响应时会提示或退回上次同步的数据。
- **世界动作由主机裁决**：帮工对话提出的小礼物、少量金钱和物品型求助会随交换上报，由主机按同一套白名单、关系、冷却与上限规则验证；通过后结果只兑现给发起的帮工。求助任务会投影到该帮工自己的任务栏，交付仍由主机核验。v1 帮工不能发起陪伴出游，出游 NPC 也始终只由主机驱动。
- **每位参与 AI 对话的玩家都要配置自己的连接**。帮工没配 API Key/模型/服务地址时，按住搭话热键会复用未配置提示并回退原版对话；不会借用或传输主机的 Key。
- **分屏（split-screen）副屏玩家 v1 暂不支持 AI 对话**，会一次性提示并回退原版对话。
- 主机未装本 Mod、双方协议版本不兼容（或隐藏配置 `EnableMultiplayerSync=false`）时，帮工整体降级为本地模式，不向主机同步 NPC 心智。

## 常用设置

Generic Mod Config Menu 中实际提供的设置：

| 设置 | 作用与建议 |
| --- | --- |
| 启用 AI 对话 | 内置对话引擎总开关；从关闭改为开启后需要重启游戏 |
| 提供商连接 | LLM 提供商、API Key、模型名、服务器地址与请求超时 |
| 语义上下文路由 | 默认开启；用一次轻量判断选择本轮需要的上下文，并可调路由超时与路由思考档位 |
| 精简世界摘要 / 精简提示上下文 | 可减少 token；若角色细节下降，可恢复默认 |
| 对话思考档位 | 仅对支持该参数的模型有效；更高不一定更适合日常对话 |
| 普通右键也用 AI | 默认关闭；建议先保留原版右键，用 <code>LeftAlt</code> 主动发起 AI 对话 |
| AI 台词频率 | 常规、送礼、婚后三档分别设置 |
| 输入对话热键 / 查看记忆快捷键 | 默认 <code>LeftAlt</code> 与 <code>LeftShift + J</code> |
| 禁用 AI 的 NPC | 用逗号或空格填写内部名字 |
| 主动求助 | 开关与每日主动开口概率 |
| AI 影响世界 | 总开关；关闭后可保留 AI 对话，但不会执行礼物、金钱和出游等效果 |
| AI 小礼物 | 开关与每日礼物机会概率范围 |
| SVE 兼容 | 安装 SVE 时建议开启 |

以下高级选项**只能直接编辑 <code>config.json</code>**（编辑前请关闭游戏并保留备份；格式错误或超出安全范围的数值可能被自动修正）：

- 有意义礼物、送钱与陪伴出游：<code>AllowAiMeaningfulGifts</code>、<code>AllowAiMoneyGifts</code>（单次上限 <code>MaxAiMoneyGiftAmount</code>）、<code>AllowAiCompanionOutings</code>；
- AI 对话额外好感：<code>EnableAiDialogueFriendship</code> 及每日上限；
- 被动行为：<code>EnablePassiveBehaviors</code>、<code>PassiveBehaviorChancePercent</code>（默认关闭；开启前建议先用行为测试键手动测试）；
- 记忆容量：<code>MaxMemoryEntriesPerNpc</code>、<code>PromptMemoryEntries</code>；
- 礼物信与记忆压缩：<code>EnableAiGiftMail</code>、<code>EnableMemoryImpressions</code>；
- 多人同步：<code>EnableMultiplayerSync</code>（默认开启；关闭后帮工不再上报主机、退回本地临时记忆，见"多人联机"一节）；
- 连接与日志：<code>SuppressConnectionCheck</code>（完全关闭连接自检；默认只在连接设置变化时自检一次）、<code>ExportAiResponseLogs</code>（AI 诊断日志开关；单个日志超过约 8MB 会自动轮转为 <code>.old</code>，不会无限增长）。

## 快捷键

- <code>LeftAlt</code> + 点击/交互 NPC：打开自由输入 AI 对话。
- <code>LeftShift + H</code>：手动触发附近 NPC 的一次小型行为，主要用于测试。
- <code>LeftShift + J</code>：打开游戏内记忆手册（关系卡、长期记忆、对话回忆、共同经历）。

输入对话热键与记忆手册快捷键可以在 GMCM 中调整；行为测试键需在 <code>config.json</code> 的 <code>BehaviorHotkey</code> 中修改。控制台版状态摘要仍可用 <code>livingnpcs_debug</code> 命令查看。

## 调试与评估工具

在 SMAPI 控制台中输入：

| 命令 | 用途 |
| --- | --- |
| <code>livingnpcs_debug [near&#124;NPC名字]</code> | 查看状态、行为理由、求助适配和记忆召回 |
| <code>livingnpcs_prompt [near&#124;NPC名字]</code> | 查看下一轮会注入内置对话引擎的隐藏上下文 |
| <code>livingnpcs_export [near&#124;all&#124;NPC名字]</code> | 导出 Markdown 调试报告 |
| <code>livingnpcs_eval</code> | 运行轻量规则自检 |
| <code>livingnpcs_giftmail</code> | 检查礼物信状态与生成结果 |
| <code>livingnpcs_forget [near&#124;NPC名字]</code> | 清除指定 NPC 的行为记忆和 AI 对话历史 |
| <code>livingnpcs_forget all confirm</code> | 永久清除当前存档中所有 NPC 的相关记忆与对话历史 |
| <code>livingnpcs_tokens [export&#124;reset]</code> | 查看、导出或重置当前存档的 LLM 用量统计 |
| <code>livingnpcs_purge_valleytalk confirm</code> | 迁移确认无误后，删除当前存档保留的旧 ValleyTalk 键；仅主机可用 |

<code>forget</code>、<code>reset</code> 和 <code>purge</code> 会删除数据；运行前请确认目标并备份存档。

## 聊天记录、日志、隐私与费用

对话会发送给你配置的 LLM 服务商。请求中可能包含：

- 你的自由输入；
- NPC 名称、人物资料与关系；
- 当前游戏日期、地点、天气和进度；
- 为保持连续性所需的近期对话与相关记忆。

API Key 只保存在 <code>Mods/LivingNPCs/config.json</code>，不会写入 Mod 的日志或导出报告。请不要把 API Key 发给任何人。

0.2.0 默认会在 <code>Mods/LivingNPCs</code> 下保存便于回顾与排错的本地文件：

- <code>conversation_logs/&lt;存档&gt;/</code>：玩家可读的 NPC 对话回忆录；
- <code>prompt_logs/&lt;存档&gt;/</code>：发送前的提示词诊断；
- <code>ai_response_logs/&lt;存档&gt;/</code>：模型原始回复和解析结果；
- <code>context_routing_logs/&lt;存档&gt;/</code>：上下文路由判断；
- <code>debug_reports/&lt;存档&gt;/</code>：通过导出命令生成的综合报告；
- <code>token_usage/&lt;存档&gt;.md</code>：手动导出的 token 统计。

这些文件可能包含你的对话和游戏信息。提交错误报告前请先检查内容并按需要删改隐私信息；**永远不要分享 API Key**。若不希望持续保存 AI 诊断日志，可以在 <code>config.json</code> 中将 <code>ExportAiResponseLogs</code> 设为 <code>false</code>；玩家可读的对话回忆录仍可能用于保留完整聊天历史。

语义路由、正式回复、AI 信件和长期记忆压缩都可能产生模型调用。实际费用取决于模型定价、聊天长度和游玩频率，可用 <code>livingnpcs_tokens</code> 查看统计。

## 常见问题

### 按住 LeftAlt 没有出现输入框

确认 NPC 在可交互距离内、当前没有其他菜单或对话占用输入，并检查 GMCM 中的“输入对话热键”。再查看 SMAPI 控制台是否显示未配置 API Key、模型或服务器地址。

### 普通右键仍然是原版台词

这是默认行为。使用 <code>LeftAlt</code> + 交互主动发起自由输入，或开启“普通右键也用 AI 对话”。

### 检测到旧版 ValleyTalk，AI 对话被停用

完全退出游戏，将旧版最外层 <code>ValleyTalk</code> 文件夹改名为 <code>.ValleyTalk</code> 或移出 <code>Mods</code>，然后重新启动。只有以点开头、未被 SMAPI 加载的备份才可以与 0.2.0 同时留在 <code>Mods</code>。

### 迁移时没有成功提示

如果该存档没有旧 NPC 对话历史或 token 台账，可能不会显示提示。检查 SMAPI 日志中的迁移摘要；若有失败警告，请保留 <code>.ValleyTalk</code> 并附日志反馈。

### 对话慢、成本高或角色表现不稳定

先确认模型适合长上下文和结构化输出。可以减少普通/送礼/婚后 AI 台词频率，尝试精简世界摘要，或换用更快、更便宜的模型；如果角色细节明显下降，再恢复默认上下文设置。

### 如何提交有用的错误报告

请尽量提供：

- 游戏、SMAPI 和 LivingNPCs 版本；
- NPC 名称、日期/时间/天气和地点；
- 你的输入与 NPC 回复；
- 复现步骤和截图；
- 通过 <https://smapi.io/log> 上传后的 SMAPI 日志链接；
- 相关诊断文件（分享前先检查隐私并确认没有 API Key）。

## 0.2.0 主要改进摘要

- 将 AI 对话引擎完整并入 LivingNPCs，统一配置、存档、日志和命令。
- 增加旧 ValleyTalk 配置、聊天记录、存档记忆与 token 数据迁移。
- 加强地点、日程、生日、关系、天气和游戏进度感知。
- 改善长期记忆压缩、偏好与昵称记忆、情绪冲突和关系修复。
- 接受求助后显示提示并进入原版任务日志；修复错误物品、误完成和奖励判定。
- 限制每日送礼机会，拦截无效物品和对白中虚构但不应执行的礼物。
- 改善出游的跨图寻路、日程恢复、节日/剧情/睡眠条件和 SVE 地点处理。
- 修复中文输入与字体、隐藏元数据泄漏、取消生成稳定性等问题。
- 加入聊天回忆录、token 统计、诊断导出和更完整的英中本地化。

## 开发构建

需要 .NET 6 SDK，以及可供编译引用的 Stardew Valley/SMAPI 安装。仓库中的项目默认带有作者本机 <code>GamePath</code>，其他环境请显式覆盖：

~~~powershell
dotnet build LivingNPCs/LivingNPCs.csproj -p:GamePath="D:\SteamLibrary\steamapps\common\Stardew Valley"
~~~

运行测试：

~~~powershell
dotnet test LivingNPCs.Tests/LivingNPCs.Tests.csproj -p:GamePath="D:\SteamLibrary\steamapps\common\Stardew Valley"
~~~

运行离线回归检查（LivingNPCs.Diagnostics，不需要启动游戏，用于确认调试命令、关键人格化规则和文档说明没有被改丢）：

~~~powershell
dotnet run --project LivingNPCs.Diagnostics\LivingNPCs.Diagnostics.csproj -- .
~~~

发布包应只包含一个可由 SMAPI 直接加载的 <code>LivingNPCs</code> 文件夹，不应再捆绑旧 ValleyTalk 或其 Content Patcher 内容包。

## 反馈

LivingNPCs 仍在持续完善。如果你发现错误、角色表现不自然、迁移或兼容问题，或者对玩法与后续扩展有任何建议，欢迎在 Nexus 的 Posts 页面留言或提交 Bug Report。

清晰的例子会非常有帮助：NPC 名称、日期/时间/天气、地点、你的输入、NPC 回复、截图、复现步骤和 SMAPI 日志，都能让我更快定位问题。分享日志前请检查其中的对话内容，并且永远不要公开你的 API Key。

## 鸣谢

LivingNPCs 0.2.0 及其内置 AI 对话引擎是本项目的一体化重写。ValleyTalk 不再包含在发布包中，也不再是运行依赖；它只会在 0.1.x 用户的迁移说明中出现。

感谢 Stardew Valley、SMAPI、Generic Mod Config Menu 与模组社区提供的生态、工具和测试反馈。
