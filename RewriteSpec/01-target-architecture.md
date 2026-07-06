# 01 · 目标架构

## 1. 总体形态

**一个 SMAPI mod、一个文件夹、一个程序集。** 对话引擎并入现有 `LivingNPCs` 工程，
旧的 ValleyTalk mod、ContentPack mod、Interop 桥接层全部消失。

```
Mods/LivingNPCs/                     ← 唯一的安装文件夹
  LivingNPCs.dll
  manifest.json                      ← UniqueID: Yuki.LivingNPCs（不变）
  i18n/                              ← default(en) + zh.json + fr.json
  assets/dialogue/                   ← 角色传记、世界观、提示词骨架（重新创作的内容）
      world/GameSummary.json
      bios/<NPCName>.json            ← 每 NPC 一份（原版 33 个 + SVE 集）
      bios-sve/<NPCName>.json        ← 仅当检测到 SVE 时加载
  config.json                        ← 运行时生成
```

仓库内源码布局（新增部分，现有 LivingNPCs 代码不动）：

```
LivingNPCs/Dialogue/
  Engine/        ← WP10 对话生成引擎（会话编排、上下文装配、后处理、流式）
  Llm/           ← WP11 LLM 提供商层（各家 API 客户端、流式、缓存、用量）
  GameHooks/     ← WP12 Harmony 补丁 + SMAPI 事件接线
  Ui/            ← WP12 输入框、流式对话窗、思考中窗口
  Content/       ← WP15 资产加载（传记/世界观）、配置、GMCM 菜单
  Persistence/   ← WP14 历史/用量/缓存的持久化 + 旧数据迁移
  Diagnostics/   ← 各类日志导出器（基本全是搬运件）
```

命名空间与目录一一对应：`LivingNPCs.Dialogue.Engine` 等。

## 2. 模块边界与跨包契约

工作包之间只通过下列接口交互。**接口由本文档钉死，各包不得私自增删成员；
确有需要时在文档"开放问题"里记录，由用户裁决。** 这些接口是全新设计，与旧代码无关。

```csharp
namespace LivingNPCs.Dialogue;

/// 引擎对游戏侧（WP12/WP16）暴露的唯一入口
public interface IDialogueEngine
{
    bool IsEnabledFor(NPC npc);
    /// 玩家开启对话/送礼/事件触发时请求一次生成；结果经 callback 回主线程
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct);
    /// 流式：每产生一个可显示片段回调一次（已做后处理的安全文本）
    Task StreamAsync(GenerationRequest request, IStreamSink sink, CancellationToken ct);
}

public interface ILlmClient        // WP11 实现，WP10 消费
{
    string ProviderId { get; }
    Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct);
    /// 事件流：文本增量 + 末尾一条 Usage 事件（流式真实用量/缓存命中回传，见 WP11 §8.3 裁决）
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, CancellationToken ct);
}

/// Kind: TextDelta | Usage | Done；TextDelta 带 Text，Usage 带 TokenUsage
public readonly record struct LlmStreamEvent(LlmStreamEventKind Kind, string Text, TokenUsage Usage);

public interface IDialogueContent  // WP15 实现，WP10 消费
{
    NpcBio GetBio(string npcName);          // 传记（可被内容包 EditData 覆盖）
    WorldSummary GetWorldSummary();
    /// variant 携带 NPC 性别与 optimized 开关，解决性别变体键查找（WP15 §8.1 裁决）
    string GetPromptSkeleton(string key, PromptVariant variant = default);
}

public interface IDialogueHistory  // WP14 实现，WP10/WP16 消费
{
    void Append(string npcName, ExchangeRecord record);
    IReadOnlyList<ExchangeRecord> Recent(string npcName, int maxItems);
}
```

`GenerationRequest/GenerationResult/LlmRequest/LlmReply/NpcBio/WorldSummary/
ExchangeRecord/IStreamSink` 的字段在各工作包文档给出；若两个文档冲突，以 WP10 为准。

## 3. 与现有 LivingNPCs 行为系统的接线（WP16）

旧世界里 LivingNPCs 通过 `IValleyTalkInterface` + 结构化隐藏数据往返。新世界里：

- `LivingNpcConversationBridge` 的三类能力改为**进程内直接调用**：
  行为上下文注入 → `GenerationRequest.BehaviorContext` 字段；
  交换记录回传 → 引擎生成完成后直接调用行为系统的 `RecordExchange`；
  礼物邮件/记忆印象这类异步任务 → 直接 `Task`，废除 requestId 轮询。
- LivingNPCs 侧解析 ValleyTalk 输出的 `ValleyTalkExchangeParser` 及其数据格式
  **原样保留**（它解析的是 LLM 输出里的结构化标记，与旧 mod 无关）。
- `manifest.json` 删除对 `dandm1.ValleyTalk` 的依赖。

## 4. 资产管线（替代 Content Patcher 方案）

- 主 mod 在 `AssetRequested` 中自行提供资产，资产名前缀 **`Mods/Yuki.LivingNPCs/`**
  （例：`Mods/Yuki.LivingNPCs/Bios/Abigail`）。默认内容从 `assets/dialogue/` 读取。
- 第三方（含未来的 SVE 增强包、其他 NPC mod）可用 Content Patcher `EditData`
  这些资产名来覆盖/扩充——保留生态扩展点，但主 mod 不再依赖 CP。
- SVE 检测：`ModRegistry.IsLoaded("FlashShifter.StardewValleyExpandedCP")` 为真时
  改用/合并 `bios-sve/` 集合。

## 5. 升级与共存策略

- 版本号：合并版从 **0.2.0** 起（老用户 0.1.x → 0.2.0 走 Nexus 正常更新）。
- 启动时检测 `dandm1.ValleyTalk` 是否仍在加载：若在，弹 SMAPI 错误级日志 + 游戏内
  HUD 提示"请删除 ValleyTalk 文件夹"，并**本 mod 的对话引擎保持关闭**（避免两套
  Harmony 补丁互踩）。行为系统照常运行。
- 旧数据迁移详见 WP14：存档内 `dandm1.ValleyTalk` 的 SaveData、旧文件夹的
  config.json（API 密钥）与 data 文件，首次运行时自动搬迁。

## 6. 实现顺序（多对话分工的集成次序）

| 阶段 | 工作包 | 依赖 | 可并行 |
|---|---|---|---|
| A | 搬运（03 清单执行 + 命名空间改写） | — | 单独一个对话 |
| B | WP11 LLM 层、WP15 内容/配置、WP14 持久化 | A | 三个对话并行 |
| C | WP10 生成引擎 | B 的接口（可先按本文档接口打桩并行开工） | 可与 B 后半并行 |
| D | WP12 游戏集成、WP16 行为系统接线 | C | 两个对话并行 |
| E | WP20 提示词创作 | 随时（只依赖 20 号文档） | 全程并行 |
| F | 验收（30） | 全部 | 用户主导 |

每个对话开工前读：README + 00 + 01 + 自己的工作包文档；改到共享接口必须停下问用户。
