# Entertainment — Realtime Language Game (Kahoot-like) — Design & Plan

> Historical design note. The AI evaluation and server-calculated 7+3 rubric in [the platform AI design](2026-10-15-game-platform-ai-design.md) supersede the reveal-count scoring described below.

Date: 2026-10-11
Status: Approved design, ready for implementation

## Goal

A public, unlinked page at `https://readysetsiivous.fi/entertainment/` where any
visitor can create or join a small realtime language-learning group. A group has
a topic description; Gemini turns that description into English sentences at
each player's CEFR level. Players take turns translating; hint words are
"covered" and can be revealed one by one. Revealing a hint scores the other
players, so solving clean scores only you. No auth, no link from the main site —
reachable only by URL.

## Scope (non-goals)

- No Keycloak / accounts. Anonymous.
- No quiz correctness grading. Score is reveal-count only.
- No persistence of in-flight game beyond what Marten already records for
  history; live game state is in-memory.
- Single page, single game per group at a time.

## Requirements

1. **Page** — `site/src/app/entertainment/page.tsx`, static export, no nav link.
2. **Create group** — mandatory fields: group name, Gemini API key (creator's
   own, stored server-side), description (min 200 chars), creator's own name,
   creator's own CEFR level (A1/A2/B1/B2/C1/C2). Server generates a unique
   6-char join key.
3. **Join group** — enter join key + preferred name + level. Creator also has a
   name/level.
4. **Turn order** — registration order; creator starts the game.
5. **Lock on start** — after start, no new joins.
6. **Round** — one player is the *translator*. Server calls Gemini for a fresh
   English sentence at the translator's level, from the group description.
   - Translator sees English sentence; its words' *base forms* are covered and
     can be unwrapped one by one.
   - Others see the English sentence plus its Finnish translation, both
     covered, unwrappable.
7. **Realtime** — SignalR hub in the .NET API. Static site connects via
   `@microsoft/signalr`.
8. **Rotation** — after each round the translator advances to the next player;
   loop until the creator ends the game.
9. **Reuse** — a group is durable; creator can edit description and start again.

## Scoring (reveal-count only, no judge)

| Action | Effect |
|---|---|
| Translator reveals 1 hint word | every other player +1 |
| Translator solves with 0 reveals | translator +5 |
| Translator skips | every other player +1, translator +0 |
| Any non-translator reveals full Finnish | every other player +2, revealer +0 |
| Translator reveals full Finnish (self) | every other player +3 |

Winner = highest total at end.

## Architecture

```
site/src/app/entertainment/page.tsx      (static)
  └─ EntertainmentClient.tsx              'use client', SignalR JS client
       ↓ @microsoft/signalr (wss/https via Cloudflare Tunnel)
api:
  EntertainmentHub : Hub                   SignalR  (/hubs/entertainment)
  GameSessionManager (in-memory)           live group + round state
  GeminiSentenceService                    Gemini calls, cached per level
  Marten: EntertainmentGroup / EntertainmentPlayer / EntertainmentRound
```

- Site stays static export; the page is pure client JS.
- API gains a SignalR hub, two anonymous REST endpoints, one Gemini client,
  three Marten documents.
- Anonymous identity = join key + name; browser holds `playerId` (GUID) +
  `groupId` in `sessionStorage` for reconnect.

## Data model (Marten documents)

**EntertainmentGroup** (durable, reusable)
`Id`, `Name`, `GeminiApiKey` (encrypted at rest), `Description` (≥200 chars),
`JoinKey` (6-char, unique), `Status` (Lobby/InGame/Finished), `CreatedUtc`,
`UpdatedUtc`.

**EntertainmentPlayer** (durable per group)
`Id`, `GroupId`, `Name`, `Level` (A1..C2), `Score`, `IsCreator`, `JoinedUtc`,
`ConnectionId` (transient, nullable).

**EntertainmentRound** (event log per round)
`Id`, `GroupId`, `RoundNumber`, `TranslatorPlayerId`, `SentenceEn`, `SentenceFi`,
`BaseForms` (JSON array), `RevealEvents`, `Scores` snapshot, `StartedUtc`,
`EndedUtc`.

Live state (current translator, covered set, revealed set, live scores) lives
in-memory in `GameSessionManager`; Marten is the source of truth for history.

## Gemini integration

- Server-side only. `GeminiSentenceService` uses the per-group `GeminiApiKey`.
- Prompt per round: generate one `{level}`-level Finnish-learning English
  sentence about `{description}`, return JSON `{sentenceEn, sentenceFi,
  baseForms[]}`.
- Model `gemini-flash-latest` (auth via `X-goog-api-key` header), retry with
  backoff on transient 429/5xx. On failure reuse cached sentence for
  that level or abort the round gracefully.
- Local dev/test uses env `GEMINI_KEY` (repo Codespaces secret). Production uses
  the creator-supplied per-group key. E2E mocks Gemini — never calls the live
  API.

## Security & error handling

- **Gemini key** — AES-GCM at rest, key from env `ENTERTAINMENT_KEY_ENC`; never
  returned to any client.
- **Join key** — 6 chars from unambiguous set (no `0/O/1/I`), unique index,
  rate-limited join attempts.
- **Anonymous** — hub auth = `playerId` GUID claim; join key is the only secret.
- **Lifecycle** — auto-purge Finished groups + players after N days.
- **Reconnect** — SignalR auto-reconnect; re-attach to group via persisted
  `playerId`.
- **Errors** — RFC 7807 for REST; hub pushes an `Error` event; Gemini
  timeout/fail falls back to a cached sentence and keeps the game alive.

## Testing strategy

- **Unit (xUnit)** — `GeminiSentenceService` prompt/parse/fallback; scoring
  engine (reveal/skip/0-reveal matrix); join-key generator.
- **Hub tests** — rotation, lobby lock, reveal dispatch via in-memory hub client.
- **E2E (Playwright)** — mock REST via `page.route` (existing pattern); for
  SignalR use the LongPolling transport so negotiate + poll are plain HTTP and
  mockable the same way, or run against a local API. Each part below ships its
  own e2e spec.

---

# Implementation plan

Implemented as vertical slices, one commit each, each with its own e2e test.
Every part builds the site and runs the new e2e before the next part.

## Part 1 — Create group

**Backend**
- `EntertainmentGroup` document + Marten mapping + index on `JoinKey`.
- AES-GCM encryption helper (`IEntertainmentKeyProtector`).
- Join-key generator (6 chars, unambiguous set, uniqueness retry).
- `POST /api/v1/entertainment/groups` → creates group (Status=Lobby), stores
  encrypted Gemini key, returns `{ groupId, joinKey, creatorId }`.

**Frontend**
- `site/src/app/entertainment/page.tsx` + `EntertainmentClient.tsx`.
- Landing: "Create group" / "Join group" buttons.
- Create form: name, Gemini key, description (200-char counter), own name,
  own level.

**E2E** — `site/e2e/entertainment-create.spec.ts`: open page, fill create form,
submit (mock REST), assert join key + lobby screen shown; assert 200-char
validation blocks short description.

## Part 2 — Join group + Lobby (introduce SignalR)

**Backend**
- `EntertainmentHub` + `GameSessionManager` (in-memory lobby player list).
- `POST /api/v1/entertainment/groups/{joinKey}/join` → adds player, broadcasts
  `playerJoined` to lobby.
- Hub `JoinLobby(groupId, playerId)` re-attach on reconnect.
- Rate-limit join attempts.

**Frontend**
- Add `@microsoft/signalr`.
- Join form (join key + name + level).
- Lobby screen: live player list, join key shown for sharing, creator "Start"
  button.

**E2E** — `site/e2e/entertainment-lobby.spec.ts`: two browser contexts; creator
creates, joiner joins with key; both see updated player list; creator sees
"Start", joiner does not.

## Part 3 — Start game + Gemini sentence + round views

**Backend**
- Hub `StartGame(groupId, playerId)` (creator only) → Status=InGame, lobby
  locks, order = join order.
- `GeminiSentenceService` generates sentence + base forms + Finnish at the
  translator's level (fallback to cache on error).
- Broadcast `roundStarted` to translator (English + covered base forms) and
  observers (English + Finnish, covered).

**Frontend**
- Translator view: covered English sentence, tap to unwrap, "Done" + "Skip".
- Observer view: covered English + covered Finnish, tap to unwrap.

**E2E** — `site/e2e/entertainment-round.spec.ts`: start game (mock Gemini), assert
covered English sentence rendered for translator and covered English+Finnish for
observer.

## Part 4 — Reveal / Skip / Scoring / Rotation / Results

**Backend**
- Hub `RevealWord`, `Skip`, `Done` → `RevealEvent` persisted, scoring engine
  applies the table above, broadcast score updates.
- Rotation to next translator; creator `EndGame` → Status=Finished, standings.

**Frontend**
- Unwrap interactions wired to hub; live score display; results/standings screen
  with winner highlight.

**E2E** — `site/e2e/entertainment-game.spec.ts`: full flow — reveal a hint
(others score), 0-reveal solve (translator scores), skip, rotation to next
player, end game, standings shown.
