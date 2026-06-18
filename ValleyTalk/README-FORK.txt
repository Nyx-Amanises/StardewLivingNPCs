ValleyTalk - modified build (bundled with LivingNPCs)
=====================================================

This is a MODIFIED build of ValleyTalk, originally created by dandm1.

  Original project : https://github.com/dandm1/ValleyTalk  (Nexus mod 30319)
  Modified source  : https://github.com/Nyx-Amanises/StardewLivingNPCs
  License          : LGPL v2.1 (see LICENSE.txt). This modified version is
                     redistributed under the same license.

Why it's bundled
----------------
LivingNPCs needs THIS build: it carries a bridge that the stock ValleyTalk
does not have. Do NOT replace this folder with the stock ValleyTalk, or
LivingNPCs loses memory, emotions, help requests and AI actions. If you
already have ValleyTalk installed, replace it with this one.

Changes vs stock ValleyTalk
---------------------------
- Streaming replies with free-text input, incl. during festivals/events.
- Bridge to LivingNPCs: emits structured hidden data (memory, emotions,
  favors, actions) and injects LivingNPCs continuity context into the prompt.
- AI dialogue for vanilla, SVE, and permitted custom NPC/content-pack contexts.
- Ridgeside Village NPCs are excluded from AI dialogue, LivingNPCs behavior,
  memories, world actions, and prompt bridging.
- Per-save / per-NPC conversation logs and token-usage tracking + export.
- Prompt-size and caching tweaks (optimized prompt toggles; NPC-bio prompt
  caching on the Claude path).

Requirements: SMAPI, Content Patcher. Set your LLM provider / API key in
this mod's Generic Mod Config Menu page.
