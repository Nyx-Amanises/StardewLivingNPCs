# 11 · WP11 LLM 提供商层功能说明书

> 读者：从未见过旧代码的实现 AI。开工前先读 00、01、03。
> 本文只描述**行为**；各家 LLM 公开 API 的线协议字段名、端点、header 名为公开事实，
> 按原样精确记录。禁止去旧代码目录核对——需要的一切都在这里，缺了就记开放问题。

## 1. 目的与范围

WP11 负责 `LivingNPCs/Dialogue/Llm/` 下的全部代码：把"一次分段的提示词 + 生成参数"
变成"一段文本 + 一份 token 用量"，屏蔽各家 LLM API 的差异。覆盖：

- 提供商抽象与统一请求模型（分段提示词、参数透传、模型名列表）；
- 8 个真实提供商 + 1 个测试桩的精确线协议（端点、认证、请求体、流式、usage、错误）；
- Prompt Caching 策略（Claude cache_control、OpenAI prompt_cache_key、DeepSeek/Gemini
  隐式缓存、llama.cpp cache_prompt）——这是近期精修过的行为，必须完整保留；
- 共享 HttpClient 生命周期、超时、重试策略；
- Android 网络可用性检查的挂载点；
- 启动/切换提供商时的连接自检与诊断分级。

**不在本包**：提示词内容的拼装（WP10/WP20）、token 用量的持久化统计（WP14 的
`TokenUsageTracker`）、配置 UI（WP15）。思考档位翻译器 `LlmThinking`、流式接口
`IStreamingLlm`、用量模型 `TokenUsage` 是搬运件（见 §2），本包只消费不重写。

## 2. 权属与搬运边界

- **重写**（本包产出）：抽象基类与全部提供商实现（旧世界的 `Llm`、`LlmOpenAiBase`、
  `LlmOpenAi`、`LlmOAICompatible`、`LlmClaude`、`LlmGemini`、`LlmDeepSeek`、
  `LlmMistral`、`LlmVolcEngine`、`LlmLlamaCpp`、`LlmDummy` 的对应物）、
  网络辅助类（旧 `Platform/NetworkHelper.cs`、`NetworkAvailabilityChecker.cs` 的对应物）、
  模型名列表能力接口（旧 `IGetModelNames` 的对应物）。这些文件在权属地图上有上游血统，
  一律按本文档行为重新实现，不得参考旧文件。
- **搬运件**（阶段 A 已复制进 `Dialogue/Llm/`，MINE，可直接阅读与调用）：
  - `LlmThinking.cs`：思考档位（auto/off/minimal/low/medium/high/xhigh）到各家请求
    参数的翻译器。本包在组请求体时调用它，接口关系见 §4.6。
  - `TokenUsage.cs`：统一用量模型，含各家 usage JSON 的解析静态方法与本地估算。
    本包每个响应路径都要正确调用对应的解析方法（口径见 §3 各节）。
  - `IStreamingLlm.cs`：旧流式接口。按 03 §2 的裁决权，本包裁定其**让位于**
    `ILlmClient.StreamAsync`（见 §5），搬运件仅作过渡期编译桥，最终删除。
- **明确废弃、不重写**：旧 `PromptFormatter.cs`（全项目无调用点，死代码）；
  Claude/Gemini 类里的 `CacheContexts` 字典（Claude 侧从未读写；Gemini 侧读了但
  结果变量从未使用，属残留死代码）。新实现不得复刻。
- 提示词侧消费的两个提供商属性（`IsHighlySensoredModel`、`ExtraInstructions`，
  见 §4.7）保留为能力标志，其取值本文钉死。

## 3. 外部契约：各提供商线协议

统一约定（下文不再重复）：请求体均为 JSON，`Content-Type: application/json; charset=utf-8`；
除特别注明外均为 POST；`max_tokens`/`maxOutputTokens` 等生成上限由调用方传入
（默认 2048）；HTTP 非 2xx 一律按错误处理（见 §4.3）。

### 3.1 OpenAI 兼容家族（OpenAI / DeepSeek / Mistral / 自定义兼容端点）

四个提供商共用一个基类实现，差异只在基地址、默认模型与三个能力开关。

| 提供商 | 配置 Provider 值 | 基地址 | 默认模型 | prompt_cache_key | stream_options | instructions 回退 |
|---|---|---|---|---|---|---|
| OpenAI 官方 | `OpenAI` | `https://api.openai.com` | `gpt-4o` | ✔ | ✔ | ✘ |
| DeepSeek | `DeepSeek` | `https://api.deepseek.com` | `deepseek-chat` | ✘ | ✔ | ✘ |
| Mistral | `Mistral` | `https://api.mistral.ai` | `mistral-large-latest` | ✘ | ✘ | ✘ |
| 兼容端点 | `OpenAiCompatible` | 用户配置 `ServerAddress` | `mistral-large-latest` | ✘ | ✘ | ✔ |

- **端点**：非流式与流式同为 `{基地址}/v1/chat/completions`；模型列表
  GET `{基地址}/v1/models`。
- **兼容端点的地址规整**：用户填的 `ServerAddress` 依次剥掉尾部 `/`、尾部
  `/chat/completions`、尾部 `/v1`，得到基地址（容忍用户把完整端点粘进配置）。
- **认证**：header `Authorization: Bearer {ApiKey}`；ApiKey 为空则不带该 header
  （本地兼容端点可无鉴权）。
- **请求体**（非流式）：`model`、`max_tokens`、`messages`（元素为
  `{"role": ..., "content": ...}`）。默认两条消息：`system` 角色装系统提示词，
  `user` 角色装"稳定世界段 + NPC 段 + 可变尾段"三段拼接（分段含义见 §4.1）。
  **不发送 temperature/top_p**——沿用各端点默认采样，这是现状行为，保留。
- **instructions 回退**（仅兼容端点）：部分兼容网关不认 `system` 角色。请求体候选
  序列在耗尽标准形态后追加一轮"回退形态"：顶层字段 `instructions` 装系统提示词，
  `messages` 只含一条 user。候选序列的完整顺序见 §4.2。
- **思考参数**：调用 `LlmThinking.AddOpenAiCompatibleThinkingParameters(body, modelName, level)`
  由搬运件按模型系注入（如 `reasoning_effort` 等），本包不关心注入细节；
  当档位为 off 时额外设 `response_format` 为 `{"type": "json_object"}`（快速 JSON
  路由通道防弱模型把 JSON 包散文里）。
- **OpenAI 官方缓存键**（Yuki 近期工作，必须保留）：官方端点按 `prompt_cache_key`
  路由缓存分片。所有 NPC 请求的开头（system + 世界摘要）完全相同，若按默认前缀
  路由会挤进同一分片，所以**按 NPC 前缀哈希分键**：对
  "系统段 + U+0001 + 稳定世界段 + U+0001 + NPC 段"（分隔符为控制字符 U+0001）
  求 SHA-256，取前 8 字节的大写十六进制，拼成 `valleytalk-{16位hex}` 作为顶层
  字段 `prompt_cache_key` 的值。
  新实现改前缀 `livingnpcs-` 亦可，但哈希输入的三段与 U+0001 分隔符不变
  （键值只影响路由不影响计费口径）。非流式与流式请求体都要带。
  DeepSeek/Mistral/兼容端点**不发送**该字段（不认识的端点可能报参数错误）。
- **非流式响应解析**：取 `choices[0].message.content`；usage 用
  `TokenUsage.FromOpenAiUsage(响应.usage)`（口径：`prompt_tokens`、
  `completion_tokens`、`total_tokens`，缓存命中读 `prompt_tokens_details.cached_tokens`
  或 DeepSeek 旧字段 `prompt_cache_hit_tokens`，推理 token 读
  `completion_tokens_details.reasoning_tokens`——解析逻辑在搬运件里，别重写）。
  **容错**：个别兼容端点对非流式请求也回 SSE 文本（以 `data:` 开头），此时按 SSE
  逐行拼接 `delta.content` 得到全文，并从带 usage 的块提取用量；`text` 字段
  （旧式 completions 形态）也接受。全文为空白视为本次尝试失败。
- **流式协议**：请求体在非流式基础上加 `stream: true`；开启 `SendStreamUsageOptions`
  的提供商（OpenAI、DeepSeek）再加 `stream_options: {"include_usage": true}`，
  让回包末尾带真实 usage（含 cached_tokens），替代本地估算——这也是近期工作，保留。
  响应为 SSE：逐行读取，空行跳过；`data:` 前缀（大小写不敏感）后为 JSON 载荷，
  载荷 `[DONE]` 表示结束；每块取 `choices[0].delta.content` 作为增量回调出去；
  任何携带非空 `usage` 对象的块（include_usage 的收尾块 choices 为空、
  DeepSeek/Mistral 默认末块）都更新"最终 usage"。无 `data:` 前缀的行按裸 JSON
  尝试同样解析（部分网关不带前缀）。流结束后若拿到过增量则成功返回；
  usage 优先用流内真实值，否则用 `TokenUsage.Estimate`（CJK 感知估算，来源标记
  `stream estimate`）。
- **流式降级链**（顺序固定）：① 流式重试耗尽后，把累计的原始响应文本按非流式
  格式再解析一次（有的端点无视 stream 参数直接回完整 JSON），解析出的全文一次性
  推给增量回调并返回（usage 标 `stream fallback estimate`）；② 仍失败则调用
  **非流式**通道兜底（禁止重试），成功则全文一次性推给回调；③ 全部失败返回错误
  响应（最后一次原始文本 + 状态码）。取消令牌在每次降级前都要检查。
- **流式思考档位**：流式请求体注入思考参数时按"非快速通道"档位
  （`LlmThinking.ForCall(fastPass: false)`），且**没有**思考参数回退序列——
  参数被拒时走上面的降级链。
- **模型列表**：GET `{基地址}/v1/models`，响应 `data` 数组内每项取 `id`。
  OpenAI/DeepSeek/Mistral 在 ApiKey 为空时直接返回空列表不发请求；兼容端点
  无条件发（可能无鉴权）。任何异常记错误日志并返回空列表。

### 3.2 Anthropic Claude（Provider 值 `Anthropic`）

- **端点**：POST `https://api.anthropic.com/v1/messages`；模型列表
  GET `https://api.anthropic.com/v1/models`。
- **认证 header**：`x-api-key: {ApiKey}` 与 `anthropic-version: 2023-06-01`。
  **不用** Bearer。Prompt caching 已 GA，**不需要** beta header。
- **默认模型**：`claude-haiku-4-5`（`claude-3-5-haiku` 已于 2026-02 退役，请求会 404；
  注释里保留这条事实以防回退）。
- **请求体与缓存断点放置**（近期精修，逐字保留策略）：
  - `model`、`max_tokens`；
  - `system` 为**数组**，两个文本块：块 1 `{"type":"text","text": 系统提示词}`
    不带缓存标记；块 2 `{"type":"text","text": 稳定世界段, "cache_control":{"type":"ephemeral"}}`
    ——断点 1 盖住"系统提示词+世界摘要"这段全 NPC 共享的稳定前缀；
  - `messages` 一条 user。其 `content`：若 NPC 段为空，直接是可变尾段字符串；
    否则是两元素数组：`{"type":"text","text": NPC段, "cache_control":{"type":"ephemeral"}}`
    + `{"type":"text","text": 可变尾段}`——断点 2 让同一 NPC 的连续轮次复用
    传记/示例段，可变尾段永远不进缓存。
  - 已知事实：低于模型最小可缓存前缀（Haiku 4.5 为 4096 token）的断点会被服务端
    **静默忽略**，不报错，行为退化为不缓存——不要因此报警。
- **响应解析**：取 `content[0].text`；空白视为失败进入重试。
- **usage 修缮口径**（近期工作，必须保留）：`TokenUsage.FromClaudeUsage` 读
  `input_tokens`、`output_tokens`、`cache_creation_input_tokens`、
  `cache_read_input_tokens`。Claude 的 `input_tokens` **只含未走缓存的余量**，
  真实 prompt 规模 = 三者之和；缓存命中记入 `CachedPromptTokens`，缓存写入记入
  `CacheWritePromptTokens`（Claude 对写入按加价计费，其他家恒为 0）。
- **模型列表**：同一组 header，响应 `data` 数组每项取 `id`；ApiKey 为空返回空列表。
- **无流式实现**：Claude 客户端不实现流式接口，引擎侧自动走非流式（见 §6）。

### 3.3 Google Gemini（Provider 值 `Google`）

- **端点**：POST
  `https://generativelanguage.googleapis.com/v1beta/models/{模型名}:generateContent?key={ApiKey}`
  ——认证走 **URL query 参数**，无认证 header。模型列表
  GET `https://generativelanguage.googleapis.com/v1beta/models?key={ApiKey}`。
- **默认模型**：`gemini-2.5-flash`。
- **请求体**：
  - `safetySettings` 数组，两项：`HARM_CATEGORY_SEXUALLY_EXPLICIT` → `BLOCK_NONE`、
    `HARM_CATEGORY_HARASSMENT` → `BLOCK_MEDIUM_AND_ABOVE`（有意放开前者：恋爱/配偶
    对话常被误杀；骚扰类保中档，逐字保留）；
  - `system_instruction.parts.text` 装系统提示词；
  - `contents.parts.text` 装三段拼接的用户内容（Gemini 通道不拆缓存段，依赖
    Gemini 2.5 系的隐式前缀缓存）；
  - `generationConfig`：`maxOutputTokens`、`temperature: 1.5`、`topP: 0.9`；
    思考档位有效时加 `thinkingConfig`（由搬运件
    `LlmThinking.BuildGeminiThinkingConfig(level, modelName)` 生成）；档位 off 时加
    `responseMimeType: "application/json"`。
- **请求体候选序列**：若 thinkingConfig 非空，先发带它的版本；随后（thinkingConfig
  为空、或档位非 auto 时）发不带 thinkingConfig 的裸版本兜底。每个候选各自带
  完整重试预算；带思考参数的候选全部失败时记一条思考回退警告（§4.6）。
- **响应解析**：`candidates[0]`，**必须** `finishReason == "STOP"` 才接受（截断/
  安全拦截一律进重试）；文本取 `content.parts[0].text`。HTTP 200 但文本为空白时
  返回错误响应（消息 `Empty response`，状态码 200，不再重试该候选）。
- **usage 口径**：`TokenUsage.FromGeminiUsage(响应.usageMetadata)`：
  `promptTokenCount`、`candidatesTokenCount`、`totalTokenCount`、
  `thoughtsTokenCount`（推理）、`cachedContentTokenCount`（隐式缓存命中，
  **已包含在 promptTokenCount 内**，统计侧不要重复相加）。
- **模型列表**：响应 `models` 数组每项取 `name`，剥掉 `models/` 前缀。

### 3.4 火山引擎 Ark（Provider 值 `VolcEngine`）

- **端点**：POST `https://ark.cn-beijing.volces.com/api/v3/chat/completions`
  （注意：路径**不含** `/v1`，与 OpenAI 家族不同，不能复用其端点拼接）；模型列表
  GET `https://ark.cn-beijing.volces.com/api/v3/models`。
- **认证**：`Authorization: Bearer {ApiKey}`。默认模型 `doubao-1.5-pro`。
- **请求体**：`model`、`max_tokens`、`messages`（system + user 两条，user 为三段
  拼接），序列化时**忽略 null 字段**。两个条件字段：
  - `thinking: {"type": "disabled"}`——仅当"本次是快速路由调用（disableThinking）
    **且**当前模型确实支持关闭思考"才发。判断为白/黑名单（小写子串匹配）：
    强制思考模型 `seed-1.6-thinking`/`seed-1-6-thinking`/`deepseek-r1` 永不发送；
    可关闭的混合模型 `seed-1.6`/`seed-1-6`、`1.5-thinking`/`1-5-thinking`、
    `deepseek-v3.1`/`deepseek-v3-1` 才发送；名单之外一律不发（给不支持的模型如
    doubao-1.5-pro 发会直接报"参数不支持"硬错误——保守设计，宁可少提速）。
    注意直接 HTTP 调用时 `thinking` 与 `model` 同级（OpenAI SDK 才需要 extra_body）。
  - `response_format: {"type": "json_object"}`——仅 disableThinking 路径（快速 JSON
    通道），主对话请求体不带。
- **响应解析与 usage**：同 OpenAI 形态（`choices[0].message.content` +
  `TokenUsage.FromOpenAiUsage`）。无流式实现。

### 3.5 llama.cpp server（Provider 值 `LlamaCpp`）

- **端点**：用户配置的 `ServerAddress` **原样作为完整端点**（通常是
  `http://host:port/completion`），不做规整。无认证。
- **请求体**：`prompt`（见下）、`n_predict`、`stream: false`、
  `cache_prompt: true`（让服务端保留上次 KV cache，相同前缀免重算——老版本
  llama.cpp 默认关闭，必须显式发）、`temperature: 1.5`（当 `n_predict == 1` 时为 0）、
  `top_p: 0.88`、`min_p: 0.05`、`repeat_penalty: 1.05`。
- **prompt 模板**：配置字段 `PromptFormat`（默认
  `[INST] {system}\n{prompt}[/INST]\n{response_start}`）按占位符字面替换：
  `{system}` ← 系统提示词、`{prompt}` ← 三段拼接、`{response_start}` ← 响应引导前缀。
  这是唯一使用 `responseStart` 参数与 `PromptFormat` 配置的提供商。
- **响应解析**：文本取顶层 `content`；`timings` 对象取 `prompt_n`/`predicted_n`
  折算 usage（`TokenUsage.FromLlamaCppTimings`），并累计到会话级性能统计。
- **概率查询**（本提供商独有，供"多选一"判定）：同端点，请求体
  `prompt`、`n_predict`、`stream:false`、`temperature: 0.8`、`top_p: 0.88`、
  `min_p: 0.05`、`cache_prompt: true`、`n_probs: 10`；响应
  `completion_probabilities[]` 每项的 `probs[]` 取 `tok_str`→`prob` 组成字典。
  抽象层提供的选项概率归并算法（把 token 前缀树上的概率递归聚合到选项桶）随
  本能力一起保留；其他提供商对概率接口一律抛"不支持"。
- **旧实现的重试是无限循环**（失败睡 1 秒再来，直到成功）。新实现**改为有限次数**
  （与其他家一致：3 次），这是明确的行为修正，记入验收。

### 3.6 Dummy（Provider 值 `Dummy`，仅 DEBUG 构建注册）

不发任何网络请求。每次调用五五开随机返回两种固定文本之一：
`- LLM generated string.` 或多行
`- LLM generated question\n% One answer\n% Another answer\n% A third answer`
（`- ` 开头是对话行、`% ` 开头是玩家选项，恰好符合引擎的输出解析格式，见 WP10）。
用途：无网/无 key 环境下调试 UI 与解析管线；单元测试的默认注入。
`IsHighlySensoredModel` 取 true。概率接口抛"不支持"。

## 4. 行为规范

### 4.1 统一请求模型：四段提示词

抽象层的核心签名（语义，不是字面）：**推理(系统段, 稳定世界段, NPC 段, 可变尾段,
响应引导 = "", 生成上限 = 2048, 允许重试 = true, 关闭思考 = false) → 响应**。
四段划分是缓存策略的地基：

| 段 | 内容（WP10 填充） | 稳定性 | Claude 断点 | OpenAI 缓存键输入 |
|---|---|---|---|---|
| 系统段 | 角色扮演总指令 | 全局稳定 | system 块 1（不标） | ✔ |
| 稳定世界段 | 世界观/游戏摘要 | 全局稳定 | system 块 2 + ephemeral | ✔ |
| NPC 段 | 传记、示例对话 | 按 NPC 稳定 | user 块 1 + ephemeral | ✔ |
| 可变尾段 | 历史、现场上下文、指令 | 每轮变化 | user 块 2（不标） | ✘ |

不做请求级缓存声明的提供商把后三段按序拼接为 user 内容。`关闭思考` 只在快速
JSON 通道（语义路由、礼物邮件、行动判定）为 true。响应对象携带：文本、错误消息、
HTTP 状态码、`TokenUsage`、成功标志。

### 4.2 重试与请求体候选序列

- 单请求体的重试预算：`允许重试 ? 3 : 1` 次，失败间隔约 100ms（llama.cpp 通道 1s），
  每次失败仅 Debug 级日志（玩家不可见）。
- OpenAI 家族/Gemini 有**请求体候选序列**（思考参数版 → auto 兜底版；兼容端点再
  ×{标准形态, instructions 形态}）；每个候选独享完整重试预算，前一个候选耗尽才换
  下一个。带思考参数的候选耗尽时调用
  `LlmThinking.LogThinkingFallbackWarning(modelName, level, 参数描述, 最后错误)`
  记一次性警告（提示用户该模型不认思考参数）。
- 从异常链提取 HTTP 状态码：`HttpRequestException`（本体或 InnerException）的
  StatusCode；取不到时错误响应默认 500。
- 全部候选耗尽：返回失败响应（原始响应文本作为错误消息 + 状态码），**不抛异常**。
  调用方（WP10）看成功标志决定回落原版对话。

### 4.3 HttpClient 生命周期与超时（近期精修，保留）

全部提供商共用**一个进程级静态 HttpClient**：连接与 TLS 握手池化复用，避免每请求
重建（也避免 socket 耗尽）。规则：

- 客户端自身 `Timeout` 设为**无限**；每个请求用"调用方取消令牌 + 超时"链接成的
  CancellationTokenSource 自行限时。这样：① 配置 `QueryTimeout` 改动即时生效
  （不用重建客户端）；② 流式请求可以不受统一超时约束地长跑。
- 默认超时 = `max(5, 配置的 QueryTimeout)` 秒；配置缺省 85。模型列表/连接检查等
  一次性调用显式传 1 分钟。
- 异常翻译（调用方依赖这些类型区分）：调用方令牌触发 → 取消异常（消息
  `Request was cancelled`）；超时触发 → `TimeoutException`（`Request timed out`）；
  HTTP 层错误 → 包一层 `InvalidOperationException`，消息含原始信息，若已拿到响应
  再附 `(HTTP {状态码} - {响应体})` 方便诊断。
- 统一入口：内容为空发 GET，否则 POST JSON；`authToken` 参数存在则加
  `Authorization: Bearer …`；`headers` 字典逐项加到请求 header（Claude 用它带
  x-api-key）。
- 流式专用入口：`HttpCompletionOption.ResponseHeadersRead` 发送，只等响应头，
  响应体流交调用方持有并负责释放，读取由调用方令牌限界。
- Android 下客户端默认 header 加
  `User-Agent: ValleyTalk/1.0 (Android; Stardew Valley Mod)`（新实现可改为
  LivingNPCs 名义，格式保持"名/版本 (Android; Stardew Valley Mod)"）。
- 提供静态 Dispose 供 mod 卸载路径调用（实践中进程随游戏退出，非关键）。

### 4.4 Android 网络可用性检查

两处挂载点，行为不同：

1. **每次推理入口**（所有真实提供商的推理与流式方法开头）：若在 Android 且
   `NetworkInterface.GetIsNetworkAvailable()` 为 false，**立即抛**
   `InvalidOperationException("Network not available")`——不发请求不重试，
   由重试循环外层捕获后按普通失败处理。非 Android 平台零开销跳过。
2. **游戏钩子侧预检**（供 WP12 的补丁在发起生成前调用）：Android 下若首查不可用，
   Warn 日志后每秒重查一次共 5 次；仍不可用则再记 Warn（"禁用本次 AI 对话生成"）
   并返回 false，调用方直接走原版对话。非 Android 恒真。提供同步包装（阻塞等待）
   给无法 async 的补丁点。
   平台判定：主游戏程序集名含 `Android`。

### 4.5 提供商注册、工厂与连接自检

- **注册表**：配置字符串 → 实现类型的字典，**大小写不敏感**。键与 §3 各节的
  Provider 值一致；`Dummy` 仅 DEBUG 构建注册。配置的 Provider 不在表内：Error 日志
  `Invalid LLM type: {值}`，引擎保持关闭（不崩游戏）。
- **构造参数**：各实现按需取 `ApiKey`、`ModelName`（空则用各自默认）、
  `ServerAddress`（仅兼容端点与 llama.cpp）、`PromptFormat`（仅 llama.cpp）。
  旧实现用反射按参数名匹配构造函数，新实现**改为工厂里显式 switch**（行为等价、
  可读可查错，注册表值直接是工厂委托）。
- **切换语义**：游戏启动（读配置后）与 GMCM 保存配置时都会重建当前客户端实例并
  整体替换；替换同时清除"引擎禁用"标志。旧实例上仍在途的请求继续跑完，结果照常
  返回（不做取消），但其连接自检结果作废（见下）。
- **连接自检**（除非配置 `SuppressConnectionCheck` 为 true）：
  - 所有本地化诊断文案**先在主线程解析完**再交给后台任务——文案解析要过游戏内容
    管线，工作线程碰它不安全；自检本身是纯网络 IO + 线程安全日志，放后台跑，
    不冻结启动/保存配置。
  - 自检 = 一次**禁止重试**的真实推理，四段分别为固定英文诊断文本：系统段
    `You are performing LLM connection testing`、三段拼起来是
    `Please just respond with 'Connection successful'`。
  - **陈旧检查防护**：自检开始前后都比对"当前全局实例还是不是自己"（引用相等），
    不是则静默放弃，不打日志（防止用户连续切换提供商时旧检查报错误导）。
  - 成功判定：响应成功、文本非空白且长度 ≥ 5。成功记 Info 一条。
  - 失败诊断分级（全部 Error 级，逐条追加）：先报"连接失败 + 模型名 + 提供商类名"；
    有错误消息附错误消息；然后 ① ApiKey 为空 → 提示配 key；② 否则若该提供商能列
    模型：模型名为空 → 提示必须配模型名；列表**包含**配置的模型名 → 提示"能取列表
    且模型名有效，可能是非文本模型/不安全端点/部分家 key 错"；列表**不含** →
    提示检查模型名；兼容端点或 llama.cpp 且地址不含 `https` → 追加不安全连接提示；
    列表为空 → 提示检查 API key；③ 不能列模型的提供商 → 通用"检查服务器地址"提示。
  - 无论怎么失败，最后补一条 **Warn**："自检失败但引擎保持启用，真实对话时再试"
    ——自检**永不**禁用引擎，这是钉死的语义。
  - 自检任务自身抛异常：捕获并 Error 一条，不影响引擎。
  - 九条文案全部走 i18n，键名（供 WP15 对齐）：`modelCheckNonBlocking`、
    `modelCheckApiKey`、`modelCheckModelName`、`modelCheckValidModelName`、
    `modelCheckCantGenerate`、`modelCheckInsecure`、`modelCheckGetNames`、
    `modelCheckGenericError`、`modelCheckSuccess`；键缺失时用英文兜底文案。
- **配置字段名**（精确，WP15 对齐）：`Provider`（默认 `"Mistral"`）、`ModelName`
  （默认空）、`ServerAddress`（默认 `"https://openrouter.ai/api"`）、`ApiKey`
  （默认空）、`PromptFormat`（默认 `"[INST] {system}\n{prompt}[/INST]\n{response_start}"`）、
  `QueryTimeout`（默认 85，秒）、`SuppressConnectionCheck`（默认 false）。

### 4.6 与 LlmThinking（搬运件）的接口关系

本包只调用这些成员，不重写其内部：`ForCall(bool fastPass)` 取本次调用档位；
`IsAuto`/`IsOff` 判档位；`AddOpenAiCompatibleThinkingParameters(JObject, model, level)`
注入 OpenAI 系思考参数；`BuildGeminiThinkingConfig(level, model)` 生成 Gemini
thinkingConfig（可能为 null）；`DescribeThinkingParameters(JObject)` 生成回退日志用
的参数描述；`LogThinkingFallbackWarning(model, level, params, providerError)` 记
回退警告。VolcEngine 的关思考白名单（§3.4）留在提供商内部，不进 LlmThinking。

### 4.7 提供商能力标志（提示词侧消费）

- `IsHighlySensoredModel`（bool）：内容审查严格的模型，提示词侧据此回避易触发
  拒答的措辞。取值：Claude、Dummy 为 **true**，其余全部 false。
- `ExtraInstructions`（string）：追加进指令区的提供商特定文案。仅 llama.cpp 非空：
  `Include only the new line and any responses in the output, no descriptions or explanations.`
  其余为空串。

### 4.8 错误分类学（汇总）

| 类别 | 例子 | 处理 | 玩家可见性 |
|---|---|---|---|
| 配置错误 | Provider 字符串无效 | 引擎不启用 | SMAPI Error 日志 |
| 自检失败 | key 错/模型名错/端点不通 | 分级诊断 + 保持启用 | Error+Warn 日志 |
| 瞬时请求失败 | 超时、5xx、解析失败、截断 | 静默重试（预算内） | 仅 Debug 日志 |
| 预算耗尽 | 连续 3 次失败 | 返回失败响应，WP10 回落原版对话 | Warn（WP10 记 API 错误消息） |
| Android 断网 | 推理入口检查失败 | 立即失败不重试；钩子侧预检 5s 后放弃本次生成 | Warn 日志 |
| 玩家/系统取消 | 流式窗口关闭 | 取消异常向上传播，不重试 | 无 |
| 禁用引擎 | （现状）仅"引擎禁用"标志位，切换提供商时清除 | 生成入口检查该标志 | — |

**永不重试**的场景：连接自检（避免启动期反复打端点）、流式链的非流式兜底
（外层已有预算）。旧世界没有任何 HTTP 状态码会自动禁用引擎（含 401/429）；
**新世界加熔断**，语义见 §8 裁决 2。

## 5. 新类型与接口建议

实现 01 §2 的 `ILlmClient`（成员钉死：`ProviderId`、`CompleteAsync(LlmRequest, ct)`、
`StreamAsync(LlmRequest, ct) → IAsyncEnumerable<LlmStreamEvent>`——事件流含文本增量
与末尾 Usage 事件，01 §2 已裁决）。建议的配套类型
（字段名可由 WP10 最终裁定，冲突以 WP10 为准）：

- `LlmRequest`：`SystemPrompt`、`StableContext`（稳定世界段）、`NpcContext`、
  `Tail`（可变尾段）、`ResponseStart`、`MaxTokens`（默认 2048）、
  `DisableThinking`、`AllowRetry`。四段结构必须保留（§4.1 缓存策略依赖它）。
- `LlmReply`：`Text`、`ErrorMessage`、`HttpStatus`、`Usage`（TokenUsage 搬运件）、
  `IsSuccess`。
- 每个提供商一个实现类，`ProviderId` 即注册表键（`OpenAI`/`DeepSeek`/`Mistral`/
  `OpenAiCompatible`/`Anthropic`/`Google`/`VolcEngine`/`LlamaCpp`/`Dummy`）。
  OpenAI 家族共享一个受保护基类（能力开关：缓存键、流式 usage、instructions 回退）。
- `LlmClientFactory`：静态注册表（键大小写不敏感）+ 显式构造 switch；对外
  `TryCreate(config) → ILlmClient?`。全局"当前客户端"的持有与切换（含自检触发、
  陈旧自检防护）建议放引擎侧一个 `LlmClientHost`，WP11 提供、WP10 消费。
- **流式 usage 的传递**：已裁决（01 §2）——`StreamAsync` 产出 `LlmStreamEvent`
  事件流，文本增量之后以一条 Usage 事件收尾（携带流内真实 usage 或估算值，
  `Source` 标注口径），WP10/WP14 直接消费，无需 Host 层旁路。
- 无流式能力的提供商（Anthropic/Google/VolcEngine/LlamaCpp/Dummy）：`StreamAsync`
  内部调 `CompleteAsync`，把全文作为单个增量 yield 一次——调用方无需感知差异，
  搬运的 `IStreamingLlm` 即可删除。
- 概率查询（仅 llama.cpp，§3.5）不进 `ILlmClient`；做成可选接口
  `ITokenProbabilities`，消费方（WP10 若仍需要）类型探测使用。

## 6. 与其他工作包的接口

- **WP10（引擎）**：唯一消费方。经 `ILlmClient` 发起补全/流式；填充四段提示词；
  依据 `IsSuccess` 回落原版对话；消费 §4.7 两个能力标志（建议并入 `ILlmClient`
  的只读属性或能力接口）。旧世界流式入口在生成侧类型探测 `IStreamingLlm`，新世界
  一律有 `StreamAsync`，探测消失。
- **WP12（游戏钩子）**：发起生成前调用 §4.4 的 Android 预检；生成期间的取消令牌
  由 UI（流式窗口关闭）传入。
- **WP14（持久化）**：`TokenUsage`（搬运件）随每次响应交给用量统计；`Source` 字段
  区分 `provider usage` / `stream estimate` / `local estimate` / `llama.cpp timings`，
  统计侧据此标注"估算"记录。
- **WP15（配置/GMCM）**：§4.5 的 7 个配置字段名；Provider 下拉项 = 注册表键；
  GMCM 保存时触发客户端重建 + 自检；模型名下拉调用各客户端的模型列表能力
  （实现 `IGetModelNames` 对应物的才显示下拉，注意该调用是**同步阻塞**的，只允许
  在 GMCM UI 线程按钮点击时调用，超时 1 分钟）；9 个 `modelCheck*` i18n 键。
- **WP16（行为系统）**：不直接接触本包；其快速判定通道经 WP10 传 `DisableThinking`。

## 7. 验收要点

1. 逐提供商对录制的真实响应样本做解析测试：OpenAI 正常 JSON / SSE 误回 / 空
   choices；Claude 三种 usage 组合（全缓存命中、首次写入、无缓存）验证
   `PromptTokens = input + cache_creation + cache_read`；Gemini `finishReason != "STOP"`
   进重试、空文本 200 直接失败；VolcEngine 白名单内外模型的 `thinking` 字段有无。
2. Claude 请求体快照测试：system 两块（第二块带 cache_control ephemeral）、NPC 段
   非空时 user content 两块（第一块带 cache_control）、NPC 段为空时 user content
   为纯字符串。
3. OpenAI 缓存键：同一 NPC 两次请求键相同，不同 NPC 键不同；DeepSeek/Mistral/
   兼容端点请求体中**无** `prompt_cache_key`；OpenAI/DeepSeek 流式体带
   `stream_options.include_usage`。
4. 流式降级链顺序：SSE 正常 → 全文=增量拼接且 usage 用流内真实值；SSE 空但整体
   是完整 JSON → 一次性回调；再失败 → 非流式兜底且其 `AllowRetry` 为 false。
5. 超时行为：改 `QueryTimeout` 配置后**下一个请求**即生效（不重建客户端）；
   调用方取消与超时抛不同异常类型。
6. 自检：`SuppressConnectionCheck` 为 true 时零网络调用；自检期间切换提供商后旧
   自检零日志；自检失败后引擎仍可发起真实生成。
7. llama.cpp：重试改为有限次后，服务端不可达时在约 3 次×1s 内放弃并返回失败响应。
8. Android 分支用假平台开关注入测试（真机冒烟由用户做）。
9. 全部网络访问经共享 HttpClient 单例（代码评审项：不得出现 `new HttpClient()`
   散落在提供商类里）。

## 8. 开放问题

1. **llama.cpp 无限重试改有限次**（§3.5）：本文已裁为 3 次，若用户希望保留旧的
   "死等本地服务"行为，请明示。
2. **连续 401/403 是否熔断**：现状永不禁用引擎，每次交互都会打一次失败请求
   （3 连击）。是否加"同一错误码连续 N 次后本存档会话内静默停用 + 一次性玩家提示"？
   现状照搬也可接受。
3. **流式 usage 的回传通道**（§5）：`ILlmClient.StreamAsync` 返回
   `IAsyncEnumerable<string>` 无处安放 TokenUsage。候选：(a) Host 层事件；
   (b) 01 的 `IStreamSink` 增加 usage 完成回调（需用户批准改共享接口）；
   (c) 流结束后由客户端缓存"最近一次 usage"供拉取。倾向 (a)。
4. **Gemini 流式**：旧实现无 Gemini 流式（streamGenerateContent 端点存在）。
   合并版是否补？默认不补，保持行为面等价。
5. **User-Agent 与缓存键前缀改名**（`ValleyTalk/1.0` → `LivingNPCs/…`、
   `valleytalk-` → `livingnpcs-`）：功能无影响，默认改名，若需保留旧值请明示。
6. `Provider` 配置默认值 `"Mistral"` + `ServerAddress` 默认 openrouter 地址是历史
   遗留组合（Mistral 客户端并不读 ServerAddress）。迁移期是否把默认 Provider 改为
   更合理的值由 WP15 与用户定，本包不动语义。

### 裁决（2026-07-06，Yuki + 架构侧，全部落定）

1. llama.cpp 改**有限 3 次重试**（采纳本文裁定）。
2. **加熔断**（Yuki 裁决）：同一 provider 连续同类致命错误触发挂起——
   **401/403 连续 2 次**：挂起引擎直至配置变更（GMCM 保存或 config 重载）才复位，
   密钥不会自愈，不做定时恢复；**429 连续 5 次**：冷却 **10 分钟**（现实时间）后
   自动恢复。挂起期间生成入口直接回退原版台词；触发时一次性 HUD 提示 + SMAPI
   error 日志（i18n 键归 WP15：`dialogue.breaker.auth`、`dialogue.breaker.rate`）。
   任何一次成功响应清零计数；连接自检不计入熔断统计。
3. 流式 usage：01 §2 已裁决为 `LlmStreamEvent` 事件流，§5 已同步。
4. Gemini 流式不补，保持行为面等价。
5. User-Agent 与缓存键前缀**改名**（`LivingNPCs/<版本>`、`livingnpcs-` 前缀）。
6. 新装默认 `Provider = "OpenAiCompatible"`（与默认 openrouter ServerAddress 组合
   自洽）；迁移用户按 WP14 导入旧值不受影响。WP15 落表。

## 9. 审计索引（行为点 → 旧代码 file:line）

洁净室注意：本节仅供**说明书撰写方与审计方**核对，实现方不得访问这些路径。

| 行为点 | 旧代码位置 |
|---|---|
| 提供商注册表（字符串→类型，忽略大小写；Dummy 仅 DEBUG） | ValleyTalk/src/ModEntry.cs:17-37 |
| 配置字段与默认值 | ValleyTalk/src/config/ModConfig.cs:14-22,57 |
| 切换实例 + 清除禁用标志 + 自检后台化/文案主线程预解析 | ValleyTalk/src/llms/Llm.cs:16-62 |
| 自检文案 i18n 键与英文兜底 | ValleyTalk/src/llms/Llm.cs:64-100 |
| 自检诊断分级、陈旧检查防护、≥5 字符成功判定 | ValleyTalk/src/llms/Llm.cs:102-176 |
| 反射构造（新实现改显式工厂） | ValleyTalk/src/llms/Llm.cs:178-193 |
| 统一推理签名（四段/2048/allowRetry/disableThinking） | ValleyTalk/src/llms/Llm.cs:206 |
| 概率归并算法（前缀树递归聚合） | ValleyTalk/src/llms/Llm.cs:220-267 |
| OpenAI 家族端点拼接 `/v1/chat/completions` | ValleyTalk/src/llms/LlmOpenAiBase.cs:144,280 |
| prompt_cache_key 生成（SHA-256 前 8 字节，`valleytalk-` 前缀） | ValleyTalk/src/llms/LlmOpenAiBase.cs:38-51 |
| 请求体候选序列（思考版→auto 版 × instructions 回退） | ValleyTalk/src/llms/LlmOpenAiBase.cs:53-130 |
| 思考 off → response_format json_object | ValleyTalk/src/llms/LlmOpenAiBase.cs:112-115 |
| 非流式解析 + SSE 误回容错 | ValleyTalk/src/llms/LlmOpenAiBase.cs:154-241,449-551 |
| 流式：stream_options.include_usage、SSE 分帧、[DONE]、delta.content | ValleyTalk/src/llms/LlmOpenAiBase.cs:244-343 |
| 流式 usage 收尾块提取 + 估算兜底 + 降级链 | ValleyTalk/src/llms/LlmOpenAiBase.cs:326-416,418-447 |
| 模型列表 GET /v1/models → data[].id | ValleyTalk/src/llms/LlmOpenAiBase.cs:558-594 |
| OpenAI 官方两开关 | ValleyTalk/src/llms/LlmOpenAI.cs:18-21 |
| DeepSeek 仅开流式 usage（缓存全自动） | ValleyTalk/src/llms/LlmDeepseek.cs:19-21 |
| 兼容端点地址规整 + instructions 回退开关 | ValleyTalk/src/llms/LlmOAICompatible.cs:8-23 |
| Claude 端点/headers（x-api-key、anthropic-version: 2023-06-01，无 beta 头） | ValleyTalk/src/llms/LlmClaude.cs:30,105-112 |
| Claude 缓存断点放置（system 两块 + user 拆块）与 4096 最小前缀注释 | ValleyTalk/src/llms/LlmClaude.cs:43-87 |
| Claude 默认模型改 haiku-4-5（3-5-haiku 退役） | ValleyTalk/src/llms/LlmClaude.cs:33-34 |
| Claude usage 修缮（input+cache_creation+cache_read） | ValleyTalk/src/Generation/TokenUsage.cs:71-95 |
| Claude 模型列表 | ValleyTalk/src/llms/LlmClaude.cs:164-202 |
| Gemini 端点（key 在 URL）与默认模型 | ValleyTalk/src/llms/LlmGemini.cs:28-34 |
| Gemini safetySettings/generationConfig（temperature 1.5, topP 0.9） | ValleyTalk/src/llms/LlmGemini.cs:238-278 |
| Gemini finishReason==STOP 门槛、空文本 200 | ValleyTalk/src/llms/LlmGemini.cs:133-174 |
| Gemini thinkingConfig 候选序列 | ValleyTalk/src/llms/LlmGemini.cs:208-236 |
| Gemini usage 口径（cachedContentTokenCount 含于 prompt） | ValleyTalk/src/Generation/TokenUsage.cs:97-120 |
| Gemini 模型列表（剥 models/ 前缀） | ValleyTalk/src/llms/LlmGemini.cs:42-76 |
| VolcEngine 端点（无 /v1）与默认模型 | ValleyTalk/src/llms/LlmVolcEngine.cs:28,110,186 |
| VolcEngine thinking:disabled 白/黑名单 | ValleyTalk/src/llms/LlmVolcEngine.cs:47-71,73-101 |
| llama.cpp 请求体（cache_prompt、采样参数、n_predict==1 → temp 0） | ValleyTalk/src/llms/LlmLlamaCpp.cs:36-53 |
| llama.cpp 模板占位符替换 | ValleyTalk/src/llms/LlmLlamaCpp.cs:28-34 |
| llama.cpp 无限重试（新实现修正点） | ValleyTalk/src/llms/LlmLlamaCpp.cs:96-102,171-177 |
| llama.cpp 概率查询（n_probs 10 / completion_probabilities） | ValleyTalk/src/llms/LlmLlamaCpp.cs:109-180 |
| Dummy 两种随机固定输出 | ValleyTalk/src/llms/LlmDummy.cs:19-29 |
| 共享 HttpClient（无限 Timeout + 请求级链接 CTS） | ValleyTalk/src/Platform/NetworkHelper.cs:18-39,99-104 |
| 异常翻译（取消/超时/HTTP 附响应体） | ValleyTalk/src/Platform/NetworkHelper.cs:106-122 |
| 流式发送（ResponseHeadersRead） | ValleyTalk/src/Platform/NetworkHelper.cs:134-137 |
| Android UA header | ValleyTalk/src/Platform/NetworkHelper.cs:29-36 |
| Android 推理入口断网即抛 | ValleyTalk/src/llms/LlmClaude.cs:93-97（各提供商同型） |
| Android 预检（5×1s 重试） | ValleyTalk/src/Platform/NetworkAvailabilityChecker.cs:15-46 |
| Android 平台判定 | ValleyTalk/src/Platform/AndroidHelper.cs:15 |
| 能力标志消费点（ExtraInstructions 进指令区） | ValleyTalk/src/Prompts.cs:1502-1505 |
| 流式类型探测（新世界消失） | ValleyTalk/src/Character.cs:626-628 |
| PromptFormatter/CacheContexts 死代码认定 | ValleyTalk/src/llms/PromptFormatter.cs、LlmClaude.cs:37、LlmGemini.cs:36,80-86 |
| 失败响应默认 500 + 状态码提取 | ValleyTalk/src/llms/LlmOpenAiBase.cs:210-228 |
| 响应对象形态 | ValleyTalk/src/Generation/LlmResponse.cs |

## 10. 实现记录（2026-07-07，WP11）

### 落位

`LivingNPCs/Dialogue/Llm/` 新增 24 个文件；测试在 `LivingNPCs.Tests/Dialogue/Llm/`（6 个文件、95 项）。

- **跨包契约**（namespace `LivingNPCs.Dialogue`）：`ILlmClient`、`LlmStreamEvent`(+Kind)、`LlmRequest`、`LlmReply`，与 01 §2 逐字一致。可见性用 `internal`（搬运件 `TokenUsage` 是 internal，公开契约会连坐编译失败；测试经既有 `InternalsVisibleTo` 访问）。
- **能力标志**：01 钉死了 `ILlmClient` 成员表，故采纳本文 §6 备选中的"能力接口"方案：`ILlmCapabilities`（`IsHighlySensoredModel`/`ExtraInstructions`），基类实现、熔断装饰器透传。
- **网络层**：`LlmHttp`（进程级单例、无限 Timeout、请求级链接 CTS、异常翻译、流式 ResponseHeadersRead、测试可注入 Handler）；`AndroidPlatform`（含假平台开关）；`NetworkAvailability`（推理入口即抛 + 钩子侧 5×1s 预检 + 同步包装）。
- **提供商**：`LlmClientBase`（候选序列×重试预算、状态码提取、默认流包装、思考回退一次性警告）→ `OpenAiChatClientBase`（缓存键、SSE 流式+三级降级链、instructions 回退、模型列表）→ OpenAI/DeepSeek/Mistral/OpenAiCompatible；another 分支 Claude/Gemini/VolcEngine/LlamaCpp/Dummy。概率归并算法独立为 `TokenProbabilityMath`，能力接口 `ITokenProbabilities` 仅 LlamaCpp 实现。
- **编排**：`LlmClientFactory`（大小写不敏感注册表 + 显式 switch 委托 + `LlmProviderMetadata` 供 WP15 驱动 GMCM 显隐/下拉）；`LlmCircuitBreaker` + `BreakerGuardedLlmClient`；`LlmClientHost`（切换语义、引擎禁用标志、CanGenerate）；`ConnectionSelfCheck`；`LlmHudNotifier`（并发队列 + 主线程 UpdateTicked 泵）。
- **过渡**：`IStreamingLlm.cs` 按 §2/§5 删除（03 §2 授权本包裁决，全仓无引用）；`LegacyLlmStubs.cs` 新增 `LegacyLlmBridge`——`Host.ReplaceClient` 时把 `LegacyLlm.Instance` 指向当前客户端（带熔断防护），礼物邮件/记忆印象/语义路由/行动判定四个搬运件调用点在 WP10 重写前即恢复可用；`cacheContext` 参数按 §2 死代码认定忽略。过渡 `DialogueConfig` 补齐 `ApiKey`/`ServerAddress`/`PromptFormat`/`SuppressConnectionCheck` 四字段并把 `QueryTimeout` 默认对齐 §4.5 的 85（最终落表归 WP15）。

### 裁决落实

1. llama.cpp 有限 3 次重试（间隔 1s），测试断言恰好 3 发后放弃。
2. 熔断：401/403 连续 2 次挂起至配置变更（`ReplaceClient` 复位，不做定时恢复）；429 连续 5 次冷却 10 分钟（可注入时钟）自动恢复；任何成功清零；自检走原始客户端、天然不计入；触发时一次性 HUD + SMAPI error（i18n 键 `dialogue.breaker.auth`/`dialogue.breaker.rate`，英文兜底已内置）。挂起期间非流式即回失败响应、流式即抛异常，均零网络。
3. 流式 usage 走 `LlmStreamEvent` 事件流（TextDelta… → Usage → Done），Source 区分 `provider usage`/`stream estimate`/`stream fallback estimate`。
4. Gemini 不补流式（经基类默认包装走非流式）。
5. UA 改 `LivingNPCs/<程序集版本> (Android; Stardew Valley Mod)`；缓存键前缀改 `livingnpcs-`（哈希输入三段与 U+0001 分隔符不变）。
6. `LlmConnectionSettings` 默认 `Provider="OpenAiCompatible"`（WP15 落表时沿用）。

### 实现判断点（规格留白处的裁定，供审计）

- **json_object/responseMimeType 门控**：§3.1/§3.3 字面为"档位 off 时"，实现收紧为 `DisableThinking && IsOff(档位)` 两条件同时成立——与 §3.4 VolcEngine"仅 disableThinking 路径"的语义对齐；否则用户把聊天档位设 Off 会把正常对话请求错标为 JSON 输出。测试覆盖两侧。
- **流式失败语义**：事件流没有错误事件位（01 钉死三种 Kind），终态失败以 `LlmStreamException`（含 HttpStatus）抛出，取消异常原样传播；WP10 §4.9 本就把异常归为重试触发条件，语义自洽。
- **Claude 空块防护**：系统段/稳定段/尾段为空时不发对应 text 块（Anthropic 拒绝空文本块；连接自检的稳定段就是空）。NPC 段空 → user content 退化为纯字符串（规格明示）。
- **自检诊断细化**：ApiKey 为空的提示按提供商元数据 `RequiresApiKey` 门控（LlamaCpp 无 key 不误报"配 key"）；"地址不含 https"提示对能列模型与不能列模型两分支都追加（llama.cpp 走 ③ 也能收到）。文案九键全部主线程预解析后交后台，键缺失用英文兜底。
- **熔断"连续"的解释**：非致命错误（超时、5xx、解析失败）与另一类致命错误都会打断连击并清零对应计数；玩家/系统取消不计入任何统计；计数按"回复"（一次调用的最终结果）而非内部尝试。
- **概率归并语义**：忽略大小写与首部空白；累计文本一旦覆盖某选项（含 token 超出选项尾部，如 "Yes."）把路径概率计入该桶；仍是选项前缀则下钻下一位置；对不上任何选项即剪枝；纯空白前缀（引导空格 token）无信息、继续下钻。
- **一次性调用超时**：`LlmRequest` 增加 internal `TimeoutOverride`（自检与模型列表显式 1 分钟），未动 01 契约面；常规请求经 `LlmHttp.DefaultTimeout` 实时读配置，改 `QueryTimeout` 下一请求即生效（有测试）。
- **流式超时切分**：请求头阶段受统一超时约束，响应体读取只受调用方令牌限界（net6 `ReadLineAsync` 无令牌重载，用"取消即掐断响应流 + 异常翻译回取消"的方式实现）。
- **HUD 线程安全**：熔断触发可能在后台线程，`Game1.addHUDMessage` 非线程安全——消息进并发队列，由 `ReplaceClient`（主线程）挂接的 UpdateTicked 泵取出后再上屏；无 SMAPI 事件环境（单元测试）自动降级为仅日志。WP12 无需额外接线。
- **llama.cpp "会话级性能统计"**：timings 折算的 usage 随响应交给调用方、经 WP10 上报 WP14 的 `TokenUsageTracker`，不另建统计子系统。

### 供下游工作包对接

- **WP10**：`LlmClientHost.Instance.Current`（带熔断）/`CanGenerate`/`Suspension`；`ILlmCapabilities` 类型探测取两个能力标志；流式按"异常=本次尝试失败"处理。
- **WP12**：生成前调 `NetworkAvailability.EnsureAvailableForGenerationAsync()`（或同步包装）；取消令牌传入 Complete/Stream。
- **WP15**：`LlmClientFactory.Providers`（有序元数据：RequiresApiKey/RequiresModelName/RequiresServerAddress/RequiresPromptFormat/SupportsModelList/SupportsThinkingLevels）驱动 GMCM 动态字段与下拉；模型列表对**原始客户端**做 `IModelNameSource` 类型探测（临时建客户端即为原始实例；不要探测熔断装饰器）；GMCM 保存调 `LlmClientHost.Instance.ReplaceClient(LlmConnectionSettings.FromConfig(...))`（主线程）；i18n 需落表 `modelCheck*` 九键 + `dialogue.breaker.auth`/`dialogue.breaker.rate`（英文兜底文案见 `ConnectionSelfCheck`/`LlmClientHost`）。
- **WP14**：每次响应的 `LlmReply.Usage`/Usage 事件即搬运件 `TokenUsage`，Source 口径已按 §4 全部落实。

### 验证

- `dotnet test LivingNPCs.Tests`：**通过 357，失败 0，跳过 1**（唯一 skip 为阶段 A 遗留的 WP15-TODO）；本包新增 95 项，覆盖 §7 全部九条验收要点（各家解析样本、Claude 请求体快照与三组 usage、缓存键同异与家族开关、流式降级链顺序与 usage 口径、超时配置即时生效、自检抑制/陈旧防护/永不禁用、llama.cpp 恰好 3 次、Android 假开关两挂载点、熔断全语义、概率归并、注册表大小写/非法值、地址规整、候选序列次序）。
- 主工程 Debug 与 Release（`-p:EnableModDeploy=false`）双配置 0 错误；新增代码 0 警告（残余警告均为阶段 A 搬运件既有 nullable 警告）。
- 验收 9 复查：`new HttpClient(` 全目录仅 `LlmHttp` 单例一处。

### 洁净室声明

本包实现全程未打开 `ValleyTalk/`、`ValleyTalk.Tests/`、`upstream-ValleyTalk/` 下任何文件（rewrite worktree 中前两者已物理删除），未以任何方式检索上游源码；仅阅读 RewriteSpec、`LivingNPCs/`、`LivingNPCs.Tests/`、阶段 A 已落位的搬运件。
