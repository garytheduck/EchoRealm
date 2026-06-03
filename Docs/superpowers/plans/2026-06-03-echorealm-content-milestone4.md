# EchoRealm Milestone #4 (Content) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the verified EchoRealm engine into the playable "Living Grove" film — priest-as-Oracle, the Astronaut, a forest world, nurture/chaos AI adaptation with an eye-tracking callout, and a networked cooperative Act-3 trial.

**Architecture:** Build content + wiring on top of the existing 4-act `FilmDirector`/`ActManager`, master-authoritative `FilmSync` networking, and `NarrativeDecisionEngine` + Claude backend. New code is small and additive; most effort is scene assembly and a few wiring hooks.

**Tech Stack:** Unity 2022.3 LTS, Built-In RP, HoloLens 2, MRTK3, Photon Fusion 2 (Shared Mode), C# 9.

**Step tags:** **[CODE]** = code edit (agent) · **[EDITOR SCRIPT]** = C# editor tool the agent writes, the user runs from a Unity menu · **[MANUAL]** = user action in the Unity Editor.

**Testing note (Unity reality):** Pure-logic code is verified with an in-Editor self-test (`[ContextMenu]`); scene/animation/networking/AI behavior is verified with Play-Mode checks + expected console log lines + a 2-device run. This adapts TDD to an MR project where most surface area is Editor-side and not unit-testable.

**Commits:** every commit message ends with the project trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Map

| File | New/Modify | Responsibility |
|---|---|---|
| `EchoRealm/Assets/Scripts/AI/CommandSentiment.cs` | Create | Classify a command as Nurture / Chaos / Neutral (pure logic) |
| `EchoRealm/Assets/Scripts/AI/PlayerBehaviorProfile.cs` | Modify | Add nurture/chaos counts + `WorldTone` to the AI summary |
| `EchoRealm/Assets/Scripts/AI/ActionCollector.cs` | Modify | `RecordWorldChange(command)` tally hook |
| `EchoRealm/Assets/Scripts/Networking/FilmSync.cs` | Modify | Tally world-tone on master; networked interaction RPC |
| `EchoRealm/Assets/Scripts/Interaction/CooperationDetector.cs` | Modify | Compare by object-id string (network-safe) |
| `EchoRealm/Assets/Scripts/Interaction/GestureManager.cs` | Modify | Route interactions through `FilmSync` to the master |
| `EchoRealm/Assets/Scripts/Interaction/EyeTrackingManager.cs` | Modify | Feed dwell → `ActionCollector.RecordGaze` |
| `EchoRealm/Assets/Scripts/AI/NarrativeDecisionEngine.cs` | Modify | Re-theme variants (verdant/scorched/twilight); add gaze to prompt |
| `EchoRealm/Assets/Scripts/AI/NarrativeManager.cs` | Modify | Add gaze + world-tone to the final-monologue prompt |
| `EchoRealm/Assets/Scripts/Characters/OracleController.cs` | Modify | Optional Animator (talk/idle) so a humanoid priest can be the Oracle |
| `EchoRealm/Assets/Scripts/Film/ActManager.cs` | Modify | Grove-themed lines; Astronaut absorbs Dobby beats; new variant keys |
| `EchoRealm/Assets/Scripts/Editor/AstronautAnimatorBuilder.cs` | Create | One-click Animator Controller from the Astronaut FBX clips |

---

## Phase 0 — Asset prep

### Task 0.1: Priest animation clips (Mixamo)

**[MANUAL]**

- [ ] **Step 1: Download clips.** On mixamo.com, with any character, download as **FBX for Unity**, *Without Skin*, these animations: **Idle** (e.g. "Breathing Idle"), **Talking** (e.g. "Talking" / "Standing Arguing"), and optionally **Praying**/"Blessing". 
- [ ] **Step 2: Import.** Put the FBXs in `EchoRealm/Assets/Polytope Studio/Lowpoly_Characters/Animations/Mixamo/`.
- [ ] **Step 3: Set rig.** Select each clip FBX → Inspector → **Rig** → Animation Type = **Humanoid** → Avatar Definition = *Create From This Model* → **Apply**.
- [ ] **Step 4: Loop.** Select the Idle (and Talking if it should loop) → **Animation** tab → tick **Loop Time** → **Apply**.
- [ ] **Step 5: Verify.** Each clip shows a green Humanoid avatar dot and previews on the dummy in the Inspector preview pane.

---

## Phase 1 — Astronaut

### Task 1.1: Animator Controller builder (editor tool)

**Files:**
- Create: `EchoRealm/Assets/Scripts/Editor/AstronautAnimatorBuilder.cs`

- [ ] **Step 1: [EDITOR SCRIPT] Write the builder.**

```csharp
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Linq;

namespace EchoRealm.EditorTools
{
    /// <summary>
    /// Builds an Animator Controller for the Generic Stylized Astronaut, mapping the
    /// trigger names AstronautController uses onto the FBX's bundled clips.
    /// Menu: EchoRealm ▸ Build Astronaut Animator
    /// </summary>
    public static class AstronautAnimatorBuilder
    {
        private const string FbxPath = "Assets/Stylized_Astronaut/Character/Astronaut.fbx";
        private const string OutPath = "Assets/EchoRealm/Animators/AstronautAnimator.controller";

        // trigger name -> clip name inside the FBX
        private static readonly (string trigger, string clip)[] Map =
        {
            ("Walk",        "Walk"),
            ("Jump",        "Jump_start"),
            ("LookAround",  "Suprise"),   // FBX spelling is "Suprise"
            ("Wave",        "Suprise"),   // no wave clip; reuse Surprise
            ("EnterPortal", "Float"),
        };
        private const string IdleClip = "Idle";

        [MenuItem("EchoRealm/Build Astronaut Animator")]
        public static void Build()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
                .OfType<AnimationClip>()
                .GroupBy(c => c.name).Select(g => g.First())
                .ToDictionary(c => c.name, c => c);

            if (clips.Count == 0)
            {
                Debug.LogError($"[AnimatorBuilder] No clips found at {FbxPath}. Check the path/import.");
                return;
            }

            System.IO.Directory.CreateDirectory("Assets/EchoRealm/Animators");
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(OutPath);
            var sm = ctrl.layers[0].stateMachine;

            // Idle = default state
            AnimatorState idle = sm.AddState("Idle");
            if (clips.TryGetValue(IdleClip, out var idleClip)) idle.motion = idleClip;
            sm.defaultState = idle;

            foreach (var (trigger, clipName) in Map)
            {
                ctrl.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                if (!clips.TryGetValue(clipName, out var clip))
                {
                    Debug.LogWarning($"[AnimatorBuilder] Clip '{clipName}' not found for trigger '{trigger}'.");
                    continue;
                }
                var state = sm.AddState(trigger);
                state.motion = clip;

                var toState = sm.AddAnyStateTransition(state);
                toState.AddCondition(AnimatorConditionMode.If, 0, trigger);
                toState.duration = 0.15f;
                toState.canTransitionToSelf = false;

                var back = state.AddTransition(idle);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.2f;
            }

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimatorBuilder] Built {OutPath} with {Map.Length} triggers + Idle.");
            Selection.activeObject = ctrl;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles.** Return to Unity, wait for compile. Expected: no console errors; menu **EchoRealm ▸ Build Astronaut Animator** exists.
- [ ] **Step 3: Run it.** Click **EchoRealm ▸ Build Astronaut Animator**. Expected log: `[AnimatorBuilder] Built Assets/EchoRealm/Animators/AstronautAnimator.controller with 5 triggers + Idle.` and the controller is selected in the Project window.
- [ ] **Step 4: [MANUAL] Sanity-check the controller.** Double-click the controller → the Animator window shows states **Idle (orange/default), Walk, Jump, LookAround, Wave, EnterPortal**, each with a clip assigned.
- [ ] **Step 5: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Editor/AstronautAnimatorBuilder.cs EchoRealm/Assets/EchoRealm/Animators/AstronautAnimator.controller
git commit -m "feat(echorealm): editor tool to build Astronaut animator from FBX clips"
```

### Task 1.2: Place & wire the Astronaut

**[MANUAL]**

- [ ] **Step 1: Place.** Drag `Assets/Stylized_Astronaut/Stylized Astronaut.prefab` as a child of **SceneRoot**. Rename it `Astronaut`. Set its Transform localPosition `(0, 0, 1.2)`, localScale check (FBX imported at 0.001 scale — if it's tiny, set localScale `(1,1,1)` on the prefab root and confirm it's roughly human height ~1.7 m; scale up if needed).
- [ ] **Step 2: Animator.** On `Astronaut`, set the **Animator → Controller** to `AstronautAnimator.controller` (from Task 1.1). Avatar should already be the Astronaut's.
- [ ] **Step 3: Controller component.** Add Component → **AstronautController**. Leave `portalTarget` empty for now (set in Phase 3).
- [ ] **Step 4: Verify.** Enter Play Mode. In the Console run nothing yet — just confirm no errors and the Astronaut stands in **Idle**. Exit Play Mode.

---

## Phase 2 — Oracle (the priest)

### Task 2.1: Make `OracleController` humanoid-capable

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Characters/OracleController.cs`

- [ ] **Step 1: [CODE] Add an Animator field.** In the `[Header("Visual References")]` block, after `oracleLight`, add:

```csharp
        [Header("Humanoid Oracle (optional — for a character model like the priest)")]
        [Tooltip("If set, the Oracle is a humanoid (priest). Talk/Idle triggers drive it while speaking.")]
        [SerializeField] private Animator bodyAnimator;
        [SerializeField] private string talkTrigger = "Talk";
        [SerializeField] private string idleTrigger = "Idle";
```

- [ ] **Step 2: [CODE] Trigger talk on speak.** In `ShowDialogue(string text)`, after `IsSpeaking = true;`, add:

```csharp
            if (bodyAnimator != null) bodyAnimator.SetTrigger(talkTrigger);
```

- [ ] **Step 3: [CODE] Trigger idle on hide.** In `HideDialogue()`, after `IsSpeaking = false;`, add:

```csharp
            if (bodyAnimator != null) bodyAnimator.SetTrigger(idleTrigger);
```

- [ ] **Step 4: [CODE] Talk during dramatic speech.** In `DramaticSpeechCoroutine`, after `IsSpeaking = true;`, add:

```csharp
            if (bodyAnimator != null) bodyAnimator.SetTrigger(talkTrigger);
```

- [ ] **Step 5: Verify compile.** Back in Unity: no errors. (The sphere path stays fully working and null-safe — `bodyAnimator` is optional.)
- [ ] **Step 6: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Characters/OracleController.cs
git commit -m "feat(echorealm): OracleController can drive a humanoid (priest) via Talk/Idle triggers"
```

### Task 2.2: Build the priest Oracle in the scene

**[MANUAL]**

- [ ] **Step 1: Priest Animator.** Create an Animator Controller `Assets/EchoRealm/Animators/OracleAnimator.controller`. Open it: add an **Idle** state (default) using the Mixamo Idle clip, and a **Talk** state using the Mixamo Talking clip. Add **Trigger** parameters `Talk` and `Idle`. Add `Any State → Talk` (condition `Talk`) and `Talk → Idle` (hasExitTime 0.9). (This mirrors the astronaut pattern; do it by hand or copy the builder.)
- [ ] **Step 2: Place priest.** Drag `Assets/Polytope Studio/Lowpoly_Characters/Prefabs/PT_Priest_StPatrick.prefab` under **SceneRoot**. Rename `Oracle`. Position localPosition `(0.8, 0, 0.6)`, rotate to face the play space. Confirm human height.
- [ ] **Step 3: Animator.** On `Oracle`, set Animator → Controller = `OracleAnimator.controller`.
- [ ] **Step 4: Dialogue text.** Right-click `Oracle` → 3D Object → **Text - TextMeshPro** (accept TMP import if prompted). Name it `OracleDialogue`, position it ~0.5 m above the priest's head, scale ~0.02, center-aligned, set a readable color. 
- [ ] **Step 5: Aura light.** Right-click `Oracle` → **Light → Point Light**, name `OracleAura`, range ~3, position at the priest's chest.
- [ ] **Step 6: Optional particle aura.** Add a child Particle System using the `ParticleAdditive` material (soft, slow, low rate) for a mystical glow.
- [ ] **Step 7: OracleController.** Add Component → **OracleController** on `Oracle`. Assign: `bodyAnimator` = the priest's Animator, `dialogueText` = `OracleDialogue`, `oracleLight` = `OracleAura`, `orbitalParticles` = the aura PS (or leave empty), leave `sphereRenderer` **empty**.
- [ ] **Step 8: Verify.** Play Mode → in the Console, temporarily call from a quick test or wait for Act 1 (Phase 6). For now confirm: no NullReference from OracleController, priest stands in Idle. Exit.

---

## Phase 3 — The grove (scene assembly)

### Task 3.1: Heart Stone + portal + trial slots

**[MANUAL]**

- [ ] **Step 1: Heart Stone.** Drag `Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/PT_Menhir_Rock_02.prefab` under **SceneRoot**. Rename `HeartStone`. localPosition `(0, 0, 0)`, scale to ~1.5 m tall.
- [ ] **Step 2: Portal.** Create an empty child of `HeartStone` named `PortalEffect` at localPosition `(0, 1, 0)`. Add a Particle System (use `ParticleAdditive`, a swirling/upward bloom). **Disable** `PortalEffect` (unchecked) — `ActManager` enables it in Act 4.
- [ ] **Step 3: Trial slots.** Create three disabled empty children under `HeartStone`: `Trial_Verdant`, `Trial_Scorched`, `Trial_Twilight`. Fill each with a simple visual for now: Verdant = a cluster of `PT_Poppy_02` + foliage around the stone; Scorched = the `FireEffect` area + a couple of charred rocks; Twilight = a few floating quads/glyph sprites. All three **disabled** by default.
- [ ] **Step 4: Verify.** Scene view shows the stone at center with three (hidden) trial groups + a hidden portal.

### Task 3.2: Forest ring + skybox

**[MANUAL]**

- [ ] **Step 1: Forest container.** Create empty `SceneRoot/Forest`.
- [ ] **Step 2: Ring it.** Around a ~2.5–3 m radius (leaving the center clear for users), place from `Assets/Supercyan Free Forest Sample/Prefabs/Mobile/`: several `Mobile_forestpack_tree_fir_tall`, `Mobile_forestpack_tree_1_leaf_1`, a few `Mobile_forestpack_stone_*`, grass patches, and 2–3 mushrooms. Add a couple of Polytope `PT_Menhir_Rock_*` / `PT_Generic_Rock_01` for variety. Use **Mobile** variants for HoloLens performance.
- [ ] **Step 3: Skybox.** Open **Window → Rendering → Lighting → Environment**. Set a daytime skybox (Unity default procedural is fine) — `SkyboxController` swaps day/night at runtime. (Optional: use the bonus `Skybox.mat` from the priest demo for night.)
- [ ] **Step 4: Verify.** Looking from the center outward you see a grove; the middle is open.

### Task 3.3: Command groups + controller references

**[MANUAL]**

- [ ] **Step 1: Effect groups.** Create `SceneRoot/Forest/ForestGroup` (put a few extra trees in it, disabled — `grow_tree` reveals them) and `SceneRoot/Forest/FlowersGroup` (poppies, disabled — `grow_flowers` reveals them). Create `SceneRoot/PathBlockade` (a row of rocks across one side, **enabled**).
- [ ] **Step 2: Find CommandExecutor.** Select the GameObject holding **CommandExecutor** (the effects/AI host under SceneRoot).
- [ ] **Step 3: Assign references.** On CommandExecutor: `forestGroup`=ForestGroup, `flowersGroup`=FlowersGroup, `pathBlockade`=PathBlockade, `astronautAnimator`=Astronaut's Animator. Leave `dobbyAnimator` **empty** (no Dobby). Confirm `weatherController`/`environmentController`/`skyboxController`/`cameraEffects` are assigned (auto-find covers them, but assign explicitly).
- [ ] **Step 4: Verify.** Enter Play Mode, open the debug voice input, type `grow_flowers` → FlowersGroup activates; `close_path`/`open_path` toggles PathBlockade; `astronaut_jump` plays the jump. No "reference not assigned" warnings. Exit.
- [ ] **Step 5: Commit (scene + animators).**

```bash
git add EchoRealm/Assets/Scenes EchoRealm/Assets/EchoRealm
git commit -m "feat(echorealm): assemble Living Grove scene (Heart Stone, Oracle, Astronaut, forest, command groups)"
```

---

## Phase 4 — AI inputs: nurture/chaos + eye tracking

### Task 4.1: Command sentiment classifier

**Files:**
- Create: `EchoRealm/Assets/Scripts/AI/CommandSentiment.cs`

- [ ] **Step 1: [CODE] Write the classifier + self-test.**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.AI
{
    public enum CommandTone { Nurture, Chaos, Neutral }

    /// <summary>Classifies a world command as nurturing, chaotic, or neutral. Pure logic.</summary>
    public static class CommandSentiment
    {
        private static readonly HashSet<string> NurtureSet = new HashSet<string>
        { "grow_tree", "grow_flowers", "spawn_butterflies", "spawn_fireflies", "day", "glow_objects", "open_path", "rain" };

        private static readonly HashSet<string> ChaosSet = new HashSet<string>
        { "fire", "earthquake", "lightning", "wind", "fog", "night", "close_path", "shrink_scene" };

        public static CommandTone Classify(string command)
        {
            if (string.IsNullOrEmpty(command)) return CommandTone.Neutral;
            string c = command.Trim().ToLowerInvariant();
            if (NurtureSet.Contains(c)) return CommandTone.Nurture;
            if (ChaosSet.Contains(c)) return CommandTone.Chaos;
            return CommandTone.Neutral; // stop_*, character anims, etc.
        }
    }

    /// <summary>Drop-on-any-GameObject self-test (right-click component ▸ Run CommandSentiment Self-Test).</summary>
    public class CommandSentimentSelfTest : MonoBehaviour
    {
        [ContextMenu("Run CommandSentiment Self-Test")]
        public void Run()
        {
            void Check(string cmd, CommandTone want)
            {
                var got = CommandSentiment.Classify(cmd);
                Debug.Log($"[SentimentTest] '{cmd}' → {got} ({(got == want ? "PASS" : "FAIL expected " + want)})");
            }
            Check("grow_tree", CommandTone.Nurture);
            Check("fire", CommandTone.Chaos);
            Check("  Earthquake ", CommandTone.Chaos);
            Check("stop_rain", CommandTone.Neutral);
            Check("astronaut_jump", CommandTone.Neutral);
        }
    }
}
```

- [ ] **Step 2: Verify.** In Unity, add `CommandSentimentSelfTest` to any temp GameObject, right-click the component → **Run CommandSentiment Self-Test**. Expected: all five log lines say **PASS**. Remove the temp component.
- [ ] **Step 3: Commit.**

```bash
git add EchoRealm/Assets/Scripts/AI/CommandSentiment.cs
git commit -m "feat(echorealm): nurture/chaos command classifier with self-test"
```

### Task 4.2: World-tone in the behavior profile

**Files:**
- Modify: `EchoRealm/Assets/Scripts/AI/PlayerBehaviorProfile.cs`

- [ ] **Step 1: [CODE] Add counters.** After `public int GazeEventCount { get; private set; }` add:

```csharp
        public int NurtureCount { get; private set; }
        public int ChaosCount   { get; private set; }
```

- [ ] **Step 2: [CODE] Record method.** After `public void RecordGaze(...)` add:

```csharp
        public void RecordWorldChange(CommandTone tone)
        {
            if (tone == CommandTone.Nurture) NurtureCount++;
            else if (tone == CommandTone.Chaos) ChaosCount++;
        }

        /// <summary>"nurturing", "chaotic", or "balanced" based on the world commands given.</summary>
        public string WorldTone
        {
            get
            {
                if (NurtureCount == 0 && ChaosCount == 0) return "untouched";
                if (NurtureCount >= ChaosCount * 2) return "nurturing";
                if (ChaosCount >= NurtureCount * 2) return "chaotic";
                return "balanced";
            }
        }
```

- [ ] **Step 3: [CODE] Surface it to the AI.** In `GetAISummary()`, before the final `Total interactions` line, add:

```csharp
                $"World tone: {WorldTone} (nurturing acts: {NurtureCount}, chaotic acts: {ChaosCount}). " +
```

- [ ] **Step 4: [CODE] Reset.** In `Reset()`, add `NurtureCount = 0; ChaosCount = 0;`.
- [ ] **Step 5: Verify compile.** No errors in Unity.
- [ ] **Step 6: Commit.**

```bash
git add EchoRealm/Assets/Scripts/AI/PlayerBehaviorProfile.cs
git commit -m "feat(echorealm): track nurture/chaos world tone in behavior profile"
```

### Task 4.3: Tally world-tone on the master

**Files:**
- Modify: `EchoRealm/Assets/Scripts/AI/ActionCollector.cs`
- Modify: `EchoRealm/Assets/Scripts/Networking/FilmSync.cs`

- [ ] **Step 1: [CODE] ActionCollector hook.** After `RecordVoiceCommand`, add:

```csharp
        /// <summary>Classify an executed world command and tally its nurture/chaos tone.</summary>
        public void RecordWorldChange(string command)
        {
            var tone = CommandSentiment.Classify(command);
            Profile.RecordWorldChange(tone);
            if (tone != CommandTone.Neutral) AddRecent($"World: {command} ({tone})");
        }
```

- [ ] **Step 2: [CODE] FilmSync tally.** In `ProcessSpeechAsAuthority`, inside the `if (response?.commands != null ...)` block, before `BroadcastCommands(response.commands);`, add:

```csharp
                foreach (var cmd in response.commands)
                    ActionCollector.Instance?.RecordWorldChange(cmd);
```

- [ ] **Step 3: Verify.** Play Mode (solo, AI reachable): type `make it rain and grow flowers`. Expected logs include `[CommandExecutor] Executing: rain`, `Executing: grow_flowers`, and the next AI act-decision summary contains `World tone: nurturing`. Exit.
- [ ] **Step 4: Commit.**

```bash
git add EchoRealm/Assets/Scripts/AI/ActionCollector.cs EchoRealm/Assets/Scripts/Networking/FilmSync.cs
git commit -m "feat(echorealm): pool nurture/chaos world tone on the master"
```

### Task 4.4: Eye tracking → AI

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Interaction/EyeTrackingManager.cs`

- [ ] **Step 1: [CODE] Feed dwell into the profile.** In `UpdateDwell()`, immediately after `OnDwellCompleted?.Invoke(CurrentTarget);`, add:

```csharp
                AI.ActionCollector.Instance?.RecordGaze(CurrentTarget.name);
```

- [ ] **Step 2: Verify (Editor).** Play Mode. Point the game-view camera (mouse-look / MRTK simulator) at the Astronaut for >2 s. Expected log: `[EyeTracking] DWELL completed on: Astronaut (...)` and `GazeEventCount` rises (visible via the next AI summary's gaze line, Task 4.5). Exit.
- [ ] **Step 3: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Interaction/EyeTrackingManager.cs
git commit -m "feat(echorealm): dwell events feed the AI behavior profile (eye tracking now influences narration)"
```

### Task 4.5: Gaze callout in the prompts

**Files:**
- Modify: `EchoRealm/Assets/Scripts/AI/NarrativeDecisionEngine.cs`
- Modify: `EchoRealm/Assets/Scripts/AI/NarrativeManager.cs`

- [ ] **Step 1: [CODE] Decision prompt.** In `NarrativeDecisionEngine.RequestDecisionAsync`, after `string behaviorSummary = ...;`, add:

```csharp
            string gazeSummary = Interaction.EyeTrackingManager.Instance != null
                ? Interaction.EyeTrackingManager.Instance.GetGazeSummary()
                : "nothing specific";
```

Then in the `prompt` string, after the `behaviorSummary` sentence, insert:

```csharp
                $"What the players watched most: {gazeSummary}. " +
```

- [ ] **Step 2: [CODE] Monologue prompt.** In `NarrativeManager.GenerateFinalMonologue`, after `string sessionSummary = BuildSessionSummary();`, add:

```csharp
            string gazeSummary = Interaction.EyeTrackingManager.Instance != null
                ? Interaction.EyeTrackingManager.Instance.GetGazeSummary()
                : "nothing specific";
```

Then in the `prompt`, change the "Here is what happened" sentence to append:

```csharp
                $"What the players watched most: {gazeSummary}. " +
```

and add a 4th instruction line: `"4. If their gaze lingered on something meaningful (the traveler, the fire), weave it in. "`.

- [ ] **Step 3: Verify.** Play Mode: stare at the Astronaut, give a couple of commands, then `SkipToAct(3)` (via FilmDirector test) to force an act decision. Expected: the `[NarrativeDecision]` log's behavior string includes `What the players watched most: Astronaut (...)`. Exit.
- [ ] **Step 4: Commit.**

```bash
git add EchoRealm/Assets/Scripts/AI/NarrativeDecisionEngine.cs EchoRealm/Assets/Scripts/AI/NarrativeManager.cs
git commit -m "feat(echorealm): Claude prompts include gaze summary (eye-tracking callout)"
```

> **Known follow-up (out of scope):** gaze + the final monologue are currently per-device (master's gaze drives the callout; each device generates its own monologue). Pooling gaze across both headsets and making monologue master-only+broadcast are optional later enhancements — noted, not done here.

---

## Phase 5 — Networked cooperation (Act-3 prerequisite)

### Task 5.1: Object-id comparison in the detector

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Interaction/CooperationDetector.cs`

- [ ] **Step 1: [CODE] Add objectId to PlayerInteraction.** In the `PlayerInteraction` class add `public string objectId;`.
- [ ] **Step 2: [CODE] Set it in the GameObject path.** In `ReportInteraction(int, GameObject, InteractionType)`, set `objectId = targetObject != null ? targetObject.name : "",` inside the `new PlayerInteraction { ... }` initializer.
- [ ] **Step 3: [CODE] Add the network-safe overload.** After that method add:

```csharp
        /// <summary>Network-safe report: identify the object by a stable id string (no GameObject ref).</summary>
        public void ReportInteraction(int playerIndex, string objectId, InteractionType interactionType)
        {
            var interaction = new PlayerInteraction
            {
                playerIndex = playerIndex,
                targetObject = null,
                objectId = objectId,
                type = interactionType,
                timestamp = Time.time
            };
            if (playerIndex == 0) lastPlayer1Action = interaction; else lastPlayer2Action = interaction;
            Log($"Player {playerIndex + 1} → {interactionType} on '{objectId}' (net)");
            CheckForCooperation();
        }
```

- [ ] **Step 4: [CODE] Compare by id.** In `CheckForCooperation()`, replace the Pattern-1 condition `if (lastPlayer1Action.targetObject == lastPlayer2Action.targetObject)` with:

```csharp
            if (!string.IsNullOrEmpty(lastPlayer1Action.objectId) &&
                lastPlayer1Action.objectId == lastPlayer2Action.objectId)
```

and in that block's `description`, use `lastPlayer1Action.objectId` instead of `lastPlayer1Action.targetObject.name`.

- [ ] **Step 5: Verify compile.** No errors.
- [ ] **Step 6: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Interaction/CooperationDetector.cs
git commit -m "feat(echorealm): cooperation detection by object-id (network-safe)"
```

### Task 5.2: Interaction RPC on FilmSync

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Networking/FilmSync.cs`

- [ ] **Step 1: [CODE] using.** Add `using EchoRealm.Interaction;` to the top of `FilmSync.cs`.
- [ ] **Step 2: [CODE] Submit + RPC.** After the `RPC_ApplyCommands` method, add:

```csharp
        // ------------------------------------------------------------------
        // Interaction → master (cooperation detection across both headsets)
        // ------------------------------------------------------------------

        /// <summary>Any device reports a player interaction; the master's detector evaluates cooperation.</summary>
        public void SubmitInteraction(int playerIndex, string objectId, InteractionType type)
        {
            if (HasStateAuthority) CooperationDetector.Instance?.ReportInteraction(playerIndex, objectId, type);
            else RPC_SubmitInteraction(playerIndex, objectId, (int)type);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitInteraction(int playerIndex, string objectId, int type)
        {
            CooperationDetector.Instance?.ReportInteraction(playerIndex, objectId, (InteractionType)type);
        }
```

- [ ] **Step 3: Verify compile.** No errors.
- [ ] **Step 4: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Networking/FilmSync.cs
git commit -m "feat(echorealm): networked interaction RPC so the master sees both players' actions"
```

### Task 5.3: Route gestures through the master

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Interaction/GestureManager.cs`

- [ ] **Step 1: [CODE] Reroute `ReportGesture`.** Replace the body of `ReportGesture(GameObject target, InteractionType type)` with:

```csharp
            int playerIndex = 0;
            var networkManager = Networking.FusionNetworkManager.Instance;
            if (networkManager != null && !networkManager.IsMaster) playerIndex = 1;

            var sync = Networking.FilmSync.Instance;
            if (sync != null)
                sync.SubmitInteraction(playerIndex, target.name, type); // → master's detector
            else
                CooperationDetector.Instance?.ReportInteraction(playerIndex, target, type); // solo/editor fallback
```

- [ ] **Step 2: Verify compile.** No errors. (Behavior: in a networked session, both headsets' grabs/taps now reach the master's `CooperationDetector`; solo still works.)
- [ ] **Step 3: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Interaction/GestureManager.cs
git commit -m "feat(echorealm): route interactions through FilmSync so cooperation works across devices"
```

### Task 5.4: Make the Heart Stone grabbable

**[MANUAL]**

- [ ] **Step 1: Collider.** On `HeartStone`, add a Collider sized to the stone (so gaze/hand rays hit it).
- [ ] **Step 2: MRTK interactable.** Add MRTK3 **StatefulInteractable** (or ObjectManipulator) to `HeartStone`.
- [ ] **Step 3: Wire events to GestureManager.** Ensure a **GestureManager** component exists (on SceneRoot). On the HeartStone's interactable, wire its **Select/Click** (and ObjectManipulator's ManipulationStarted) UnityEvents → `GestureManager.OnObjectTapped` / `OnObjectGrabbed`. (GestureManager forwards to FilmSync.)
- [ ] **Step 4: Verify (Editor, solo).** Play Mode, air-tap/grab the HeartStone twice quickly → log `[Cooperation] Player 1 → ...`. (Real cooperation needs two devices — Phase 7.)

---

## Phase 6 — Variants + act content

### Task 6.1: Re-theme the variant sets

**Files:**
- Modify: `EchoRealm/Assets/Scripts/AI/NarrativeDecisionEngine.cs`

- [ ] **Step 1: [CODE] Replace `BuildDefaultVariantSets()`** with the grove-themed, nurture/chaos version:

```csharp
        private static ActVariantSet[] BuildDefaultVariantSets() => new[]
        {
            new ActVariantSet
            {
                fromAct = 2,
                variants = new[]
                {
                    new SceneVariant {
                        key = "verdant", displayName = "The Verdant Trial",
                        aiDescription = "The players mostly NURTURED the grove (grew trees/flowers, butterflies, daylight). " +
                            "The heart is wrapped in blooming overgrowth the two must part together. Choose for a nurturing world tone.",
                        fallbackOracleLine = "The grove has bloomed for you. Now part its embrace — together.",
                        fallbackMood = "calm" },
                    new SceneVariant {
                        key = "scorched", displayName = "The Scorched Trial",
                        aiDescription = "The players mostly unleashed CHAOS (fire, storms, earthquakes, night). " +
                            "The heart is ringed by flame and storm the two must calm together. Choose for a chaotic world tone.",
                        fallbackOracleLine = "You stirred the grove's fury. Now quiet it — together.",
                        fallbackMood = "scared" },
                    new SceneVariant {
                        key = "twilight", displayName = "The Twilight Trial",
                        aiDescription = "The players were BALANCED or watchful. Hidden glyphs on the stones reveal only " +
                            "when both focus on the heart together. Choose for a balanced/observant world tone.",
                        fallbackOracleLine = "Between bloom and fire lies a hidden path. Look — together.",
                        fallbackMood = "curious" }
                }
            },
            new ActVariantSet
            {
                fromAct = 3,
                variants = new[]
                {
                    new SceneVariant {
                        key = "triumphant", displayName = "Final Triumfal",
                        aiDescription = "A nurturing, harmonious session that solved the trial well. Warm, proud, celebratory.",
                        fallbackOracleLine = "You shaped this world with care, and it answered in kind.",
                        fallbackMood = "joyful" },
                    new SceneVariant {
                        key = "bittersweet", displayName = "Final Melancolie",
                        aiDescription = "A chaotic or hard-won session. Reflective, gentle, acknowledging the storm they raised.",
                        fallbackOracleLine = "You raised storms, yet found your way through them. That, too, is wisdom.",
                        fallbackMood = "sad" },
                    new SceneVariant {
                        key = "mysterious", displayName = "Final Misterios",
                        aiDescription = "A watchful, exploratory session. Ambiguous and wondrous; raises more questions than answers.",
                        fallbackOracleLine = "What you witnessed here — was it memory, or prophecy? Only you can say.",
                        fallbackMood = "mysterious" }
                }
            }
        };
```

- [ ] **Step 2: Verify compile.** No errors. (Inspector `variantSets` should be left **empty** so these defaults build at runtime; if previously populated, clear it.)
- [ ] **Step 3: Commit.**

```bash
git add EchoRealm/Assets/Scripts/AI/NarrativeDecisionEngine.cs
git commit -m "feat(echorealm): re-theme act variants to nurture/chaos (verdant/scorched/twilight)"
```

### Task 6.2: Grove lines + Astronaut beats + new variant keys

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Film/ActManager.cs`

- [ ] **Step 1: [CODE] Intro lines.** Replace the `oracleIntroLines` default array with:

```csharp
        [SerializeField] private string[] oracleIntroLines = new string[]
        {
            "Welcome, travelers, to EchoRealm.",
            "This grove lives, and it listens. Your voice shapes what grows here.",
            "A wanderer has fallen among us, far from home.",
            "Find the Origin Echo — the grove's heart — and you may send him back.",
            "Speak... and the world will answer."
        };
```

- [ ] **Step 2: [CODE] Act 1 — Astronaut reacts (replace the Dobby block).** In `RunAct1()`, replace the `var dobby = DobbyController.Instance; ...` block with:

```csharp
            // The lost traveler stirs and looks around, startled.
            var astronaut = AstronautController.Instance;
            if (astronaut != null)
            {
                astronaut.PlayAnimation("LookAround");
                yield return new WaitForSeconds(3f);
            }
```

- [ ] **Step 3: [CODE] Act 3 — grove default + keys.** In `RunAct3`, change the default `variant` from `"cooperative"` to `"verdant"`, and the default `oracleLine` to `"The grove's heart is hidden. Reach it — together."`. Replace `PickObstacleForVariant` with:

```csharp
        private GameObject PickObstacleForVariant(string variant)
        {
            switch (variant)
            {
                case "scorched" when obstacleChaoticVariant    != null: return obstacleChaoticVariant;
                case "twilight"  when obstacleMysteriousVariant != null: return obstacleMysteriousVariant;
                default: return challengeObstacle; // verdant / default
            }
        }
```

- [ ] **Step 4: [CODE] Act 4 — Astronaut farewell (replace the Dobby block).** In `RunAct4()`, replace the `var dobby = DobbyController.Instance; ...` block with:

```csharp
            // The traveler steps through; the Oracle gives a final blessing.
            if (oracle != null)
            {
                oracle.Speak("Go now, traveler. The grove will remember you both.");
                yield return new WaitForSeconds(3f);
            }
```

- [ ] **Step 5: Verify compile.** No errors. (`DobbyController` is no longer referenced by ActManager.)
- [ ] **Step 6: Commit.**

```bash
git add EchoRealm/Assets/Scripts/Film/ActManager.cs
git commit -m "feat(echorealm): grove-themed act lines, Astronaut absorbs companion beats, new variant keys"
```

### Task 6.3: Assign trial objects to ActManager

**[MANUAL]**

- [ ] **Step 1: Assign.** Select the ActManager host. Set `challengeObstacle` = `Trial_Verdant`, `obstacleChaoticVariant` = `Trial_Scorched`, `obstacleMysteriousVariant` = `Trial_Twilight`, `portalEffect` = `HeartStone/PortalEffect`. Set `cooperationGoal` = 3.
- [ ] **Step 2: Verify.** No empty fields; Play Mode `SkipToAct(3)` activates one trial group. Exit.

---

## Phase 7 — Integration

### Task 7.1: Solo Editor play-through

**[MANUAL]**

- [ ] **Step 1.** Enter Play Mode (Editor acts as solo master; AI reachable via Claude key).
- [ ] **Step 2.** Watch Act 1: Oracle (priest) appears and speaks the grove intro; Astronaut looks around.
- [ ] **Step 3.** Act 2: type a mix of commands (e.g. several `grow_*`/`day` for nurturing, OR `fire`/`earthquake` for chaos); gaze at the Astronaut a few times.
- [ ] **Step 4.** Verify the Act 2→3 `[NarrativeDecision]` log shows the matching variant (`verdant` for nurturing, `scorched` for chaos) and a behavior summary containing `World tone:` and `What the players watched most:`.
- [ ] **Step 5.** Act 3: the matching `Trial_*` group activates. (Solo: use `CooperationDetector.ForceCooperationEvent` 3× via a temp debug button, or skip.) Act 4: portal blooms, Oracle delivers the AI monologue, Astronaut Floats through.
- [ ] **Step 6.** Confirm: no NullReferences, no startup particles, particles only on command.

### Task 7.2: Two-device dress rehearsal

**[MANUAL]**

- [ ] **Step 1.** Build & deploy to the HoloLens (master). Run the Editor as client (or a second device). Both scan the `EchoRealm-Anchor` QR.
- [ ] **Step 2.** Confirm co-location: the grove + Heart Stone appear in the same physical spot on both.
- [ ] **Step 3.** Both users speak commands — confirm both headsets' commands change the shared world (master interprets, broadcasts).
- [ ] **Step 4.** Act 3: both users grab the Heart Stone together 3× → `[Cooperation]` count reaches the goal on the master → trial solves on both.
- [ ] **Step 5.** Confirm the ending reflects the combined world tone, and the Oracle's lines reference what was watched.
- [ ] **Step 6.** Export `SessionLogger` data for the thesis evaluation.

---

## Self-Review

**Spec coverage:** Concept→Phases 3/6; viewers' role→6.1/6.2 lines; 4 acts→6.2 + existing ActManager; AI nurture/chaos→4.1–4.3 + 6.1; eye-tracking callout→4.4/4.5; cooperative trial→5.x + 3.1 + 6.3; scene composition→Phase 3; Oracle humanoid→2.x; Astronaut→1.x; build scope/deps→covered; out-of-scope items explicitly not implemented. ✅
**Placeholder scan:** trial-variant visuals in 3.1 are intentionally simple (real content), not placeholders; all code steps contain full code. ✅
**Type consistency:** `CommandTone`/`CommandSentiment.Classify`, `RecordWorldChange`, `WorldTone`, `SubmitInteraction`/`RPC_SubmitInteraction`, `ReportInteraction(int,string,InteractionType)`, `bodyAnimator`/`talkTrigger`/`idleTrigger`, variant keys `verdant/scorched/twilight` + `triumphant/bittersweet/mysterious` are used consistently across tasks. ✅
