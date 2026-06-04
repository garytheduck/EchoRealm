# EchoRealm — Gaze-Directed Object Manipulation (Tier A) — Design Spec

**Status:** Approved design — pending spec review
**Date:** 2026-06-04
**Author:** Samuel Dascalu (with Claude)

**Goal:** Let a viewer manipulate the specific scene object they are *looking at*, by voice addressed to **"Claude"** — **scale, move, rotate, reset** — with the change **shared and co-located** across both headsets.

**One-line:** "Claude, make this bush bigger" (while looking at it) → the bush scales up on every headset.

---

## 1. Scope

**In (Tier A):**
- Target = the object under the user's **eye gaze** (`EyeTrackingManager.CurrentTarget`).
- Wake word **"Claude"** distinguishes object-ops from world commands.
- Actions: **scale**, **move**, **rotate**, **reset**.
- **Claude infers magnitude** from natural language ("a bit", "a lot", "a little more") → clamped.
- Manipulation is **networked + co-located** (both headsets see it on the same object).
- **Comprehensive late-join sync** — a device joining at any time inherits the *full* modified world (global commands + per-object manipulations + future generated objects); nothing auto-resets.

**Out (later):**
- Spoken spatial references ("the cloud above me", "the bush on my right") → **Tier B**.
- Generating brand-new objects/effects → **deferred generative feature**.
- Manipulating characters / story objects (Oracle, Astronaut, HeartStone, portal, trials).

---

## 2. User-facing behavior

| Say (while looking at a prop) | Result (on both headsets) |
|---|---|
| "Claude, make **this** bigger / a lot bigger" | Scale up (more for "a lot") |
| "Claude, make it a bit smaller" | Scale down slightly |
| "Claude, move it a little to the right / up / closer" | Translate in that direction (your frame) |
| "Claude, turn this to the left" | Rotate (yaw) |
| "Claude, put it back / reset this" | Restore its original transform |
| (no "Claude") | Normal world command — unchanged |
| "Claude, …" but not looking at a manipulable prop | Gentle "Look at the object first" (log/Oracle), no-op |

---

## 3. Architecture & data flow

Gaze and "your right/up" are **egocentric** (relative to the speaker), so the **speaking device resolves them locally**, then sends a **frame-independent op** that the master broadcasts to everyone.

```
[Speaker device]
 1. Mic → VoiceCommandProcessor.ProcessSpeechText(text)
 2. Wake word "Claude" at phrase start?  ── no ──▶ existing world-command path (unchanged)
        │ yes
 3. target = EyeTrackingManager.CurrentTarget → walk up to a ManipulableObject
        │  (none? → "look at the object first", no-op)
 4. Claude parse: SendObjectOpAsync(phrase, objectContext) → AIObjectOp {action, direction, magnitude}
 5. Convert egocentric op → SceneRoot-LOCAL, frame-independent op:
        scale → factor;  move → SceneRoot-local delta;  rotate → yaw degrees;  reset
 6. FilmSync.SubmitObjectOp(objectId, op)
        ├─ master  → apply + RPC_ApplyObjectOp(All)
        └─ client  → RPC_SubmitObjectOp(StateAuthority) → master → RPC_ApplyObjectOp(All)
[Every device]
 7. RPC_ApplyObjectOp(objectId, op) → registry.Find(objectId).Apply(op)  (clamped, relative to SceneRoot)
```

Because the op is expressed in **SceneRoot-local, frame-independent terms** and applied to the **same object id** under each device's QR-aligned SceneRoot, all headsets stay co-located.

---

## 4. Components

**New**
- `Interaction/ManipulableObject.cs` — tags a prop as AI-manipulable; records the **original** local transform; exposes `Apply(AIObjectOp)` and `ResetTransform()`; clamps. Carries the friendly **type** (cloud/bush/rock/tree/flower/mushroom).
- `Interaction/ManipulableRegistry.cs` — at startup, **auto-discovers** manipulable props under SceneRoot (excludes the protected set), assigns each a **stable id = hierarchy path under SceneRoot**, infers **type from name**. Lookup by id; resolve a gazed GameObject → its `ManipulableObject`.
- `AI/AIObjectOp.cs` — `[Serializable]` result of the Claude parse: `action` (scale/move/rotate/reset), `direction` (left/right/up/down/closer/farther/cw/ccw), `magnitude` (small/medium/large or a number).

**Modified**
- `AI/VoiceCommandProcessor.cs` — wake-word "Claude" detection (at phrase start, with mishearing variants); the object-op branch (gaze → parse → submit).
- `AI/ClaudeBackend.cs` — `SendObjectOpAsync(phrase, objectContext)` + a focused prompt that returns the `AIObjectOp` JSON.
- `AI/IAIBackend.cs` — add `SendObjectOpAsync` (Ollama can throw NotImplemented / return null; Claude-only is fine).
- `Networking/FilmSync.cs` — `SubmitObjectOp` + `RPC_SubmitObjectOp` + `RPC_ApplyObjectOp`; (optional) fold manipulated objects into late-join state.

---

## 5. Object identity & registry

- **Manipulable set:** every prop under SceneRoot that has a renderer + collider, **except** the protected subtrees (Oracle, Astronaut, HeartStone, portal, trial objects) — the same list already used by `SceneManipulationReporter`.
- **Stable id:** the object's **hierarchy path under SceneRoot** (e.g. `Environment/Clouds/Cloud_02`). Deterministic and identical on both devices because they load the same scene. (No per-prop manual tagging.)
- **Type inference:** name → type map, e.g. `Cloud*`→cloud, `*Shrub*`→bush, `*Rock*`→rock, `*Tree*`→tree, `*Flower*`→flower, `*Mushroom*`→mushroom; default "object". Used only to give Claude a friendly label and to sanity-check (e.g. "this cloud" vs a bush).
- **Gaze resolution:** `EyeTrackingManager.CurrentTarget` may be a child collider → walk up parents until a `ManipulableObject` is found.

---

## 6. Action parsing (Claude)

`SendObjectOpAsync` sends: the spoken phrase, the gazed object's **type** and **current scale**, and the allowed actions. Claude returns **only** JSON:

```json
{ "action": "scale|move|rotate|reset",
  "direction": "bigger|smaller|left|right|up|down|closer|farther|none",
  "magnitude": "small|medium|large" }
```

**Magnitude → numbers (applied locally, clamped):**
- scale: small ×1.15 / medium ×1.4 / large ×1.8 (and inverses 0.87 / 0.71 / 0.55 for smaller). Clamp the **result** to **0.3×–3×** of the object's original scale.
- move: small 0.1 m / medium 0.25 m / large 0.5 m, along the requested **egocentric** direction. Clamp the **result** to within **1 m** of the original position.
- rotate: small 15° / medium 45° / large 90° yaw. No clamp (wraps).
- reset: ignore magnitude; restore original transform.

"a bit/a little" → small, "a lot/much" → large, otherwise medium. Claude does this mapping; we still clamp.

---

## 7. Networking

- New op message carries: `objectId` (string), `opType` (0 scale / 1 move / 2 rotate / 3 reset), and a compact payload (`float factor`, `Vector3 localDelta`, `float degrees`).
- The **speaker** converts the egocentric direction to a **SceneRoot-local** vector before submitting (so "my right" becomes a fixed SceneRoot-local direction that every device applies identically).
- Flow mirrors the existing command path: `SubmitObjectOp` → master applies + `RPC_ApplyObjectOp(All)`; clients route via `RPC_SubmitObjectOp(StateAuthority)`. `RpcSources.All → RpcTargets.StateAuthority` is the sanctioned shared-mode pattern (already used for speech/interaction/pocket).
- **Late join (REQUIRED, comprehensive):** the master holds the authoritative current world state and replays ALL of it to any device that joins at any time:
  - (a) the **global-command log** (extends the existing `WorldStateCsv` replay),
  - (b) the **current local transform** of every manipulated object,
  - (c) **generated-object descriptors** when that feature lands.
  Object state is synced as **absolute** local transforms, which are **idempotent** (re-applying on a peer that already has them is a no-op), so it can be broadcast safely; chunk if it exceeds Fusion's RPC size limit. The latecomer arrives into the identical co-located world — nothing missing.
- **No automatic resets:** act transitions, pocket/unpocket, and late-join all PRESERVE every world change and manipulation.

---

## 8. Safety & limits

- Each `ManipulableObject` records its **original** local scale/position/rotation at startup. This serves two roles: the clamp reference (Section 6) and the target of the **manual-only** "Claude, reset this" command.
- **Reset is manual-only — nothing auto-reverts.** Act transitions, pocket/unpocket, and late-join all preserve manipulations; the world only reverts when the user explicitly says "reset this" on the object they're looking at.
- Clamps (Section 6) prevent objects from vanishing, exploding, or flying away.
- Object-ops are **not** fed into the narrative AI's nurture/chaos profile (they're direct utility, not story-shaping) — but **may be logged** for the user study (see Future).

---

## 9. Risks & open questions

- **"Claude" ≈ "cloud" mishearing** (and you have clouds): match the wake word only at the **start** of the phrase, accept close variants (claude/claud/cloud/clyde/klaus) **only in that leading position**, and keep "cloud" usable as an object word later in the sentence.
- **Eye calibration:** "this" depends on each user being eye-calibrated on HL2; uncalibrated → gaze target is unreliable (Tier B's spatial phrases are the fallback).
- **Prop hand-grabs are local:** individual props are hand-grabbable (local, not networked); mixing a local hand-move with a networked AI op can desync a prop's base pose between headsets. Acceptable for Tier A; networking hand-grabs is future work.
- **Latency:** the extra Claude call adds ~1–2 s before the object changes. Acceptable; could add a tiny "thinking" cue.
- **Ambiguity:** none within Tier A — gaze names exactly one object.

---

## 10. Acceptance criteria (on-device test)

1. Master: look at a bush, "Claude, make this bigger" → it scales up; "smaller", "move it right", "turn it left", "reset this" all work.
2. The same change appears on the **client**, on the **same** object, co-located.
3. The same commands initiated **from the client** also affect both.
4. A **protected** object (Oracle/HeartStone/Astronaut/portal) does **not** respond.
5. "Claude, …" while **not** looking at a manipulable prop → gentle no-op message.
6. Clamps hold (can't shrink to nothing or fling a prop away); "reset this" returns it exactly.
7. **Late join:** with several props already manipulated AND weather changed, a client that connects *afterwards* comes up showing the same modified, co-located world — nothing reset.

---

## 11. Future (explicitly not Tier A)

- **Tier B:** spoken spatial references ("the cloud above me", "the bush on my right") via an egocentric scene inventory sent to Claude.
- **Generative:** create brand-new objects/lights/particles from description.
- Network individual hand-grabs of props.
- Log object-ops as a study metric (interaction richness, multimodal reference success rate).
