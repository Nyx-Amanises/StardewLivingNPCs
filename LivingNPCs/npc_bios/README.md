# 完整 NPC 对话传记

`npc_bios/` 是 LivingNPCs 的完整对话传记入口。它与 `npc_profiles/` 并行，但用途不同：

- `npc_profiles/` 是轻量行为资料，补充性格、背景提示和行为倾向。
- `npc_bios/` 提供完整 `NpcBio`，直接参与 AI 对话的人物背景、关系、特质、话题、样例与肖像语义。

这两个目录都不会创建 NPC 本体。目标 NPC 必须先由游戏或另一个 Mod 正常加入，并且仍会受到 LivingNPCs 的角色资格、配置与兼容策略限制。

## 磁盘目录与虚拟资产

随主 Mod 接受社区 PR 时，使用来源 Mod 的 `manifest.json` UniqueID 做命名空间：

```text
Mods/LivingNPCs/npc_bios/<来源Mod UniqueID>/bios/<NPC内部名>.json
```

本地化完整传记放到：

```text
Mods/LivingNPCs/npc_bios/<来源Mod UniqueID>/locales/<locale>/<NPC内部名>.json
```

只有该来源 UniqueID 已被 SMAPI 加载时，这个命名空间才激活，避免来源 Mod 没安装时把传记错套给另一个同名 NPC。若两个已加载来源都为同一内部名提供通过读取与校验的有效传记，LivingNPCs 会记录冲突并全部跳过，不按文件枚举顺序任选一份；只有坏文件的来源会单独警告，不会阻断另一份唯一有效的传记。

玩家也可以使用不带来源命名空间的扁平路径做本机显式覆盖：

```text
Mods/LivingNPCs/npc_bios/<NPC内部名>.json
Mods/LivingNPCs/npc_bios/<locale>/<NPC内部名>.json
```

扁平文件优先级最高、不会检查来源 Mod，适合整合包作者或玩家明确知道自己要覆盖哪个 NPC 的场景；主仓社区 PR 应使用 UniqueID 命名空间。

实际安装文件夹可被玩家改名，因此以 `manifest.json` 的 `Yuki.LivingNPCs` 为准。`Mods/Yuki.LivingNPCs/Bios/<NPC内部名>` 是 SMAPI 的虚拟资产名，并不是必须存在的 Windows 文件夹。LivingNPCs 会把上面的 JSON 提供成这个虚拟资产。

例如游戏语言为 `zh-CN` 时，加载顺序是：

```text
扁平本地覆盖：zh-CN → zh → 默认
已加载来源命名空间：locales/zh-CN → locales/zh → bios 默认
内置 SVE / 原版传记
Data/Characters 轻量回退
```

无效或损坏的本地化文件会记录警告，再继续尝试下一层。社区文件优先于内置文件；Content Patcher 对最终虚拟资产的修改仍在其后生效。

## 快速开始

1. 确认 NPC 的内部名和来源 Mod UniqueID，不要使用翻译后的显示名。
2. 复制 [`_template.json`](./_template.json)，放到 `<来源UniqueID>/bios/<NPC内部名>.json`；翻译版放到 `<来源UniqueID>/locales/<locale>/<NPC内部名>.json`。locale 目录使用规范大小写，例如 `zh-CN`、`pt-BR`、`zh-Hans`。
3. 填写完整资料，并保留所有顶层字段。字段名及大小写必须与模板完全一致；文件必须是严格 JSON，不允许注释、尾逗号、重复属性或额外的 `$type`/`$id` 元数据。
4. 运行校验。源码仓库根目录执行：

   ```powershell
   python tools/validate_community_bios.py --repository LivingNPCs/npc_bios
   ```

   已安装的 Mod 文件夹内也会附带校验器，可执行：

   ```powershell
   python npc_bios/validate_community_bios.py npc_bios
   ```

   `--repository` 会把扁平本地覆盖视为错误，供主仓 PR 与 CI 使用；已安装 Mod 里的本地校验不加该参数，扁平覆盖只会提示 warning。校验器需要 Python 3，但游戏运行本身不依赖 Python；它只供传记作者提交前检查。

5. 完整重启游戏。`npc_bios` 不使用文件监视器；直接修改磁盘文件不会自动热重载。

手工放进主 Mod 文件夹的文件可能在升级或重新安装时被覆盖，建议保留备份；长期独立维护时优先做成 Content Patcher 内容包。

## 性能影响

传记不是每帧、每秒或每轮重新扫描。LivingNPCs 只在进程内扫描一次已激活的来源目录；SMAPI 第一次请求某个 NPC 的虚拟资产时检查对应 locale 候选，并只读取实际存在的小型 JSON，随后由 SMAPI 资产缓存和 LivingNPCs 的人物缓存复用。正常规模下，这部分 CPU、内存和磁盘开销可以忽略。

更长的 `Biography`、`Dialogue` 或 `PromptOverrides` 会增加发给模型的 token，可能提高接口费用和生成延迟；因此运行时与 CI 都限制文件大小、字段长度和集合数量。真正影响体验的通常是 prompt 长度与模型速度，而不是本地 JSON 读取。

## 字段说明

| 字段 | 规则与作用 |
| --- | --- |
| `Biography` | 必填且不可为空。人物稳定背景、日常生活、矛盾、喜恶和与农夫的关系弧；为空会让整份传记进入回退链。 |
| `Relationships` | 关系表。键和 `id` 使用稳定内部标识；`Heading` 与 `Description` 会进入完整人物上下文。不要写 NPC 当前不应知道的秘密。 |
| `Traits` | 特质表，按重要性排序。精简上下文只取前 4 条，部分礼物文本只取前 3 条。 |
| `BiographyEnd` | 完整上下文的收尾，适合描述说话节奏、幽默、克制和情感边界。 |
| `Gender` | 通常留空并使用 `Data/Characters`。该覆盖值依赖当前语言提示词，不建议写死英文。 |
| `Unique` | 兼容保留的独特描述，当前没有稳定的独立消费点，通常留空。 |
| `ExtraPortraits` | 额外肖像语义，只接受 `u` 或十进制帧 `6`–`4095`，并且必须与最终肖像表真实帧一致。 |
| `Preoccupations` | 近期心事候选，会与喜爱/讨厌礼物组成话题池；不保证每轮出现。 |
| `Dialogue` | 可选样例对白。默认 `{}`；不得复制原 Mod 对白、事件文本或其他受版权保护的原文。 |
| `HomeLocationBed` | 兼容字段，当前主要保留旧格式，通常为 `false`。 |
| `UsePatchedDialogue` | 必须为 `false`。设为 `true` 会绕过未授权对白保护，因此 `npc_bios` 运行时会拒绝；确有作者授权时改用独立 Content Patcher 包。 |
| `PromptOverrides` | 必须为 `{}`。它会替换受信提示词骨架，因此 `npc_bios` 运行时会拒绝非空值；高级覆盖只能通过明确审核的 Content Patcher 包发布。 |

运行时字段 `Missing`、`ResolvedGender`、`TopicPool`、`ValidPortraits` 不属于 JSON，禁止提交。

## 内容与授权边界

- 优先写稳定事实和你自己的概括，不要逐句改写原对白。
- `PermitAiUse: true` 只是内容包作者允许运行时把其文本提供给 AI，不等于版权或再分发许可证，也不能替另一个 Mod 作者授权。
- 来源 Mod 未明确授权时保持 `Dialogue: {}`；`npc_bios` 中的 `UsePatchedDialogue` 无论如何都必须为 `false`。完整传记的原创事实摘要仍可独立工作。
- 剧透必须在 PR 中标明。重大秘密不应无条件写进开局即可见的 `Relationships` 或 `Biography`。
- 提交者必须拥有所提交文字的权利，并授权 LivingNPCs 项目把它随 Mod 收录与再分发。

## 通过 Content Patcher 独立发布

不想把文件提交到主仓库时，可以发布独立 Content Patcher 内容包。下面是可直接补全的最小 `manifest.json`：

```json
{
  "Name": "LivingNPCs Bio - Example NPC",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Full LivingNPCs biography for ExampleNpc.",
  "UniqueID": "YourName.LivingNPCsBio.ExampleNpc",
  "MinimumApiVersion": "4.1.0",
  "PermitAiUse": true,
  "ContentPackFor": {
    "UniqueID": "Pathoschild.ContentPatcher",
    "MinimumVersion": "2.3.0"
  },
  "Dependencies": [
    {
      "UniqueID": "Yuki.LivingNPCs",
      "IsRequired": true
    },
    {
      "UniqueID": "Author.CustomNpc",
      "IsRequired": true
    }
  ]
}
```

把 `Author.CustomNpc` 替换成真正创建该 NPC 的来源 Mod UniqueID。独立 Bio 包必须依赖来源 Mod，或在支持多个可选来源时给每条 `EditData` 添加相应 `HasMod` 条件；否则同内部名 NPC 可能在来源缺失时误套传记。

对应的 `content.json`：

```json
{
  "Format": "2.3.0",
  "Changes": [
    {
      "Action": "EditData",
      "Target": "Mods/Yuki.LivingNPCs/Bios/ExampleNpc",
      "Entries": {
        "Biography": "...",
        "Relationships": {},
        "Traits": {},
        "BiographyEnd": "...",
        "Gender": "",
        "Unique": "",
        "ExtraPortraits": {},
        "Preoccupations": [],
        "Dialogue": {},
        "HomeLocationBed": false,
        "UsePatchedDialogue": false,
        "PromptOverrides": {}
      }
    }
  ]
}
```

只有你有权授权本包文本供 AI 使用时，才能保留 manifest 中的 `"PermitAiUse": true`。它主要影响 LivingNPCs 是否采用运行时被内容包修改过的 NPC 原对白；它**不会**自动审查、阻止或授权你写入 `Biography`、`Dialogue` 或 `PromptOverrides`。

Content Patcher 在 `npc_bios` 读取与校验完成后编辑最终对象，因此会绕过 64 KiB、字段长度、`UsePatchedDialogue=false` 和 `PromptOverrides={}` 等直接投放规则。请把 CP 包视为需要自行安全与版权审计的高级代码型扩展：`Dialogue` 会被传记样例加载器直接合并，非空 `PromptOverrides` 可替换受信提示词，`UsePatchedDialogue=true` 也没有自动核验原作者许可。

虚拟资产最终只以 NPC 内部名为键。如果两个同时安装的 NPC Mod 使用完全相同的内部名，它们无法同时拥有两份无条件生效的传记；应由兼容内容包使用 Content Patcher 条件明确选择来源。

LivingNPCs 会先规整少数兼容别名，文件名与虚拟资产目标应使用规整后的键：`GuntherSilvian → Gunther`、`MarlonFay → Marlon`、`MorrisTod → Morris`、`HankSVE → Hank`。多配偶类 Mod 添加在名字末尾的 `·`、`•`、`-` 克隆后缀也会被去掉。

## 社区 PR

提交前请从 [NPC biography 专用 PR 模板](https://github.com/Nyx-Amanises/StardewLivingNPCs/compare?expand=1&template=npc-biography.md) 创建 PR，并提供：来源 Mod 名称、链接、UniqueID、测试版本、NPC 内部名、语言、剧透等级、事实依据、原创/授权声明以及游戏内测试结果。文件必须放在与来源 UniqueID 完全一致的命名空间下。建议一个来源 Mod 或一个紧密主题一个 PR，避免混入无关格式化。

Ridgeside Village 当前仍受硬性 AI 兼容策略限制；添加完整传记不会解除该限制。
