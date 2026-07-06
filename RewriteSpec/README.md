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

### 第 0 步：隔离环境（开工前做一次）

```
git push                                        # 异地备份现状
git worktree add ..\LivingNPCs-rewrite rewrite  # 重写专用文件夹 + rewrite 分支
```

重写全部发生在 `..\LivingNPCs-rewrite`（rewrite 分支），本文件夹留在 master
继续做 0.1.5 发版，互不干扰。**阶段 A 完成后，立即在 rewrite 分支上删除
`ValleyTalk/`、`ValleyTalk.Tests/` 两个目录并提交**——此后 B/C/D 阶段的实现
对话物理上无旧代码可读，洁净室从"纪律"升级为"事实"。（`../upstream-ValleyTalk`
仅供审计，实现对话同样禁读。）

### 排班表（共 9 个对话）

| 批次 | 对话 | 工作包 | 工作目录 | 可同时开 |
|---|---|---|---|---|
| 现在 | ① | 阶段 A 搬运（03） | ..\LivingNPCs-rewrite | ①②③ 三个并行 |
| 现在 | ② | WP20 提示词骨架+世界观创作 | 任意（交 JSON 稿） | |
| 现在 | ③ | WP20 传记创作（33 原版 + 23 SVE） | 任意（交 JSON 稿） | |
| A 完成后 | ④⑤⑥ | WP11 / WP14 / WP15 | 同一 worktree，目录不相交 | 三个并行* |
| B 完成后 | ⑦ | WP10 引擎（最大包，单独专注） | 同 | 单独 |
| C 完成后 | ⑧⑨ | WP12 / WP16 | 同 | 两个并行* |
| D 完成后 | 审校 | 30 号验收：反向权属扫描+静态审计 | 主文件夹 | 用户冒烟并行 |

*并行注意：同一 worktree 里并行的对话各管各的目录（Llm/、Persistence/、Content/），
唯一冲突点是同时编译会锁文件——给每个对话的开场白加一句"编译失败若因文件锁，
等几秒重试"即可；若想零摩擦，也可以每包再开一个 worktree 分支、完成后合回 rewrite。
嫌折腾就把 ④⑤⑥ 顺序做，多花一两天但零协调成本。

### 开场白模板（每个实现对话）

> 你在 `<worktree 路径>` 工作（rewrite 分支）。先读 RewriteSpec/README.md、00、01，
> 再读你的工作包文档 RewriteSpec/<编号>，各文档"开放问题"末尾的裁决块是最终结论。
> 严格遵守 00 的洁净室纪律：禁止打开 ValleyTalk/、ValleyTalk.Tests/、
> upstream-ValleyTalk/ 下任何文件（若目录已删除则无此虑），禁止搜索其源码镜像。
> 完成后确保编译通过、LivingNPCs.Tests 全绿，在工作包文档末尾追加"实现记录"一节，
> 并做一次独立提交（提交信息注明工作包编号）。

WP20 两个创作对话的开场白改为：读 RewriteSpec/20-prompt-inventory.md 全文 +
15 号文档 §3（容器格式），素材从 Stardew Valley Wiki 取，交付到文档指定目录，
英文原版 ValleyTalk 的任何文本不得参考（你本来也接触不到）。

- 跨包接口（01 §2）出现分歧时：停下来问用户，不要自行改接口。
- 每个工作包独立提交序列，便于审计与回滚。

## 状态

- [x] 权属地图（2026-07-06 生成，上游语料 16141 行）
- [x] 骨架文档 00 / 01 / 03
- [x] 工作包文档 10–16、20、30（2026-07-06 全部完成，合规审校通过：零代码搬运、零上游文本引用）
- [ ] 阶段 A–F 实施

## 待用户裁决的开放问题

**已全部裁决（2026-07-06，Yuki + 架构侧授权）**：结论写在各工作包文档
"开放问题"小节末尾的"### 裁决"块里，实现对话以裁决块为准。要点：
401/429 加熔断（WP11 裁决 2）；控制台命令改名不留别名（WP12 裁决 3）；
迁移后旧键保留（WP14 裁决 1）；配置撞名按 WP15 §3.1 合并（WP15 裁决 2）；
其余采纳各文档建议项。

已由架构侧裁决并写入 01 §2：ILlmClient.StreamAsync 改为事件流（回传流式真实 usage）、
IDialogueContent.GetPromptSkeleton 增加 variant 参数（性别/优化版变体）。
