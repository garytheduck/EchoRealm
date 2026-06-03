# EchoRealm — Story & Scene Design (Milestone #4: Content)

**Date:** 2026-06-03
**Status:** Approved in brainstorm — pending implementation plan
**Builds on:** existing 4-act `FilmDirector`/`ActManager` flow, `FilmSync` master-authoritative networking, QR co-location, `CommandExecutor` vocabulary, `NarrativeDecisionEngine` + Claude backend.

---

## 1. Concept — "The Living Grove"

An **astronaut crash-lands in EchoRealm**, an ancient grove that is *alive* and answers to the human voice. Its guardian — the **Oracle** (a robed priest with a staff) — tells the two visitors that their words shape this world, and that the lost traveler can only return home once the grove's heart, the **Origin Echo**, awakens.

**How the visitors treat the world — nurturing it to bloom or stirring it to chaos — shapes the trial they face (Act 3) and how the story ends (Act 4).** The adaptation is driven by **Claude**, fresh on every playthrough.

## 2. Roles

- **The two viewers (HoloLens wearers):** voice-wielding *visitors*. The Oracle addresses them directly; their pooled spoken commands reshape the grove. They are co-located via QR so both see the same holograms in the same physical spot.
- **Oracle (priest):** the grove's guardian and the AI's voice in-scene. Appears near the Heart Stone, narrates each act, reacts in-character. Mood is color-coded via an aura light (calm=blue, excited=gold, mysterious=purple, warning=red).
- **Astronaut:** the lost traveler the viewers guide. Reacts to the world (Surprise to chaos), and in the finale **Floats through the portal home**. Absorbs the companion beats that the unused "Dobby" character had in code.
- **Dobby:** **dropped** (no asset). Its Act-1 reaction line and Act-4 farewell move to the Astronaut.

## 3. The Four Acts

| Act | Name | ~Time | Beats |
|---|---|---|---|
| 1 | Awakening | 40s | Grove materializes around both users. Astronaut stirs (`Idle→Surprise`), asks *"Where… where am I?"*. Oracle appears at the Heart Stone (aura purple), speaks the (reworded) intro lines: *"This grove lives, and it listens. Your voice shapes what grows here… Find the Origin Echo to send the traveler home."* |
| 2 | The World Responds | 2–3 min | Oracle (gold/excited): *"Speak what you wish to see."* Voice goes live. Users shape the grove (nurture vs chaos). Astronaut reacts to big changes. Oracle occasionally comments in-character. Each command from both headsets is tagged nurture/chaos and pooled on the master. Ends on command-count or time (`FilmDirector`). |
| 3 | The Grove's Trial | 1–2 min | AI picks a trial variant mirroring the world made. Cooperative win condition (see §5). Solved → Oracle (excited): *"Together, you've found the way. The heart opens."* |
| 4 | The Origin Echo | 40s | Portal blooms at the Heart Stone. Oracle delivers the AI-chosen transition line + an **AI-generated final monologue** (tone per ending variant). Astronaut walks to the portal and **Floats through** (`Float` clip) with a parting line. Fade to end screen. |

## 4. AI-Adaptive Narrative

### Inputs analyzed (pooled on master)
- **Voice commands** — each classified **nurture** (`grow_tree`, `grow_flowers`, `spawn_butterflies`, `spawn_fireflies`, `day`, gentle `rain`, `glow_objects`, `open_path`) vs **chaos** (`fire`, `earthquake`, `lightning`, `wind`/storm, `fog`, `night`, `close_path`, `shrink_scene`). Counter-commands (`stop_*`) are neutral. Both headsets feed the master via the existing `FilmSync.RPC_SubmitSpeech` path.
- **Eye tracking (NEW wiring)** — connect `EyeTrackingManager.OnDwellCompleted → ActionCollector.RecordGaze(name)` so gaze enters `PlayerBehaviorProfile`, and add `EyeTrackingManager.GetGazeSummary()` to the AI prompt.

### Decision points (Claude, via `NarrativeDecisionEngine.RequestDecisionAsync`)
- **End of Act 2 → Act-3 trial variant:** `verdant` (nurture-dominant) · `scorched` (chaos-dominant) · `twilight` (mixed).
- **End of Act 3 → Act-4 ending:** `triumphant` · `bittersweet` · `mysterious`, plus an AI-written closing monologue (`NarrativeManager.GenerateFinalMonologue`).
- Claude returns JSON `{chosen_variant, oracle_narration, mood, narrative_reason}` (existing `AINarrativeDecision`). The decision is broadcast to all peers via `FilmSync.DriveAct`.

### Eye-tracking callout (the chosen "flashy" feature)
Claude weaves the most-gazed object into the Oracle's narration — e.g. *"Your gaze never left the traveler — compassion guides you"* vs *"You stared into the flames."* Implemented by including the gaze summary in the decision and monologue prompts.

### Config changes
Re-theme the three existing `SceneVariant` entries for each transition (in `NarrativeDecisionEngine` / Inspector) from interaction-style descriptions to **nurture/chaos** descriptions, so Claude chooses on world-tone. Variant **count is unchanged** (still 3 + 3), so no extra obstacles to build.

### Fallback
If Claude is unreachable, `NarrativeDecisionEngine.BuildFallback` returns the first variant with its `fallbackOracleLine`/`fallbackMood` (existing behavior). The film never stalls.

## 5. Cooperative Trial (Act 3)

- **Win condition (all variants, shared):** both players act on the **Heart Stone** (central menhir) within `cooperationWindowSeconds` (~3s), repeated `cooperationGoal` (3) times → detected by `CooperationDetector` (Pattern 1: same-object simultaneous interaction). On success, `ActManager.RunAct3` completes.
- **Networking requirement (NEW):** route each player's interaction report to the **master's** `CooperationDetector` + `ActionCollector` (today gestures are local-only). Mirror the voice path: a `FilmSync` RPC `[All→StateAuthority]` carrying `(playerIndex, objectId, interactionType)`.
- **Visual variants (same win condition):**
  - **Verdant** — Heart Stone wrapped in blooming vines/growth the two part together.
  - **Scorched** — Heart Stone ringed by fire/storm the two calm together.
  - **Twilight** — glyphs on the stones that reveal only when both focus together.

## 6. Scene Composition (under the QR-anchored `SceneRoot`)

- **Heart Stone** — `PT_Menhir_Rock_02` at center; the portal site (`ActManager.portalEffect` parented here).
- **Oracle** — `PT_Priest_StPatrick` beside the Heart Stone (+ `OracleController`, dialogue TMP, aura light, optional particle aura). Staff already on model.
- **Astronaut** — `Stylized Astronaut` near center (+ `AstronautController` + new Animator Controller).
- **Forest ring** — Supercyan **Mobile** prefabs (fir/leaf trees, stones, grass, mushrooms) + Polytope environment (trees, rocks, menhirs, `PT_Poppy_02` flowers, shrubs) arranged around an **open central play space** for the two users.
- **Effect controllers** — existing `WeatherController`, `EnvironmentController`, `SkyboxController`, `CameraEffects`, with the `ParticleAdditive` material assigned.
- **Trial-variant objects** — three objects (verdant/scorched/twilight) parented at the Heart Stone; `ActManager` toggles the AI-chosen one.
- **Props** — scrolls / open Bible (Polytope) near the Oracle for flavor.
- **UI** — boot-status label, Oracle + Astronaut dialogue TMP, end screen.
- **Skybox** — day (procedural) / night via `SkyboxController` (bonus `Skybox.mat` available).

## 7. Build Scope

**Reused as-is:** 4-act `FilmDirector`/`ActManager`; `FilmSync` networking; QR co-location; `CommandExecutor` vocabulary; `NarrativeDecisionEngine`; `AIManager`/`ClaudeBackend`; particle controllers.

**New / adapted code:**
1. `OracleController` → humanoid priest mode (drive Animator + aura light + dialogue + optional particles instead of glowing sphere).
2. Eye-tracking → AI hook (`OnDwellCompleted → RecordGaze`; gaze summary into prompts).
3. Gesture/cooperation **networking** (interaction-report RPC to master).
4. Nurture/chaos command classifier feeding the behavior profile.
5. Re-themed `NarrativeDecisionEngine` variant configs.
6. Astronaut **Animator Controller** (Generic; map triggers → clips: `Walk→Walk`, `Jump→Jump_start`, `EnterPortal→Float`, `LookAround→Suprise`, `Idle→Idle`).
7. Scene-assembly + character/prop wiring (editor scripts where possible).

**Assets:** imported ✓ (astronaut, priest, forest, Polytope env/props). **Still needed:** ~2–3 free **Mixamo** clips for the priest (Idle + Talking/Gesture, optional Blessing) — priest is **Humanoid**, so they retarget.

## 8. Dependencies & Open Items
- Mixamo clips for the priest (Humanoid retarget).
- Eye calibration + `GazeInput` capability on device for real eye tracking (Editor uses head-forward raycast).
- Gesture/cooperation networking is a prerequisite for the Act-3 cooperative win across two real devices.
- Do **not** import the Polytope **URP** `.unitypackage`s (project is Built-in RP).

## 9. Success Criteria
- A complete ~5–7 min experience runs across two co-located HoloLens devices: shared acts, shared world, one AI-driven outcome.
- The AI's adaptation is **legible**: an observer can tell that a nurturing session produced a verdant trial + warm ending, and a chaotic session produced a scorched trial + darker ending — and the Oracle references what the users *watched*.
- No startup particles / no crashes; particles only on command (regression already fixed).
- Session data (variant, reasoning, behavior) captured by `SessionLogger` for the thesis evaluation.

## 10. Out of Scope (YAGNI — deferred, easy to add later)
- Personalized ending that names specific commands; two-user harmony/discord ending; unique "Echo" epitaph artifact; live Oracle improvisation in Act 2; a Dobby character.
