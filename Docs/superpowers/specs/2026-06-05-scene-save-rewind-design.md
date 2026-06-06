# EchoRealm — Scene Save & Rewind: Design Spec

- **Date:** 2026-06-05
- **Status:** Approved design (pre-implementation)
- **Author:** Samuel Dascalu (with Claude)
- **Scope:** Add the ability to (a) **rewind** the live scene to its state at an earlier moment, and (b) **save** a completed scene and later **load it on HoloLens as a frozen, view-only diorama you can step/rewind through**.

---

## 1. Context & motivation

EchoRealm is a live, AI-branched, multi-HoloLens MR film: acts advance on voice-command counts / timers, Acts 3–4 are chosen at runtime by the AI, the Oracle narrates via TTS, and two headsets stay in sync through a master client (Photon Fusion 2 **Shared Mode**).

Prof. Vatavu asked two questions:
1. How hard is it to **save a completed scene and replay it**?
2. Can we **rewind the scene** (e.g. 20 s / 1 min)?

Key realization from the codebase: the **late-join system already reconstructs the full visual scene** from current act+variant, a cumulative world-command log, and a per-object transform dictionary — all designed to be *idempotently replayed* (`FilmSync.Spawned`/`ApplyWorldStateSnapshot`/`RPC_SetObjectState`). That replay layer is exactly what both features need. Object ops are **relative** (`ApplyScale` multiplies, `ApplyMove` adds) and world commands are **idempotent toggles**, so an *ordered replay from a baseline* reconstructs the exact state at any point in time.

## 2. Goals / non-goals

**Goals**
- Rewind the **state** of the live scene to an earlier moment, preserving every AI decision made up to that moment. Networked: both headsets jump together.
- Record **everything that happened**: every world command, every object modification, every act transition + AI variant, and every AI utterance (what the Oracle said), all timestamped.
- At scene end, decide whether to **save** the whole recorded timeline to disk.
- Load a saved scene on a single HoloLens as a **view-only** experience: see the final state, and **step/scrub/rewind** through how it got there, with the AI's words shown at each step.

**Non-goals**
- No frame-accurate reproduction of **animations or audio**. We reconstruct scene *state*, not the live performance. Character animation mid-pose, TTS audio, and particle timing are not rewound.
- No video recording.
- No mid-scene saving (save is a single end-of-scene decision).
- No shared/co-located playback of saved scenes (offline playback is solo). Shared playback is a possible future extension.

## 3. Requirements (decisions made)

| # | Decision |
|---|---|
| R1 | **Load behavior:** saved scene loads to the final state, and the user can **step through history** (timeline scrub) with the AI's line shown at each step. |
| R2 | **Networking scope:** **Rewind is live & networked** (master jumps the world back and broadcasts; reuses master/RPC). **Saved-scene playback is solo & offline** (no Photon session; replay runs locally from the file). |
| R3 | **Controls:** **Hand-menu buttons** (MRTK), not voice — the voice channel feeds the AI and a spoken "rewind" would be misinterpreted. |
| R4 | **Recording representation:** **Event timeline + replay** (Approach A). One timestamped, ordered event list is the single source of truth; state at any T = reset-to-baseline + replay events ≤ T. |
| R5 | **Rewind semantics:** rewinding truncates the timeline to T; the live session continues from there (checkpoint semantics — the discarded "future" is gone). |
| R6 | **Save trigger:** decided **once, at scene end** (Act 4 completes or `EndFilm`) via a `Save / Discard` prompt. Recording itself runs throughout; only persistence is end-of-scene. |
| R7 | **Rewind available in both contexts:** live (during the actual scene) and during saved-scene playback (offline player mirrors the live Rewind 20s / 1m controls plus a scrubber). |
| R8 | **Isolation:** the whole subsystem is additive and self-contained. If removed/disabled, the film behaves byte-for-byte as today. |
| R9 | **AI-memory rollback:** rewinding also rolls the AI's behavioral memory back to T — the cumulative `PlayerBehaviorProfile` (in `ActionCollector`) + `NarrativeManager`'s command logs — so rewound commands do NOT influence future AI variant decisions or the final monologue. Commands given after the rewind count normally. |
| R10 | **Hand-grab capture:** direct hand manipulations of props (MRTK `ObjectManipulator`), not just voice "Claude…" ops, are recorded as absolute `ObjectState` events, so a hand-moved/resized prop saves and rewinds to its state at T. Requires `ManipulableRegistry` to be live (it provides the props + stable ids). |

## 4. Architecture overview — Approach A (event timeline + replay)

The recording is a **baseline + an ordered event list**. A single pure engine, `ApplyStateAt(timeline, T)`, resets to baseline and re-applies every event with timestamp ≤ T, in order. Both features call it:

- **Live rewind to 1 min ago** = `ApplyStateAt(tl, now − 60)` on the master, then broadcast the resulting absolute state.
- **Step / rewind in saved playback** = `ApplyStateAt(tl, events[k].t)` locally.
- **Show final saved state** = `ApplyStateAt(tl, ∞)`.

At EchoRealm's scale (dozens–low-hundreds of events per session) replay-from-zero is instant, so no keyframes are needed. If determinism issues ever arise, periodic absolute keyframes can be added later (graceful upgrade to a hybrid) with no rewrite.

## 5. Data model

```csharp
[Serializable] public class SceneTimeline {
    public TimelineMeta meta;          // sessionId, savedAtIso, durationSec, finalAct, sceneVersion, eventCount
    public List<TimelineEvent> events; // everything that happened, in order, with timestamps
}

[Serializable] public class TimelineEvent {
    public float t;          // seconds since film start
    public EventKind kind;   // WorldCommand | ObjectOp | ActTransition | AiUtterance
    public bool transient;   // momentary FX (earthquake/lightning/anim triggers) — skipped on seek
    // payload — only the fields relevant to `kind` are populated:
    public string id;        // object id | command name | speaker
    public string text;      // AI utterance | variant | decision/mood
    public int i;            // op type | act number
    public float f;          // scale factor | yaw degrees
    public Vector3 v;        // move delta
}

public enum EventKind { WorldCommand, ObjectOp, ActTransition, AiUtterance }
```

- **Flat tagged struct on purpose:** Unity's built-in `JsonUtility` does not serialize polymorphic subclasses; a flat record round-trips trivially and avoids adding a Newtonsoft dependency.
- **Baseline is implicit = the scene as authored.** Every prop's original transform is already captured in `ManipulableObject.Awake` (`_origScale/_origPos/_origRot`); world flags default in `CommandExecutor`. Live rewind calls a new `ResetToBaseline()` before replay; offline playback gets baseline for free from a fresh scene load. `meta.sceneVersion` lets a stale save warn instead of misbehaving.

## 6. Components

**New:**
- `TimelineRecorder` — master-side observer. Subscribes to source events and appends `TimelineEvent`s. Never calls back into the film. Also snapshots the AI's memory (ActionCollector + NarrativeManager) at each event and exposes `RestoreAiMemoryAt(t)` for rewind.
- `TimelineReplayer` — pure engine: `ApplyStateAt(timeline, upTo, seeking)`. Operates via `CommandExecutor.ExecuteCommand`, `ManipulableObject` ops, and `ActManager.ApplyActState`. Core logic is also exposed over an abstract state model for headless unit testing.
- `SceneArchive` — `Save()` / `List()` / `Load(name)`; JSON in `Application.persistentDataPath/EchoRealmSaves/`. Also writes `SessionLogger.ExportLog()` text alongside as `.txt`.
- `ReplaySessionController` — offline view-only driver: loads an archive, drives a scrub index, enforces view-only, hosts the timeline UI + AI transcript panel.
- `RewindMenu` (live) — MRTK hand-menu: `Rewind 20s · Rewind 1m`.
- `ReplayUI` (offline) — save-picker list, timeline scrubber + prev/next-change + play/pause + `Rewind 20s / 1m`, AI transcript panel, read-only banner.

**Touched (additive only):**
- `OracleController.Speak/SpeakDramatic` → new thin event `OnSpoke(text, mood)` (one `Invoke`, no behavior change).
- `FilmSync.RPC_ApplyObjectOp` → new thin event `OnObjectOpApplied(...)`; new `RequestRewind(seconds)` + `RPC_RewindApply(...)`.
- `ActManager` → new thin event `OnActStarted(act, variant)`; new `ApplyActState(act, variant)` (quiet visual setup; `StartAct` untouched).
- `FilmDirector.EndFilm()` → one-line trigger of the end-of-scene Save/Discard prompt.
- `EchoRealmBootstrapper` → one early branch: `if (replayMode) { startReplay(); return; }`.
- `ManipulableRegistry` → new additive getter exposing the registered props (read-only enumeration) so `ResetToBaseline` can reset them all; existing `FindById`/`Resolve` untouched.
- `ActionCollector` / `NarrativeManager` / `PlayerBehaviorProfile` → additive capture/restore methods so the AI's accumulated memory can be snapshotted and rolled back on rewind (R9). Existing record/query paths untouched.
- `FilmDirector` → additive `RewindToAct(int)` to re-arm the act flow when a rewind crosses an act boundary.

## 7. Capture map

| What's recorded | Source choke point | Hook |
|---|---|---|
| World command (`rain`/`night`/`grow_tree`…) | `CommandExecutor.ExecuteCommand` | **Existing** `OnCommandExecuted`. Subscribe only. |
| What the AI *decided* (commands + mood) | `VoiceCommandProcessor` | **Existing** `OnAIResponseReceived`. Subscribe only. |
| Voice command text (what the user said) | `VoiceCommandProcessor` | **Existing** `OnSpeechRecognized`. Subscribe only. |
| What the AI *said* (Oracle lines) | `OracleController.Speak/SpeakDramatic` | **New** thin `OnSpoke(text, mood)`. |
| Object op (scale/move/rotate/reset) | `FilmSync.RPC_ApplyObjectOp` | **New** thin `OnObjectOpApplied(...)`. |
| Act transition + AI variant | `ActManager.StartAct` | **New** thin `OnActStarted(act, variant)`. |

Net edits to existing files: **three one-line event raises + one new method (`ApplyActState`) + one bootstrap branch + one `EndFilm` call.** Each new event no-ops without a subscriber.

## 8. Replay engine semantics

`ApplyStateAt(timeline, upTo, seeking)`:
1. `ResetToBaseline()` — reset every registered `ManipulableObject` to its original transform; clear `CommandExecutor` world flags to defaults; deactivate environment groups (forest/flowers/obstacles/portal). (Offline: a fresh scene load already is baseline, but the call is idempotent.)
2. For each event with `t ≤ upTo`, in order:
   - `WorldCommand` → `CommandExecutor.ExecuteCommand(name)` (skip if `transient && seeking`).
   - `ObjectOp` → `ManipulableObject.ApplyScale/ApplyMove/ApplyYaw/ResetTransform`.
   - `ActTransition` → `ActManager.ApplyActState(act, variant)` (quiet — no coroutine/TTS).
   - `AiUtterance` → no scene effect; surfaced by the offline transcript panel.
3. Result is the exact scene state at `upTo`.

Determinism holds because op clamps are relative to per-object originals captured at `Awake` (identical every run, same scene).

## 9. Flows

### 9.1 Live rewind (networked, master-authoritative)
1. User taps `Rewind 20s/1m` on the hand-menu. On master → act directly; on client → RPC to master.
2. Master computes `T = max(0, filmTime − seconds)`; raises a brief internal **"rewinding" guard** (NOT `WorldPocket`, to avoid the "any speech unpockets" behavior).
3. Master truncates its timeline to ≤ T, then `ResetToBaseline()` + replay ≤ T → recomputes authoritative `_objStates`, world flags, current act.
   - **Restores the AI's behavioral memory to T (R9):** `TimelineRecorder.RestoreAiMemoryAt(T)` rolls back `ActionCollector.Profile` (voice/nurture/chaos/etc. counts + recent actions) and `NarrativeManager`'s command logs to the snapshot taken at T, and `FilmDirector.RewindToAct` re-arms the act flow. Why this is necessary: the AI's variant choice reads this cumulative profile, NOT the visual timeline, and `ResetForNewAct` never clears the cumulative profile — so truncating the timeline alone would leave rewound commands influencing the next AI decision.
4. Master broadcasts the resulting **absolute** state: `RPC_SetObjectState` per prop (idempotent), world-command CSV path for environment, quiet `ApplyActState` for the act — tied together by one new `RPC_RewindApply`.
5. Guard released. Live film continues from T; new events append; the discarded tail is gone (R5).

Replay-to-T is synchronous and instant → a sub-frame jump, not a visible scrub.

### 9.2 Save (single end-of-scene decision)
- Recording runs throughout the scene.
- On scene end (`EndFilm`, reached by Act 4 completion or otherwise), show `Save this scene? [Save] [Discard]` (optional name).
- On **Save**: `SceneArchive.Save()` writes `{meta, events}` JSON to `…/EchoRealmSaves/scene_<sessionId>_act<N>.json` plus the session-log `.txt`. Each save is its own file → versions coexist.
- Save is master-only (it owns the authoritative timeline); the file lands on the master's device.
- **Trade-off:** no mid-scene save → an app crash mid-session loses that scene (acceptable for demos; optional periodic safety-flush is a future option).

### 9.3 Offline step-through (solo, view-only)
- Startup choice: **Start Live Film** / **View Saved Scene** (the replay branch). In replay mode, QR → Fusion → voice → film never start, so "view-only" is automatic.
- **View Saved Scene** → save-picker list (date · duration · final act · #changes, newest first) → load → fresh scene is baseline → `ApplyStateAt(end)` shows the final diorama.
- World-anchored timeline panel: slider over N changes, prev/next-change, play/pause (auto-advance at readable pace), `Rewind 20s / 1m`, time/step readout, **AI transcript panel** (what the AI said/decided up to the current step). Every scrub = `ApplyStateAt(events[k].t)`.
- `ReplaySessionController` disables prop `ObjectManipulator`s; a persistent **"VIEWING SAVED SCENE — read only"** banner is shown.

## 10. UI surfaces
- **Live:** hand-anchored MRTK panel — `Rewind 20s · Rewind 1m` (reuses the `ResumeButton`/`WorldPocket` UI pattern).
- **End of scene:** `Save / Discard` prompt (optional name).
- **Offline:** startup choice → save-picker → timeline scrubber + transcript + read-only banner.

## 11. Edge cases / robustness
- **Rewind past start** → clamp `T ≥ 0`.
- **AI response in flight during rewind** → the guard drops command application for its window; a late reply can't corrupt the T-state.
- **Transient events on seek** → skipped (only persistent state reconstructed).
- **Object id missing on replay** (scene changed) → skip + warn; `sceneVersion` surfaces an "older save" note.
- **Corrupt/empty/incompatible file** → try/catch → "couldn't load" in picker, no crash.
- **Empty timeline** → saves; playback shows baseline diorama.
- **Long session memory** → events are tiny; soft-warn past a large cap.

## 12. Testing strategy
The engine is pure/deterministic → most tests run **without a headset**:
- **EditMode unit tests** on `ApplyStateAt` over an abstract state model (`Dictionary<id,(scale,pos,rot)>` + world-flag set):
  - replay-to-T == hand-computed expected at several T;
  - replay-to-end == live final state;
  - rewind-to-T-then-continue == fresh replay of the resulting timeline (checkpoint consistency, R5);
  - idempotent re-apply of absolute object state.
- **SceneArchive round-trip** (serialize → deserialize → deep-equal).
- **Recorder capture test** — fire source events, assert correct `TimelineEvent`s in order.
- **PlayMode smoke test** — tiny synthetic timeline → scrub → assert objects match.
- **Manual on-device** — 2-headset live-rewind sync; end-of-scene Save/Discard; offline load + scrub + transcript + read-only lock.
- **Regression** — with the subsystem disabled, existing flows are untouched *by construction* (new events no-op without subscribers).

## 13. Isolation / non-regression guarantees (R8)
- `TimelineRecorder` is a pure observer; it never calls into the film.
- The three new events do nothing when unsubscribed → removing the recorder leaves the film byte-for-byte unchanged.
- New methods (`ApplyActState`, `RequestRewind`, `SceneArchive`) are additive; existing methods/flows are not modified in behavior.
- Replay is a separate boot branch — the live path (QR/Fusion/voice/film) is never altered, only *not started* in replay mode.
- Subsystem sits behind one bootstrap toggle; off = zero overhead.

## 14. Open questions / future extensions
- **Crash-safety:** optional periodic timeline flush so a crash mid-session doesn't lose the scene.
- **Shared saved-scene playback:** both headsets co-load and scrub a saved scene in sync (networked scrub state + QR re-co-location).
- **Keyframes:** add periodic absolute snapshots if any op ever proves non-replayable, upgrading Approach A → hybrid.
- **Scene-version migration:** strategy if the prop hierarchy changes between save and load beyond a friendly warning.

## 15. File inventory (for the implementation plan)
**New scripts** (under `EchoRealm/Assets/Scripts/`): `Film/TimelineRecorder.cs`, `Film/TimelineReplayer.cs`, `Film/SceneArchive.cs`, `Film/ReplaySessionController.cs`, `Film/SceneTimeline.cs` (data types), `Interaction/RewindMenu.cs`, `Interaction/ReplayUI.cs`, `AI/AiMemoryState.cs`.
**New tests:** EditMode assembly for `TimelineReplayer` + `SceneArchive` round-trip + recorder capture.
**Touched (additive):** `Characters/OracleController.cs`, `Networking/FilmSync.cs`, `Film/ActManager.cs`, `Film/FilmDirector.cs`, `Film/EchoRealmBootstrapper.cs`, `Interaction/ManipulableRegistry.cs` (expose an enumeration of registered props for `ResetToBaseline`), and for AI-memory rollback (R9): `AI/PlayerBehaviorProfile.cs`, `AI/ActionCollector.cs`, `AI/NarrativeManager.cs`.
