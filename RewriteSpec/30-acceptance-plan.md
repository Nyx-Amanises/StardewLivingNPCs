# 30 · 验收与测试计划

> 阶段 F，用户（Yuki）主导。前置：阶段 A–E 全部完成，各工作包文档的
> "验收要点"小节已由对应实现对话自查通过。

## 1. 静态审计（自动化，任何对话可执行）

1. **洁净室量化验证（最重要）**：把权属工具反向指向新代码——
   `python tools/ownership_map.py --target LivingNPCs/Dialogue --out /tmp/audit`
   要求：所有新写文件 `upstream_ratio ≤ 10%`，且命中行经抽查均为通用惯用句。
   搬运件不参与此项（本来就是 MINE）。结果存档进 `RewriteSpec/audit/`。
2. 残留引用清零：
   - `grep -r "namespace ValleyTalk" LivingNPCs/ Shared/` → 0；
   - `grep -ri "dandm1" LivingNPCs/ Shared/` → 仅允许出现在 WP14 迁移代码与
     WP12 共存检测（白名单注释标注）；
   - `grep -r "ValleyTalk/" LivingNPCs/` → 仅允许 WP14 迁移中的旧资产/键名字符串。
3. manifest 检查：单一 manifest，UniqueID `Yuki.LivingNPCs`，版本 0.2.0，
   无 `dandm1.*` 依赖，UpdateKeys 指向 Yuki 的 Nexus 页。
4. `tools/audit_prompt_literals.py` 与 `tools/verify_anchors.py` 照常通过。
5. 敏感项：新 config 模板不含真实 API 密钥；日志不打印密钥（抽查日志输出代码）。

## 2. 构建与测试

- Release 构建零警告新增；`LivingNPCs.Tests`（含并入的原 ValleyTalk.Tests）全绿，
  无遗留 `Skip="WP1x-TODO"`。
- 打包按既有发版流程（csproj Version + manifest 同步；zip 结构单文件夹；
  排除 config.json）。**不再有 ValleyTalk / CPValleyTalk / SVE Extensions 三个包**；
  SVE 支持若按 01 §4 内置检测则确认 zip 中含 bios-sve 资产。

## 3. 进游戏冒烟（用户执行，两条存档线）

### 线 1：全新存档
| # | 场景 | 通过标准 |
|---|---|---|
| 1 | 与任意原版 NPC 对话（普通日常） | AI 台词生成，流式显示，无卡顿/死锁 |
| 2 | 自由文本输入（键盘） | 输入框正常、提交生成、Esc 取消 |
| 3 | 节日中对话 / 事件中对话 | 特殊路径可用，不炸事件脚本 |
| 4 | 送礼（喜/厌各一次） | 礼物反应台词生成；行为系统计数正常 |
| 5 | 求助/委托流程 | LivingNPCs 行为链完整（发起→完成→回礼上限生效） |
| 6 | 礼物邮件生成 | 邮件收到、文本合规（校验器生效） |
| 7 | 记忆印象（长期记忆压缩） | 触发后无报错，印象注入后续对话背景 |
| 8 | 婚后配偶对话 | 婚姻专属路径生成正常 |
| 9 | GMCM 全菜单过一遍 | 每项可改可存；改 provider/model 热生效或按文档提示重载 |
| 10 | provider 至少 2 家实测（Claude + 1 家 OAI 系） | 生成成功；token 用量统计与缓存命中率有数 |
| 11 | 中文/英文游戏语言各跑 1–2 | 语言路由正确，无混语 |
| 12 | 断网/错 key | 玩家可见的温和失败，游戏可继续，原版台词回退 |
| 13 | SVE 存档（若装 SVE） | SVE NPC 用 SVE 传记集；RSV NPC（若装）仍被排除 |

### 线 2：老存档迁移（拿 0.1.5 真实存档备份）
| # | 场景 | 通过标准 |
|---|---|---|
| 14 | 首次载入 | 迁移日志出现，一次性提示；二次载入不重复迁移（幂等） |
| 15 | 数据连续性 | 老 NPC 记得旧对话/事件历史；token 台账延续 |
| 16 | 配置迁移 | 旧 ValleyTalk config.json 的 API 密钥与设置被导入 |
| 17 | 旧 mod 未删除的共存场景 | 明确警告 + 本引擎关闭 + 不崩溃；删除旧文件夹后恢复正常 |
| 18 | 多人：主机迁移 + 农场工人加入 | 农场工人不执行迁移、功能按 WP14 降级语义 |

## 4. 提示词内容验收（对照 WP20 §4 抽查表）

- 随机抽 5 个原版 NPC 传记对照 Wiki 事实核查（人际关系、日程、性格无编造）；
- 骨架节齐全性 checklist 过一遍；
- 中英双语各抽 2 个 NPC 实际对话 10 轮，主观质量不低于旧版。

## 5. 发布与回滚

- 发布说明如实写明：对话引擎为独立重新实现，不含 ValleyTalk 代码（00 §4）。
- 保留 0.1.5 zip 与旧存档备份；0.2.0 出严重问题时回滚路径：删新装旧（迁移设计
  须保证旧键未被删除——对齐 WP14 的"保留旧键"决策）。
- 验收全过后：删除仓库中 `ValleyTalk/`、`ValleyTalk.Tests/`（git 历史保留），
  归档 `RewriteSpec/`（含 audit 结果与各实现对话记录）作为独立创作证据链。

## 6. 未覆盖项（明示，不装作覆盖了）

- Android 真机未测（无设备）；保留代码路径但发布说明标注"未验证"。
- fr-FR 等第三语言文案本次不随包（上游译文弃用），社区可后补。
- 大规模多人（3+ 玩家）未测。
