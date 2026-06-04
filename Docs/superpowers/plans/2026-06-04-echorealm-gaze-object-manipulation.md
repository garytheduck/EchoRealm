# Gaze-Directed Object Manipulation (Tier A) — Implementation Plan

> **For agentic workers:** execute task-by-task; each task ends in a commit. `[CODE]` = I write it; `[MANUAL]` = Samuel does it in the Unity Editor / on device.

**Goal:** "Claude, make this bigger" — manipulate the gazed-at prop by voice (scale/move/rotate/reset), Claude-inferred magnitude, networked + co-located, with comprehensive late-join sync and no auto-resets.

**Spec:** `Docs/superpowers/specs/2026-06-04-echorealm-gaze-object-manipulation-design.md`

**Architecture:** The speaking device resolves the egocentric target (eye gaze) + direction, converts it to a frame-independent op, and submits it to the master, which broadcasts it to all (apply by stable id under each device's QR-aligned SceneRoot). The master keeps the authoritative current transform of every manipulated object and replays it to late joiners (idempotent absolute sync).

**Tech:** Unity 2022.3 Built-In RP, MRTK3, Photon Fusion 2 shared mode, Claude backend.

---

## File map

- Create `EchoRealm/Assets/Scripts/AI/AIObjectOp.cs` — parsed op `{action,direction,magnitude}`.
- Create `EchoRealm/Assets/Scripts/Interaction/ManipulableObject.cs` — per-prop: original transform, clamped Apply*/Reset, absolute Get/SetLocal.
- Create `EchoRealm/Assets/Scripts/Interaction/ManipulableRegistry.cs` — auto-discovery, id=path, kind-from-name, lookup, gaze resolve.
- Create `EchoRealm/Assets/Scripts/Interaction/ObjectOpMath.cs` — magnitude→numbers + egocentric→parent-local conversion (pure functions).
- Modify `EchoRealm/Assets/Scripts/AI/IAIBackend.cs` — add `SendObjectOpAsync`.
- Modify `EchoRealm/Assets/Scripts/AI/ClaudeBackend.cs` — implement `SendObjectOpAsync`.
- Modify `EchoRealm/Assets/Scripts/AI/OllamaClient.cs` — stub `SendObjectOpAsync` (returns null).
- Modify `EchoRealm/Assets/Scripts/AI/AIManager.cs` — pass-through `SendObjectOpAsync` (primary→fallback).
- Modify `EchoRealm/Assets/Scripts/Networking/FilmSync.cs` — `SubmitObjectOp` + RPCs + master object-state dict + late-join replay.
- Modify `EchoRealm/Assets/Scripts/AI/VoiceCommandProcessor.cs` — "Claude" wake word + object-op branch.

---

## Task 1 — AIObjectOp model + backend interface `[CODE]`

**Files:** create `AI/AIObjectOp.cs`; modify `AI/IAIBackend.cs`, `AI/OllamaClient.cs`, `AI/AIManager.cs`.

- [ ] `AIObjectOp.cs`:
```csharp
using System;
namespace EchoRealm.AI
{
    [Serializable]
    public class AIObjectOp
    {
        public string action;     // scale | move | rotate | reset
        public string direction;  // bigger|smaller|left|right|up|down|closer|farther|none
        public string magnitude;  // small | medium | large
    }
}
```
- [ ] `IAIBackend`: add `System.Threading.Tasks.Task<AIObjectOp> SendObjectOpAsync(string phrase, string objectContext);`
- [ ] `OllamaClient`: `public Task<AIObjectOp> SendObjectOpAsync(string phrase, string objectContext) => Task.FromResult<AIObjectOp>(null);` (Claude-only feature).
- [ ] `AIManager`: add a pass-through that tries primary then fallback (mirror `SendCommandRequestAsync`), and on the inner `_client` wrapper too.

**Verify:** compiles in Unity (no errors in Console).

**Commit:** `feat(ai): AIObjectOp model + SendObjectOpAsync backend hook`

---

## Task 2 — ClaudeBackend.SendObjectOpAsync `[CODE]`

**File:** modify `AI/ClaudeBackend.cs`.

- [ ] Implement:
```csharp
public async Task<AIObjectOp> SendObjectOpAsync(string phrase, string objectContext)
{
    if (!IsKeyConfigured()) return null;
    string prompt =
        "Translate a short spoken request into ONE manipulation of a single 3D object the user is looking at. " +
        $"Object: {objectContext}. User said: \"{phrase}\". " +
        "Respond ONLY with JSON (no markdown): " +
        "{\"action\":\"scale|move|rotate|reset\",\"direction\":\"bigger|smaller|left|right|up|down|closer|farther|none\",\"magnitude\":\"small|medium|large\"}. " +
        "Rules: bigger/smaller -> scale. move/push/pull/bring with a direction -> move (closer=toward me, farther=away). " +
        "turn/rotate/spin -> rotate (direction left or right). put it back/reset/undo/original -> reset. " +
        "Magnitude: 'a bit'/'a little'/'slightly' -> small; 'a lot'/'much'/'way'/'huge' -> large; otherwise medium.";
    string raw = await SendPromptAsync(prompt);
    if (string.IsNullOrEmpty(raw)) return null;
    raw = StripCodeFences(raw);
    try { return JsonUtility.FromJson<AIObjectOp>(raw); }
    catch (Exception ex) { Debug.LogError($"[Claude] Bad AIObjectOp JSON: {ex.Message}\nRaw: {raw}"); return null; }
}
```

**Verify:** compiles.

**Commit:** `feat(ai): Claude parses a spoken object-manipulation into AIObjectOp`

---

## Task 3 — ManipulableObject `[CODE]`

**File:** create `Interaction/ManipulableObject.cs`.

- [ ] Component holding identity + original transform + clamped ops + absolute setters:
```csharp
using UnityEngine;
namespace EchoRealm.Interaction
{
    public class ManipulableObject : MonoBehaviour
    {
        public string Id;     // assigned by the registry (path under SceneRoot)
        public string Kind;   // cloud/bush/rock/tree/flower/mushroom/object

        private Vector3 _origScale, _origPos; private Quaternion _origRot; private bool _captured;

        private void Awake()
        {
            _origScale = transform.localScale; _origPos = transform.localPosition; _origRot = transform.localRotation;
            _captured = true;
        }

        public string Context() => $"a {Kind} (current size {transform.localScale.x / Mathf.Max(_origScale.x,1e-4f):F2}x of original)";

        public void ApplyScale(float factor)
        {
            var s = transform.localScale * factor;
            float ratio = s.x / Mathf.Max(_origScale.x, 1e-4f);
            ratio = Mathf.Clamp(ratio, 0.3f, 3f);
            transform.localScale = _origScale * ratio;
        }

        public void ApplyMove(Vector3 parentLocalDelta)
        {
            var p = transform.localPosition + parentLocalDelta;
            transform.localPosition = _origPos + Vector3.ClampMagnitude(p - _origPos, 1f);
        }

        public void ApplyYaw(float degrees) => transform.localRotation = Quaternion.Euler(0f, degrees, 0f) * transform.localRotation;

        public void ResetTransform()
        {
            if (!_captured) return;
            transform.localScale = _origScale; transform.localPosition = _origPos; transform.localRotation = _origRot;
        }

        // Absolute setters for networking + late-join (idempotent).
        public void SetLocal(Vector3 scale, Vector3 pos, Quaternion rot)
        { transform.localScale = scale; transform.localPosition = pos; transform.localRotation = rot; }
        public void GetLocal(out Vector3 scale, out Vector3 pos, out Quaternion rot)
        { scale = transform.localScale; pos = transform.localPosition; rot = transform.localRotation; }
    }
}
```

**Verify:** compiles.

**Commit:** `feat(interaction): ManipulableObject — clamped per-prop ops + reset + absolute sync`

---

## Task 4 — ManipulableRegistry `[CODE]`

**File:** create `Interaction/ManipulableRegistry.cs`.

- [ ] Auto-discover the user's grabbable props (those with an `ObjectManipulator`) under SceneRoot, minus protected; id = path under SceneRoot; kind from name:
```csharp
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;
namespace EchoRealm.Interaction
{
    public class ManipulableRegistry : MonoBehaviour
    {
        public static ManipulableRegistry Instance { get; private set; }
        [SerializeField] private Transform sceneRoot;
        [Tooltip("Subtrees that must NOT be voice-manipulable: Oracle, Astronaut, HeartStone, portal, trials.")]
        [SerializeField] private Transform[] protectedObjects;

        private readonly Dictionary<string, ManipulableObject> _byId = new();

        private void Awake() { Instance = this; }
        private void Start()
        {
            if (sceneRoot == null && QRAnchorManager.Instance != null) sceneRoot = QRAnchorManager.Instance.SceneRoot;
            if (sceneRoot == null) { Debug.LogError("[Manip] No SceneRoot."); return; }

            foreach (var om in sceneRoot.GetComponentsInChildren<ObjectManipulator>(true))
            {
                var t = om.transform;
                if (t == sceneRoot) continue;        // that's the whole-world grab
                if (IsProtected(t)) continue;
                var mo = t.GetComponent<ManipulableObject>() ?? t.gameObject.AddComponent<ManipulableObject>();
                mo.Id = PathUnder(sceneRoot, t);
                mo.Kind = InferKind(t.name);
                _byId[mo.Id] = mo;
            }
            Debug.Log($"[Manip] Registered {_byId.Count} manipulable props.");
        }

        public ManipulableObject FindById(string id) => id != null && _byId.TryGetValue(id, out var mo) ? mo : null;

        public ManipulableObject Resolve(GameObject gazed)
        {
            for (var t = gazed != null ? gazed.transform : null; t != null; t = t.parent)
            { var mo = t.GetComponent<ManipulableObject>(); if (mo != null) return mo; }
            return null;
        }

        private bool IsProtected(Transform t)
        {
            if (protectedObjects == null) return false;
            foreach (var p in protectedObjects) if (p != null && (t == p || t.IsChildOf(p))) return true;
            return false;
        }
        private static string PathUnder(Transform root, Transform t)
        {
            var stack = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) stack.Add(c.name);
            stack.Reverse(); return string.Join("/", stack);
        }
        private static string InferKind(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("cloud")) return "cloud";
            if (n.Contains("shrub") || n.Contains("bush")) return "bush";
            if (n.Contains("rock")) return "rock";
            if (n.Contains("tree") || n.Contains("pine")) return "tree";
            if (n.Contains("flower")) return "flower";
            if (n.Contains("mushroom")) return "mushroom";
            return "object";
        }
    }
}
```

**Verify:** in Editor Play, the Console logs "[Manip] Registered N manipulable props." with N matching the number of grabbable props (clouds, bushes, rocks, etc.).

**Commit:** `feat(interaction): ManipulableRegistry — auto-discover props, stable ids, gaze resolve`

---

## Task 5 — ObjectOpMath (magnitude → numbers + egocentric → parent-local) `[CODE]`

**File:** create `Interaction/ObjectOpMath.cs`.

- [ ] Pure helpers used by the speaking device:
```csharp
using UnityEngine;
namespace EchoRealm.Interaction
{
    public enum ObjOpType { Scale = 0, Move = 1, Rotate = 2, Reset = 3 }

    public static class ObjectOpMath
    {
        public static float ScaleFactor(string direction, string magnitude)
        {
            float up = magnitude == "small" ? 1.15f : magnitude == "large" ? 1.8f : 1.4f;
            return direction == "smaller" ? 1f / up : up;
        }
        public static float MoveMeters(string magnitude) => magnitude == "small" ? 0.1f : magnitude == "large" ? 0.5f : 0.25f;
        public static float YawDegrees(string direction, string magnitude)
        {
            float d = magnitude == "small" ? 15f : magnitude == "large" ? 90f : 45f;
            return direction == "left" ? -d : d;
        }
        // Egocentric direction (relative to the speaker's head) -> the prop's PARENT-local delta,
        // which is frame-consistent + co-located across devices (same hierarchy + QR-aligned SceneRoot).
        public static Vector3 MoveDelta(Transform cam, Transform prop, string direction, string magnitude)
        {
            Vector3 world =
                direction == "right"  ?  cam.right :
                direction == "left"   ? -cam.right :
                direction == "up"     ?  cam.up :
                direction == "down"   ? -cam.up :
                direction == "closer" ? -cam.forward :
                direction == "farther"?  cam.forward : Vector3.zero;
            if (world == Vector3.zero) return Vector3.zero;
            Vector3 local = prop.parent != null ? prop.parent.InverseTransformDirection(world) : world;
            return local.normalized * MoveMeters(magnitude);
        }
    }
}
```

**Verify:** compiles.

**Commit:** `feat(interaction): ObjectOpMath — magnitude mapping + egocentric→local conversion`

---

## Task 6 — FilmSync: networked object op + master state + late-join `[CODE]`

**File:** modify `Networking/FilmSync.cs`.

- [ ] Add a master-side store of each manipulated object's absolute local transform:
```csharp
private struct ObjState { public Vector3 scale, pos; public Quaternion rot; }
private readonly System.Collections.Generic.Dictionary<string, ObjState> _objStates = new();
```
- [ ] Submit + RPCs (mirror the speech/pocket pattern; `RpcTargets.All` includes the master, so apply happens there too — do NOT also apply directly):
```csharp
public void SubmitObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
{
    if (HasStateAuthority) RPC_ApplyObjectOp(id, opType, factor, delta, degrees);
    else RPC_SubmitObjectOp(id, opType, factor, delta, degrees);
}

[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_SubmitObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
    => RPC_ApplyObjectOp(id, opType, factor, delta, degrees);

[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
private void RPC_ApplyObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
{
    var mo = Interaction.ManipulableRegistry.Instance?.FindById(id);
    if (mo == null) return;
    switch ((Interaction.ObjOpType)opType)
    {
        case Interaction.ObjOpType.Scale:  mo.ApplyScale(factor); break;
        case Interaction.ObjOpType.Move:   mo.ApplyMove(delta);   break;
        case Interaction.ObjOpType.Rotate: mo.ApplyYaw(degrees);  break;
        case Interaction.ObjOpType.Reset:  mo.ResetTransform();   break;
    }
    if (HasStateAuthority) // master records the resulting absolute state for late joiners
    {
        mo.GetLocal(out var s, out var p, out var r);
        _objStates[id] = new ObjState { scale = s, pos = p, rot = r };
    }
}
```
- [ ] Late-join: the joining client asks; the master re-sends each absolute state (idempotent on existing peers). Add the request in `Spawned()` (client) and the two RPCs:
```csharp
// inside Spawned(), after the existing world-state catch-up:
if (!HasStateAuthority) RPC_RequestObjectStates();
```
```csharp
[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_RequestObjectStates()
{
    foreach (var kv in _objStates)
        RPC_SetObjectState(kv.Key, kv.Value.scale, kv.Value.pos, kv.Value.rot);
}

[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
private void RPC_SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot)
{
    var mo = Interaction.ManipulableRegistry.Instance?.FindById(id);
    if (mo != null) mo.SetLocal(scale, pos, rot); // absolute → idempotent on peers that already match
}
```

**Verify:** compiles. (Networked behavior verified on device in Task 9.)

**Commit:** `feat(net): networked per-object manipulation + comprehensive late-join object state`

---

## Task 7 — VoiceCommandProcessor: "Claude" wake word + object-op path `[CODE]`

**File:** modify `AI/VoiceCommandProcessor.cs`.

- [ ] In `ProcessSpeechText`, AFTER the pocket/START/unpocket metas and BEFORE the global `FilmSync.SubmitSpeech`, add the wake-word branch:
```csharp
// "Claude, ..." → manipulate the object I'm looking at (gaze-targeted). Matched only at the START
// of the phrase (accept the common "cloud" mishearing there) so "cloud" still works mid-sentence.
string trimmed = meta.TrimStart();
bool addressed = trimmed.StartsWith("claude") || trimmed.StartsWith("claud")
              || trimmed.StartsWith("cloud")  || trimmed.StartsWith("clyde") || trimmed.StartsWith("klaus");
if (addressed)
{
    await HandleObjectCommand(text);
    return;
}
```
- [ ] Add the handler (runs on the speaking device — uses ITS gaze + camera):
```csharp
private async System.Threading.Tasks.Task HandleObjectCommand(string text)
{
    var reg = EchoRealm.Interaction.ManipulableRegistry.Instance;
    var eyes = EchoRealm.Interaction.EyeTrackingManager.Instance;
    var mo = (reg != null && eyes != null) ? reg.Resolve(eyes.CurrentTarget) : null;
    if (mo == null) { Log("Claude (object): not looking at a manipulable object — ignored."); return; }

    if (aiManager == null || !aiManager.IsReachable) { Log("Claude (object): AI unavailable.", true); return; }
    var op = await aiManager.SendObjectOpAsync(text, mo.Context());
    if (op == null || string.IsNullOrEmpty(op.action)) { Log("Claude (object): no op parsed.", true); return; }

    var sync = EchoRealm.Networking.FilmSync.Instance;
    var cam = Camera.main != null ? Camera.main.transform : null;
    var M = EchoRealm.Interaction.ObjectOpMath;

    int opType; float factor = 1f, degrees = 0f; Vector3 delta = Vector3.zero;
    switch (op.action)
    {
        case "scale":  opType = (int)EchoRealm.Interaction.ObjOpType.Scale;  factor = M.ScaleFactor(op.direction, op.magnitude); break;
        case "move":   opType = (int)EchoRealm.Interaction.ObjOpType.Move;   delta  = cam != null ? M.MoveDelta(cam, mo.transform, op.direction, op.magnitude) : Vector3.zero; break;
        case "rotate": opType = (int)EchoRealm.Interaction.ObjOpType.Rotate; degrees = M.YawDegrees(op.direction, op.magnitude); break;
        case "reset":  opType = (int)EchoRealm.Interaction.ObjOpType.Reset;  break;
        default: Log($"Claude (object): unknown action '{op.action}'.", true); return;
    }
    Log($"Claude (object): {op.action}/{op.direction}/{op.magnitude} on '{mo.Id}'.");
    if (sync != null) sync.SubmitObjectOp(mo.Id, opType, factor, delta, degrees);
}
```

**Verify:** compiles. In Editor (mouse = gaze), say via `ProcessDebugInput("claude make this bigger")` while the simulated gaze hits a prop → Console shows the op + the prop scales.

**Commit:** `feat(voice): "Claude" wake word manipulates the gazed-at object`

---

## Task 8 — Editor wiring `[MANUAL]`

- [ ] Add a **ManipulableRegistry** component (to GameManager or SceneRoot). Assign **SceneRoot**, and **Protected Objects** = Oracle, Astronaut, HeartStone, Portal, trial objects (same set as `SceneManipulationReporter`).
- [ ] Confirm props you want manipulable have an **ObjectManipulator** (they already do) and a **collider** on the gaze layer.
- [ ] Confirm **GazeInput** capability is enabled (Player Settings → Publishing → Capabilities) and the device is **eye-calibrated**.
- [ ] No per-prop tagging needed — the registry adds `ManipulableObject` automatically.

**Verify:** Play in Editor → "[Manip] Registered N…" with the expected props.

---

## Task 9 — On-device test `[MANUAL]`

Build → deploy to both headsets, then run the acceptance criteria from the spec:
- [ ] Master: gaze a bush → "Claude, make this bigger / smaller / move it right / turn it left / reset this" all work.
- [ ] The same object changes on the **client**, co-located.
- [ ] Initiating from the **client** also affects both.
- [ ] A **protected** object (Oracle/HeartStone/Astronaut/portal) does not respond.
- [ ] "Claude, …" while not looking at a prop → gentle no-op.
- [ ] **Late join:** manipulate several props + change weather, THEN connect the client → it comes up showing the same modified world (nothing reset).

---

## Notes / known limits (from spec)
- Individual **hand-grabs** of props remain local (not networked) in Tier A; mixing a local hand-move with a networked op can desync a prop's base pose. Future work.
- The extra Claude call adds ~1–2 s latency before the object changes.
- Tier B (spoken spatial references) and generative objects are separate, later efforts.
