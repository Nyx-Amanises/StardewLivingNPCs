# Contributing to LivingNPCs

Bug fixes, tests, documentation, translations, lightweight `npc_profiles`, and full `npc_bios` contributions are welcome.

## Full NPC biographies

Read [`LivingNPCs/npc_bios/README.md`](./LivingNPCs/npc_bios/README.md) before submitting a biography. Start from `_template.json`, use the normalized NPC internal name, and place files under `npc_bios/<SourceUniqueID>/bios/` or localized complete files under `npc_bios/<SourceUniqueID>/locales/<locale>/`.

Before opening a pull request:

1. Keep every template field with exact casing, use strict JSON (no comments, trailing commas, duplicate properties, or metadata fields), then run `python tools/validate_community_bios.py --repository LivingNPCs/npc_bios`.
2. Test the NPC in game with a clean SMAPI log, including a first conversation and a later conversation.
3. State the source mod, URL, UniqueID, tested version, language, spoiler level, and factual basis.
4. Do not copy dialogue, event text, wiki prose, or mod-page prose. If any submitted text is derived from licensed source material, link the exact permission or license.
5. `UsePatchedDialogue` must be false and `PromptOverrides` must be empty in `npc_bios`; the runtime and CI reject them. Keep `Dialogue` empty unless the pull request explains why samples are needed and includes redistribution/AI-use permission.
6. Confirm that you own the submitted writing or are authorized to submit it, and grant the LivingNPCs project permission to include and redistribute it with the mod.

Open the GitHub compare page with the dedicated checklist using [the NPC biography pull-request template](https://github.com/Nyx-Amanises/StardewLivingNPCs/compare?expand=1&template=npc-biography.md). If GitHub preserves an existing query string, append `&template=npc-biography.md` manually.

`PermitAiUse: true` is only a runtime AI-use signal. It is not a copyright license and does not grant redistribution rights.

请中文贡献者直接阅读完整的[中文传记指南](./LivingNPCs/npc_bios/README.md)与 NPC biography PR 模板；其中列出了内部名、剧透、授权、实机测试和高风险字段的完整清单。
