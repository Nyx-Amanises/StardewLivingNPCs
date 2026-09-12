# 模型思考档位与后台输出预算

官方参数核对日期：2026-09-12。

记忆印象压缩和 AI 礼物信各使用 **10,000 token** 的总输出上限，包含模型的推理和最终正文。提示词仍要求简短文字；上限不要求模型写满。已有记忆失败回队列、礼物信失败用模板的恢复流程保持不变。

## 配置和请求

设置菜单中的“后台任务思考档位”沿用 `RoutingThinkingLevel` 配置键，适用于语义路由、动作判定、元数据提取、记忆印象和礼物信；聊天继续使用 `ChatThinkingLevel`。

两个下拉框固定提供 Auto、Off、Minimal、Low、Medium、High、XHigh、Max、Ultra。切换提供商或模型名、保存和重新打开菜单都会保留所选档位，模型名为空、暂时拼错或使用自定义别名也不会把选项缩为 Auto。菜单仅规范大小写和首尾空白，模型支持范围的转换发生在请求发送时，不回写配置。

GMCM 的选项在注册时固定，编辑值在保存时逐项提交，因此不能根据正在编辑的模型名过滤下拉框。仅更换模型名不再重建菜单；更换提供商仍会刷新对应的连接字段。旧版已经存成 Auto 的档位无法推断原选择，需要在修复版中重新选择并保存。

`LlmRequest.OutputFormat` 单独表示纯文本或 JSON。邮件和记忆使用纯文本；三个分类/提取任务明确请求 JSON。选择 Off 不再把纯文本任务自动变成 JSON。请求重试克隆保留输出格式和 token 上限。

## 模型能力

Auto 表示省略思考控制参数、使用服务端默认值。下表列出实际请求可用档位并省略 Auto；Off 表示模型可以关闭思考。菜单选项不随此表过滤。

| 模型 | 请求可用档位 |
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

- 接受并保留 Max 和 Ultra；已核实的公开 API 没有统一的原生 Ultra 档，请求中的 Ultra 转换为该模型最高合法档位，配置仍保存 Ultra。
- DeepSeek 的 Minimal → Low；Medium / XHigh → High；Ultra → Max，遵循官方兼容表。旧 R1 / reasoner 的第三方托管兼容行为不套用 V4 新能力。
- GPT-6 的 Off / Minimal → Low；GPT-5.6 的 Minimal → Low；GPT-5.5 的 Max / Ultra → XHigh。GPT-5、GPT-5.1 和 Codex 型号按各自的旧能力限制处理。
- Gemini 3 不接受完全关闭思考，旧 Off 归一到模型最低档；高于 High 的值归一到 High。Gemini 2.5 保留已有的 `thinkingBudget` 协议和数值映射，不发送 3 系的 `thinkingLevel`。
- 新 Claude 使用 `thinking.type=adaptive` 和 `output_config.effort`；旧模型使用合法的 `budget_tokens`，始终小于请求总上限。读取回复时跳过 thinking 块、收集 text 块。
- 未识别的兼容端点模型不推断额外能力，请求省略思考控制参数；菜单继续显示完整选项并保留选择，之后换回已识别模型即可使用该档位。模型名会按原值发送，不能依靠档位适配修正拼写。具体模型名称的支持、账号可用性和端点类型仍由提供商决定。

## 官方依据

- [OpenAI GPT-6 Astra](https://developers.openai.com/api/docs/models/gpt-6-astra)
- [OpenAI GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol) / [Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)
- [OpenAI GPT-5.5](https://developers.openai.com/api/docs/models/gpt-5.5) / [GPT-5.1](https://developers.openai.com/api/docs/models/gpt-5.1) / [GPT-5.3 Codex](https://developers.openai.com/api/docs/models/gpt-5.3-codex)
- [DeepSeek Thinking Mode 和兼容映射](https://api-docs.deepseek.com/guides/thinking_mode)
- [DeepSeek 当前模型与别名](https://api-docs.deepseek.com/quick_start/pricing)
- [Claude effort](https://platform.claude.com/docs/en/build-with-claude/effort) / [thinking 版本限制](https://platform.claude.com/docs/en/build-with-claude/thinking-troubleshooting)
- [Gemini thinking](https://ai.google.dev/gemini-api/docs/thinking) / [原生 ThinkingConfig](https://ai.google.dev/api/generate-content#ThinkingConfig)
