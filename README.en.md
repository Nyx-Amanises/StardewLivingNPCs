# LivingNPCs 0.2.0

[中文 README](./README.md)

> Talk to the people of Pelican Town in your own words, and let them genuinely remember, care about, and respond to what you have been through together.

LivingNPCs adds a built-in AI dialogue engine, long-term memory, emotions and relationship pacing, and controlled NPC behaviors to Stardew Valley. It does not just generate a throwaway line: NPCs respond in light of their personality, their relationship with you, game progress, the current place and time, and your past conversations; when it fits, a conversation can also grow into a help request, a return gift, a short companion outing, or a small everyday behavior.

## 0.2.0: the all-in-one rewrite

0.2.0 is a complete rewrite, and it installs differently from 0.1.x:

- The AI dialogue engine was rewritten from scratch and is now built directly into LivingNPCs.
- The release package contains a single `LivingNPCs` mod folder.
- **ValleyTalk and Content Patcher are no longer required.**
- English and Simplified Chinese character profiles and progression awareness for vanilla and Stardew Valley Expanded (SVE) are built in; the separate SVE dialogue content pack is no longer needed.
- Compatible legacy ValleyTalk settings, chat transcripts, NPC dialogue memories, and token ledgers can be migrated automatically.
- AI dialogue, memory, emotions, help requests, gifts, outings, logs, and usage statistics are now managed by one mod.

- Current version: `0.2.0`
- Mod ID: `Yuki.LivingNPCs`
- Nexus: <https://www.nexusmods.com/stardewvalley/mods/47704>
- Source: <https://github.com/Nyx-Amanises/StardewLivingNPCs>

## Requirements

- Stardew Valley 1.6.x
- [SMAPI 4.1.0 or later](https://smapi.io/)
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) (optional, strongly recommended)
- An LLM provider/API, or a compatible local server you run yourself

LivingNPCs **does not bundle a model and does not provide a free API**. Speed, cost, privacy, and dialogue quality depend on the provider and model you choose.

## Fresh installation

1. Fully exit the game and SMAPI.
2. Unzip the 0.2.0 release package.
3. Put the single `LivingNPCs` folder into the game's `Mods` folder.
4. Check that the final path looks like `Stardew Valley/Mods/LivingNPCs/manifest.json`, without an extra nested folder of the same name.
5. Launch the game through SMAPI.
6. In Generic Mod Config Menu, open **LivingNPCs → AI Dialogue Engine** and complete the model connection settings.

If GMCM is not installed, you can also close the game after the first launch and edit `Mods/LivingNPCs/config.json` by hand.

## Upgrading from 0.1.x / migrating an existing save

> **Complete the folder rename below before launching 0.2.0 for the first time**, so the new version can find and import the old configuration and folder data.

Back up your saves and the old `LivingNPCs` / `ValleyTalk` folders first, then:

1. Fully exit the game and SMAPI.
2. In the game's `Mods` folder, rename the **outer** legacy `ValleyTalk` folder to `.ValleyTalk`:

   ~~~text
   Mods/ValleyTalk/  →  Mods/.ValleyTalk/
   ~~~

   If the old package used a nested `ValleyTalk/ValleyTalk` layout, rename the outermost folder that contains all the old components. Do not add a dot to the old `LivingNPCs` folder; it must be replaced by the new version.

3. Extract the new `LivingNPCs` folder into `Mods`, replacing the old folder of the same name. Do not keep two versions loaded from different subfolders.
4. Launch through SMAPI and load every save you want to migrate once. In multiplayer, the host must load the save to migrate save data.
5. If the save contains migratable legacy conversation data, an in-game message appears: *"LivingNPCs: migrated dialogue memories of N NPCs from ValleyTalk."* After seeing it, make one normal in-game save.

Migration tries to preserve NPC dialogue history and related save memories, compatible provider/API key/model/server and conversation settings, the token ledger, local farmhand history, and readable transcripts in `conversation_logs`. It never deletes old files or legacy save keys automatically; the renamed `.ValleyTalk` folder is ignored by SMAPI and can stay as a backup.

Notes:

- **Do not leave the old ValleyTalk active under its original name.** If 0.2.0 detects legacy ValleyTalk running, the built-in dialogue engine and migration stay disabled to avoid two competing dialogue patch sets.
- A save with no old chat history or token data may show no "migrated N NPCs" message; that alone does not mean migration failed.
- If the game reports a partial migration, keep `.ValleyTalk`, check the SMAPI log, and include it in your report.

## First-time AI setup

Recommended flow via GMCM:

1. Open **LivingNPCs → AI Dialogue Engine**.
2. Choose the LLM provider.
3. If you just switched providers, save once and reopen the settings page. Switching providers clears the previous API key and refreshes the provider-specific fields.
4. Fill in the API key, model name, and server address as required by the provider.
5. Save and watch the SMAPI console for the non-blocking connection self-check.
6. Load a save, approach an NPC, and hold `LeftAlt` while clicking or using the interaction button to open the free-text input box.

Supported providers:

| Provider | Typical required fields |
| --- | --- |
| OpenAI | API key, model name |
| OpenAI-compatible | API key, model name, server address (for custom gateways; the base address is enough — `/v1` suffixes are normalized automatically) |
| OpenRouter | API key (endpoint and default model built in; one key reaches many models, including free ones) |
| Zhipu (GLM), Moonshot (Kimi), Alibaba DashScope (Qwen), SiliconFlow | API key (endpoints built in; leave the model name empty for each provider's default — Zhipu's default `glm-4-flash` is free) |
| Ollama, LM Studio (local) | No API key; just run the local server (endpoints built in, set the model name to a locally installed model) |
| Anthropic (Claude) | API key, model name |
| Google (Gemini) | API key, model name |
| DeepSeek | API key, model name |
| Mistral | API key, model name |
| VolcEngine (Doubao) | API key, model name |
| llama.cpp (local) | The full endpoint of a server you run yourself (e.g. `http://127.0.0.1:8080/completion`); the "Prompt format" template must match your model's instruction format (defaults to the Mistral `[INST]` style, adjustable directly in GMCM) |

LivingNPCs performs a non-blocking self-check after saving connection settings. A failed self-check never permanently disables the engine; the next real conversation retries. Common errors:

- `401/403`: invalid API key, missing permission, or the wrong provider selected;
- `429`: requests too fast, exhausted quota, or provider rate limiting;
- model not found: wrong model name, or the key cannot access that model;
- timeouts: check the server address, network, or request timeout setting.

Stronger models are generally more consistent at roleplay, long-term memory, and the hidden structured information the mod relies on. Smaller or cheaper models can work, but drift and unstable action decisions become more likely.

### Free 5-minute setup

Don't want to pay before trying it? All three of these start free (quotas and policies are up to each provider):

1. **Google Gemini (free tier):** create an API key at <https://aistudio.google.com>, pick the "Google (Gemini)" provider, paste the key, and leave the model name empty (defaults to `gemini-2.5-flash`).
2. **OpenRouter (free models):** create a key at <https://openrouter.ai>, pick the "OpenRouter" provider, and set the model name to any model tagged `:free` on their model list.
3. **Local Ollama (fully free, offline, private):** install <https://ollama.com>, run `ollama pull qwen3:8b`, pick the "Ollama (local)" provider, and leave everything else empty. Needs a reasonably strong PC; small local models are noticeably weaker at roleplay and structured output than large cloud models.

Free options are great for a first taste; for long-term play a stronger model gives clearly better dialogue quality, memory stability, and action decisions.

## How to talk to NPCs

- **Free-text AI dialogue:** hold `LeftAlt` (default) and click the NPC or use the interaction button. A "thinking..." box shows while generating (press `Esc` or gamepad B to cancel); the reply then appears in the classic dialogue box with the vanilla letter-by-letter reveal.
- **Normal dialogue:** plain right-click still shows vanilla lines by default.
- **Make plain right-click use AI too:** enable "AI for normal right-click dialogue" in GMCM.
- **Gift and marriage lines:** the AI generation frequency can be set separately for each.
- **Disable AI for specific characters:** list internal names, separated by commas or spaces.

If generation fails, times out, or returns unusable content, the mod falls back safely and never blocks normal save loading just to show an AI line.

## Main gameplay

- **Long-term memory.** NPCs can keep and recall facts, preferences, dislikes, nicknames, promises, boundaries, shared moments, and unresolved issues. When the record grows past its capacity, older memories are compressed into stable "relationship impressions" so saves do not grow without bound.
- **Emotion and relationship pacing.** Responses weigh vanilla hearts, conversation history, trust, longer-term emotions, and recent conflict. New acquaintances do not act like lifelong friends; grievances do not vanish in the next line. Apologies, gifts, and follow-ups can gradually repair a relationship.
- **Community impressions.** Important interactions can leave limited impressions with witnesses and close circles. Retellings decay and blur over time instead of making the whole town omniscient.
- **Personal help requests.** NPCs may occasionally ask for a suitable item. Once clearly accepted, the request enters the vanilla quest log and only completes when the correct item is actually delivered, with a small friendship or material reward.
- **Gifts, money, and personal mail.** Under friendship, value, cooldown, and item checks, an NPC may offer a small gift, a little money, or a rarer meaningful gift. Birthday, reciprocal, and thank-you mail can be written in character, with stable templates as fallback. Per-NPC candidate gift pools are listed in [原版NPC个性礼物池.md](./原版NPC个性礼物池.md) (Chinese, with item IDs).
- **Companion outings.** After a clearly accepted invitation, an NPC can temporarily leave their schedule, walk through real map boundaries to a supported destination, stay a while, and then resume. Festivals, story events, sleep, bad weather, and unsafe map states are blocked.
- **Progress awareness.** The AI context can see the date, time, season, weather, festivals, location, relationships, the NPC's current activity, and part of the game progress, so characters talk about what is actually happening.
- **Small behaviors and world actions.** Dialogue can influence restrained behaviors (facing you, emotes, stepping closer, keeping distance). Every world-affecting action passes a whitelist and a second local validation; the model can never run arbitrary commands.
- **In-game Memory Book.** Press `LeftShift + J` to open a book with every NPC you know: a relationship card (emotion, closeness, trust, nickname, unresolved tension, the settled "relationship impression"), grouped long-term memories, past AI conversations by date, and shared moments (outings, favors, gifts). Mouse wheel and gamepad supported.

## SVE and custom NPCs

- `EnableSveCompatibility` is on by default. With SVE installed, built-in SVE profiles, relationships, and progression context are used; SVE is never required.
- Third-party NPCs use game data and a conservative generic fallback; without a dedicated profile, characters may be less detailed. The community can drop JSON profiles into `Mods/LivingNPCs/npc_profiles/` to add or override characters — see [npc_profiles/README.md](./LivingNPCs/npc_profiles/README.md).
- LivingNPCs respects content authors' AI-use permissions. Text from content packs that do not permit AI use is not copied into prompts.
- This release does not claim dedicated, hand-tuned support for other large expansions such as Ridgeside Village or East Scarp.

## Multiplayer (v1: host-authoritative)

Since 0.2.0, LAN/invite-code multiplayer is supported at a basic level, provided **both the host and the farmhands install this mod** (each with their own API key and provider settings):

- **There is exactly one NPC mind, owned by the host's save.** Farmhands generate dialogue locally with their own key; finished exchanges are reported to the host, which records memories/emotions/trust in the single authoritative ledger and pushes a read-only relationship view for each NPC back to farmhands — everyone perceives the same relationship history. Friendship earned from a farmhand's dialogue goes to that farmhand.
- **Farmhand conversation history stays on the farmhand's machine** (the `multiplayer/` folder). The memory book's "Talks" tab reads local history; the other tabs (bond/memories/moments) request a fresh snapshot from the host on open, falling back to the last synced data (with a notice) if the host does not respond.
- **World actions are judged by the host.** Small gifts, modest money grants, and item help requests proposed by a farmhand conversation are reported with the exchange and checked by the host against the same whitelist, relationship, cooldown, and cap rules. Approved results are granted only to the initiating farmhand. Their help request is projected into their own quest log, while delivery is still verified by the host. Farmhands cannot start companion outings in v1, and outing NPCs remain host-driven.
- **Every player who wants AI dialogue needs their own provider connection.** A farmhand missing an API key/model/server setting gets the existing configuration hint and vanilla dialogue; the host's key is never borrowed or transmitted.
- **Split-screen secondary players cannot use AI dialogue in v1** — they get a one-time notice and vanilla dialogue.
- If the host does not have the mod, the protocol versions differ, or the hidden `EnableMultiplayerSync=false` setting is used, farmhands fall back to local mode and do not synchronize the NPC mind with the host.

## Common settings

Settings actually available in Generic Mod Config Menu:

| Setting | Purpose |
| --- | --- |
| Enable AI dialogue | Master switch for the built-in engine; turning it on requires a game restart |
| Provider connection | LLM provider, API key, model name, server address, request timeout |
| Semantic context routing | On by default; one lightweight call selects the context needed this turn (timeout and thinking level adjustable) |
| Optimized world summary / concise prompt context | Reduce tokens; restore defaults if character detail drops |
| Chat thinking level | Only affects models that support it |
| AI for normal right-click | Off by default |
| AI line frequencies | Separate general / gift / marriage settings |
| Typed-dialogue hotkey / inspect-memory hotkey | Default `LeftAlt` and `LeftShift + J` |
| Disabled characters | Internal names, comma or space separated |
| Help requests | Toggle and daily offer chance |
| AI world actions | Master switch; turning it off keeps AI dialogue but stops gifts, money, and outings |
| AI small gifts | Toggle and daily chance range |
| SVE compatibility | Recommended on when SVE is installed |

The following advanced options **can only be edited in `config.json`** (close the game first and keep a backup; invalid values may be auto-corrected):

- Meaningful gifts, money, and outings: `AllowAiMeaningfulGifts`, `AllowAiMoneyGifts` (cap `MaxAiMoneyGiftAmount`), `AllowAiCompanionOutings`;
- AI-chat bonus friendship: `EnableAiDialogueFriendship` and its daily cap;
- Passive behaviors: `EnablePassiveBehaviors`, `PassiveBehaviorChancePercent` (off by default; test with the manual behavior hotkey first);
- Memory sizes: `MaxMemoryEntriesPerNpc`, `PromptMemoryEntries`;
- Gift mail and memory compression: `EnableAiGiftMail`, `EnableMemoryImpressions`;
- Multiplayer sync: `EnableMultiplayerSync` (on by default; when off, farmhands stop reporting to the host and fall back to session-local memory — see the Multiplayer section);
- Connection and logging: `SuppressConnectionCheck` (fully disables the self-check; by default it only runs when connection settings change) and `ExportAiResponseLogs` (AI diagnostic logs; each log rotates to `.old` past ~8MB, so they never grow without bound).

## Hotkeys

- `LeftAlt` + click/interact: open free-text AI dialogue.
- `LeftShift + H`: manually trigger one small behavior on a nearby NPC (mainly for testing).
- `LeftShift + J`: open the in-game Memory Book (relationship card, long-term memories, past conversations, shared moments).

The dialogue and memory-book hotkeys can be changed in GMCM; the behavior test key is `BehaviorHotkey` in `config.json`. Console state summaries remain available via the `livingnpcs_debug` command.

## Debug and evaluation tools

Type these in the SMAPI console:

| Command | Purpose |
| --- | --- |
| `livingnpcs_debug [near\|NPC]` | State, behavior reasons, help-request fit, and memory recall |
| `livingnpcs_prompt [near\|NPC]` | The hidden context prepared for the next generation |
| `livingnpcs_export [near\|all\|NPC]` | Export a Markdown diagnostic report |
| `livingnpcs_eval` | Run a lightweight rule sanity check |
| `livingnpcs_giftmail` | Inspect gift-mail state and generated text |
| `livingnpcs_forget [near\|NPC]` | Clear one NPC's behavior memory and AI dialogue history |
| `livingnpcs_forget all confirm` | Permanently clear all NPC memories and dialogue history in this save |
| `livingnpcs_tokens [export\|reset]` | View, export, or reset the save's LLM usage statistics |
| `livingnpcs_purge_valleytalk confirm` | After a verified migration, delete retained legacy ValleyTalk save keys; host only |

`forget`, `reset`, and `purge` delete data; back up the save and check the target first.

## Transcripts, logs, privacy, and cost

Conversations are sent to the LLM provider you configure. Requests can include your typed text, the NPC's name/profile/relationship, the in-game date, location, weather and progress, and the recent conversations and memories needed for continuity.

The API key is stored only in `Mods/LivingNPCs/config.json` and is never written to the mod's logs or exported reports. Never share your API key.

By default 0.2.0 keeps local files under `Mods/LivingNPCs` for review and troubleshooting: `conversation_logs/` (readable per-NPC memoirs), `prompt_logs/`, `ai_response_logs/`, `context_routing_logs/`, `debug_reports/`, and `token_usage/`. These may contain your conversations and game information — review and redact before sharing. Set `ExportAiResponseLogs` to `false` in `config.json` to stop the ongoing AI diagnostic logs.

Semantic routing, the final reply, AI-written mail, and long-term memory compression can all use model calls. Use `livingnpcs_tokens` to inspect usage; billing depends on your provider and playtime.

## FAQ

- **Holding LeftAlt shows no input box.** Check the NPC is in interaction range, no other menu is open, and the hotkey in GMCM; then check the SMAPI console for a missing API key/model/server message.
- **Plain right-click is still vanilla.** That is the default; use `LeftAlt` + interact, or enable "AI for normal right-click dialogue".
- **"Legacy ValleyTalk detected, AI dialogue disabled."** Fully exit the game, rename the old outer `ValleyTalk` folder to `.ValleyTalk` (or move it out of `Mods`), and restart.
- **Slow, expensive, or inconsistent output.** Make sure the model handles long context and structured output; lower AI line frequencies, try the optimized world summary, or switch to a faster model.

## Development build

Requires the .NET 6 SDK and a Stardew Valley/SMAPI installation to compile against. The projects default to the author's local `GamePath`; override it explicitly elsewhere:

~~~powershell
dotnet build LivingNPCs/LivingNPCs.csproj -p:GamePath="D:\SteamLibrary\steamapps\common\Stardew Valley"
~~~

Run tests:

~~~powershell
dotnet test LivingNPCs.Tests/LivingNPCs.Tests.csproj -p:GamePath="D:\SteamLibrary\steamapps\common\Stardew Valley"
~~~

Run the offline regression check (LivingNPCs.Diagnostics, no game needed):

~~~powershell
dotnet run --project LivingNPCs.Diagnostics\LivingNPCs.Diagnostics.csproj -- .
~~~

The release package should contain a single `LivingNPCs` folder loadable by SMAPI, without the legacy ValleyTalk or its Content Patcher pack.

A deeper systems overview of the behavior/memory layer lives in [LivingNPCs/README.md](./LivingNPCs/README.md) (Chinese); the rewrite specifications and decisions are archived under [RewriteSpec/](./RewriteSpec/README.md).

## Feedback

If you find a bug, unnatural behavior, a migration or compatibility problem, or have gameplay ideas, please leave a Nexus comment or Bug Report. Helpful reports include the game/SMAPI/mod versions, the NPC, date/time/weather, location, your input and the NPC's reply, screenshots, reproduction steps, and a parsed SMAPI log link from <https://smapi.io/log>. Check diagnostic files for private content before sharing, and never include your API key.

## Credits

LivingNPCs 0.2.0 and its built-in AI dialogue engine are a from-scratch rewrite. ValleyTalk is no longer bundled and no longer a runtime dependency; it only appears in the migration notes for 0.1.x users.

Thanks to Stardew Valley, SMAPI, Generic Mod Config Menu, and the modding community for the ecosystem, tools, and testing feedback.
