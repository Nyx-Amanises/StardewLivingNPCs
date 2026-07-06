# 12 · 游戏集成规格（WP12：Harmony 补丁 / SMAPI 事件接线 / UI）

> 开工前必读：README、00、01、本文档。实现代码落在 `LivingNPCs/Dialogue/GameHooks/`
> 与 `LivingNPCs/Dialogue/Ui/`，命名空间 `LivingNPCs.Dialogue.GameHooks` / `LivingNPCs.Dialogue.Ui`。
> 本文档只做功能级描述，不含旧代码；游戏/SMAPI/Harmony 的类型与签名为客观事实，逐字精确。

## 1. 目的与范围

WP12 负责把对话引擎（WP10 的 `IDialogueEngine`）接进星露谷本体：

1. 一组 Harmony 补丁，接管原版 NPC 对话的产生与显示时机（右键搭话、送礼、婚后台词、
   节日/事件台词、被动台词替换），把它们改道到 AI 生成管线；
2. SMAPI 事件接线（启动、存档载入、逐帧调度、资产注册的时序框架）；
3. 玩家自由文本输入 UI（原生对话框内打字）、"思考中"占位窗、流式对话窗的生命周期管理；
4. 旧 mod（`dandm1.ValleyTalk`）共存检测；
5. Android 平台差异的隔离层。

不在本包范围：生成管线内部（WP10）、LLM 客户端（WP11）、配置/GMCM 菜单与资产内容
（WP15）、持久化（WP14）、行为系统接线（WP16）。

## 2. 权属与搬运边界

按 02/03 的甄别结论，本包相关文件分三类：

**搬运件（阶段 A 已复制，本包只挂接、不重写）**：
- `Ui/StreamingDialogueWindow.cs`（MINE）——流式对话窗；
- `Ui/DialogueUiStateGuard.cs`（MINE）——对话 UI 状态守卫；
- `Ui/ThinkingDialogueController.cs`（MIXED 误报）——"思考中"对话框控制器；
- `Ui/NativeDialogueTextInputController.cs`（MIXED 误报）——原生对话框文本输入控制器；
- `GameHooks/DialogueBox_ThinkingDialogue_Patch.cs`（MIXED 误报）——DialogueBox 四连补丁；
- `GameHooks/Event_CheckAction_Patch.cs`（MIXED 误报）——事件/节日中发起输入的补丁
  （其中一处对旧上下文清理方法的调用按 03 §1.3 改接新接口）。

**重写件（本包按本文档行为规范重新实现）**：其余 13 个补丁、文本输入请求队列
（旧 TextInputHandler.cs 的功能）、启动接线（旧 ModEntry 的职责映射，§4.7）、
UI 文案取值助手中游戏集成侧用到的部分（实际由 WP15 的 `IDialogueContent`/i18n 提供）、
Android 平台助手三件套、游戏内时间戳类型（StardewTime 的功能，见 §5）、常量与枚举。

**明确废弃（不得搬运、也无需重写）**：
- `ValleyTalk/InputTextBox.cs`——**这是第三方 mod StackSplitRedux 的源码残片**，位于
  csproj 目录之外、从未参与编译，与本项目无关，禁止复制；
- `UI/ThinkingWindow.cs`、`UI/DialogueTextInputMenu.cs`、`UI/DialogueTextInputBox.cs`、
  `UI/DialogueTextInputMenuWrapper.cs`——早期独立菜单式输入/思考窗实现，现行代码中
  **零引用**（已被"原生对话框"方案取代），功能不进入新架构。其中安卓虚拟键盘的
  位置避让思路记录在 §4.6，供将来需要独立菜单时参考。

## 3. 外部契约（补丁目标签名表）

Harmony 实例 ID 用 `Yuki.LivingNPCs`（即 `ModManifest.UniqueID`）。所有目标均为
Stardew Valley 1.6 程序集 `StardewValley` 中的成员，签名逐字如下：

| # | 目标成员 | 补丁类型 | 用途一句话 |
|---|---|---|---|
| P1 | `StardewValley.NPC.checkAction(Farmer who, GameLocation l)` → `bool` | prefix | 按住触发键点击 NPC 时发起自由输入 |
| P2 | `StardewValley.NPC.CurrentDialogue` 属性 getter → `Stack<Dialogue>` | postfix | 拦截生成占位标记、记录台词/旁听 |
| P3 | `StardewValley.NPC.checkForNewCurrentDialogue(int heartLevel, bool noPreface)` → `bool` | prefix+postfix | 把新产生的常规台词替换为生成占位 |
| P4 | `StardewValley.NPC.GetGiftReaction(Farmer giver, Object gift, int taste)` → `Dialogue` | prefix | 送礼反应改走异步生成 |
| P5 | `StardewValley.NPC.addMarriageDialogue(string dialogue_file, string dialogue_key, bool gendered, string[] substitutions)` → `void` | prefix | 过滤配偶晨间家务台词并暂存原文 |
| P6 | `StardewValley.NPC._PushTemporaryDialogue(string translationKey)` → `void`（私有，按方法名字符串定位） | prefix | 临时台词（度假村等）替换为生成占位 |
| P7 | `StardewValley.NPC.tryToGetMarriageSpecificDialogue(string dialogueKey)` → `Dialogue` | prefix | 婚后特定键台词（fun/jobReturn）占位 |
| P8 | `StardewValley.NPC.tryToRetrieveDialogue(string preface, int heartLevel, string appendToEnd)` → `Dialogue` | prefix | 常规检索台词整体占位 |
| P9 | `StardewValley.Dialogue.chooseResponse(Response response)` → `bool` | prefix | 接管本 mod 生成的应答选项 |
| P10 | `StardewValley.Dialogue.TryGetDialogue(NPC speaker, string translationKey)` → `Dialogue`（静态） | prefix | 雨天台词占位 |
| P11 | `StardewValley.Game1.DrawDialogue(Dialogue dialogue)` → `void`（静态） | prefix | 吞掉带跳过标记的对话显示 |
| P12 | `StardewValley.Game1.drawDialogueBox()`（无参重载，静态）→ `void` | prefix | 空对白栈残留 UI 守卫 |
| P13 | `StardewValley.GameLocation.GetLocationOverrideDialogue(NPC character)` → `string` | prefix | 地点覆盖台词：输入触发 + 被动占位 |
| P14 | `StardewValley.MarriageDialogueReference.GetDialogue(NPC n)` → `Dialogue` | prefix | 婚后台词引用改走生成 |
| P15 | `StardewValley.Event.checkAction(xTile.Dimensions.Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)` → `bool` | prefix | 事件/节日中发起自由输入（搬运件） |
| P16 | `StardewValley.Menus.DialogueBox.getCurrentString()` → `string` | prefix | 输入框/思考框显示文本接管（搬运件） |
| P17 | `StardewValley.Menus.DialogueBox.draw(SpriteBatch b)` → `void` | postfix | 叠画输入中的玩家文本（搬运件） |
| P18 | `StardewValley.Menus.DialogueBox.receiveLeftClick(int x, int y, bool playSound)` → `void` | prefix | 输入/思考期间吞左键（搬运件） |
| P19 | `StardewValley.Menus.DialogueBox.receiveKeyPress(Keys key)` → `void` | prefix | 输入按键路由、思考中 Esc 取消（搬运件） |

无 transpiler。P16–P19 与 P15 为搬运件补丁，本包负责把它们的 Harmony attribute 保持
可被 `PatchAll` 扫到（或集中改为显式 `harmony.Patch(...)` 注册，二选一，见 §5）。

补丁涉及的其他游戏 API（供实现对照，均为公开成员，除注明外）：
`Dialogue.dialogues`（`List<DialogueLine>`）、`DialogueLine.Text`、`Dialogue.speaker`、
`Dialogue.temporaryDialogueKey`、`Dialogue.removeOnNextMove`、`Dialogue.exitCurrentDialogue()`、
`Dialogue.getResponseOptions()`、`Response.responseKey`/`responseText`、
`DialogueBox.characterDialogue`、`DialogueBox.characterIndexInDialogue`、
`Game1.currentSpeaker`、`Game1.activeClickableMenu`、`Game1.exitActiveMenu()`、
`Game1.dialogueUp`/`dialogueTyping`/`dialogueButtonShrinking`/`currentDialogueCharacterIndex`、
`Game1.keyboardDispatcher.Subscriber`（`IKeyboardSubscriber`）、`Game1.eventUp`、
`Game1.content.LoadString`/`LoadStringReturnNullIfNotFound`、`Game1.LoadStringByGender`、
`NPC.grantConversationFriendship(Farmer who, int amount = 20)`、`NPC.shouldSayMarriageDialogue`、
`NPC.currentMarriageDialogue`、`NPC.IsInvisible`、`NPC.isSleeping`、`Farmer.CanMove`、
`Event.actors`、`Event.FestivalName`、`Game1.characterData`、`Game1.getCharacterFromName`。
旧实现另经反射写过 `Dialogue` 的私有字段 `finishedLastDialogue`（P9 中置 false）；
新实现若可用公开 API 达成同效可不用反射，否则保留该反射点并注释原因。

**内部标记契约**（与 WP10 共享的字符串常量，精确保留，存于新常量类）：
- 生成占位标记 `$$$%%%`（可后接 `#` + 原版台词原文，作为生成参考）；
- 显示跳过标记 `$$%%`；
- 本 mod 对话键前缀 `SLD_`，保留键：`SLD_Silent`、`SLD_TypedResponse`、`SLD_Input`、
  `SLD_Thinking`、`SLD_Streaming`、`SLD_Error`、`SLD_Conversation`。
  （这些是已发布存档/历史数据中可能出现的标识符，属兼容契约。）

## 4. 行为规范

### 4.1 通用跳过条件（几乎所有补丁共用）

补丁在以下情况必须放行原版逻辑（prefix 返回 true / postfix 不动结果）：

- 引擎总开关关闭、LLM 不可用（WP11 报告初始化失败）；
- 该 NPC 被 `IDialogueEngine.IsEnabledFor(npc)` 判为不启用。其内部含：
  NPC 为 null；命中"策略黑名单"（Ridgeside Village NPC 名单及
  `Custom_Ridgeside_` 前缀地图内的 NPC，名单数据随 WP10 的策略类搬运/重建）；
  用户配置的禁用角色列表命中；"未授权内容包"模式下该 NPC 无传记数据；
- **频率抽签失败**：配置提供三个 0–4 档频率（常规 / 婚后 / 送礼），4 = 总是，
  0 = 从不，1–3 = 按 x/4 概率随机。带"当日记忆"变体：同一 NPC 当日首次抽签结果
  缓存到当天结束（游戏日变更时失效），常规台词类补丁用带记忆变体，避免同一天
  内忽有忽无；
- **被动类补丁**（P2、P3、P6、P7、P8、P10、P13 的被动半段、P14）额外受
  "常规右键也生成 AI"布尔配置门控（默认关，关闭时这些补丁全部放行原版）；
- Android 且网络不可用（见 §4.6）。

事件/节日中的特殊性：P1（NPC.checkAction）在事件里不会被原版调用，入口换成 P15
（Event.checkAction）；P2 在事件 Speak 指令路径下只记录不改写（见 4.2.2）。

### 4.2 逐补丁行为

#### P1 `NPC.checkAction` prefix —— 主动搭话入口
先清理该 NPC 对白栈中残留的"思考中"占位对白（防上次生成异常残留）。然后判断：
配置的触发键（`StardewModdingAPI.SButton` 类型，默认 `SButton.LeftAlt`，可映射手柄键）
当前是否按下（用 `IModHelper.Input.IsDown`）。以下任一为真则放行原版：NPC 隐身
（`IsInvisible`）、NPC 睡眠（`isSleeping.Value`）、玩家不可移动（`who.CanMove` 为假）、
触发键未按下或未配置（`SButton.None`）、NPC 未启用（4.1，用主动频率=送礼档以外的
默认满档，即不抽签）。全部通过时：清空引擎的"上次会话上下文"，取该 NPC 的
"开始对话提示语"文案（i18n 键，带 NPC 显示名 token，缺省英文兜底），向输入请求
队列（§4.4）提交一次输入请求；`__result` 置 false（表示未发生互动？不——原方法
语义是"是否处理了此次交互"，此处置 false 并跳过原方法，实测可阻止原版对话弹出），
prefix 返回 false。

#### P2 `NPC.CurrentDialogue` getter postfix —— 占位拦截与台词记录
这是最复杂的补丁。行为分四步：

1. 若栈顶是"思考中"占位对白但思考控制器已不认账（非当前活跃），弹掉它并返回
   （防串台）。
2. 空栈直接返回。
3. **调用来源过滤**：旧实现用 `System.Diagnostics.StackTrace` 取上溯第 2 帧方法名，
   仅当来源方法名含 `drawDialogue` 才继续——即只在游戏真正要把这段对白画出来时才
   动作，避免在 AI/寻路等无关读取时误触发。新实现**必须提供等效的来源判定**，
   允许沿用栈帧探测（记录其脆弱性），更推荐改为：由 P11（`Game1.DrawDialogue`
   prefix）在真正显示前做拦截/记录，P2 只负责第 1 步的清栈守卫——两方案二选一，
   在开放问题里向用户报备所选方案。
4. 来源确认后，取栈顶对白首行：
   - **首行文本 == 生成占位标记 `$$$%%%`**：Android 网络不可用时弹掉占位、压入一条
     内容为 `...` 的 `Dialogue` 并返回。否则弹掉占位；若该对白还有后续行，把后续行
     文本用空格连接作为"原版参考台词"；向生成调度器（WP10 的异步入口）发起一次
     "基础生成"请求（NPC、键 `default`、参考台词）；置 `Game1.currentSpeaker` 为该
     NPC；清空整个栈并返回（后续由思考窗→生成结果接管显示，见 4.5）。
   - **其他文本（原版即将显示的真台词）**：记录进历史。若来源帧方法名以 `Speak`
     开头（事件脚本 Speak 指令），按"事件台词"记录：携带 `Game1.currentLocation.currentEvent`
     的演员列表与 `FestivalName`；否则按"日常台词"记录，旧实现用"IL 偏移不大于
     历史最小值才记录"的启发式去重（同一台词经多个调用点重复到达时只记录最早
     调用点的那次）——新实现可改为按（NPC、游戏日、文本）去重，效果等同即可。
     无论哪类，同地图 4.5 格内、可收礼、非本人的 NPC 都追加一条"旁听记录"。
     记录接口对齐 WP10 的历史模块。

#### P3 `NPC.checkForNewCurrentDialogue` prefix+postfix —— 常规台词替换
prefix 仅留 Trace 日志（新实现可省略 prefix）。postfix：原方法返回 false 或 NPC
未启用（被动门控+常规频率）时不动。Android 网络不可用不动。当前已有一次针对该
NPC 的生成在途时不动。栈为空/栈顶无台词行不动；栈顶首行已是占位标记不动（防重复）。
否则：把栈顶对白的全部非空行用空格连接为原文；构造新文本 = 占位标记，若原文非空
则 `占位标记#原文`；对话键 = 栈顶的 `temporaryDialogueKey`（若非空），否则
`heart_{heartLevel}`（`noPreface` 为真时前缀用 `default` 代替 `heart`）；弹掉栈顶，
压入新 `Dialogue(npc, 对话键, 新文本)` 并把原对白的 `removeOnNextMove` 与
`temporaryDialogueKey` 抄过去。

#### P4 `NPC.GetGiftReaction` prefix —— 送礼反应
NPC 未启用（送礼频率档，不带当日记忆）→ 放行。已有该 NPC 的生成在途 → 放行。
Android 无网络 → 放行。否则向生成调度器发起"送礼生成"（NPC、礼物 `Object`、
好感 `taste` 原样透传给 WP10）；`__result` 返回一条内容为跳过标记 `$$%%` 的
`Dialogue` 并对其调用 `exitCurrentDialogue()`（这样原版调用方拿到"合法对白"但
P11 会吞掉显示，不打断送礼流程本身——好感与每日送礼计数仍由原版逻辑处理）；
prefix 返回 false。

#### P5 `NPC.addMarriageDialogue` prefix —— 配偶家务台词过滤
无条件跳过原方法（prefix 返回 false），并复刻原版效果：构造
`MarriageDialogueReference(dialogue_file, dialogue_key, gendered, substitutions)`。
若 `dialogue_key` **不在**下述跳过清单，则 `shouldSayMarriageDialogue.Value = true`
且把引用加进 `currentMarriageDialogue`（等同原版）。若在清单中：改为解析出本地化
原文（键 = `DialogueFile + ":" + DialogueKey`，gendered 时用 `Game1.LoadStringByGender`，
否则 `Game1.content.LoadString`，解析失败静默跳过），暂存进一个静态"并入下次婚后
台词"缓冲。跳过清单（逐字）：`NPC.cs.4463`、`NPC.cs.4462`、`NPC.cs.4470`、
`NPC.cs.4474`、`NPC.cs.4481`、`MultiplePetBowls_watered`（皆为"我浇了水/喂了动物/
修了栅栏"类晨间播报，单独显示很啰嗦，合并进下一次生成参考更自然）。
注意：此补丁**不检查 NPC 是否启用**——它对所有配偶生效；缓冲的内容只会被 P14
消费，P14 会做启用检查。已知副作用：引擎关闭时该补丁仍改变原版行为（清单内
台词消失）。新实现应修正为：NPC 未启用时放行原版。

#### P6 `NPC._PushTemporaryDialogue` prefix —— 临时台词占位
被动门控+常规频率；Android 无网络放行。键以 `Resort` 开头且存在
`Resort_Marriage` + 原键后缀的字符串资源时，先把键重映射过去（已婚 NPC 度假村
台词）。若栈顶已有相同 `temporaryDialogueKey` 则放行（原版自己会去重）。否则用
`Game1.content.LoadString(translationKey)` 取原文，压入 `Dialogue(npc, translationKey,
占位标记#原文)`，`removeOnNextMove = true`、`temporaryDialogueKey = translationKey`，
然后**仍返回 true 放行原方法**——原版看到栈顶键相同会跳过自己的压栈，等效于替换。
任何异常时返回 false 抑制原方法（保守吞错）。

#### P7 `NPC.tryToGetMarriageSpecificDialogue` prefix
被动门控+婚后频率（不带当日记忆）；Android 无网络放行。仅当 `dialogueKey` 以
`funReturn_` 或 `jobReturn_` 开头时：`__result` = 占位标记对白（键原样），返回 false。
其余放行。

#### P8 `NPC.tryToRetrieveDialogue` prefix
被动门控+常规频率（带当日记忆）；Android 无网络放行。直接
`__result = new Dialogue(npc, $"{preface}_{heartLevel}", 占位标记)`，返回 false。

#### P9 `Dialogue.chooseResponse` prefix —— 应答选项接管
说话者未启用 → 放行。**只要有任何一个选项的 `responseKey` 不以 `SLD_` 前缀开头
→ 放行**（保证原版问答/事件选择完全不受影响；本补丁只接管 WP10 生成的选项组）。
- 选项键 == `SLD_Silent`：向会话历史记录一条空的玩家台词，`__result = true`，
  返回 false（对话正常关闭）。
- 其余情况先做两步清理：若对白最后一行文本等于"请回应"提示文案（WP10 在生成
  对白尾部追加的引导行，文案键由 WP15 提供）则移除之；把本对白中尚未进入
  当前会话上下文的行并入上下文聊天历史（排除重复与字面 `skip` 行）。
- 选项键 == `SLD_TypedResponse`：向输入请求队列提交输入请求（提示语用"你的回应"
  文案键；携带 NPC、NPC 当前 `LoadedDialogueKey`（为空用 `default`）、上述聊天
  历史），`__result = true`，返回 false（关闭当前对话，让输入框接管）。
  注：`LoadedDialogueKey` 是**本 fork 加在 Dialogue/NPC 侧的扩展属性**，非原版成员，
  新实现由 WP10 定义等价物（生成请求的对话键回传）。
- 普通文本选项：把私有字段 `finishedLastDialogue` 置 false（防原版把对话标记为
  已完结导致 UI 状态错误），以聊天历史 + 该选项文本（玩家台词）发起"会话生成"，
  `__result = true`，返回 false。

#### P10 `Dialogue.TryGetDialogue`（静态）prefix —— 雨天台词
被动门控+常规频率（带当日记忆）。仅当 `translationKey` 以
`Characters\Dialogue\rainy:` 开头（反斜杠为字面单反斜杠）时：
`__result = new Dialogue(speaker, translationKey, 占位标记)`，返回 false。其余放行。
注意目标是静态方法，patch 方法不得声明 `__instance`。

#### P11 `Game1.DrawDialogue(Dialogue)` prefix —— 跳过标记吞显
对白为 null / 无行 → 放行。首行文本以跳过标记 `$$%%` 开头 → 返回 false（什么都
不显示；配合 P4 让送礼流程静默等待异步生成）。其余放行。

#### P12 `Game1.drawDialogueBox()` prefix —— 残留 UI 守卫（挂接搬运件）
转调搬运件 `DialogueUiStateGuard.TrySkipEmptySpeakerDraw()`：当前 `Game1.currentSpeaker`
非空但其对白栈已空时，清理对话 UI 全局状态（关菜单、清 speaker、复位
`dialogueUp`/`dialogueTyping`/`dialogueButtonShrinking`/`currentDialogueCharacterIndex`、
非事件中恢复玩家移动）并返回 false 跳过本次绘制；否则放行。这是对"思考窗/输入框
关闭时序竞态导致原版画空栈崩溃"的最后防线。

#### P13 `GameLocation.GetLocationOverrideDialogue` prefix —— 双重角色
`character` 为 null → 放行。**前半（主动输入）**：触发键按下 且 NPC 不隐身、不睡、
玩家可移动、NPC 启用 → 与 P1 相同地发起输入请求，`__result = string.Empty`，
返回 false。（P1 已拦截大多数点击；此补丁覆盖原版从地点覆盖台词入口进来的路径，
两者都要判键，先到先得。）**后半（被动占位）**：被动门控+常规频率（带当日记忆）
不通过 → 放行；通过 → `__result = 占位标记`，返回 false。

#### P14 `MarriageDialogueReference.GetDialogue` prefix —— 婚后台词生成
被动门控+婚后频率。已有该 NPC 生成在途 → 放行。消费 P5 的"并入下次"缓冲：缓冲
非空时把当前引用自身的本地化原文也解析出来追加（失败跳过），把全部缓冲行用空格
连接为参考台词并清空缓冲。然后按调用来源分两路（旧实现again用栈帧第 2 帧方法名
是否含 `checkAction` 判断；新实现可用"当前是否处于玩家交互调用"标志替代）：
- 来自 checkAction（玩家主动右键配偶）：返回占位标记对白（有参考台词则
  `占位#参考台词`，键 = `DialogueKey`），走 P2 的异步占位路径；
- 其他（原版在非交互时机预取，如清晨排队）：**同步**调用生成管线拿结果对白
  （阻塞 `.Result`）。⚠ 已知风险：这会卡主线程直到生成完成/超时。新实现必须
  改为占位对白 + 异步（与 checkAction 路径合流），除非用户裁决保留同步。
生成结果非 null 时 `__result` = 结果、返回 false；否则放行。

#### P15 `Event.checkAction` prefix —— 事件/节日中发起输入（搬运件）
触发键未按下 / 玩家不可移动 / 事件无演员 → 放行。在演员列表中找与点击图块距离
≤1.25 格、不隐身不睡的 NPC；找不到或 NPC 未启用 → 放行。否则清空会话上下文、
发起输入请求（同 P1），`__result = true`，返回 false。搬运时把其中对旧上下文
清理方法的调用改接新引擎接口（03 §1.3）。

#### P16–P19 `DialogueBox` 四连（搬运件）
- getCurrentString prefix：若该 box 是活跃的输入框 → 返回提示语文本并把
  `characterIndexInDialogue` 钉到大值（免打字机动画），拦截；否则若是活跃思考框 →
  返回思考文案 + 1–3 个循环省略号（450ms 周期），拦截；否则放行。输入框判定
  优先于思考框。
- draw postfix：输入框活跃时在对白框文本区叠画玩家已输入文本 + 闪烁光标
  （450ms），逐字符换行、超行滚动显示尾部、右侧留头像区。
- receiveLeftClick prefix：输入框或思考框活跃时吞掉点击。
- receiveKeyPress prefix：输入框活跃 → 路由特殊键（Enter 提交、Esc 取消、
  Left/Right/Home/End 光标、Back/Delete 删除+按住重复）并拦截；思考框活跃 →
  仅 Esc 触发"取消当前生成"（见 4.5），其余全吞；否则放行。

### 4.3 SMAPI 事件接线全景

新架构中对话引擎的接线由一个 `DialogueEngineBootstrapper`（名字建议，§5）完成，
并入 LivingNPCs 现有 `ModEntry.Entry` 调用。订阅全景（含各 WP 挂载点，本包负责
框架与次序）：

| 事件 | 订阅者与职责 | 说明 |
|---|---|---|
| `GameLoop.GameLaunched` | ① 共存检测（§4.8）；② 通过后执行 `harmony.PatchAll()`（或显式注册全部补丁）；③ GMCM 注册对话引擎配置节（WP15） | LivingNPCs 现有 GMCM 注册也在此事件，注意合并为同一处理器内先后调用 |
| `GameLoop.SaveLoaded` | ① 存档作用域状态重置（引擎会话缓存、事件历史会话缓存，WP10/WP14 暴露 Reset 接口）；② 会话转录导出器全量导出（Diagnostics 搬运件）；③ 用量台账载入当前存档（WP14） | 多人联机时注意 WP14 的仅主机规则 |
| `GameLoop.Saving` | 用量台账写盘、对话/事件历史写盘（WP14 的订阅，Bootstrapper 只保证注册时机） | |
| `GameLoop.ReturnedToTitle` | 存档作用域状态重置（同 SaveLoaded ①）、用量台账切回会话档 | |
| `GameLoop.UpdateTicked` | ① 输入请求队列泵（§4.4）；② 生成调度泵（有挂起请求且 `Game1.activeClickableMenu == null` 时开始生成并弹思考窗，§4.5）；③ 输入控制器的按键重复计时（仅输入会话期间动态订阅/退订）；④ 生成完成后的"回主线程"一次性处理器（挂上即拆） | 均为轻量早退检查 |
| `Content.AssetRequested` / `Content.AssetsInvalidated` | 资产注册与缓存失效（提示词骨架、世界观、逐 NPC 传记），归 WP15 | **时序陷阱见下** |
| `Display.*`、`Input.ButtonPressed`、`GameLoop.DayStarted` | 本包**不订阅** | 触发键不用 ButtonPressed，而是在补丁 prefix 内用 `IModHelper.Input.IsDown(SButton)` 即时查询；"当日"概念用 `Game1.Date.TotalDays` 惰性判断，无需 DayStarted |

**时序要求**：
1. 旧实现的 AssetRequested 注册散落在懒加载单例的构造器里（提示词缓存、世界观
   构建器、逐 Character 实例），首次访问才注册——存在"资产在注册前被请求"的隐患。
   新实现**必须在 Entry 阶段集中注册全部资产提供器**（WP15 提供注册入口，
   Bootstrapper 调用），资产名前缀按 01 §4 为 `Mods/Yuki.LivingNPCs/`。
2. `PatchAll` 从 Entry 移到 GameLaunched（共存检测之后），Harmony 在 GameLaunched
   打补丁完全来得及（首次对话交互远晚于此）。
3. 控制台命令注册在 Entry（`ICommandHelper.Add`）。沿用两条命令的功能：
   用量统计（子命令 空/`export`/`reset`）与遗忘历史（子命令 空或`near`/NPC 名/
   `all`+`confirm` 二段确认；near = 4.5 格内最近 NPC；NPC 名解析支持内部名/显示名/
   `Game1.characterData` 键，忽略大小写）。命令名改为 `livingnpcs_tokens` /
   `livingnpcs_forget`（开放问题 #3）。
4. 引擎总开关（配置）关闭时：跳过补丁、调度器、输入管理器与命令注册，但
   GameLaunched/SaveLoaded/ReturnedToTitle 的基础订阅照常（保证 GMCM 里能重新开启
   ——旧实现关掉后需重启游戏，新实现照抄该语义即可，运行时热开启不作要求）。

### 4.4 自由文本输入交互流

**入口**（三个，全部汇入同一个输入请求队列）：
- 键盘/手柄：按住配置触发键（`SButton`，默认 LeftAlt；手柄玩家可在 GMCM 绑定
  手柄键）+ 左键/手柄 A 点击 NPC → P1（常规）/ P13 前半（地点覆盖路径）/ P15
  （事件、节日中）；
- 对话中续聊：生成对白的选项列表中选"自己输入"（P9 的 `SLD_TypedResponse`）、
  或流式窗的 Typed 选项；
- Android：无物理触发键，实际入口仅"对话中续聊"一类（开放问题 #4）。

**队列语义**（重写件，替代旧 TextInputManager）：请求 = {提示语, NPC, 对话键,
聊天历史（可空）}。同一时刻只保留最后一个请求。UpdateTicked 泵在
`Game1.activeClickableMenu == null` 时消费请求 → 启动原生输入控制器。请求发起方
（补丁）已把当前对话关闭（`__result=true` 等），所以通常下一两帧即打开。

**输入会话**（搬运件 NativeDialogueTextInputController 承担，此处记录其对外行为）：
- 打开：向 NPC 对白栈压一条键为 `SLD_Input`、文本为提示语的 `Dialogue`，
  `Game1.currentSpeaker = npc`，调 `Game1.DrawDialogue(dialogue)` 让原版弹出
  DialogueBox，然后记住这个 box；把 `Game1.keyboardDispatcher.Subscriber` 设为自己的
  `IKeyboardSubscriber` 实现（接收字符输入，含 IME 提交的字符串——中文输入依赖
  此通道）；动态订阅 UpdateTicked 做 Back/Delete 按住重复（首延迟 320ms、重复
  48ms）。字符上限 500，控制字符丢弃。
- 输入期间的输入抑制：P16 钉住打字机动画并显示提示语；P17 叠画输入文本与光标；
  P18 吞左键；P19 把按键送进控制器——游戏本体因 DialogueBox 打开天然处于"对话中"
  状态（玩家冻结），无需额外抑制移动。
- **提交（Enter）**：收尾（见下）后带文本回调。回调侧（重写件）：文本空白 → 仅
  重置状态，什么都不发生；非空 → 把文本作为玩家台词追加进请求携带的聊天历史，
  调 `npc.grantConversationFriendship(Game1.player)`（每天首次搭话的好感与"已交谈"
  勾选，等同原版对话），然后发起"会话生成"（对齐 WP10 的 GenerationRequest，
  类型=玩家自由输入会话）。
- **取消（Esc）**：收尾后以空字符串回调 → 不生成、无副作用。
- 收尾：从 NPC 对白栈精准移除 `SLD_Input` 对白（引用相等）；若正关闭的就是输入框
  或 NPC 栈已空，用 UiStateGuard 清对话 UI 状态并恢复玩家控制；解除键盘订阅
  （仅当 Subscriber 仍是自己）；退订 UpdateTicked；清空全部静态状态。

### 4.5 "思考中"窗与流式窗生命周期

**思考中（非流式路径，当前默认）**：
1. 生成调度泵（WP10 侧的异步编排器，其 UI 钩子属本包契约）在开始一次生成时调用
   思考控制器 Start(npc)：向 NPC 对白栈压键 `SLD_Thinking`、文本"某某正在思考…"
   （i18n，带显示名 token）的对白，`removeOnNextMove = false`、`temporaryDialogueKey`
   = 同键，`Game1.DrawDialogue` 弹出，记住 box。
2. 显示期间：P16 提供动态省略号文本；P18/P19 吞输入；**Esc 语义** = 调度器
   `CancelActiveGeneration()`：世代号自增 + 取消标志置位，立即关思考窗；迟到的
   生成结果因世代号不匹配被静默丢弃（底层 LLM 请求靠自身超时结束，不强杀）。
3. 生成完成（后台线程）：**必须**通过"下一个 UpdateTicked 一次性处理器"回主线程
   （桌面端无 SynchronizationContext，await 续体在线程池上，而 DrawDialogue/菜单
   状态只能主线程动）。回到主线程后：世代号仍匹配 → 关思考窗，若结果对白非空则
   `Game1.currentSpeaker = npc` + `Game1.DrawDialogue(结果)` 交接给原版 DialogueBox
   （结果对白可含 WP10 生成的 `SLD_` 应答选项，后续走 P9）；世代号不匹配 → 直接
   丢弃。异常路径同样回主线程：关思考窗 + 显示 `...` 兜底对白（键 `SLD_Error`）。
4. 收尾清栈逻辑与输入框对称（RemoveStale / TryDiscardInactiveTop / UiStateGuard），
   P1 与 P2 的守卫步兜住所有残留。

**流式窗（搬运件 StreamingDialogueWindow，当前为已具备但未启用的路径）**：
`IClickableMenu` 子类，作为 `Game1.activeClickableMenu` 直接显示（不经 DialogueBox）。
外观复用原版对话框框体与头像（内部临时构造键 `SLD_Streaming` 的 DialogueBox 取
布局）。对外接口：构造(npc)；`AppendToken(string)`（线程安全，可从流式回调线程
直呼）逐 token 追加并重排页；`Complete(finalText, options, onResponseSelected,
onFinished)` 定稿并挂应答选项。交互：点击/Enter/Space 推进（先补完打字机、再翻页、
最后进选项列表或结束）；选项列表用 Up/Down/W/S + Enter/Space 选择、鼠标可点；
Esc 在选项列表选中 Silent 类选项（无则直接结束）。选项模型（搬运件
StreamingResponseOption）分三类：普通文本（作为玩家台词再次发起会话生成）、
Silent（记录空玩家台词）、Typed（转输入请求队列）。调度器侧的流式编排（消费
`IDialogueEngine.StreamAsync` + 本窗）在旧代码中存在但无调用方——**是否启用流式
路径由 WP10 文档裁决**，本包只保证窗体搬运可用、接口如上。

### 4.6 Android 平台差异全清单

1. **平台检测**：`Game1.game1` 所在程序集名含 `Android` 判为安卓（运行时检测，
   无条件编译）。
2. **网络可用性门控**：P2、P3、P4、P6、P7、P8 在动作前查网络。桌面恒真；Android
   实际探测（探测实现归 WP11 重写件 NetworkHelper，配置项"跳过连接检查"可绕过），
   失败后每秒重试、共 5 次。⚠ 旧实现是同步阻塞（async over sync `.Result`），最坏
   卡游戏线程 5 秒——新实现改为：首查失败立即放行原版台词，同时后台探测刷新一个
   缓存标志供下次使用。
3. **虚拟键盘**：原生输入路径依赖 `Game1.keyboardDispatcher`，安卓端由游戏本体
   IME 桥接（无需本 mod 干预）。历史遗留的独立菜单方案曾按"虚拟键盘 ≈ 屏幕高
   1/3"把菜单上移避让——仅作参考记录，新架构不实现。
4. **文件 IO**：安卓存储权限受限，全部落盘路径必须限制在
   `IModHelper.DirectoryPath` 之下，并把 `UnauthorizedAccessException`/`IOException`
   降级为日志+返回空（本包提供该安全 IO 助手的重写件，Diagnostics/WP14 消费）。
5. **主线程回跳**：生成完成回 UI 的"下一 tick"机制对全平台统一（见 4.5.3），
   安卓不额外分支。
6. **输入入口**：无键盘触发键，见 4.4/开放问题 #4。

### 4.7 启动/关闭时序：旧 ModEntry 职责映射

旧引擎 ModEntry 消失，其职责并入 `LivingNPCs/ModEntry.cs`（Yuki 原创，可直接改）。
现有 Entry 次序（I18n → 读配置/迁移/校验 → ModCompatibility → 订 GameLaunched →
EnableMod 早退 → 社区档案加载 → BehaviorEngine 构造+RegisterEvents）。需并入的
初始化清单（建议插在 BehaviorEngine 之后，全部包进 `DialogueEngineBootstrapper`）：

1. 对话引擎配置载入（并入 LivingNPCs ModConfig 或子对象，WP15 定；含旧 config
   迁移，WP14 定）；
2. 日志门面初始化（统一 `[LivingNPCs]` 前缀，实际用 SMAPI IMonitor）；
3. 资产提供器集中注册（WP15 入口，本包保证时机在任何资产请求前）；
4. LLM 提供商选择与客户端初始化（WP11：按配置的 Provider 名从注册表取实现，
   非法名记错误日志并保持引擎关闭）；
5. 内容包 AI 授权扫描：遍历 `IModRegistry.GetAll()` 中 `IsContentPack` 的包，
   manifest `ExtraFields` 无 `PermitAiUse`（bool 或可解析字符串，键名忽略大小写）
   =true 者记入"未授权"集合；集合非空时置全局 BlockModdedContent 标志（4.1 用），
   并输出警告日志说明"包内容照常显示但不参与 AI 生成、作者可在 manifest 加
   permitAiUse 授权"；
6. 输入请求队列与生成调度泵的 UpdateTicked 订阅；
7. 用量追踪器事件注册（WP14）与控制台命令注册（§4.3.3）；
8. GameLaunched 处理器扩展：共存检测 → PatchAll → GMCM 对话节注册；
9. SaveLoaded / ReturnedToTitle 处理器扩展（§4.3 表）。

`GetApi()`：LivingNPCs 现有公共 API（GetConversationContext 等三方法）保留归 WP16；
旧引擎的 interop API 不再暴露（00 非目标）。关闭时序无特殊要求（SMAPI 无卸载）；
`Saving` 落盘由 WP14 保证。

### 4.8 旧 mod 共存检测（01 §5）

GameLaunched 中：`helper.ModRegistry.IsLoaded("dandm1.ValleyTalk")` 为真 →
- SMAPI Error 级日志（i18n，中英双语说明"检测到旧 ValleyTalk，对话引擎已停用，
  请删除 Mods 下的 ValleyTalk 文件夹（注意可能是嵌套的 ValleyTalk/ValleyTalk）"）；
- 进存档后 HUD 提示一次（`Game1.addHUDMessage`，`HUDMessage.error_type` 类型；在
  SaveLoaded 时机弹，GameLaunched 时还没进世界看不到）；
- **跳过 PatchAll 与调度器启动**（两套补丁互踩是硬风险），资产注册可照常；
- 行为系统（BehaviorEngine）不受影响照常运行。
检测放 GameLaunched 而非 Entry，因 Entry 阶段其他 mod 可能尚未全部载入 registry。

## 5. 新类型与接口建议

（命名可调，职责边界不可调；均在 `LivingNPCs.Dialogue.*`）

- `GameHooks.DialogueEngineBootstrapper`：§4.7 清单的唯一入口，暴露
  `Attach(IModHelper, IMonitor, ModConfig)`；持有 Harmony 实例与共存检测结果。
- `GameHooks.PatchGuards`（静态）：§4.1 通用跳过条件的集中实现
  （`IsEnabledFor` 转发 + 频率抽签 + 当日记忆 + 被动门控 + 网络门控），全部补丁
  只调用它，不各自散写。
- `GameHooks.*_Patch`：每个目标一个补丁类，Harmony attribute 标注，类名按
  `目标类型_方法_Patch` 惯例；或集中显式注册——二选一后全包统一。
- `GameHooks.DialogueMarkers`（静态常量）：§3 的标记与 `SLD_` 键名。
- `Ui.TypedInputRequestQueue`：§4.4 队列（替代旧 TextInputManager）。
- `Ui.GenerationUiCoordinator`：§4.5 思考窗生命周期 + 世代号/取消 + 回主线程
  一次性处理器的封装；对 WP10 暴露 `BeginThinking(NPC)`、`CompleteOnMainThread(...)`、
  `CancelActive()`。（旧代码中这坨逻辑长在异步编排器里；新架构把 UI 部分留在
  WP12，编排留在 WP10，以此接口缝合。）
- `Platform.AndroidEnv`（静态）：IsAndroid、安全文件 IO；`Platform.NetworkGate`：
  §4.6.2 的非阻塞网络门控（探测委托注入自 WP11）。
- `GameTime.StardewTimestamp`：游戏内时间戳（年/季/日/时刻），支持相加减天数、
  比较、`ToAbsoluteDays`（年×112+季×28+日）、"多久之前"的本地化描述分档
  （刚刚/一小时内/今天早些/昨天/N 天前/N 天前含日期/今年早些/去年/很久以前，
  文案键归 WP15）。（旧 StardewTime 为 UPSTREAM，须重写；WP10/14 也要用，
  由本包出类型、字段语义按上述钉死。）
- 枚举重建（UPSTREAM，字面为游戏数据标识符，按需重建）：星期
  （Mon…Sun+Generic）、季节（Spring/Summer/Fall/Winter）、配偶动作
  （patio/funLeave/jobLeave/funReturn/jobReturn/spouseRoom——对应原版婚后行为键，
  P7 消费其中两个前缀）、随机情境（Rainy/Indoor/Outdoor/OneKid/TwoKids/
  Good/Neutral/Bad）。主要消费方是 WP10 的传记数据模型，放
  `LivingNPCs.Dialogue.Engine` 亦可，与 WP10 协商归属。

## 6. 与其他工作包的接口

- **WP10（引擎）**：本包只经 `IDialogueEngine`（IsEnabledFor / GenerateAsync /
  StreamAsync）发起生成；补丁收集的台词/旁听/事件记录经 WP10 暴露的历史记录接口
  写入（Append 语义，含时间戳与来源类型）；`GenerationRequest` 需能表达：类型
  （基础/会话/送礼）、NPC、对话键、参考原文、聊天历史、礼物+taste。生成结果对白
  的 `SLD_` 选项组格式由 WP10 定义，P9 按 §4.2 消费。冲突时以 WP10 文档为准。
- **WP11（LLM）**：网络探测委托；提供商初始化失败信号（4.1 总门控）。
- **WP15（内容/配置）**：本文提到的全部 UI 文案键（开始对话提示、你的回应、
  正在思考、请回应引导行、共存警告、命令输出）与配置项（触发键、三频率、被动
  生成开关、禁用角色表、跳过连接检查、TypedResponses 模式）;资产注册入口与
  GMCM 注册次序。
- **WP14（持久化）**：SaveLoaded/Saving/ReturnedToTitle 的落盘与迁移挂载点。
- **WP16（行为系统）**：生成完成后的 RecordExchange 直调发生在 WP10 编排内，
  本包不触碰；LivingNPCs ModEntry 的改造（§4.7）与 WP16 共用一个文件，先后合并
  时以 Bootstrapper 单入口原则避免冲突。

## 7. 验收要点

1. 全部 19 个补丁目标在 SMAPI 日志无 patch 失败告警；`harmony.GetPatchedMethods()`
   数量与 §3 表一致。
2. 手动冒烟（桌面）：按住 LeftAlt 点 NPC 弹输入框，中文 IME 可输入，Enter 后出
   思考窗→生成对白→选项可选/可静默/可续输；Esc 在输入框=取消无副作用、在思考窗
   =取消生成且不再弹结果；送礼后无原版即时反应、稍后出生成反应且好感变化正常；
   雨天/心级台词按频率配置出现占位生成；配偶晨间家务播报不再单独弹出。
3. 节日中（如蛋节）按住触发键点 NPC 能发起输入；节日台词被记录进历史（P2 事件
   路径）；不按键时节日原版交互完全正常。
4. 频率调 0 后所有被动路径与原版逐字节一致；禁用角色表命中的 NPC 全路径原版。
5. 装一个未授权内容包 → 启动出警告、外来 NPC 不生成；manifest 加 `PermitAiUse:
   true` 后恢复。
6. 与旧 `dandm1.ValleyTalk` 同装 → 错误日志 + 进档 HUD 提示 + 本引擎补丁未注册
   （原版对话正常，LivingNPCs 行为系统正常）。
7. 快速连点/边生成边走开/生成中换图：无空栈崩溃、无卡死残留 UI（P12 守卫日志
   最多 Trace/Warn，不出现异常）；玩家控制总能恢复（非事件中）。
8. 现有 `LivingNPCs.Tests` 全绿；本包新增行为的可测部分（队列语义、世代号取消、
   频率抽签+当日记忆、名称解析）有单测。

## 8. 开放问题

1. **P2 的调用来源判定**：沿用栈帧探测还是改为 P11 集中拦截（§4.2.2 方案 B）？
   方案 B 更稳但改变了记录时点（DrawDialogue 才记录），需确认旁听/事件记录不漏。
2. **P14 的同步生成路径**：本文档建议一律改异步占位；若用户观察到原版某些时机
   （清晨配偶排队台词）异步占位显示效果差，再议。
3. 控制台命令改名 `livingnpcs_*` 后，是否保留 `valleytalk_*` 别名一个版本期？
4. Android 玩家没有触发键，主动搭话入口仅剩对话内 Typed 选项；是否给 Android 加
   "长按 NPC"或虚拟按钮入口？（旧版同样缺失，非回归。）
5. 流式窗是否在 0.2.0 启用（归 WP10 裁决，本包接口已备好）。
6. P9 对 `finishedLastDialogue` 的私有字段反射：新实现先验证不写它是否真有 UI
   异常，能省则省。
7. `PermitAiUse` 白名单常量（旧代码有一个空的许可 ID 数组）是否保留硬编码白名单
   机制，还是纯 manifest 字段驱动。

### 裁决（2026-07-06，Yuki + 架构侧，全部落定）

1. P2 采用**方案 B（P11 集中拦截）**，弃栈帧探测；30 号验收冒烟 1/3 项须专门
   验证旁听与事件台词记录不漏（此为方案 B 的已知风险点）。
2. P14 **一律改异步占位**；冒烟发现清晨配偶排队台词效果差再回头议。
3. 控制台命令改名 `livingnpcs_*`（Yuki 裁决），**不保留** `valleytalk_*` 别名。
4. Android 本版**不加**新入口（非回归，0.2.0 范围外，记入 backlog）。
5. 流式窗按 WP10 裁决（保持现状行为面：原生对话框流式；`StreamingDialogueWindow`
   搬运保留但不接线）。
6. `finishedLastDialogue` 反射：先省略，验收期发现 UI 异常再加回。
7. 废除硬编码白名单，纯 manifest/内容字段驱动。

## 9. 审计索引（功能描述 ↔ 旧代码出处）

| 本文档节 | 旧文件:行 |
|---|---|
| P1 | ValleyTalk/src/Patches/NPC_CheckAction_Patch.cs:16-47 |
| P2 | ValleyTalk/src/Patches/NPC_CurrentDialogue_Patch.cs:12-93 |
| P3 | ValleyTalk/src/Patches/NPC_CheckForNewCurrentDialogue_Patch.cs:10-71 |
| P4 | ValleyTalk/src/Patches/NPC_GetGiftReaction_Patch.cs:9-34 |
| P5（跳过清单六键） | ValleyTalk/src/Patches/NPC_AddMarriageDialogue_Patch.cs:12-47 |
| P6 | ValleyTalk/src/Patches/NPC_PushTemporaryDialogue_Patch.cs:10-53 |
| P7 | ValleyTalk/src/Patches/NPC_TryToGetMarriageSpecificDialogue_Patch.cs:9-31 |
| P8 | ValleyTalk/src/Patches/NPC_TryToRetrieveDialogue_Patch.cs:9-26 |
| P9 | ValleyTalk/src/Patches/Dialogue_ChooseResponse_Patch.cs:15-81 |
| P10 | ValleyTalk/src/Patches/Dialogue_TryGetDialogue_Patch.cs:11-24 |
| P11/P12 | ValleyTalk/src/Patches/Game1_DrawDialogue_Patch.cs:11-38 |
| P13 | ValleyTalk/src/Patches/GameLocation_GetLocationOverrideDialogue_Patch.cs:10-45 |
| P14 | ValleyTalk/src/Patches/MarriageDialogueReference_GetDialogue_Patch.cs:13-76 |
| P15（搬运件） | ValleyTalk/src/Patches/Event_CheckAction_Patch.cs:13-47 |
| P16–P19（搬运件） | ValleyTalk/src/Patches/DialogueBox_ThinkingDialogue_Patch.cs:8-77 |
| 通用跳过/频率/当日记忆/被动门控 | ValleyTalk/src/Generation/DialogueBuilder.cs:595-663; src/RsvAiPolicy.cs:9-43; src/config/ModConfig.cs:14-57 |
| 输入请求队列 | ValleyTalk/src/TextInputHandler.cs:12-94 |
| 原生输入会话/收尾/键重复 | ValleyTalk/src/UI/NativeDialogueTextInputController.cs:34-185,342-427 |
| 思考窗生命周期/文本 | ValleyTalk/src/UI/ThinkingDialogueController.cs:19-174 |
| UI 状态守卫 | ValleyTalk/src/UI/DialogueUiStateGuard.cs:11-141 |
| 调度泵/世代号/取消/回主线程 | ValleyTalk/src/Generation/AsyncBuilder.cs:33-185,299-356 |
| 流式窗接口与交互 | ValleyTalk/src/UI/StreamingDialogueWindow.cs:44-86,160-329; AsyncBuilder.cs:187-297（无调用方） |
| 启动次序/命令/内容包扫描 | ValleyTalk/src/ModEntry.cs:117-176,178-252,309-345 |
| GameLaunched/SaveLoaded/ReturnedToTitle | ValleyTalk/src/ModEntry.cs:347-367 |
| 资产注册（懒加载隐患） | ValleyTalk/src/PromptCache.cs:14-30; src/GameSummaryBuilder.cs:50-73; src/Character.cs:68-87 |
| 用量事件注册 | ValleyTalk/src/TokenUsageTracker.cs:58-64 |
| Android 三件套/网络门控 | ValleyTalk/src/Platform/AndroidHelper.cs:15-45; AndroidFileHelper.cs:25-102; NetworkAvailabilityChecker.cs:15-46 |
| 时间戳类型语义 | ValleyTalk/src/StardewTime.cs:6-188 |
| 标记/键前缀/常量 | ValleyTalk/src/enums/SldConstants.cs:9-13; src/VtConstants.cs:5-11 |
| 枚举字面 | ValleyTalk/src/enums/{Weekday,Season,SpouseAction,RandomAction}.cs |
| 废弃 UI 件（零引用佐证） | ValleyTalk/src/UI/{ThinkingWindow,DialogueTextInputMenu,DialogueTextInputBox,DialogueTextInputMenuWrapper}.cs；InputTextBox.cs（StackSplitRedux 残片，位于 csproj 之外） |
