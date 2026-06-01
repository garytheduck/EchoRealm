# Networked Shared Film — Design Spec

**Date:** 2026-06-01
**Project:** EchoRealm (MR interactive film, HoloLens 2, Unity 2022.3 + MRTK3 + Photon Fusion 2)
**Status:** Approved design — ready for implementation plan
**Builds on:** QR co-location (#1, done) and AI-adaptive narrative (#2, done)

---

## 1. Problem

The film currently runs as an **independent copy on each headset**. Only the test cube is networked. The Oracle/acts, the AI's chosen scene variant, voice commands, and world/weather changes all happen locally per device, so two users do **not** share one experience — they watch two parallel, drifting films, and a user's voice only affects their own world.

## 2. Goal / Success Criteria

Two (or N) co-located HoloLens users experience **one** AI-adaptive film together:
- Both headsets are always in the **same act and the same AI-chosen variant**, in sync.
- A voice command from **any** headset changes the **shared** world (e.g. one user says "make it rain" → both see rain, in the same physical spot via the QR `SceneRoot`).
- The AI's branching decisions are driven by the **combined** behavior of all headsets (pooled voice/gesture/cooperation), not just one device.
- A single device (master only) still runs the full film unchanged.

## 3. Chosen Approach: A — Master-authoritative `FilmSync` NetworkBehaviour

(Considered and rejected: **B — RPC-only relay** (no stored state → no late-join snapshot, missed RPCs desync); **C — per-system NetworkBehaviours** (more objects/authority to keep consistent; overkill now).)

**Core principle:** the **master** HoloLens is the single authority. A master-owned `FilmSync` `NetworkObject` is the single source of truth for film state. The master runs all "brain" logic (act flow, AI, combined behavior profile). Clients run no film logic of their own — they **replay** the master's networked state + broadcast RPCs on their own co-located content. "Master authority" means the master is the **referee that aggregates all input and decides the single shared outcome** — every headset is a full participant.

## 4. Fusion 2 Specifics (to follow in implementation)

- `FilmSync` is a `NetworkBehaviour` on a `NetworkObject`. The **master spawns it** via `Runner.Spawn(...)` (same proven pattern as `TestCubeSpawner`); clients receive it by replication. `Spawned()` sets a singleton `FilmSync.Instance` on every device. State authority = the spawner (master).
- **Networked state:** `[Networked] int CurrentAct`, `[Networked] NetworkString<_16> ChosenVariant`. Change detection uses Fusion 2's `ChangeDetector` (obtained in `Spawned()`, polled in `Render()`) — not the removed v1 `OnChanged` attribute.
- **RPCs** (strings are serialized by Fusion, so longer text like the Oracle line rides in an RPC param, not a `[Networked]` string):
  - `[Rpc(RpcSources.All, RpcTargets.StateAuthority)] RPC_SubmitSpeech(string text)` — client → master.
  - `[Rpc(RpcSources.StateAuthority, RpcTargets.All)] RPC_StartAct(int act, string variant, string mood, string narration)` — master → all.
  - `[Rpc(RpcSources.StateAuthority, RpcTargets.All)] RPC_ApplyCommands(string commandsCsv)` — master → all.
- **Master check:** `FusionNetworkManager.IsMaster` (`Runner.IsSharedModeMasterClient`).

## 5. Components & Changes

### 5.1 `Assets/Scripts/Networking/FilmSync.cs` (NEW `NetworkBehaviour`)
- Singleton `Instance` set in `Spawned()`.
- `[Networked] int CurrentAct`, `[Networked] NetworkString<_16> ChosenVariant` — minimal late-join snapshot.
- `ChangeDetector` in `Render()`: when `CurrentAct`/`ChosenVariant` change, **a late-joining client** starts that act (catch-up). Live transitions use `RPC_StartAct` (carries the full decision incl. narration).
- **Master-side API:**
  - `DriveAct(int act, AINarrativeDecision d)` — master only: sets `CurrentAct`/`ChosenVariant` (snapshot) and calls `RPC_StartAct(act, d.chosen_variant, d.mood, d.oracle_narration)`.
  - `SubmitSpeech(string text)` — called on any device. If master: process locally (5.4). If client: `RPC_SubmitSpeech(text)`.
  - `BroadcastCommands(string[] commands)` — master only: `RPC_ApplyCommands(string.Join(",", commands))`.
- **RPC handlers:**
  - `RPC_SubmitSpeech` (runs on master): calls the master's authoritative speech processing (5.4).
  - `RPC_StartAct` (runs on all): reconstructs an `AINarrativeDecision { chosen_variant, mood, oracle_narration }` and calls `ActManager.Instance.StartAct(act, decision)`.
  - `RPC_ApplyCommands` (runs on all): splits the CSV and calls `CommandExecutor.Instance.ExecuteCommand(cmd)` for each.

### 5.2 `Assets/Scripts/Networking/FusionNetworkManager.cs`
- After `OnSessionJoined` (master only), spawn the `FilmSync` prefab via `Runner.Spawn`. Expose nothing new beyond what exists; `FilmSync.Instance` is how others reach it.
- A serialized `NetworkObject filmSyncPrefab` reference (assigned in Inspector) for the master to spawn.

### 5.3 `Assets/Scripts/Film/FilmDirector.cs`
- `StartFilm`, `OnActCompleted`, and `RequestActDecision` run **master-only** (`if (!IsMaster) return;`).
- The master no longer calls `actManager.StartAct(...)` directly; it calls `FilmSync.Instance.DriveAct(act, decision)`. `DriveAct`'s `RPC_StartAct` then drives `ActManager` on **all** devices (incl. master) — single code path.
- Act 1/2 (which previously passed `null`/default) call `DriveAct(act, null)`; `FilmSync` sends a default decision.

### 5.4 `Assets/Scripts/AI/VoiceCommandProcessor.cs`
- `ProcessSpeechText(text)` no longer calls the AI + executes locally. Instead: `FilmSync.Instance.SubmitSpeech(text)`.
- **Master-authoritative processing** — a master-only method `FilmSync.ProcessSpeechAsAuthority(text)` (called by both `SubmitSpeech` on the master and the `RPC_SubmitSpeech` handler): `ActionCollector.RecordVoiceCommand(text)` → `AIManager.SendCommandRequestAsync(...)` → on response, `BroadcastCommands(response.commands)` and raise the master's `VoiceCommandProcessor.OnAIResponseReceived` so the master's `NarrativeManager`/UI still react.
- If `FilmSync.Instance` is null (e.g. no networking / editor before spawn), fall back to the current local path so single-device/editor testing still works.

### 5.5 `Assets/Scripts/AI/ActionCollector.cs`
- Unchanged API. Recording now occurs on the **master** (because the master is where speech is interpreted). The master's `ActionCollector` is the combined profile the AI reads at transitions. (Gesture/cooperation recording is deferred — see §6.)

### 5.6 `Assets/Scripts/AI/CommandExecutor.cs`
- Unchanged logic. Now invoked via `FilmSync.RPC_ApplyCommands` on every device instead of once locally. `ExecuteCommand` is already per-command and idempotent enough for this.

### 5.7 `Assets/Scripts/AI/AIManager.cs` / `NarrativeDecisionEngine.cs`
- No structural change, but they are now **only called on the master** (from `FilmDirector` and the master speech path). Clients never call the AI.

## 6. Data Flow

**Act transition:** master `FilmDirector` (Act N ends) → `NarrativeDecisionEngine` (reads combined `ActionCollector`) → variant → `FilmSync.DriveAct(N+1, decision)` → sets `[Networked]` snapshot + `RPC_StartAct` → **all devices** `ActManager.StartAct(N+1, variant)`.

**Client voice command:** client `VoiceCommandProcessor` → `FilmSync.RPC_SubmitSpeech(text)` → master: `ActionCollector.Record` + `AIManager` (Claude) → commands → `FilmSync.RPC_ApplyCommands` → **all devices** `CommandExecutor.Execute` → shared world change (co-located via `SceneRoot`).

**Master voice command:** same, but `SubmitSpeech` processes locally on the master (no inbound RPC), then `RPC_ApplyCommands` to all.

## 7. Testing

- **Two peers:** HoloLens (master) + Unity Editor Play Mode (client) on the same `EchoRealm` session, or two HoloLenses.
- Verify: act transitions fire on both in sync; a **client's** voice command changes the world on **both**; the master's `ActionCollector` shows **combined** counts; the AI variant reflects pooled behavior.
- **Single device** (master only) runs the full film unchanged (FilmSync with 1 player; master is its own authority; null-guard fallback covers editor-without-peer).

## 8. Edge Cases

- **`FilmSync.Instance` null** (pre-spawn, or no networking): `VoiceCommandProcessor`/`FilmDirector` fall back to the existing local path (current behavior), so editor/solo testing is unaffected.
- **Client joins mid-film:** picks up `CurrentAct`/`ChosenVariant` from the `[Networked]` snapshot and starts that act; prior world/weather changes are not back-filled (deferred, §9).
- **Master spawn timing:** the bootstrapper starts the film only after Photon joins; the master spawns `FilmSync` in `OnSessionJoined` before `StartFilm`, so `Instance` exists when needed. Guard anyway.

## 9. Out of Scope (this round)

- **Character speech/animation networking** (Oracle/Dobby) — same RPC pattern (`RPC_OracleSpeak`, animation triggers), added in #4 when characters exist in the scene.
- **Networked gesture/cooperation input** — only voice is networked now; gestures/cooperation join in #4 with interaction content. (`ActionCollector` still supports them locally.)
- **World-state late-join snapshot** — act/variant snapshot via `[Networked]`; world flags do not yet, so a headset joining mid-film won't back-fill prior weather until the next command.

## 10. Prerequisites Summary

- A `FilmSync` prefab (NetworkObject + `FilmSync` script) referenced by `FusionNetworkManager`.
- Co-located content under `SceneRoot` (already done in #1) so synced effects appear in the same physical place.
- AI reachable on the master (Claude key / Ollama) as in #2.
