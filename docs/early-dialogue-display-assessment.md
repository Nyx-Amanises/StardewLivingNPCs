# 提前显示正文的架构评估

2026-09-12，基于当前工作区的静态阅读。本文只评估，不接入新展示路径；未构建、运行游戏、读取用户存档或发送线上请求。引用中的方法名用于定位，行号可能随后续修改移动。

**结论：技术上可行，但需要独立的展示会话和正文定稿边界。** 现有 SSE 开关只改变传输方式。建议先验证“同一原生对话框中，完整正文可读、最终结果尚未提交”的生命周期，再评估在正文行结束时发出 `BodyReady`。不能直接恢复旧预览，也不能用提前 `CommitResult` 代替提前展示。

当前路径可概括为：请求快照与路由 → 主请求（首个正文之前的等待、正文、选项、隐藏 JSON）→ 元数据与最终修订 → 主线程展示 → 一次提交。提前展示只能重叠其中一部分等待与玩家阅读时间，不会让模型尚未生成的正文提前出现。

**当前为什么缓冲。**

| 层级 | 已核实行为与仓库证据 |
| --- | --- |
| 传输 | [OpenAiCompatibleClient.CompleteAsync](../LivingNPCs/Dialogue/Llm/OpenAiCompatibleClient.cs)，约 58–91 行：`UseStreamingDialogueTransport` 开启且不是快速辅助请求时，消费 `base.StreamAsync`，把 `TextDelta` 累积到 `StringBuilder`，完整结束后才返回一个 `LlmReply`。注释明确说明这是网关可靠性的传输选择，调用方仍只收到完整回复。 |
| 游戏入口 | [GenerationScheduler](../LivingNPCs/Dialogue/Engine/GenerationScheduler.cs)，约 21–22、177–225 行：生产路径只调用 `engine.GenerateAsync`；当前没有 `StreamingDialogueWindow` 的运行时实例化。类注释记录旧预览因“预览读完 → 正式框重放”的体验被撤下，最终呈现统一使用原生对话框。 |
| 主响应判定 | [DialogueEngine.GenerateAsync / StreamAsync](../LivingNPCs/Dialogue/Engine/DialogueEngine.cs)，约 140–312 行：先完整解析，再检查错误语言；最多两次尝试，第二次前等待两秒。`StreamAsync` 虽然向 sink 转发原始增量，但 `OnCompleted` 仍在 `FinalizeResultAsync` 之后。 |
| 元数据 | 同文件 `FinalizeResultAsync`，约 847–935 行：Conversation/Gift 先校验 inline 元数据；失败才调用完整 classifier，再按条件调用旧 action supplement。其他触发没有这段辅助分类。 |
| 最终文本 | 同文件约 950–1026 行：还会协调求助正文与选项、恢复出游目标、裁定立即出游是否结束对话、规范称呼、校正表情、拼接原生回应菜单。这里的输出才是当前实际展示和存入历史的正文。 |
| 提交 | `GenerationScheduler.Present` 先检查代际及世界/菜单状态，重新验证肖像，`Draw` 成功后才 `CommitResult`；[GenerationCommit.TryClaim](../LivingNPCs/Dialogue/IDialogueEngine.cs) 用原子 claim 保证最多一次。失败占位结果没有 commit 载荷。 |

`inline` 成功要求最后一行独立的 `!LIVINGNPCS_META`、完整 JSON 和 `complete:true`；伪标记、截断及尾随内容不能证明分类完成。见 [LivingNpcMetadataExtractionPass.ParseInlineResponse](../LivingNPCs/Dialogue/Engine/LivingNpcMetadataExtractionPass.cs)，约 165–252 行。正常 inline 路径不再发送元数据请求，因此**等主响应全部返回后再提前显示，通常只能省去少量本地工作，几乎没有网络等待收益**。收益主要出现在 classifier fallback：该调用默认采用 8 秒超时配置、1600 输出预算；后续 action supplement 受配置和相关性条件限制，使用 512 输出预算。超时是上限配置，不是每次实际耗时，两者也不是每次都会运行。

**可以在哪个阶段开始显示。**

| 阶段 | 可以减少的可见等待 | 限制 |
| --- | --- | --- |
| 完整 `GenerationResult` | 当前行为 | 正文、选项、结束判定和副作用载荷都已确定。 |
| 完整主响应已通过解析和语言检查，但元数据处理未结束 | classifier/action fallback 的等待可与阅读重叠 | 正常 inline 路径几乎无收益；必须先解决后续正文修订。 |
| SSE 中完整的 NPC 正文行已结束，形成 `BodyReady`，主响应尾部仍在接收 | 同一请求后续选项和 JSON 尾部，以及可能的辅助分类等待 | inline 合同规定 NPC 正文是一条以 `-` 开头的行，分页放在行内。应先支持这个明确边界；无前缀、非规范多行或边界不清时退回完整响应路径。`BodyReady` 不是主请求成功或元数据完成。 |
| 安全的句子/页面前缀已到达 | 还能重叠剩余正文生成时间 | 需要真正的增量解析、稳定分页和可撤销的尝试状态；复杂度最高，适合最后评估。 |

[OpenAiChatClientBase.ParseSseLine](../LivingNPCs/Dialogue/Llm/OpenAiChatClientBase.cs)，约 656–690 行，只把 `delta.content` 作为文本增量；`reasoning_content` 不进入正文。网络首个 content 到来之前的等待包括服务端排队、提示词处理、推理与网络时间，不能由当前日志单独拆出“纯思考时间”。无论哪一种 UI 方案，都不能缩短这段模型端等待；只有 reasoning、没有 content 的预算耗尽回复仍然没有可显示正文。

**旧流式组件可复用的部分和必须补齐的边界。**

- [StreamingDialogueUpdateQueue](../LivingNPCs/Dialogue/Ui/StreamingDialogueUpdateQueue.cs) 已有线程安全的数据交接、按 tick 合并、完成覆盖预览、忽略完成后的增量。SpriteText、肖像和布局留在游戏线程，这个分工可以保留。队列没有 generation/attempt 身份；`RetryReset` 只是清空原文。如果重置与第二次尝试的文本在同一轮 drain 合并，窗口没有独立事件把页码和阅读进度归零。重新接线时应传递显式尝试重置事件，并在 UI 接收点拒绝迟到代际。
- [StreamingDialoguePreview.ExtractVisibleText](../LivingNPCs/Dialogue/Engine/StreamingDialoguePreview.cs) 是对累计字符串的清洗，不是完整的增量协议解析器。按当前实现，累计内容停在 `- 好的\n!LIV` 时，尚未识别完整元数据标记，可能暂时显示 `!LIV`。旧预览只剥除完整的尾部物品命令；[ResponseParser.CleanDialogueLine](../LivingNPCs/Dialogue/Engine/ResponseParser.cs) 才会清除全部方括号命令、过滤肖像并执行长度校验。原始增量不能当作已校验文本；所有标记切分位置都需要验收。
- [StreamingDialogueWindow.Advance / ApplyPendingUpdates / RebuildPages](../LivingNPCs/Dialogue/Ui/StreamingDialogueWindow.cs)，约 233、277、481 行：已有“末页读完但未完成时不进入回应选项”的雏形，Esc/B 可请求取消。最终到达时仍会重建所有页面，只按旧页码和字符数截断；正文或换行改变后，不能保证玩家停在同一段内容。它还使用独立的 81 ms/字动画，不等同于原生打字机。
- 当前 `ThinkingDialogueController` 与 [DialogueBox_ThinkingDialogue_Patch](../LivingNPCs/Dialogue/GameHooks/DialogueBox_ThinkingDialogue_Patch.cs) 只为点点点壳服务：点击被拦截，普通按键不推进，Esc/B 取消。仅替换 `getCurrentString` 并不能获得正确的分页、原生动画和动态回应菜单。建议保留原生外观与单一对话会话，对“正文可读、末页等待、最终选项”进行明确管理；原生 `DialogueBox` 内部翻页状态能否原地续接，仍需独立的 UI 验证。
- 如果选择复用旧 `StreamingDialogueWindow`，必须让它从开始到结束承载同一轮对话，并补齐原生行为；不能预览之后再 `Draw` 一遍完整正文。它当前不调用 `CommitResult`，从 `ParsedLines` 展示，也不会执行只存在于 `FormattedLine` 的可信配偶送礼命令。原生路线中的该命令由引擎在最终格式化时加入；不能为补功能而放行模型原始物品命令。
- 原生回复选择还经过 [Dialogue_ChooseResponse_Patch](../LivingNPCs/Dialogue/GameHooks/DialogueResponsePatches.cs)：静默写空玩家行、生成选项传递本次玩家文本、自定义输入进入 `TypedInputRequestQueue`。替换展示载体必须保留这些语义及 `TypedResponses` 三种模式，并保持展示拦截器的历史去重握手。

**正文早于元数据的核心风险是“玩家已经读过，最终结果还会变”。** 求助协调可能删改正文；称呼规范化会改文字；情绪分类影响 `$a` 是否合理；立即出游会清除选项并要求点完关闭。不能先显示原文，再静默改成另一句存入历史。需要由共享后处理规则建立“此正文此后不再修改”的保证；无法保证的响应继续走当前完整等待。单凭精简了提示词动作合同、没看到某个关键词或暂时没有 actions，不能证明正文稳定，也不能授权动作。

提前展示也不能提前提交一份空分析。[BehaviorEngine.TryExecuteConversationActions / BuildEffectiveConversationActions](../LivingNPCs/Behavior/BehaviorEngine.cs)，约 1306–1412 行，会按最终可见台词过滤动作；空 actions 仍可能经规则恢复明确的出游邀请，而且一次成功回复会消费当天送礼机会。提前提交空分析既不能保证没有效果，也会占用唯一 commit，妨碍之后的正式结果。必须继续等完整最终结果，在主线程确认同一展示会话仍有效后，一次性写历史并交给行为层。行为层保留世界动作配置、场景、关系及多人主机权限校验。

当前提交时点是“最终对话框成功显示”，不是“玩家已读完所有页”。随后行为层按 tick 应用交换；出游后续推进还受 [CompanionOutingRules.PlanTick](../LivingNPCs/Behavior/Rules/CompanionOutingRules.cs) 的菜单暂停约束。新流程应保留这个区别：预览可见不构成提交；最终结果安装成功后提交一次，再开放最终选项或关闭。不要未经设计把所有提交延迟到关窗，也不要让未完成的预览提前解锁下一轮输入。

**建议的分阶段方案，均为后续工作建议。**

1. **先记录各阶段等待，保持现有 UI。** 现有 [LlmTransportTiming](../LivingNPCs/Dialogue/Llm/LlmTransportTiming.cs) 已记录 headers、first-content、complete；`DialogueEngine.LogDiagnostics` 已区分 routing、main、metadata、action。未来增加正文边界到达、首字实际绘制、最终安装/提交时间，并关联 generation/attempt。first-content 可能只是 `-`，不是玩家真正看到字的时间。用可控假客户端先验证测量口径，再在获准的运行验证中观察实际收益；不预估“能快几秒”。
2. **先验证完整主响应后的单窗口展示会话。** 设置正文就绪、最终就绪、已取消、已提交等独立状态；基础解析和语言判定通过后，只有可保证后续不改写的正文才能提前交付。把不依赖元数据的清洗放到共享准备步骤，无法证明稳定的求助/礼物/出游及待裁决响应保留完整等待。玩家可阅读已确定页面，读到末页时等待；最终安装不重播、不倒退阅读位置，提交后才开放回应。此阶段主要验证生命周期和隐藏 fallback 等待，不能宣称正常 inline 已显著提速。
3. **再把正文事件前移到 SSE 的完整行边界。** 经独立的可测试解析器产生 `BodyReady`，继续接收选项、JSON、usage 和传输结束；保持完整结束标记、严格 inline 校验和现有失败资格。兼容端点忽略 SSE 而返回完整 JSON、以及没有真流式能力的提供商，仍按完整主响应交付一次，不额外重发来模拟流式。保持第二阶段的正文稳定保证；无明确边界时回退。
4. **最后才考虑逐句/逐页增量。** 只追加稳定页面，保留跨分块控制标记的未决后缀，避免每个 JSON token 都重排已读正文。必须先有完整的重试、错误语言、断流及最终差异策略；实际尾部等待很短时，这一阶段可能不值得做。

会话守卫需要随展示状态一起扩展。当前 `CanPresentNow` 只接受无菜单或本次思考框；直接换成旧自定义流式窗，会把最终结果当作“他人菜单”丢弃。`CancelActiveGeneration` 当前关闭的也是思考框。新会话必须让所有正文/完成事件通过代际、菜单所有权、世界就绪、过日及主屏检查，并接入 [ResetSaveScopedState](../LivingNPCs/Dialogue/GameHooks/DialogueEngineBootstrapper.cs) 的取消清理。未定稿时 Esc/B 或存档作用域切换应使该轮整体作废：已读预览不进入历史、记忆、动作或多人交换上报。定稿并提交后的普通关窗则保持原有语义。

**可验证的验收案例。** 以下是需要覆盖的案例，不表示本次已执行。

| 场景 | 必须观察到的结果 |
| --- | --- |
| 先连续返回 reasoning，随后才有 content；或 reasoning 后直接 length 失败 | 没有推理/协议内容露出，没有虚构正文；首个可见正文之前的等待如实统计，预算耗尽仍走失败路径。 |
| 正文很快、选项及合法 inline JSON 延迟到达 | 第三阶段可以在正文边界显示；同一窗口继续阅读，最终不重放，不另发 classifier，选项只在最终判定后出现。 |
| inline 缺失、截断、伪标记、`complete:false`，辅助分类成功/超时 | 稳定正文可继续阅读，末页等待；保持既有 fallback 规则，不提前提交空分析，完整终态只安装一次。 |
| 每个字符位置切分 `!LIVINGNPCS_META`、`%` 选项、`#$q/$r`、分页/肖像及物品命令 | 任一中间画面不露协议碎片、不执行模型命令；普通百分比不被误删。未完成控制标记等待更多数据。 |
| 已显示一部分后断流、缺少必需完成标记、错误语言或不可解析正文 | 失败尝试不入账；重试明确清除旧尝试和阅读位置，第二次文本不接到旧句末尾；保留最大尝试数和费用记录。 |
| 正文后元数据要求改写求助、表情或结束对话；立即出游清除误生成选项 | 不出现“屏幕读到 A、历史存成 B”；无法保证正文一致的场景完整等待，不能用最终替换掩盖已读差异。 |
| 连续点击末页、Esc/B、正文后立即返回标题或换存档；旧请求迟到 | 未完成不进入下一轮；取消后无历史/行为/多人报告，迟到 token 与 final 都被代际拒绝，玩家控制和键盘订阅正常释放。 |
| 过日、其他 mod 打开菜单、主副分屏交替 tick | 不夺取或关闭他人菜单；不在副屏消费完成/提交；旧会话不污染新会话。 |
| 最终送礼、配偶原生命令、立即出游、拒绝/未来计划 | 授权及可见证据规则保持一致，物品/每日机会/历史最多处理一次；预览无副作用，菜单等待与出游时序不回归。 |
| 中文长句、多页、窗口缩放、肖像资源变化、生成/静默/自定义回应 | 页码和已读位置稳定、无重复首屏；肖像按展示时资源重验；三类回应仍经正确入口续接，历史不重复。 |

既有回归可作为后续测试基础：[StreamingRetryAndParserEdgeFixTests](../LivingNPCs.Tests/Dialogue/Engine/StreamingRetryAndParserEdgeFixTests.cs)、[StreamingDialogueUpdateQueueTests](../LivingNPCs.Tests/Dialogue/Ui/StreamingDialogueUpdateQueueTests.cs)、[GenerationSchedulerCancellationTests](../LivingNPCs.Tests/Dialogue/Engine/GenerationSchedulerCancellationTests.cs)、[GenerationSchedulerPresentGuardTests](../LivingNPCs.Tests/Dialogue/Engine/GenerationSchedulerPresentGuardTests.cs)、[LivingNpcInlineMetadataTests](../LivingNPCs.Tests/Dialogue/LivingNpcInlineMetadataTests.cs) 和 [OpenAiStreamingTests](../LivingNPCs.Tests/Dialogue/Llm/OpenAiStreamingTests.cs)。它们覆盖现有队列、重试、取消和协议语义；不会自动证明新增展示会话的原生分页与无重播行为，后者仍需 UI 验证。
