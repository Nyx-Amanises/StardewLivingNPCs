# LivingNPCs

LivingNPCs 是一个星露谷物语 SMAPI companion mod，用来实验更安全、更像真实生活的 NPC 行为系统。

这个 Mod 的定位是：**ValleyTalk 负责 AI 对话，LivingNPCs 负责 NPC 行为层**。

当前第一阶段会刻意保守：

- 用快捷键触发附近 NPC 的小行为。
- 在修改 NPC 状态前做安全检查。
- 记录 NPC 最近做过的小行为。
- 支持更多安全微行为：停下看向玩家、环顾四周、后退一步。
- 记录玩家点击 NPC 开始聊天的轻量互动记忆。
- 记录送礼与事件/节日互动记忆，并把这些上下文送入 ValleyTalk。
- 维护每个 NPC 的轻量当前状态，例如心情、注意度和回应倾向。
- 维护每个 NPC 对玩家的长期熟悉度，让后续行为和 AI 对话能带上一点“认识你多久了”的感觉。
- 记录互动节奏，并按原版好感度和 LivingNPCs 熟悉度分层判断今天聊几次算自然。
- 为 NPC 添加轻量行为倾向，让慢热、外露、温和、好奇等不同角色在同样场景下表现不完全一样。
- 支持自定义 NPC 的基础性格推断。只要角色写入了 Stardew Valley 1.6 的 `Data/Characters`，LivingNPCs 会读取礼貌程度、社交倾向、乐观程度、年龄、家庭关系和是否可恋爱等字段，生成保守的行为/对话倾向。
- 覆盖已加载的 Stardew Valley Expanded 和 Ridgeside Village 角色：有专属摘要的角色使用手写背景提示，其余角色会带上所属 Mod 的世界观提示，并回落到 `Data/Characters` 推断。
- 读取当前场景，例如时间、天气、地点、附近 NPC 和玩家与 NPC 的关系，并把这些因素用于行为选择、短期心情和 ValleyTalk 上下文。
- 可选：把行为、关系、礼物、事件和场景整理成 ValleyTalk 的隐藏上下文，让后续对话知道刚才发生了什么，也知道该用什么语气回应。
- 可选：请求 OpenAI-compatible 接口，让 AI 在白名单行为里选择一个意图。

## 启动和验证

你本机的 SMAPI 启动程序是：

```text
D:\SteamLibrary\steamapps\common\Stardew Valley\StardewModdingAPI.exe
```

快速测试时，直接双击这个文件即可。

如果想从 Steam 点“开始游戏”也自动通过 SMAPI 启动，在 Steam 里打开：

```text
星露谷物语 -> 属性 -> 通用 -> 启动选项
```

填入：

```text
"D:\SteamLibrary\steamapps\common\Stardew Valley\StardewModdingAPI.exe" %command%
```

进入游戏后，打开 Generic Mod Config Menu，找到 `LivingNPCs`。如果能看到设置页，第一行会显示：

```text
LivingNPCs is loaded. If you can see this page, the mod is running.
```

看到这行就说明 Mod 已经成功加载。

## 当前行为

- `FacePlayer`：NPC 转向玩家。
- `Emote`：NPC 转向玩家，并显示一个小表情。
- `ApproachPlayer`：NPC 尝试寻路走到玩家旁边的安全格子。默认只会在手动按快捷键时选择。
- `Pause`：NPC 短暂停下，并注意到玩家。
- `LookAround`：NPC 环顾周围，表现出对场景的注意。
- `StepAway`：NPC 尝试后退到安全格子，通常用于关系较浅、重复互动过多或需要空间的场景。

默认行为快捷键是 `LeftShift + H`。靠近 NPC 后按住左 Shift 再按 `H`，NPC 会尝试触发一次小行为。

默认查看记忆快捷键是 `LeftShift + J`。靠近 NPC 后按住左 Shift 再按 `J`，会把该 NPC 的 LivingNPCs 行为记忆输出到 SMAPI 控制台。

默认还会在你点击 NPC 开始聊天时，记录一条 `ConversationStarted` 互动记忆，并提前把最近记忆推送给 ValleyTalk。这个功能不会拦截原版对话操作，也不会消耗 NPC 每日行为次数。

如果你拿着物品点击 NPC，LivingNPCs 会记录一条礼物记忆，包括物品名和可推断的喜好层级。它不会拦截原版送礼流程，只会把这次送礼作为后续状态和 ValleyTalk 对话的上下文。

如果你在事件或节日场景中与 NPC 互动，LivingNPCs 会记录一条事件互动记忆，让后续对话知道这次互动发生在特殊场景中。

LivingNPCs 还会维护一个轻量 NPC 状态层。行为、被动反应、玩家主动聊天都会改变 NPC 的当前状态；每天开始时状态会向普通状态回落，避免状态永久堆积。

除了当天状态，LivingNPCs 还会保存 NPC 对玩家的长期熟悉度。主动聊天会让熟悉度增长更明显，手动触发的小互动也会少量增长；熟悉度每天有增长上限，所以不会因为连续刷快捷键一下子堆满。这个值不会像注意度一样每天大幅衰减，它更像 NPC 对玩家的长期印象。

LivingNPCs 还会记录互动节奏。每天第一次打招呼、同一天重复聊天、连续几天都来聊天、隔了很多天才再见面，都会让 NPC 的短期状态和行为概率略有不同。互动节奏会参考原版好感心数和 LivingNPCs 熟悉度：关系浅时重复聊天更客气、更有距离；关系深时，同一天多几次短聊天也可以像自然的日常闲聊；只有超过当前关系的舒适上限后，才会逐渐表现出“有点频繁”。

每个 NPC 现在还有轻量行为倾向。比如有的人更慢热，有的人更容易用表情回应，有的人更愿意靠近玩家。这个倾向不是强制脚本，而是给 `Auto` 行为选择和 ValleyTalk 上下文一个基础性格偏移。

对自定义 NPC，LivingNPCs 会优先查找内置角色资料；如果没有，就从游戏已经加载的 `Data/Characters` 推断基础性格。这样 SVE、RSV 和其他内容包 NPC 不会全部落到随机默认性格上。SVE 和 RSV 的角色都会被识别为对应 Mod 的人物：有专属摘要的角色会带工作、家庭、村庄角色或明显性格压力点；没有专属摘要的角色也会带所属 Mod 的世界观提示、家庭关系和基础性格推断。

ValleyTalk 可以对 SVE/RSV 自定义 NPC 生成对话，因为它会遍历游戏加载后的 `Data/Characters`。不过 ValleyTalk 默认尊重内容包作者授权：如果 SVE/RSV 内容包没有在 manifest 里声明 `PermitAiUse:true`，ValleyTalk 会显示这些内容包的原始游戏对话，但不会把内容包原文作为 AI 生成样本。LivingNPCs 的角色摘要和行为记忆仍然会注入 ValleyTalk，用来补足角色语气和背景连续性。

LivingNPCs 会读取当前世界场景。清晨、深夜、雨雪、室内外、酒馆/商店/农场/矿区、附近是否有其他 NPC，以及玩家与 NPC 的好感心数都会形成一个轻量场景偏移。它不会强迫 NPC 做某个动作，只会影响 `Auto` 行为选择概率、NPC 的短期心情/回应倾向，并作为 ValleyTalk 的安静上下文。

当手动行为模式设为 `Auto` 时，NPC 状态会反过来影响行为选择。注意度和回应倾向越高，NPC 越可能走近或明显回应；心情更外露时，更容易显示表情。固定测试模式仍然按你选择的行为执行，方便验证单个动作。

当同一个 NPC 与你逐渐熟悉后，`Auto` 模式下更容易出现走近、转向或自然回应。查看记忆时也会显示“对玩家熟悉度”“互动节奏”“互动舒适度”“行为倾向”“当前场景”和“情境影响”，ValleyTalk prompt 里会收到对应上下文。

LivingNPCs 现在给 ValleyTalk 的不再只是简单记忆列表，而是一份更强的“隐藏连续性摘要”。它会按优先级告诉模型：NPC 当前语气应该偏向什么、关系是否足够熟、今天是不是聊得太频繁、刚才是否送过礼物、是否处于事件/节日、附近是否有人、场景是否会让 NPC 更忙、更警觉或更放松。同时它会要求 ValleyTalk 不要直接复述这些记录，而是把它们自然折进下一句角色台词里。

## 配置项

主要配置在 `config.json`，也可以在游戏内通过 Generic Mod Config Menu 修改：

- `BehaviorHotkey`：触发 NPC 行为的快捷键。
- `InspectMemoryHotkey`：查看附近 NPC 行为记忆的快捷键。
- `ManualBehaviorMode`：手动触发时固定测试哪种行为，可选自动、转向玩家、显示表情、走近玩家、停下看向玩家、环顾四周、后退一步。
- `ManualEmoteId`：手动表情测试使用的表情编号，`16` 通常是感叹号。
- `MaxMemoryEntriesPerNpc`：每个 NPC 最多保存多少条行为记忆到存档。
- `PromptMemoryEntries`：每次最多把多少条最近行为记忆发送给 ValleyTalk。
- `EnableConversationMemory`：点击 NPC 开始聊天时，记录一条互动记忆并提前推送 ValleyTalk 上下文。
- `EnableNpcState`：启用 NPC 当前状态和长期熟悉度，例如心情、注意度、回应倾向和对玩家的熟悉程度。
- `NpcStateDailyDecay`：每天开始时 NPC 状态向普通状态回落的幅度。
- `EnablePassiveBehaviors`：是否允许 NPC 偶尔自动反应。
- `PassiveBehaviorChancePercent`：每 10 分钟触发被动行为的概率。
- `MaxBehaviorsPerNpcPerDay`：每个 NPC 每天最多触发多少次行为。
- `MaxInteractionDistanceTiles`：LivingNPCs 能影响 NPC 的最大距离。
- `AllowFacePlayer`：允许 NPC 转向玩家。
- `AllowEmotes`：允许 NPC 显示表情。
- `AllowApproachPlayer`：允许 NPC 短距离走近玩家。
- `EnableValleyTalkPromptBridge`：把 LivingNPCs 行为记忆发送给 ValleyTalk。
- `ShowHudMessages`：手动触发后显示执行结果或失败原因，方便测试。

## AI 行为规划

AI planner 默认关闭。要用本地 OpenAI-compatible 服务测试，可以编辑 `config.json`：

```json
{
  "EnableAiPlanner": true,
  "AiPlannerEndpoint": "http://localhost:11434/v1/chat/completions",
  "AiPlannerModel": "your-model-name"
}
```

模型不能直接控制游戏对象，只能从白名单行为里选择一个 intent。LivingNPCs 会验证返回 JSON，只有通过安全检查后才执行。

期望的返回格式：

```json
{
  "intent": "FacePlayer",
  "reason": "short in-world reason",
  "emoteId": 16
}
```

如果 AI 超时、失败或返回非法内容，LivingNPCs 会自动使用规则行为兜底，不会卡住游戏。
