# 模型思考档位与后台输出预算

官方参数核对日期：2026-09-12。

记忆印象压缩和 AI 礼物信各使用 **10,000 token** 的总输出上限，包含模型的推理和最终正文。提示词仍要求简短文字；上限不要求模型写满。已有记忆失败回队列、礼物信失败用模板的恢复流程保持不变。

## 配置和请求

设置菜单中的“后台任务思考档位”沿用 `RoutingThinkingLevel` 配置键，适用于语义路由、动作判定、元数据提取、记忆印象和礼物信；聊天继续使用 `ChatThinkingLevel`。保存模型变更后菜单会更新可用档位。

`LlmRequest.OutputFormat` 单独表示纯文本或 JSON。邮件和记忆使用纯文本；三个分类/提取任务明确请求 JSON。选择 Off 不再把纯文本任务自动变成 JSON。请求重试克隆保留输出格式和 token 上限。

## 模型能力

Auto 表示省略思考控制参数、使用服务端默认值。下表省略 Auto；Off 只在模型可以关闭思考时显示。

| 模型 | 菜单可用档位 |
| --- | --- |
| GPT-6 Astra | Low、Medium、High、XHigh、Max |
| GPT-5.6 / Sol / Terra / Luna | Off、Low、Medium、High、XHigh、Max |
| GPT-5.5 / 5.4 / 5.2 | Off、Low、Medium、High、XHigh |
| DeepSeek Flash / V4 Flash / V4 Pro | Off、Low、High、Max |
| Claude Fable / Mythos 5、5.1 | Low、Medium、High、XHigh、Max |
| Claude Opus / Sonnet 5、Opus 4.7 / 4.8 | Off、Low、Medium、High、XHigh、Max |
| Claude Opus / Sonnet 4.6 | Off、Low、Medium、High、Max |
| Gemini 3.8 / 3.7 Flash、3.1 Pro | Low、Medium、High |
| Gemini 3.6 / 3.5 Flash、3 Flash、3.1 Flash-Lite | Minimal、Low、Medium、High |
| Gemini 3 Pro | Low、High |
| Gemini 3.1 Flash-Lite-Image | Minimal、High |

旧配置兼容：

- 接受 Max 和 Ultra；已核实的公开 API 没有统一的原生 Ultra 档，因此菜单只显示原生选项，保存的 Ultra 归一为该模型最高合法档位。
- DeepSeek 的 Minimal → Low；Medium / XHigh → High；Ultra → Max，遵循官方兼容表。旧 R1 / reasoner 的第三方托管兼容行为不套用 V4 新能力。
- GPT-6 的 Off / Minimal → Low；GPT-5.6 的 Minimal → Low；GPT-5.5 的 Max / Ultra → XHigh。GPT-5、GPT-5.1 和 Codex 型号按各自的旧能力限制处理。
- Gemini 3 不接受完全关闭思考，旧 Off 归一到模型最低档；高于 High 的值归一到 High。Gemini 2.5 保留已有的 `thinkingBudget` 协议和数值映射，不发送 3 系的 `thinkingLevel`。
- 新 Claude 使用 `thinking.type=adaptive` 和 `output_config.effort`；旧模型使用合法的 `budget_tokens`，始终小于请求总上限。读取回复时跳过 thinking 块、收集 text 块。
- 未识别的模型不推断额外能力，菜单显示 Auto。具体模型名称的支持、账号可用性和端点类型仍由提供商决定。

## 官方依据

- [OpenAI GPT-6 Astra](https://developers.openai.com/api/docs/models/gpt-6-astra)
- [OpenAI GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol) / [Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)
- [OpenAI GPT-5.5](https://developers.openai.com/api/docs/models/gpt-5.5) / [GPT-5.1](https://developers.openai.com/api/docs/models/gpt-5.1) / [GPT-5.3 Codex](https://developers.openai.com/api/docs/models/gpt-5.3-codex)
- [DeepSeek Thinking Mode 和兼容映射](https://api-docs.deepseek.com/guides/thinking_mode)
- [DeepSeek 当前模型与别名](https://api-docs.deepseek.com/quick_start/pricing)
- [Claude effort](https://platform.claude.com/docs/en/build-with-claude/effort) / [thinking 版本限制](https://platform.claude.com/docs/en/build-with-claude/thinking-troubleshooting)
- [Gemini thinking](https://ai.google.dev/gemini-api/docs/thinking) / [原生 ThinkingConfig](https://ai.google.dev/api/generate-content#ThinkingConfig)
