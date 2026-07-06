# LivingNPCs 对话引擎重写说明书包（RewriteSpec）

目的：把捆绑的修改版 ValleyTalk（上游 dandm1，LGPL）替换为完全原创的对话引擎，
并入 LivingNPCs 单一 mod。本目录是**交给实现 AI 的全部输入**——实现方不读旧代码
（洁净室，规则见 00）。

## 文档索引

| 编号 | 文件 | 性质 |
|---|---|---|
| 00 | 00-goals-and-constraints.md | 目标、洁净室纪律、硬约束（**所有人必读**） |
| 01 | 01-target-architecture.md | 目标架构、跨包接口、实现顺序（**所有人必读**） |
| 02 | 02-ownership-map.md / ownership_map.json | 权属地图（自动生成，审计用） |
| 03 | 03-carryover-manifest.md | 阶段 A：搬运清单 |
| 10 | 10-spec-dialogue-engine.md | WP10 对话生成引擎 |
| 11 | 11-spec-llm-providers.md | WP11 LLM 提供商层 |
| 12 | 12-spec-game-integration.md | WP12 Harmony 补丁 / SMAPI 事件 / UI |
| 14 | 14-spec-persistence-migration.md | WP14 持久化与旧数据迁移 |
| 15 | 15-spec-content-config.md | WP15 资产 / 配置 / GMCM / i18n |
| 16 | 16-spec-livingnpcs-wiring.md | WP16 与行为系统接线（去桥接） |
| 20 | 20-prompt-inventory.md | 提示词清单与创作指南（交给写提示词的 AI） |
| 30 | 30-acceptance-plan.md | 验收与测试计划（用户主导） |

## 怎么分工（给用户的操作手册）

每个工作包开一个新对话，开场白模板：

> 你在 `<仓库路径>` 工作。先读 RewriteSpec/README.md、00、01，再读你的工作包文档
> RewriteSpec/<编号>。严格遵守 00 的洁净室纪律：禁止打开 ValleyTalk/、
> ValleyTalk.Tests/、upstream-ValleyTalk/ 下任何文件。完成后确保编译通过、
> LivingNPCs.Tests 全绿，并在工作包文档末尾追加"实现记录"一节。

- 顺序按 01 §6 的阶段表：A（搬运）→ B（11/14/15 并行）→ C（10）→ D（12/16 并行）。
  WP20（提示词创作）随时可做，与代码无依赖。
- 跨包接口（01 §2）出现分歧时：停下来问用户，不要自行改接口。
- 每个工作包一个独立 git 分支或至少独立提交序列，便于审计与回滚。

## 状态

- [x] 权属地图（2026-07-06 生成，上游语料 16141 行）
- [x] 骨架文档 00 / 01 / 03
- [x] 工作包文档 10–16、20、30（2026-07-06 全部完成，合规审校通过：零代码搬运、零上游文本引用）
- [ ] 阶段 A–F 实施

## 待用户裁决的开放问题（实施前过一遍）

各工作包文档末尾的"开放问题"小节汇总，其中需要 Yuki 拍板的：
- WP11 §开放问题：401/429 是否加熔断（现状每次交互重试 3 次不熔断）
- WP12 §开放问题：控制台命令是否改名（valleytalk_* → livingnpcs_*）等 7 项
- WP14 §8.1：迁移后旧键保留（推荐，支持回滚）还是删除
- WP15 §8.2：配置字段与 LivingNPCs ModConfig 撞名的合并方案确认
- WP16 §8.1：即时线索暂存的生命周期
- WP20 §开放问题：两处旧键拼写瑕疵是否借机更名、法语支持范围、传记中文版容器形式

已由架构侧裁决并写入 01 §2：ILlmClient.StreamAsync 改为事件流（回传流式真实 usage）、
IDialogueContent.GetPromptSkeleton 增加 variant 参数（性别/优化版变体）。
