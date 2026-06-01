# Networked Shared Film Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make two (or N) co-located HoloLens users share ONE AI-adaptive film — same act/variant in sync, and any headset's voice command changes the shared world — by routing all film authority through a master-owned `FilmSync` NetworkBehaviour.

**Architecture:** The master HoloLens is the sole authority. A master-spawned `FilmSync` NetworkObject holds `[Networked]` act/variant (late-join snapshot) and exposes RPCs: clients send recognized speech to the master (`RPC_SubmitSpeech`), the master interprets it with the AI and broadcasts the resulting commands (`RPC_ApplyCommands`) and act transitions (`RPC_StartAct`) to all devices. Live sync uses RPCs; a one-time read in `Spawned()` catches up late joiners. Clients run no AI/act-flow logic of their own.

**Tech Stack:** Unity 2022.3, Photon Fusion 2 (Shared Mode), C#, UWP/ARM64 HoloLens 2. Builds on QR co-location (`SceneRoot`) and the AIManager/Claude pipeline.

**Verification note:** No CLI test runner exists. "Verify" = (a) **Compile** — Unity Console clean; (b) **Editor** — Play Mode behavior/logs; (c) **Device/Multipeer** — two peers. Each task states which.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Scripts/Networking/FilmSync.cs` | The networked film spine (state + RPCs + master AI processing) | **Create** |
| `Assets/Scripts/AI/VoiceCommandProcessor.cs` | Route speech to FilmSync; expose `RaiseAIResponse`; keep solo fallback | Modify |
| `Assets/Scripts/Film/FilmDirector.cs` | Master-only act flow; drive acts via FilmSync; Act-2 count from combined profile | Modify |
| `Assets/Scripts/Networking/FusionNetworkManager.cs` | Master spawns the FilmSync NetworkObject on session start | Modify |
| `Assets/Scenes/MainScene.unity` + a `FilmSync` prefab | Wire the prefab to FusionNetworkManager | Editor (manual) |

---

## Task 1: Create the FilmSync NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/Networking/FilmSync.cs`

- [ ] **Step 1: Write the full FilmSync component**

```csharp
using Fusion;
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Film;

namespace EchoRealm.Networking
{
    /// <summary>
    /// Master-authoritative spine for the shared film. The master owns this NetworkObject
    /// (spawned by FusionNetworkManager). It holds the current act/variant as networked
    /// state (a late-join snapshot) and relays everything else via RPCs:
    ///   • clients send recognized speech to the master (RPC_SubmitSpeech),
    ///   • the master interprets it with the AI and broadcasts world commands (RPC_ApplyCommands),
    ///   • the master broadcasts act transitions (RPC_StartAct).
    /// Clients run no AI/act-flow logic of their own — they replay the master's decisions
    /// on their own co-located content (aligned via the QR SceneRoot).
    /// </summary>
    public class FilmSync : NetworkBehaviour
    {
        /// <summary>Singleton — set on every device in Spawned().</summary>
        public static FilmSync Instance { get; private set; }

        // Minimal late-join snapshot. Live transitions go via RPC_StartAct.
        [Networked] public int CurrentAct { get; set; }
        [Networked] public NetworkString<_16> ChosenVariant { get; set; }

        public override void Spawned()
        {
            Instance = this;
            Debug.Log($"[FilmSync] Spawned. HasStateAuthority={HasStateAuthority} (master={Runner.IsSharedModeMasterClient}).");

            // Late-join catch-up: if the film already advanced before we joined, jump to it.
            if (CurrentAct > 0)
            {
                var d = new AINarrativeDecision
                {
                    chosen_variant = ChosenVariant.ToString(),
                    mood = "mysterious",
                    oracle_narration = ""
                };
                Debug.Log($"[FilmSync] Late join — catching up to Act {CurrentAct} (variant '{d.chosen_variant}').");
                ActManager.Instance?.StartAct(CurrentAct, d);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // Act flow — called by the master's FilmDirector
        // ------------------------------------------------------------------

        /// <summary>Master only: record act+variant as networked state and broadcast the transition.</summary>
        public void DriveAct(int act, AINarrativeDecision decision)
        {
            if (!HasStateAuthority) return;

            string variant = decision?.chosen_variant ?? "default";
            CurrentAct = act;
            ChosenVariant = variant;
            RPC_StartAct(act, variant, decision?.mood ?? "mysterious", decision?.oracle_narration ?? "");
        }

        // RpcTargets.All includes the master, so ActManager.StartAct runs once on every device.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_StartAct(int act, string variant, string mood, string narration)
        {
            var d = new AINarrativeDecision { chosen_variant = variant, mood = mood, oracle_narration = narration };
            Debug.Log($"[FilmSync] RPC_StartAct → Act {act} (variant '{variant}').");
            ActManager.Instance?.StartAct(act, d);
        }

        // ------------------------------------------------------------------
        // Voice → shared world
        // ------------------------------------------------------------------

        /// <summary>Called on any device. Master interprets locally; clients forward to the master.</summary>
        public void SubmitSpeech(string text)
        {
            if (HasStateAuthority) ProcessSpeechAsAuthority(text);
            else RPC_SubmitSpeech(text);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitSpeech(string text)
        {
            ProcessSpeechAsAuthority(text); // runs on the master
        }

        /// <summary>Master only: pool the behavior, interpret with the AI, broadcast the commands.</summary>
        private async void ProcessSpeechAsAuthority(string text)
        {
            ActionCollector.Instance?.RecordVoiceCommand(text); // combined profile lives on the master

            var ai = AIManager.Instance;
            if (ai == null || !ai.IsReachable)
            {
                Debug.LogWarning("[FilmSync] No AI backend reachable on the master; ignoring speech.");
                return;
            }

            var exec = CommandExecutor.Instance;
            string scene = exec != null ? exec.GetSceneStateDescription() : "unknown";
            string[] available = exec != null ? exec.GetAvailableCommands() : new string[0];

            var response = await ai.SendCommandRequestAsync(text, scene, available);
            if (response?.commands != null && response.commands.Length > 0)
            {
                BroadcastCommands(response.commands);
                VoiceCommandProcessor.Instance?.RaiseAIResponse(response); // master UI/NarrativeManager react
            }
            else
            {
                Debug.LogWarning("[FilmSync] AI returned no commands for the submitted speech.");
            }
        }

        /// <summary>Master only: tell every device to execute these commands.</summary>
        public void BroadcastCommands(string[] commands)
        {
            if (!HasStateAuthority || commands == null || commands.Length == 0) return;
            RPC_ApplyCommands(string.Join(",", commands));
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ApplyCommands(string commandsCsv)
        {
            var exec = CommandExecutor.Instance;
            if (exec == null || string.IsNullOrEmpty(commandsCsv)) return;
            foreach (var raw in commandsCsv.Split(','))
            {
                string cmd = raw.Trim();
                if (cmd.Length > 0) exec.ExecuteCommand(cmd);
            }
        }
    }
}
```

- [ ] **Step 2: Verify (Compile)**

Switch to Unity, let it recompile, open the Console.
Expected: no compile errors. (`AIManager.Instance`, `ActManager.Instance`, `CommandExecutor.Instance`, `ActionCollector.Instance`, `VoiceCommandProcessor.Instance` all already exist as singletons; `AINarrativeDecision` fields `chosen_variant`/`mood`/`oracle_narration` exist.)

- [ ] **Step 3: Commit**

```bash
git add EchoRealm/Assets/Scripts/Networking/FilmSync.cs
git commit -m "feat(net): add master-authoritative FilmSync NetworkBehaviour"
```

---

## Task 2: Route VoiceCommandProcessor through FilmSync

**Files:**
- Modify: `Assets/Scripts/AI/VoiceCommandProcessor.cs`

- [ ] **Step 1: Replace the body of `ProcessSpeechText`**

Find the method `private async System.Threading.Tasks.Task ProcessSpeechText(string text)` and replace its entire body with:

```csharp
        private async System.Threading.Tasks.Task ProcessSpeechText(string text)
        {
            LastRecognizedText = text;
            OnSpeechRecognized?.Invoke(text);

            // Networked path: hand the speech to the master via FilmSync. The master
            // interprets it (AI), pools it into the combined behavior profile, and
            // broadcasts the resulting world commands to every headset.
            var sync = EchoRealm.Networking.FilmSync.Instance;
            if (sync != null)
            {
                Log($"Routing speech to FilmSync (master interprets): '{text}'");
                sync.SubmitSpeech(text);
                return;
            }

            // Fallback (no networking yet / editor before spawn): interpret + execute locally.
            ActionCollector.Instance?.RecordVoiceCommand(text);

            if (aiManager == null || !aiManager.IsReachable)
            {
                Debug.LogWarning("[Voice] No AI backend available. Cannot process voice command.");
                return;
            }
            if (aiManager.IsBusy)
            {
                Log("AI backend is busy processing another request. Skipping.");
                return;
            }

            string sceneState = commandExecutor != null ? commandExecutor.GetSceneStateDescription() : "unknown";
            string[] availableCommands = commandExecutor != null ? commandExecutor.GetAvailableCommands() : new string[0];

            Log($"Sending to AI ({aiManager.ActiveBackendName}): speech='{text}', scene='{sceneState}'");
            var response = await aiManager.SendCommandRequestAsync(text, sceneState, availableCommands);

            if (response != null)
            {
                Log($"AI response: commands=[{string.Join(", ", response.commands ?? new string[0])}], mood={response.mood}");
                OnAIResponseReceived?.Invoke(response);
                if (commandExecutor != null && response.commands != null)
                    commandExecutor.ExecuteCommands(response);
            }
            else
            {
                Log("AI returned null response.", isWarning: true);
            }
        }
```

- [ ] **Step 2: Add the `RaiseAIResponse` method**

Immediately after `ProcessSpeechText` (still inside the class), add:

```csharp
        /// <summary>
        /// Raise the AI-response event. Called by FilmSync on the master after it
        /// interprets speech authoritatively, so the master's NarrativeManager/UI still react.
        /// </summary>
        public void RaiseAIResponse(AICommandResponse response)
        {
            OnAIResponseReceived?.Invoke(response);
        }
```

- [ ] **Step 3: Verify (Compile)**

Return to Unity, recompile, check the Console.
Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/AI/VoiceCommandProcessor.cs
git commit -m "feat(net): route voice through FilmSync (master interprets); keep solo fallback"
```

---

## Task 3: Make FilmDirector master-driven via FilmSync

**Files:**
- Modify: `Assets/Scripts/Film/FilmDirector.cs`

- [ ] **Step 1: Add `IsMaster` + `GoToAct` helpers**

Find the `private void Awake()` method. Immediately ABOVE it, add these two helpers:

```csharp
        /// <summary>True if this device drives the film (the Photon master, or solo/editor with no Fusion master).</summary>
        private bool IsMaster =>
            EchoRealm.Networking.FusionNetworkManager.Instance == null ||
            EchoRealm.Networking.FusionNetworkManager.Instance.IsMaster;

        /// <summary>Start an act through FilmSync (networked) when available, else locally (solo/editor).</summary>
        private void GoToAct(int act, AINarrativeDecision decision)
        {
            if (EchoRealm.Networking.FilmSync.Instance != null)
                EchoRealm.Networking.FilmSync.Instance.DriveAct(act, decision);
            else if (actManager != null)
                actManager.StartAct(act, decision);
        }
```

- [ ] **Step 2: Gate `StartFilm` to master and route through `GoToAct`**

Find `public void StartFilm()` and replace the line `actManager.StartAct(1);` AND add a master gate. The method becomes:

```csharp
        public void StartFilm()
        {
            if (IsPlaying) return;
            if (!IsMaster)
            {
                Log("Not master — the master drives the film via FilmSync. Skipping local StartFilm.");
                return;
            }

            IsPlaying = true;
            IsFinished = false;
            Log("Film STARTED.");

            SessionLogger.Instance?.LogEvent(EventType.System, "Film started");

            GoToAct(1, null);
        }
```

- [ ] **Step 3: Gate `Update` to master and use the combined profile for Act 2 completion**

Find `private void Update()` and replace it entirely with:

```csharp
        private void Update()
        {
            if (!IsMaster) return; // only the master advances the film

            if (act2Active)
            {
                bool timeUp = Time.time - act2StartTime >= act2MaxDuration;
                int voiceCount = AI.ActionCollector.Instance != null
                    ? AI.ActionCollector.Instance.Profile.VoiceCommandCount
                    : 0;
                bool enoughCommands = voiceCount >= minVoiceCommands;

                if (timeUp || enoughCommands)
                {
                    act2Active = false;
                    string reason = timeUp ? "time limit reached" : $"voice commands reached ({minVoiceCommands})";
                    Log($"Act 2 advancing: {reason}");
                    actManager.CompleteAct2();
                }
            }
        }
```

- [ ] **Step 4: Gate `OnActCompleted` to master and route transitions through `GoToAct`**

Find `private async void OnActCompleted(int completedAct)`. Add a master gate at the very top, and change the three `actManager.StartAct(...)` calls to `GoToAct(...)`. The relevant parts become:

```csharp
        private async void OnActCompleted(int completedAct)
        {
            if (!IsMaster) return; // clients replay acts but never drive transitions

            Log($"Act {completedAct} completed.");

            NarrativeManager.Instance?.AdvanceAct();
            ActionCollector.Instance?.ResetForNewAct();

            switch (completedAct)
            {
                case 1:
                    act2StartTime = Time.time;
                    act2Active    = true;
                    GoToAct(2, null);
                    break;

                case 2:
                    _act3Decision = await RequestActDecision(fromAct: 2, toAct: 3);
                    GoToAct(3, _act3Decision);
                    break;

                case 3:
                    _act4Decision = await RequestActDecision(fromAct: 3, toAct: 4);
                    GoToAct(4, _act4Decision);
                    break;

                case 4:
                    EndFilm();
                    break;
            }
        }
```

- [ ] **Step 5: Route `SkipToAct` through `GoToAct`**

Find `public void SkipToAct(int actNumber)`. Replace its `actManager.StartAct(actNumber);` line with `GoToAct(actNumber, null);`.

- [ ] **Step 6: Verify (Compile)**

Return to Unity, recompile, check the Console.
Expected: no compile errors. (`ActionCollector.Instance.Profile.VoiceCommandCount` exists; `FusionNetworkManager.Instance` and `.IsMaster` exist; `FilmSync.Instance` exists from Task 1.)

- [ ] **Step 7: Commit**

```bash
git add EchoRealm/Assets/Scripts/Film/FilmDirector.cs
git commit -m "feat(net): FilmDirector drives acts via FilmSync, master-only, combined Act-2 count"
```

---

## Task 4: Spawn FilmSync on the master

**Files:**
- Modify: `Assets/Scripts/Networking/FusionNetworkManager.cs`

- [ ] **Step 1: Add a serialized prefab field**

Find the `[Header("Session Settings")]` block near the top of the class and add, right after the `maxPlayers` field:

```csharp
        [Header("Shared Film")]
        [Tooltip("Prefab with NetworkObject + FilmSync. The master spawns it once per session.")]
        [SerializeField] private NetworkObject filmSyncPrefab;
```

- [ ] **Step 2: Spawn it on the master after the session starts**

In `StartSession()`, find the success branch:

```csharp
            if (result.Ok)
            {
                int playerCount = Runner.SessionInfo != null ? Runner.SessionInfo.PlayerCount : -1;
                Log($"✓ SESSION STARTED in {stopwatch.ElapsedMilliseconds}ms | " +
                    $"LocalPlayer={Runner.LocalPlayer} | IsMaster={IsMaster} | " +
                    $"PlayersInSession={playerCount} | Region={(Runner.SessionInfo != null ? Runner.SessionInfo.Region : "?")}");
                OnSessionJoined?.Invoke();
            }
```

Replace it with (adds the master-only spawn before firing the event):

```csharp
            if (result.Ok)
            {
                int playerCount = Runner.SessionInfo != null ? Runner.SessionInfo.PlayerCount : -1;
                Log($"✓ SESSION STARTED in {stopwatch.ElapsedMilliseconds}ms | " +
                    $"LocalPlayer={Runner.LocalPlayer} | IsMaster={IsMaster} | " +
                    $"PlayersInSession={playerCount} | Region={(Runner.SessionInfo != null ? Runner.SessionInfo.Region : "?")}");

                // The master spawns the shared-film state object; clients receive it by replication.
                if (IsMaster && filmSyncPrefab != null)
                {
                    Runner.Spawn(filmSyncPrefab);
                    Log("Spawned FilmSync (master is the film authority).");
                }
                else if (filmSyncPrefab == null)
                {
                    Log("filmSyncPrefab not assigned — film will run un-networked (per-device).", isError: true);
                }

                OnSessionJoined?.Invoke();
            }
```

- [ ] **Step 3: Verify (Compile)**

Return to Unity, recompile, check the Console.
Expected: no compile errors. New **Film Sync Prefab** field appears on the `FusionNetworkManager` component.

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/Networking/FusionNetworkManager.cs
git commit -m "feat(net): master spawns the FilmSync object on session start"
```

---

## Task 5: Create the FilmSync prefab and wire it (Unity Editor, manual)

**Files:**
- Create: `Assets/Prefabs/FilmSync.prefab` (via the Editor)
- Modify: `Assets/Scenes/MainScene.unity` (assign the prefab)

- [ ] **Step 1: Create the prefab**

In the Hierarchy: right-click → **Create Empty** → name it `FilmSync`. With it selected, in the Inspector:
1. **Add Component → Network Object** (Fusion).
2. **Add Component → Film Sync** (the new script).

Then drag `FilmSync` from the Hierarchy into `Assets/Prefabs/` to make it a prefab, and **delete the instance from the Hierarchy** (the master spawns it at runtime — it must not also exist as a scene object).

- [ ] **Step 2: Register the prefab with Fusion**

Fusion auto-detects `NetworkObject` prefabs via its NetworkProjectConfig. If prompted, let Fusion add it. (If you later see "prefab not found in NetworkProjectConfig", open **Fusion → Network Project Config** and confirm `FilmSync` is in the Prefabs list, or click the refresh/import button.)

- [ ] **Step 3: Assign the prefab to FusionNetworkManager**

In the Hierarchy, select the GameObject holding **FusionNetworkManager** → in the Inspector set **Film Sync Prefab** = the `FilmSync` prefab from `Assets/Prefabs/`.

- [ ] **Step 4: Save the scene**

`Ctrl+S`.

- [ ] **Step 5: Verify (Editor Play Mode, single device)**

Press **Play**. The master (you, solo) should spawn FilmSync. Expect in the Console:
```
[FusionNetwork] Spawned FilmSync (master is the film authority).
[FilmSync] Spawned. HasStateAuthority=True (master=True).
```
Then the film should run as before (Act 1→2→…), now driven through FilmSync:
```
[FilmSync] RPC_StartAct → Act 1 (variant 'default').
[ActManager] === ACT 1 STARTED (variant: default) ===
```
Fire a few `DebugVoiceInput` commands → confirm `[FilmSync] Routing speech to FilmSync` then `[FilmSync] RPC_ApplyCommands`-driven `[CommandExecutor] Executing: ...`. The solo film should behave exactly as in milestone #2.

- [ ] **Step 6: Commit**

```bash
git add EchoRealm/Assets/Prefabs/FilmSync.prefab EchoRealm/Assets/Prefabs/FilmSync.prefab.meta EchoRealm/Assets/Scenes/MainScene.unity EchoRealm/Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion
git commit -m "chore(net): add FilmSync prefab, register with Fusion, wire to FusionNetworkManager"
```

(If `NetworkProjectConfig.fusion` isn't at that path or didn't change, omit it from the `git add`.)

---

## Task 6: Two-peer verification

**Files:** none (build + manual test)

- [ ] **Step 1: Single device sanity (already covered)**

Confirm Task 5 Step 5 passed — the solo film runs through FilmSync with no regressions.

- [ ] **Step 2: Two peers — Editor (master) + a second peer**

Easiest is HoloLens + Editor, OR Fusion's editor multipeer. With two peers in the same `EchoRealm` session:
- One becomes master (`IsMaster=True`), the other client (`IsMaster=False`).
- On the **client**, expect `[FilmSync] Spawned. HasStateAuthority=False`.

- [ ] **Step 3: Verify shared act flow**

Watch both Consoles: act transitions (`[FilmSync] RPC_StartAct → Act N`) should fire on **both** peers at the same time, with the **same variant** (the master chose it).

- [ ] **Step 4: Verify shared world from a CLIENT command**

Trigger a voice command on the **client** (DebugVoiceInput in the client editor, or speak on the client HoloLens). Expect:
- Client log: `[FilmSync] Routing speech to FilmSync` (no local AI call on the client).
- Master log: `[FilmSync] ...` AI interpretation → `RPC_ApplyCommands`.
- **Both** peers: `[CommandExecutor] Executing: <cmd>` → same world change on both.

- [ ] **Step 5: Verify combined behavior**

After commands from both peers, trigger an act transition and confirm the master's `[NarrativeDecision] ... Behavior:` line shows the **pooled** `Voice commands given: N` (master + client counts combined), and the AI variant reflects it.

- [ ] **Step 6: Commit any tuning**

```bash
git add -A
git commit -m "chore(net): tune shared-film networking after two-peer test"
```

---

## Self-Review

**Spec coverage:**
- §5.1 FilmSync (networked state + 3 RPCs + master speech processing) → Task 1 ✓
- §5.2 FusionNetworkManager spawns FilmSync → Task 4 ✓
- §5.3 FilmDirector master-only + DriveAct + Act-2 via combined profile → Task 3 ✓
- §5.4 VoiceCommandProcessor routes to FilmSync + RaiseAIResponse + fallback → Task 2 ✓
- §5.5 ActionCollector recording on master → Task 1 (`ProcessSpeechAsAuthority`) ✓
- §5.6 CommandExecutor via RPC_ApplyCommands → Task 1 ✓
- §5.7 AI master-only → Tasks 1 & 3 (only master calls AI) ✓
- §6 data flows → Tasks 1–3 ✓
- §7 testing → Tasks 5–6 ✓
- §8 edge cases (null Instance fallback, late join, spawn timing) → Tasks 1–4 ✓

**Placeholder scan:** No TBD/TODO; every code step shows full before/after or full file. ✓

**Type/name consistency:** `FilmSync.Instance`, `DriveAct(int, AINarrativeDecision)`, `SubmitSpeech(string)`, `BroadcastCommands(string[])`, `RaiseAIResponse(AICommandResponse)`, `IsMaster`, `GoToAct(int, AINarrativeDecision)`, `ActionCollector.Instance.Profile.VoiceCommandCount`, `filmSyncPrefab` — used consistently across tasks and match existing singletons/signatures. ✓
