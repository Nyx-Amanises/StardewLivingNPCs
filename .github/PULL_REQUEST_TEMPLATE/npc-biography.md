## NPC biography / NPC 完整传记

- Source mod / 来源 Mod：
- Source URL / 链接：
- Source UniqueID：
- Tested source version / 测试版本：
- NPC internal name / 内部名：
- NPC display name / 显示名：
- Language / 语言：
- Spoilers / 剧透等级（无 / 轻微 / 重大）：

## Basis and permissions / 依据与授权

- Stable facts used / 使用的稳定事实：
- Original clean-room summary or licensed source / 原创概括或许可来源：
- Source author's AI-use permission, if relevant / 原作者 AI 使用许可：

- [ ] I wrote these summaries in my own words and did not copy dialogue, event text, wiki prose, or mod-page prose.
- [ ] I own this contribution or am authorized to submit it, and I grant LivingNPCs permission to include and redistribute it with the mod.
- [ ] I understand that `PermitAiUse: true` is not a copyright or redistribution license.

## High-risk fields / 高风险字段

- Is `Dialogue` non-empty? If yes, explain and link permission:
- [ ] `UsePatchedDialogue` is `false` (required for `npc_bios`).
- [ ] `PromptOverrides` is empty (required for `npc_bios`).
- Is `ExtraPortraits` non-empty? If yes, list the verified final frame indices:

## Verification / 验证

- [ ] `python tools/validate_community_bios.py --repository LivingNPCs/npc_bios` passes.
- [ ] The JSON keeps every template field with exact casing and contains no comments, trailing commas, duplicate properties, or metadata fields.
- [ ] The filename and virtual target use LivingNPCs' normalized NPC key (normally the exact internal name; documented SVE aliases are the exception).
- [ ] The file is under `npc_bios/<SourceUniqueID>/bios/` or `npc_bios/<SourceUniqueID>/locales/<locale>/`.
- [ ] Tested a first conversation and a later conversation in game.
- [ ] Tested the submitted language and its fallback behavior.
- [ ] Tested low and high friendship where practical.
- [ ] SMAPI shows no biography load or validation warning.
- [ ] This PR contains one source mod or one tightly related biography set and no unrelated formatting.

Additional test notes / 补充测试说明：
