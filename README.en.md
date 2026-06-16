# StardewLivingNPCs

[中文 README](./README.md)

An experimental umbrella repo that wires an **AI conversation** layer and an **NPC behavior** layer into *Stardew Valley*.

The repo has two parts:

- `LivingNPCs/` — this project's own SMAPI companion mod: memory, state, behavior, relationship pacing, and controlled AI influence on the game world.
- `ValleyTalk/` — a local fork of dandm1/ValleyTalk that generates the AI dialogue, plus streaming dialogue, log export, hidden metadata, and third-party context injection.

> **Attribution & license** — `ValleyTalk/` is a **modified build of [dandm1/ValleyTalk](https://github.com/dandm1/ValleyTalk)** (Nexus mod 30319), redistributed under its original **LGPL v2.1** (see [`ValleyTalk/LICENSE.txt`](./ValleyTalk/LICENSE.txt)). It is bundled here only because LivingNPCs needs a small bridge that stock ValleyTalk lacks. `LivingNPCs/` is this project's own code.

One-line design:

> ValleyTalk decides **what an NPC says**; LivingNPCs decides **what an NPC remembers, why it speaks that way, and what it may safely do**.

> **Players:** the Nexus mod page is the place to start — it has the bundled download and a player-facing guide. This file is the developer/architecture overview.

## What's implemented

### 1. AI conversation pipeline

- Free-text AI conversation through ValleyTalk.
- The free-text entry also covers festival / event scenes: hold the configured key and click a festival NPC to open the AI input box instead of only the vanilla festival lines.
- Chinese prompt + in-game Chinese output supported.
- A real streaming reply UI: text appears as it is generated, inside the portrait dialogue box, rather than only a "thinking" placeholder.
- A single reply can continue across multiple vanilla dialogue pages.
- The AI can decide whether the exchange should naturally end; if so it returns `endConversation=true` instead of forcing another choice.
- Every AI conversation produces hidden metadata for LivingNPCs:
  - `rapportDelta` — extra friendship from this chat.
  - `memories` — facts worth remembering long-term.
  - `emotionImpact` — effect on the NPC's longer-term feelings.
  - `conflicts` — interpersonal friction worth remembering.
  - `ambientFollowUp` — a later overhead-bubble follow-up line.
  - `behaviorInfluences` — behavior leanings that keep acting for minutes to days.
  - `helpRequests` / `helpRequestUpdates` — small favors an NPC offers, and later completion / refusal.
  - `actions` — at most one controlled world-action request.

### 2. NPC memory system

Several distinct memory layers, not just "the last line":

| Layer | Purpose |
| --- | --- |
| Recent interactions | behaviors, conversation starts, gifts, event interactions, world actions the NPC actually performed |
| Current state | mood, attention, openness, current response leaning |
| Long-term familiarity | how long / how well the NPC has known the player |
| Interaction pacing | chats today, consecutive-day streaks, long gaps since last seen |
| Long-term memory | AI-distilled facts, preferences, boundaries, relationship info |
| Player-preference memory | liked categories, disliked items, habits, values, goals |
| Community impressions | the player's social traces other NPCs witnessed or heard from a close circle |
| Help requests | favors the NPC asked for, due dates, completion status |
| Emotion & conflict memory | the NPC's longer feelings toward the player, friction, severity, recovery |
| Nickname memory | how the player asked to be addressed, and whether the NPC accepted |

Long-term memory is not fixed-phrase matching. ValleyTalk returns structured memory candidates from the whole exchange; LivingNPCs only keeps the important, still-useful ones. Memories describing the player are tagged `playerPreference` and stored separately. A deterministic "player requested nickname" fallback covers cases where the model misses it.

There is also a retrieval/curation layer:

- On save, memories are deduplicated by "type + stable topic"; repeats are merged, not stacked forever.
- Each memory stores topic, tags, importance, reinforcement count, last-updated and last-recalled times, and recall count.
- Tags are extracted from both the AI metadata and text keywords (farm, mines, fishing, library, flower, coffee, night, …).
- Before each ValleyTalk context build, relevance is scored against current location, time, weather, recent behavior, gifts, events, and open help requests.
- Only the few currently-relevant memories enter the prompt, and recently-used ones are slightly down-weighted so the NPC doesn't loop on one topic.

### 3. Emotions & conflict

Beyond the short-term `Mood`, each NPC keeps a longer interpersonal state:

- Current emotion: `Happy`, `Calm`, `Jealous`, `Worried`, `Grateful`, `Disappointed`, `Uneasy`, `Upset`, `Angry`, `Sad`.
- Emotion intensity: `0-100`.
- Conflict memory: cause, severity, status, whether healed by apology / gift / time.
- Relationship trust: a separate `0-100` value that gates how deeply the NPC opens up. (This is independent of vanilla hearts.)

Triggers include AI `emotionImpact` / `conflicts`, gift results, worry on long absence or when the player is hurt, light jealousy from romantic-bystander situations, gratitude after a completed help request, and disappointment after a let-down.

Rules:

- Liked / loved gifts raise good feelings and help soften existing conflict.
- Disliked / hated gifts cause unease or upset, and at the extreme are written into conflict memory.
- If the AI judges a genuine apology / repair, it can return `apology` and `repairDelta`.
- Each morning emotions ease toward calm and conflict severity decays.
- Unresolved conflict lowers approach/affection leanings and, when severe, blocks gifts, money and favors.
- Ordinary conflict heals gradually via apology, gifts and time; severe conflict needs a multi-step chain (apology + a fitting gift + time + a real talk-it-out) and softens gradually instead of resetting on one polite line.
- A severe conflict that is genuinely repaired can leave the relationship slightly deeper than before.

An **emotional expression style** layer makes "the same upset" play differently per character (e.g. Flor goes quiet and reflective; Shane gets guarded; Haley is sharper but cools fast; Harvey leans to polite worry). It also nudges the actual numbers: daily emotion decay, conflict decay, repair responsiveness, and how long a severe conflict must last.

### 4. Relationship & chat pacing

LivingNPCs weighs **vanilla hearts** and its **own long-term familiarity** together:

- When the bond is shallow, repeated chats in one day read as more polite/reserved.
- Once closer, several chats a day feel more natural.
- Consecutive days, long gaps, and "first chat today" all shape how warm the next line should be.
- Relationship trust gates how private the NPC gets: low trust stays surface-level; high trust allows more vulnerable, personal content.

On top of vanilla daily-talk friendship, AI chats grant a small "chat-quality bonus":

- `rapportDelta` is judged by the model, range `0-30` (ordinary pleasant chat ~`10-15`, warmer ~`16-24`, rare strong connection ~`25-30`).
- Each NPC has a daily cap on AI-chat bonus friendship, default `30`.

### 4.5. NPC social layer

LivingNPCs keeps a limited-spread "community impression" layer between NPCs:

- Spending repeated time with someone, finishing their favor, or being openly close in public can be noticed by nearby NPCs.
- Eyewitnesses store a higher-confidence `Witnessed` memory.
- Using `FriendsAndFamily` from `Data/Characters`, news passes to a close circle as lower-confidence `CloseCircle`.
- If a fact is public enough and happens in `Town` or `Saloon`, a few present NPCs get a weaker `PublicRumor` impression.
- Facts have privacy tiers `Public` / `Personal` / `Private`; the more private, the more it depends on high trust or a very small close circle.
- The same fact gets each NPC's own tone (gentle, reserved, direct, curious), so it doesn't sound identical in every mouth.
- ValleyTalk only ever receives the one or two most relevant impressions, and is told to stay vague about second-hand news (no omniscient NPCs).
- Messages have a lifecycle (`fresh` → `settled` → `fading`) and decay out; eyewitness memories last longer than retold or public ones.
- Each morning a few NPCs may retell fresh news to acquaintances; expressive/curious ones share more, guarded ones keep it in. Retelling accumulates "relay depth" and distortion, so wording grows vaguer rather than more precise.
- Stable circles exist: family/close ties, the saloon regulars, and the young-people group (the latter two mostly carry public news, not private matters).

So Haley might vaguely know you talk to Emily a lot, Robin might hear you helped Maru, and saloon patrons present at the time keep a fuzzy impression of what you just did.

### 4.6. Game-progress awareness

LivingNPCs compresses "how far this save has gotten" into a world-stage summary, not just the current season:

- Route: Community Center restored, Joja route, or undecided.
- Unlocks: bus, greenhouse, minecart, Ginger Island, movie theater.
- Player vocation leaning: farming / fishing / foraging / mining / combat, from real profession records.
- Farm scale: starting / small / established / large, estimated from crops, finished buildings and animals.
- Family stage: spouse and number of children.
- Time stage: Y1 spring newcomer, Y1 settling in, Y2 integrated, Y3+ old resident.

This is given to ValleyTalk with constraints: don't write a Y1-spring player as an old-timer, or a Y3+ player as a newcomer; treat repaired facilities as everyday fact and don't mention unbuilt ones as if they exist; acknowledge marriage/kids/a big farm when appropriate.

Progress is split into two tiers:

- **Public facts** — route, public facilities, which year you're in, marriage and kids.
- **Privately-knowable info** — rough farm scale, vocation leaning. Only exposed when the relationship is close enough, the NPC is on the farm to see it, the NPC's job/interest gives a real reason to know (Robin/Marnie for the farm, Willy for fishing, Clint for mining), or you told them directly.

There is also an **SVE / RSV progress-awareness layer**:

| Expansion | Recognized progress |
| --- | --- |
| SVE | Grandpa's shed restored, Apples, Enchanted Grove, Aurora Vineyard restored, Crimson Badlands, Castle Village Outpost, Susan, Joja Emporium |
| RSV | Visited Ridgeside, cable car, public gatherings, greenhouse restored, Ridge Forest, Spirit Realm, Ninja House, Undreya, Daia |

Filtered by NPC origin: SVE characters treat confirmed SVE milestones as real; RSV characters treat confirmed RSV milestones as part of Ridgeside life; vanilla NPCs treat them as distant background unless context supports knowing. Unconfirmed milestones are explicitly marked unfinished to avoid spoilers or invented progress.

This layer also gates help-request depth (Y1 spring: only light one-step favors; later Y1: occasional two-step; Y2+: fuller multi-step when relationship and context fit) and passes through each NPC's own values (community-minded characters weigh the Community Center more; Pierre vs. Morris diverge on Joja; Pam on the bus, Willy on Ginger Island, farm types on the greenhouse, family types on marriage/kids). It applies to vanilla, SVE and RSV characters.

### 4.7. Debug & eval tools

| Tool | Purpose |
| --- | --- |
| `LeftShift + J` | print a nearby NPC's state, memory and recall results to the SMAPI console |
| `livingnpcs_debug [near\|<NPC>]` | an NPC's state, recent behavior reasons, help-request fit and memory recall |
| `livingnpcs_prompt [near\|<NPC>]` | the full hidden context about to be injected into ValleyTalk |
| `livingnpcs_export [near\|all\|<NPC>]` | a Markdown debug report under `Mods/LivingNPCs/debug_reports/<save>/` |
| `livingnpcs_eval` | a light in-game runtime check that key personality rules still hold |
| `livingnpcs_restore_gift_mail [latest\|all]` | restore unclaimed LivingNPCs gift letters that vanished from the mailbox |
| `livingnpcs_giftmail` | diagnose LivingNPCs gift mail: status, mailbox location, whether `Data/mail` has the entry, generated text, orphaned dead letters |

There is also an offline regression check (no game needed):

```powershell
dotnet run --project LivingNPCs.Diagnostics\LivingNPCs.Diagnostics.csproj -- .
```

It verifies key debug abilities, emotion styles, help-request explanations, behavior-choice reasons, and that README notes weren't accidentally dropped.

### 5. NPC behavior layer

Safe micro-behaviors: `FacePlayer`, `Emote`, `ApproachPlayer`, `Pause`, `LookAround`, `StepAway`.

Behaviors come from three sources:

- **Manual** — a hotkey triggers one behavior on a nearby NPC, for testing.
- **Passive** — nearby NPCs react on a small chance (configurable; ships off by default).
- **Dialogue-driven** — AI conversations leave short-term leanings so the next few minutes-to-days of behavior grow out of that exchange.

Behavior choice considers the NPC's light personality, current relationship/familiarity, whether you've chatted too often today, time/weather/location/indoors/nearby NPCs, world progress, and recent dialogue leanings — and writes the result back into memory and state.

Dialogue-driven leanings currently supported:

| Leaning | Effect |
| --- | --- |
| `visit_location` | the NPC genuinely mentions wanting to go somewhere; if you later meet there, it picks that up |
| `comforted` / `stay_near` / `pause_to_talk` | after comfort/closeness, more willing to approach, linger and respond |
| `offended` / `give_space` | after offense or needing space, more likely to keep distance or step back |

### 6. Controlled AI influence on the world

The AI can *request* a few world actions, but **cannot freely change the game**:

```text
AI proposes an action in hidden metadata
-> LivingNPCs checks relationship, location, cooldown, frequency and caps
-> only whitelisted, safety-checked actions run
-> the result is written back to NPC memory
```

Current whitelist:

| Action | Rule |
| --- | --- |
| `give_small_gift` | at least a little familiar; at most one AI gift per NPC per day |
| `give_meaningful_gift` | at least friendly, plus one of: high relationship / recent special event / important long-term memory; separate cooldown |
| `give_money` | at least friendly; defaults to 100g, capped by config (default cap 250g) |
| `water_nearby_crops` | at least friendly; farm only; only planted-but-unwatered nearby crops |
| `companion_outing` | both clearly agree to go somewhere together now; the NPC uses vanilla schedule-style cross-map routing and stays at least 2 in-game hours on arrival |
| `festival_interaction` | light special interactions in festival / event scenes only |
| `assist_quest` | light assistance around the player's existing quests; never completes them |

Deliberately not enabled: planting crops, arbitrary NPC teleports (outings prefer vanilla schedule pathing and real map exits; a safe-position fallback is only allowed once the NPC is off-screen and routing failed), permanent schedule edits, changing quests/story/world state, or any non-whitelisted command.

### 7. Gift system

AI gifts have two tiers:

- The catalog has 96 vanilla items: 64 small gifts, 32 meaningful gifts.
- The 34 vanilla giftable NPCs use a "shared pool + per-character personality pool"; expansion NPCs currently use the shared pool only.
- Per-character pools and item IDs are in [`原版NPC个性礼物池.md`](./原版NPC个性礼物池.md).

**Small gifts** (everyday, light, low-value) are weighted-random, scored by the NPC's traits, hearts/relationship tier, season, job/background, what the chat just touched on, recurring long-term topics, player-preference memory (weighted above ordinary topics), whether the item is in that NPC's personality pool, and the last 3 AI gifts (same item excluded, similar categories slightly down-weighted). Season only adds a match bonus; it no longer hard-bans off-season gifts.

**Meaningful gifts** are not more frequent, but more deliberate: only when the relationship is deep, a special event just happened, or the chat clearly hit an important memory. Candidates lean toward gems, fine cooking, desserts and artisan goods, with a separate cooldown (default 7 days; bypassable at 8 hearts but still subject to one AI gift per day).

### 8. NPC help requests

A light "help-request" layer where an NPC asks the player for a small favor. New requests are item requests only:

| Type | Completion |
| --- | --- |
| `item_request` | the NPC asks for a low-value, whitelisted item; hand it over to complete |

`question_request` is no longer projected as a quest; "ask me something" stays ordinary AI conversation so the quest log has no deliverable-less entries.

Lifecycle:

- A new request starts `Offered` — the NPC merely asked; not in the quest log yet.
- After the player clearly agrees, the AI returns `accepted`, the request becomes `Pending`, and it projects into the vanilla quest log.
- If the player declines, the AI returns `declined`; no quest, just a light relationship memory.
- A request can have up to 3 steps, each a stage-appropriate item request; the log shows the current step, intermediate steps advance, and the full reward comes on the last step.
- If `followUpPotential` is `deeper_relationship`, completing it more easily deepens the relationship in later chats.
- Failure reactions distinguish "never agreed, so didn't do it" from "agreed but didn't", expressed per character (direct, withdrawn, or gentle).

A request can surface two ways: from the current chat hitting a fitting point, or from a once-per-day roll on the first chat with an eligible NPC (default `25%`) deciding whether the NPC brings up a favor unprompted. Either way it must pass the gates: no existing open request for that NPC, at least some familiarity with relationship trust over the configured threshold, no clear unresolved conflict, not currently `Angry`/`Upset`, and the per-NPC cooldown between new requests. Generation also considers job, personality, season, progress, relationship stage, and what was just discussed.

In the quest log:

- Entries show only who is asking, the item, and the due date — no reward hint and no claimable reward box (matching vanilla "Item Delivery" Help Wanted quests).
- LivingNPCs still owns the underlying state; it just projects into the vanilla log so it feels like a vanilla quest extension.

Item requests respect progress: only low-value seasonal items obtainable this season; no minerals before the mines open; no amethyst until you've gone deep enough; if nothing fits, the NPC simply doesn't open a request instead of switching to a question.

On completion: light `Grateful`; relationship trust and familiarity rise; intermediate steps grant a little trust/openness, with the full friendship reward on the last step; an extra `50-100` friendship (range configurable); if the request carries a gold reward it is paid immediately on hand-in (vanilla item-delivery style, not a next-day letter); a small thank-you gift by default; a possible overhead-bubble thank-you the next day; and a recorded "shared experience" milestone. Declines/expiries cause only light `Disappointed`.

### 9. ValleyTalk context bridge

LivingNPCs pushes a "hidden continuity summary" to ValleyTalk rather than a few raw logs. It tells the model: the NPC's current tone, how close you are, whether you've already chatted a lot today, recent gifts/events, nearby NPCs, how time/weather/location should color things, key recent memories, known player preferences, recent community impressions, any open help request, any current/just-healed conflict, and any nickname the NPC accepted. It also instructs the model not to restate the mechanics, not to mention LivingNPCs/prompt/AI/JSON, and to absorb only the one or two most relevant points as normal NPC speech.

### 10. Custom NPC / SVE / RSV support

**LivingNPCs:** prefers built-in profiles; SVE recognized characters have hand-written summaries (with common alias handling); RSV has summaries for many main villagers, others fall back to `Data/Characters` inference; custom NPCs without a profile get base traits inferred from `Data/Characters`. SVE and RSV get dedicated recognition (summary-backed characters get clearer hints; others carry their mod's worldview hint and fall back to inference). World-progress awareness, personalized attitudes, relationship memory and help requests all apply to SVE/RSV characters. A `LivingNPCs/npc_profiles/` directory lets the community add or override profiles via JSON (single-object, array, or `{ "profiles": [...] }`), with a bundled `_template.json` — no C# changes needed.

**ValleyTalk fork:** reads custom NPCs already loaded at runtime; adds `AllowLocalContentPackDialogueForAi` (on by default) so SVE/RSV characters can trigger AI dialogue; and auto-generates a light biography from `Data/Characters` (age, politeness, sociability, optimism, datability, family/friends) when one isn't provided.

### 10.5. Prompt size

A full SVE/RSV AI prompt stacks: vanilla world summary (~13.7k chars) + expansion world summary (~5k for SVE) + the current NPC's biography + sample dialogue + game state + chat history + ValleyTalk event history + the LivingNPCs hidden context + memories/preferences/impressions/help/emotion — easily 20k+ chars. Levers to trim it: the ValleyTalk fork has `UseOptimizedGameSummaryPrompt` and `UseOptimizedLivingNpcMetadataPrompt` (both off by default), and LivingNPCs has `ConcisePromptContext` (off by default) which sends a slimmer hidden continuity context. Debug logs break prompt size down by section (`system, game, npc, core, instructions, command, responseStart`).

### 11. Conversation logs & observability

ValleyTalk exports a readable per-save, per-NPC chat log:

```text
Mods/ValleyTalk/conversation_logs/<save>/<NPC>.md
```

Logs are kept by turn (player input, NPC reply, in-game date/time). With ValleyTalk debug logging on, each request also prints staged timings and a prompt-character breakdown, so toggling the optimized-prompt switches gives a concrete size comparison. LivingNPCs' inspect-memory hotkey prints nearby NPC state, pacing, long-term memory and recent behavior to the console.

### 12. Shared experiences

Completing a companion outing (`companion_outing`, see §6) records a standalone "shared experience" milestone:

- It grows a natural follow-up a few days later (e.g. the NPC mentions "that trip was nice").
- It makes that NPC more willing to propose a similar new outing when the scene fits.
- It also spreads as a limited community impression among witnesses and the close circle (see §4.5).

The outing itself stays deliberately conservative: it never rewrites the permanent schedule (only a one-off temporary cross-map route, then a vanilla-style return); it doesn't force the player to follow or show "follow me" prompts (the NPC stands at a public spot chosen by semantic anchors + map safety scoring); after settling a while with the player nearby it may show at most one low-frequency scene emote; and the outing's stage, destination, activity, stay time, standing-spot semantics and whether the player is present are injected into ValleyTalk as hidden context.

### 13. Token usage

ValleyTalk accumulates AI token usage per session and per save:

- Prefers official usage from the provider (OpenAI-compatible, Claude, Gemini, VolcEngine, llama.cpp); otherwise writes a local estimate marked `estimated`.
- Aggregates by total, by model, by NPC, and recent requests.
- Per-save stats are saved with the save and exported to `ValleyTalk/token_usage/<save>.md`.

Console: `valleytalk_tokens` (summary), `valleytalk_tokens export`, `valleytalk_tokens reset`.

## Overall flow

```text
Player interacts with an NPC
-> LivingNPCs records the conversation start / gift / event context
-> LivingNPCs builds a hidden context summary and pushes it to ValleyTalk
-> ValleyTalk generates a reply from character profile, game state, history and that summary
-> ValleyTalk also emits hidden metadata
-> LivingNPCs parses it
-> updates long-term memory, player-preference memory, dialogue-driven behavior, emotion/conflict, bonus friendship, follow-up bubbles, controlled world actions
-> writes limited-spread community impressions among witnesses and the close circle
-> the new state feeds the next conversation
```

## Install & build

### Dependencies

- Stardew Valley 1.6.x
- SMAPI
- Content Patcher (required by ValleyTalk's content pack)
- Generic Mod Config Menu
- An LLM provider / API key for ValleyTalk

### Local dev build

```powershell
cd LivingNPCs
dotnet build
```

```powershell
cd ValleyTalk
dotnet build src\ValleyTalk.csproj -p:GamePath="D:\SteamLibrary\steamapps\common\Stardew Valley"
```

`Pathoschild.Stardew.ModBuildConfig` deploys the mod to the game's `Mods` folder on a successful build, and produces a distributable zip under each project's `bin/Release/net6.0/`.

## Hotkeys

- `LeftShift + H` — trigger one LivingNPCs micro-behavior on a nearby NPC.
- `LeftShift + J` — print a nearby NPC's state and memory to the SMAPI console.

## Main config

LivingNPCs ships a deliberately small in-game Generic Mod Config Menu (enable mod, HUD messages, the inspect-memory hotkey, the help-request toggle, the AI world-actions master plus watering/festival/quest-assist, SVE/RSV compatibility, and the concise-prompt switch). Everything else lives in `config.json` with safe defaults, including:

- Hotkeys and manual-behavior test mode; memory sizes; whether to log conversation starts.
- Help requests: pending cap, cooldown days, minimum relationship trust, daily offer chance, completion reward range.
- AI-chat bonus friendship and its daily cap; ambient follow-ups; dialogue-driven behaviors and how many days they linger.
- AI world actions: per-action toggles (small / meaningful gift, money, watering, outing, festival, quest assist); meaningful-gift cooldown; money cap; watering tiles; minimum outing stay.
- State / emotion / conflict daily decay; passive-behavior chance; daily behavior cap; interaction range.
- ValleyTalk prompt bridge; `ConcisePromptContext`; the optional AI behavior planner.

The ValleyTalk fork adds: provider config (OpenAI-compatible / others), `AllowLocalContentPackDialogueForAi`, the optimized world-summary and LivingNPCs-metadata prompt switches, custom prompt/language/entry config, and the free-input hotkey (which now also works in festival / event scenes).

## Not done yet

- More hand-authored semantic anchors for vanilla and expansion maps.
- Richer follow-up chains growing out of completed shared experiences (multi-step invites, cross-location experiences, deeper quest-line tie-ins).
- Longer-arc emotional evolution (cross-season grudges, long-term reassurance, more complex echoes when re-hurt after a repair).
- A fuller hand-written custom-NPC profile library (the JSON profile entry point exists; the focus shifts to filling it).
- More advanced (currently-disabled) world actions such as planting, assigning work, or changing routes.

## License

- `ValleyTalk/` keeps the original project's **LGPL v2.1** license; see `ValleyTalk/LICENSE.txt`. ValleyTalk is created by [dandm1](https://github.com/dandm1/ValleyTalk); this is a modified build.
- `LivingNPCs/` is this repo's own new code.
