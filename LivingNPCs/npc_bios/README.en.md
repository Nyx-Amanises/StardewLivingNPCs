# Full NPC dialogue biographies

`npc_bios/` is the complete `NpcBio` contribution and drop-in directory. It complements, rather than replaces, `npc_profiles/`: profiles add lightweight behavior/personality hints, while biographies directly supply dialogue background, relationships, traits, topics, samples, and portrait semantics.

Neither format creates an NPC. The character must already be added by the game or another mod.

## Paths and priority

Repository/community biographies are namespaced by the source mod's exact manifest UniqueID:

```text
Mods/LivingNPCs/npc_bios/<SourceUniqueID>/bios/<InternalName>.json
Mods/LivingNPCs/npc_bios/<SourceUniqueID>/locales/<locale>/<InternalName>.json
```

The namespace is active only when SMAPI has loaded that source UniqueID. If two active sources provide valid, successfully parsed biographies for the same internal name, LivingNPCs reports a conflict and uses neither. An invalid-only source is warned about but does not block the one remaining valid biography. This prevents a biography from attaching to an unrelated same-name NPC when its real source mod is absent.

Players and curated modpacks may use unscoped local overrides when the target is intentional:

```text
Mods/LivingNPCs/npc_bios/<InternalName>.json
Mods/LivingNPCs/npc_bios/<locale>/<InternalName>.json
```

Unscoped files have the highest provider priority and do not check a source mod. Repository pull requests should use the UniqueID namespace.

The physical mod folder may be renamed. `Mods/Yuki.LivingNPCs/Bios/<InternalName>` is the SMAPI virtual asset name, not a required Windows directory. LivingNPCs maps the files above to that asset.

For `zh-CN`, candidates are checked in this order:

```text
unscoped local override: zh-CN → zh → default
active source namespace: locales/zh-CN → locales/zh → bios default
built-in SVE/vanilla biography
Data/Characters fallback
```

An invalid localized file is warned about and skipped. Content Patcher can still edit the final virtual asset after this provider loads it.

## Authoring

Copy [`_template.json`](./_template.json) to `<SourceUniqueID>/bios/<InternalName>.json` (or `<SourceUniqueID>/locales/<locale>/<InternalName>.json`), keep every top-level field, and replace the placeholders. Locale directories use canonical casing such as `zh-CN`, `pt-BR`, or `zh-Hans`. Field names and casing must exactly match the template; files use strict JSON with no comments, trailing commas, duplicate properties, or extra `$type`/`$id` metadata. From a source checkout, run:

```powershell
python tools/validate_community_bios.py --repository LivingNPCs/npc_bios
```

The installed mod also ships the validator; from its folder, run:

```powershell
python npc_bios/validate_community_bios.py npc_bios
```

`--repository` rejects unscoped local overrides for pull requests and CI. The installed-mod command intentionally omits it, so intentional local overrides produce a warning instead of an error. The validator requires Python 3, but the game and mod do not; Python is only an authoring-time check.

Restart the game after changing disk files; this directory is not watched for hot reload.

Files manually placed inside the main mod folder may be overwritten by an update or reinstall, so keep a backup. Use a separate Content Patcher pack for long-term independent distribution.

Biography files are not rescanned every frame or conversation round. LivingNPCs scans the active source directories once per process; the first request for an NPC checks its locale candidates and reads only files that actually exist, then SMAPI and LivingNPCs caches reuse the result. Local CPU, memory, and disk impact is negligible at normal sizes. Very long biography, dialogue, or prompt-override text can still increase model tokens, API cost, and response latency, so both runtime and CI enforce size limits.

Important rules:

- `Biography` must be non-empty.
- Order `Traits` by importance; compact contexts only use the first four, and some gift text uses the first three.
- Keep `Dialogue` empty unless you own or have explicit permission to redistribute the samples. Never copy event or dialogue text from the source mod.
- `UsePatchedDialogue` must be false in `npc_bios`; the runtime rejects true because it bypasses unlicensed-dialogue protection. Use an explicitly authorized Content Patcher pack for that advanced case.
- `PromptOverrides` must be empty in `npc_bios`; the runtime rejects it because community data may not replace trusted prompt sections. Publish reviewed advanced overrides through Content Patcher.
- `ExtraPortraits` only accepts `u` or decimal frames 6–4095 that match the final portrait sheet.
- `PermitAiUse: true` is an AI-runtime permission flag, not a copyright or redistribution license.
- Contributors must own the submitted text and grant the LivingNPCs project permission to include and redistribute it with the mod.

The final virtual asset is keyed only by NPC internal name. If two installed NPC mods use the same name, the scoped loader fails closed; use conditional Content Patcher compatibility data to select one source when both must be supported.

LivingNPCs normalizes a few compatibility aliases before requesting the asset: `GuntherSilvian → Gunther`, `MarlonFay → Marlon`, `MorrisTod → Morris`, and `HankSVE → Hank`. It also removes trailing `·`, `•`, or `-` clone suffixes. Use the normalized key for the filename and virtual target.

See the Chinese guide above for the full field reference and [open the dedicated NPC biography pull-request template](https://github.com/Nyx-Amanises/StardewLivingNPCs/compare?expand=1&template=npc-biography.md) for the submission checklist. Ridgeside Village remains blocked by the current AI compatibility policy; a biography does not bypass it.

## Independent Content Patcher packs

A complete minimal `manifest.json` can use `ContentPackFor: Pathoschild.ContentPatcher`, require both `Yuki.LivingNPCs` and the source NPC mod, and set top-level `PermitAiUse: true` only when the pack author has the right to authorize the pack's own text. If one pack supports optional source mods, put the matching `HasMod` condition on each `EditData` change instead. This prevents a same-name NPC from receiving the biography when its real source is absent. The `content.json` should use `Format: 2.3.0`, a `Changes` array, and an `EditData` change targeting `Mods/Yuki.LivingNPCs/Bios/<InternalName>` with the complete fields shown in `_template.json`.

Content Patcher edits the final object after direct `npc_bios` validation. It therefore bypasses the 64 KiB and field-length limits as well as the direct-folder bans on `UsePatchedDialogue=true` and non-empty `PromptOverrides`. Treat a CP pack as a trusted code-like extension requiring its own security and copyright review. `PermitAiUse` does not validate, block, or license `Biography`, biography `Dialogue`, or `PromptOverrides`; biography `Dialogue` is merged directly, and prompt overrides can replace trusted sections.
