# NPC 人物资料扩展

`npc_profiles/` 是 LivingNPCs 的社区人物资料入口。

只要把合法的 `.json` 文件放进这个目录，LivingNPCs 启动时就会自动读取它们。这样自定义 NPC 不需要等待主程序重新编译，也能获得更贴近角色的：

- 性格倾向。
- 背景摘要。
- 对话风格提示。
- 行为上的轻微偏好差异。

## 文件规则

- 文件名以 `_` 开头时会被跳过，例如 `_template.json`。
- 其他 `.json` 文件会在启动时自动加载。
- 一个文件可以写：
  - 单个角色对象；
  - 一个角色数组；
  - 或 `{ "profiles": [ ... ] }` 结构。
- `npcNames` 里的每个名字都会注册成同一个资料入口，建议同时填写：
  - 游戏内部名字；
  - 常见别名；
  - 如果确实需要，再加显示名。
- 外部 JSON 与内置资料同名时，外部 JSON 会覆盖内置条目，方便社区后续修正。

## 字段说明

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `npcNames` | 是 | 至少一个名字。优先填内部名字。 |
| `promptLabel` | 是 | 给 AI 的简短性格摘要，建议英文。 |
| `debugLabel` | 是 | 给玩家调试时看的简短标签，可中文。 |
| `approachModifier` | 是 | 靠近玩家倾向的轻微修正，通常在 `-0.10` 到 `0.10`。 |
| `emoteModifier` | 是 | 表情 / 外露反应倾向的轻微修正，通常在 `-0.10` 到 `0.10`。 |
| `passiveEmoteId` | 是 | 被动反应常用表情编号。 |
| `reason` | 是 | 供内部解释的简短英文原因。 |
| `sourceLabel` | 否 | 资料来源名称，例如某个自定义 NPC 包名。 |
| `sourceDebugLabel` | 否 | 调试输出时显示的来源名称。 |
| `backgroundPrompt` | 否 | 简短、克制、尽量少剧透的人物背景摘要。 |
| `dialoguePrompt` | 否 | 对话风格提示，说明应该怎样说，而不是直接写台词。 |

可以直接从 [`_template.json`](./_template.json) 复制开始。

## 推荐写法

### 1. 先写“稳定事实”

优先写：

- 职业。
- 家庭关系。
- 长期目标。
- 明显的性格张力。
- 在社区里的位置。

少写：

- 只有后期剧情才揭示的大剧透。
- 粉丝推测。
- 逐句搬运原对白。

### 2. 把“事实”和“说话方式”分开

`backgroundPrompt` 负责“这个人是谁”。

`dialoguePrompt` 负责“这个人平时怎么说话”。

这样 AI 比较不容易把资料直接念出来。

### 3. 不要把角色写成单一形容词

比起：

```text
cheerful
```

更推荐：

```text
bright, practical, family-minded, and a little impatient when work piles up
```

这样角色在不同场景里会更有层次。

## 社区提交流程

1. 在 `npc_profiles/` 下新增一个 `.json` 文件。
2. 尽量一个 Mod 一个文件，或一个角色一个文件，便于以后维护。
3. 以 `_template.json` 为起点填写资料。
4. 在提交说明里写清：
   - 角色来自哪个 Mod；
   - 使用的是哪个内部名字；
   - 资料依据；
   - 是否包含轻微剧透。
5. 提交前至少确认：
   - JSON 能正常解析；
   - `npcNames` 与游戏里的实际内部名一致；
   - `backgroundPrompt` 没有大段复制原 mod 文本；
   - 语气提示不会把角色改写成另一个人。

## 示例

```json
{
  "npcNames": ["ExampleNpc"],
  "promptLabel": "careful, scholarly, courteous, and quietly curious",
  "debugLabel": "谨慎、学者气、礼貌",
  "approachModifier": 0.01,
  "emoteModifier": 0.02,
  "passiveEmoteId": 8,
  "reason": "scholarly custom NPC temperament",
  "sourceLabel": "Example NPC Pack",
  "sourceDebugLabel": "Example NPC Pack（社区资料）",
  "backgroundPrompt": "A local archivist who spends most days preserving old records and quietly helping neighbors trace family history.",
  "dialoguePrompt": "Use precise, gentle language and let curiosity about local history surface naturally."
}
```

## 为什么人物库不直接全塞进代码

因为自定义 NPC 的数量会越来越多，而且社区对人物理解也会继续修正。把可扩展资料放在 JSON 里，后面我们可以更快地：

- 接受补丁。
- 给单个角色做热修正。
- 让玩家按自己的整合包补本地人物资料。
