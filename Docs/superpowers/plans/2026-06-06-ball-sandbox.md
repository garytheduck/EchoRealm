# Ball Sandbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an isolated, networked physics ball to EchoRealm that spawns on a voice phrase and bounces off the HoloLens-scanned room, without touching the film, AI, or rewind systems.

**Architecture:** New `EchoRealm.Sandbox` code (Assembly-CSharp, no new asmdef). Pure logic (phrase match, anchor-pose math) lives in static classes; Unity glue (`NetworkBehaviour` + `MonoBehaviour`s) wraps it. The ball is a Fusion 2 Shared-Mode `NetworkObject` whose pose syncs relative to the QR anchor (mirroring `FilmSync`); the spawning device owns it and authority transfers on grab (mirroring `NetworkedTestCube`). Collision is layer-gated so the ball touches only the spatial mesh. All Unity-asset setup (layers, physics matrix, prefabs, scene wiring) is done by one Editor menu script.

**Tech Stack:** Unity (Assembly-CSharp), Photon Fusion 2 (Shared Mode), MRTK3 `ObjectManipulator`, AR Foundation 5.2 `ARMeshManager` (HoloLens spatial mesh via MS OpenXR 1.11.2), Unity PhysX.

---

## Testing approach in Unity (read first)

This codebase has no test harness and no asmdefs, and the feature is mostly MR/networking/physics that only runs in the Editor or on-device. So:

- **Pure logic** (`BallPhrases`, `SandboxMath`) is verified by an **Editor self-check** (`BallSandboxSelfCheck`) that runs assertions and logs PASS/FAIL — written before the logic exists (Task 1).
- **Unity/MR glue** (spawn, authority, pose sync, ARMeshManager colliders, MRTK grab/throw, real bounce) is verified by **in-Editor play test with a stand-in floor** (Task 7) and a **two-HoloLens device test** (Task 8), each with concrete expected observations. These are the spec's acceptance criteria.

There is no fake unit test for code that can't be meaningfully unit-tested.

## File structure

**Create (all new):**
- `EchoRealm/Assets/Scripts/Sandbox/BallPhrases.cs` — pure: map an utterance → Spawn/Remove/None.
- `EchoRealm/Assets/Scripts/Sandbox/SandboxMath.cs` — pure: world↔anchor-relative pose, spawn placement.
- `EchoRealm/Assets/Scripts/Sandbox/NetworkedBall.cs` — `NetworkBehaviour`: physics, authority, pose sync, grab/throw.
- `EchoRealm/Assets/Scripts/Sandbox/BallSpawner.cs` — `MonoBehaviour`: spawn/clear/cap.
- `EchoRealm/Assets/Scripts/Sandbox/BallVoiceHook.cs` — `MonoBehaviour`: registers the speech interceptor.
- `EchoRealm/Assets/Scripts/Sandbox/SpatialMeshManager.cs` — `MonoBehaviour`: ARMeshManager → colliders/layer/material/wireframe.
- `EchoRealm/Assets/Scripts/Editor/BallSandboxSelfCheck.cs` — Editor menu: assert pure logic.
- `EchoRealm/Assets/Scripts/Editor/BallSandboxSetup.cs` — Editor menu: layers, matrix, materials, prefabs, scene wiring.

**Modify (the only existing-code change):**
- `EchoRealm/Assets/Scripts/AI/VoiceCommandProcessor.cs` — add a generic static `SpeechInterceptor` delegate + one early-return check.

**Unity assets created by the setup script (Task 6), committed by the user with their scene work:**
- Layers `SpatialMesh`, `BallSandbox`; collision matrix rule; `BallBouncy`/`MeshLowFriction` physic materials; `Ball` prefab; `SpatialMeshChunk` prefab; `BallSandbox` scene objects.

---

## Task 1: Pure logic + Editor self-check (test-first)

**Files:**
- Create: `EchoRealm/Assets/Scripts/Editor/BallSandboxSelfCheck.cs`
- Create: `EchoRealm/Assets/Scripts/Sandbox/BallPhrases.cs`
- Create: `EchoRealm/Assets/Scripts/Sandbox/SandboxMath.cs`

- [ ] **Step 1: Write the self-check (the "test") first**

`EchoRealm/Assets/Scripts/Editor/BallSandboxSelfCheck.cs`:
```csharp
using UnityEngine;
using UnityEditor;
using EchoRealm.Sandbox;

namespace EchoRealm.SandboxEditor
{
    /// <summary>Editor-only assertions for the sandbox's pure logic. Run via the menu.
    /// This is the unit-test substitute for code that can run without a device.</summary>
    public static class BallSandboxSelfCheck
    {
        [MenuItem("EchoRealm/Ball Sandbox/Run Self-Check")]
        public static void Run()
        {
            int fail = 0;

            // --- BallPhrases ---
            Check(ref fail, BallPhrases.Match("Drop a ball now") == BallPhrases.Intent.Spawn, "spawn: 'drop a ball'");
            Check(ref fail, BallPhrases.Match("spawn ball") == BallPhrases.Intent.Spawn, "spawn: 'spawn ball'");
            Check(ref fail, BallPhrases.Match("please remove the ball") == BallPhrases.Intent.Remove, "remove: 'remove the ball'");
            Check(ref fail, BallPhrases.Match("clear balls") == BallPhrases.Intent.Remove, "remove: 'clear balls'");
            Check(ref fail, BallPhrases.Match("make it rain") == BallPhrases.Intent.None, "none: unrelated");
            Check(ref fail, BallPhrases.Match("") == BallPhrases.Intent.None, "none: empty");

            // --- SandboxMath round-trip: world -> anchor-relative -> world is identity ---
            var anchorPos = new Vector3(1f, 2f, 3f);
            var anchorRot = Quaternion.Euler(10f, 45f, 0f);
            var worldPos = new Vector3(-2f, 0.5f, 4f);
            var worldRot = Quaternion.Euler(0f, 90f, 15f);
            SandboxMath.ToAnchorRelative(anchorPos, anchorRot, worldPos, worldRot, out var rp, out var rr);
            SandboxMath.FromAnchorRelative(anchorPos, anchorRot, rp, rr, out var wp, out var wr);
            Check(ref fail, (wp - worldPos).magnitude < 1e-3f, "math: pos round-trip");
            Check(ref fail, Quaternion.Angle(wr, worldRot) < 0.05f, "math: rot round-trip");

            // --- SandboxMath spawn placement: in front, flattened, lowered ---
            var p = SandboxMath.SpawnPositionInFront(Vector3.zero, Quaternion.Euler(30f, 0f, 0f), 0.5f, 0.2f);
            Check(ref fail, Mathf.Abs(p.z - 0.5f) < 1e-3f, "math: forward is flattened to horizontal");
            Check(ref fail, Mathf.Abs(p.y + 0.2f) < 1e-3f, "math: lowered by 'down'");

            if (fail == 0) Debug.Log("[BallSandbox] Self-Check PASSED (all assertions).");
            else Debug.LogError($"[BallSandbox] Self-Check FAILED: {fail} assertion(s).");
        }

        private static void Check(ref int fail, bool ok, string label)
        {
            if (ok) Debug.Log($"[BallSandbox]   PASS: {label}");
            else { fail++; Debug.LogError($"[BallSandbox]   FAIL: {label}"); }
        }
    }
}
```

- [ ] **Step 2: Confirm it "fails"**

In Unity, the Console shows compile errors (`BallPhrases`/`SandboxMath` don't exist). That is the red state.

- [ ] **Step 3: Implement `BallPhrases`**

`EchoRealm/Assets/Scripts/Sandbox/BallPhrases.cs`:
```csharp
namespace EchoRealm.Sandbox
{
    /// <summary>Pure utterance → intent matcher for the ball sandbox. Forgiving substring match
    /// (the HoloLens recognizer is noisy), mirroring the pocket/unpocket handling in
    /// VoiceCommandProcessor. No AI, no allocation beyond ToLowerInvariant.</summary>
    public static class BallPhrases
    {
        public enum Intent { None, Spawn, Remove }

        private static readonly string[] SpawnPhrases =
            { "drop a ball", "drop the ball", "spawn a ball", "spawn ball", "new ball", "give me a ball" };
        private static readonly string[] RemovePhrases =
            { "remove the ball", "remove ball", "remove balls", "delete the ball", "delete ball", "clear balls", "clear the balls" };

        public static Intent Match(string text)
        {
            if (string.IsNullOrEmpty(text)) return Intent.None;
            string t = text.ToLowerInvariant();
            foreach (var p in RemovePhrases) if (t.Contains(p)) return Intent.Remove;
            foreach (var p in SpawnPhrases) if (t.Contains(p)) return Intent.Spawn;
            return Intent.None;
        }
    }
}
```

- [ ] **Step 4: Implement `SandboxMath`**

`EchoRealm/Assets/Scripts/Sandbox/SandboxMath.cs`:
```csharp
using UnityEngine;

namespace EchoRealm.Sandbox
{
    /// <summary>Pure pose math for the ball sandbox. The ball lives in world space but is synced
    /// relative to the QR anchor so it stays co-located on the real floor across headsets — the
    /// same anchor-relative scheme FilmSync uses for SceneRoot.</summary>
    public static class SandboxMath
    {
        public static void ToAnchorRelative(Vector3 anchorPos, Quaternion anchorRot,
            Vector3 worldPos, Quaternion worldRot, out Vector3 relPos, out Quaternion relRot)
        {
            Quaternion inv = Quaternion.Inverse(anchorRot);
            relPos = inv * (worldPos - anchorPos);
            relRot = inv * worldRot;
        }

        public static void FromAnchorRelative(Vector3 anchorPos, Quaternion anchorRot,
            Vector3 relPos, Quaternion relRot, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = anchorPos + anchorRot * relPos;
            worldRot = anchorRot * relRot;
        }

        /// <summary>A spawn point in front of a head pose, flattened to horizontal and lowered to
        /// roughly chest height so the ball is immediately reachable.</summary>
        public static Vector3 SpawnPositionInFront(Vector3 headPos, Quaternion headRot, float forward, float down)
        {
            Vector3 fwd = headRot * Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
            return headPos + fwd * forward + Vector3.down * down;
        }
    }
}
```

- [ ] **Step 5: Run the self-check, expect PASS**

In Unity: menu **EchoRealm ▸ Ball Sandbox ▸ Run Self-Check**. Expected Console: `Self-Check PASSED (all assertions).`

- [ ] **Step 6: Commit**

```bash
git add EchoRealm/Assets/Scripts/Sandbox/BallPhrases.cs EchoRealm/Assets/Scripts/Sandbox/SandboxMath.cs EchoRealm/Assets/Scripts/Editor/BallSandboxSelfCheck.cs
git add EchoRealm/Assets/Scripts/Sandbox/BallPhrases.cs.meta EchoRealm/Assets/Scripts/Sandbox/SandboxMath.cs.meta EchoRealm/Assets/Scripts/Editor/BallSandboxSelfCheck.cs.meta
git commit -m "feat(sandbox): pure ball-phrase + anchor-pose logic with editor self-check"
```
(`.meta` files are generated by Unity when it imports the new scripts — add them so the repo stays consistent. Same for every task below.)

---

## Task 2: Generic speech interceptor hook (the one existing-file edit)

**Files:**
- Modify: `EchoRealm/Assets/Scripts/AI/VoiceCommandProcessor.cs` (add a field near the other public events; add one check at the top of `ProcessSpeechText`).

- [ ] **Step 1: Add the static delegate field**

In `VoiceCommandProcessor`, just after the `OnAIResponseReceived` event declaration (around line 48), add:
```csharp
        /// <summary>Optional first-chance interceptor for raw recognized speech. If it returns true,
        /// the utterance is fully consumed and is NOT forwarded to the AI / narrative / ActionCollector
        /// pipeline. Lets isolated modules (e.g. the ball sandbox) handle their own voice phrases
        /// without coupling this class to them. Null by default. Set by EchoRealm.Sandbox.BallVoiceHook.</summary>
        public static System.Func<string, bool> SpeechInterceptor;
```

- [ ] **Step 2: Add the early-return check**

In `ProcessSpeechText`, immediately after `OnSpeechRecognized?.Invoke(text);` (line 265), add:
```csharp
            // Isolated modules get first refusal on the raw utterance. If one consumes it, it dies
            // here — never reaching the AI, FilmSync, or ActionCollector. Keeps the ball sandbox
            // invisible to the narrative/variant decision.
            if (SpeechInterceptor != null && SpeechInterceptor(text)) return;
```

- [ ] **Step 3: Verify it compiles and is inert**

In Unity, Console shows no new errors. With no interceptor registered, behavior is unchanged (existing voice commands still work). Manual confirmation only.

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/AI/VoiceCommandProcessor.cs
git commit -m "feat(sandbox): generic SpeechInterceptor hook in voice dispatcher"
```

---

## Task 3: NetworkedBall (physics, authority, pose sync, throw)

**Files:**
- Create: `EchoRealm/Assets/Scripts/Sandbox/NetworkedBall.cs`

- [ ] **Step 1: Implement `NetworkedBall`**

`EchoRealm/Assets/Scripts/Sandbox/NetworkedBall.cs`:
```csharp
using Fusion;
using UnityEngine;
using EchoRealm.Networking; // QRAnchorManager

namespace EchoRealm.Sandbox
{
    /// <summary>
    /// A networked physics ball. Only the state authority simulates PhysX; its pose is published
    /// relative to the QR anchor and every other device displays that pose kinematically (same
    /// anchor-relative scheme as FilmSync). Authority transfers to whoever grabs it (same idiom as
    /// NetworkedTestCube). Throw velocity is tracked locally and applied on release.
    ///
    /// Prefab requirements (built by BallSandboxSetup): NetworkObject, Rigidbody, SphereCollider,
    /// MeshFilter+MeshRenderer, MRTK ObjectManipulator (events wired to OnGrabStart/OnGrabEnd),
    /// layer = BallSandbox.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkedBall : NetworkBehaviour
    {
        [Tooltip("Compensates for HoloLens hand-tracking under-reading release speed.")]
        [SerializeField] private float throwVelocityMultiplier = 1.3f;

        [Networked] public Vector3 AnchorRelPos { get; set; }
        [Networked] public Quaternion AnchorRelRot { get; set; }

        private Rigidbody _rb;
        private QRAnchorManager _anchor;
        private bool _held;
        private Vector3 _lastHeldPos;
        private Vector3 _heldVel;

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            _anchor = QRAnchorManager.Instance;
            ApplyAuthorityPhysicsState();

            // A late/remote copy snaps to the already-published pose immediately.
            if (!HasStateAuthority && _anchor != null && AnchorRelRot != default)
            {
                SandboxMath.FromAnchorRelative(_anchor.AnchorPosition, _anchor.AnchorRotation,
                    AnchorRelPos, AnchorRelRot, out var w, out var r);
                transform.SetPositionAndRotation(w, r);
            }
        }

        public override void FixedUpdateNetwork()
        {
            // Authority publishes its simulated (or hand-driven) world pose as anchor-relative truth.
            if (HasStateAuthority && _anchor != null)
            {
                SandboxMath.ToAnchorRelative(_anchor.AnchorPosition, _anchor.AnchorRotation,
                    transform.position, transform.rotation, out var p, out var r);
                AnchorRelPos = p;
                AnchorRelRot = r;
            }
        }

        public override void Render()
        {
            // Non-authority follows the networked pose (interpolation-free is fine at ball speeds;
            // upgrade to a NetworkTransform later if smoothing is needed).
            if (!HasStateAuthority && _anchor != null)
            {
                SandboxMath.FromAnchorRelative(_anchor.AnchorPosition, _anchor.AnchorRotation,
                    AnchorRelPos, AnchorRelRot, out var w, out var r);
                transform.SetPositionAndRotation(w, r);
            }
        }

        public override void StateAuthorityChanged() => ApplyAuthorityPhysicsState();

        private void Update()
        {
            // Track hand velocity while held so we can throw on release.
            if (!_held) return;
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector3 v = (transform.position - _lastHeldPos) / dt;
                _heldVel = Vector3.Lerp(_heldVel, v, 0.5f);
            }
            _lastHeldPos = transform.position;
        }

        /// <summary>Dynamic only when we own it and it's not in a hand; kinematic + follower otherwise.</summary>
        private void ApplyAuthorityPhysicsState()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            bool simulate = HasStateAuthority && !_held;
            _rb.isKinematic = !simulate;
            _rb.useGravity = simulate;
            if (simulate) _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        // --- Wired to MRTK ObjectManipulator events (see BallSandboxSetup manual step) ---

        public void OnGrabStart()
        {
            if (Object != null && Object.IsValid && !HasStateAuthority) Object.RequestStateAuthority();
            _held = true;
            _heldVel = Vector3.zero;
            _lastHeldPos = transform.position;
            if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; } // hand drives it
        }

        public void OnGrabEnd()
        {
            _held = false;
            ApplyAuthorityPhysicsState();
            if (_rb != null && !_rb.isKinematic)
                _rb.velocity = _heldVel * throwVelocityMultiplier; // the throw
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

In Unity, Console shows no errors. (Behavioral verification happens in Task 7 once the prefab exists.)

- [ ] **Step 3: Commit**

```bash
git add EchoRealm/Assets/Scripts/Sandbox/NetworkedBall.cs EchoRealm/Assets/Scripts/Sandbox/NetworkedBall.cs.meta
git commit -m "feat(sandbox): NetworkedBall with authority-gated physics, anchor pose sync, throw"
```

---

## Task 4: BallSpawner + BallVoiceHook

**Files:**
- Create: `EchoRealm/Assets/Scripts/Sandbox/BallSpawner.cs`
- Create: `EchoRealm/Assets/Scripts/Sandbox/BallVoiceHook.cs`

- [ ] **Step 1: Implement `BallSpawner`**

`EchoRealm/Assets/Scripts/Sandbox/BallSpawner.cs`:
```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using EchoRealm.Networking; // FusionNetworkManager

namespace EchoRealm.Sandbox
{
    /// <summary>Spawns/clears balls on the LOCAL device. In Shared Mode any client may spawn and
    /// owns what it spawns. Caps live count (evicts oldest). Decoupled from the film entirely.</summary>
    public class BallSpawner : MonoBehaviour
    {
        [Header("Prefab (NetworkObject + NetworkedBall)")]
        [SerializeField] private NetworkObject ballPrefab;

        [Header("Tuning")]
        [SerializeField] private int maxBalls = 5;
        [SerializeField] private float spawnForward = 0.5f; // metres in front of the head
        [SerializeField] private float spawnDown = 0.2f;    // metres below eye line (~chest)

        private readonly List<NetworkObject> _balls = new List<NetworkObject>();

        private NetworkRunner Runner =>
            FusionNetworkManager.Instance != null ? FusionNetworkManager.Instance.Runner : null;

        public void SpawnBall()
        {
            var runner = Runner;
            if (runner == null || !runner.IsRunning) { Debug.LogWarning("[BallSandbox] Runner not running — cannot spawn."); return; }
            if (ballPrefab == null) { Debug.LogError("[BallSandbox] ballPrefab not assigned."); return; }

            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[BallSandbox] No Camera.main — cannot place ball."); return; }

            Vector3 pos = SandboxMath.SpawnPositionInFront(
                cam.transform.position, cam.transform.rotation, spawnForward, spawnDown);

            _balls.RemoveAll(b => b == null);
            if (_balls.Count >= maxBalls)
            {
                var oldest = _balls[0];
                _balls.RemoveAt(0);
                if (oldest != null) runner.Despawn(oldest);
            }

            var ball = runner.Spawn(ballPrefab, pos, Quaternion.identity, runner.LocalPlayer);
            if (ball != null) { _balls.Add(ball); Debug.Log($"[BallSandbox] Spawned ball at {pos}. Live={_balls.Count}"); }
        }

        public void ClearBalls()
        {
            var runner = Runner;
            foreach (var b in _balls)
                if (b != null && runner != null) runner.Despawn(b); // only succeeds for balls we own
            _balls.Clear();
            Debug.Log("[BallSandbox] Cleared local balls.");
        }
    }
}
```

- [ ] **Step 2: Implement `BallVoiceHook`**

`EchoRealm/Assets/Scripts/Sandbox/BallVoiceHook.cs`:
```csharp
using UnityEngine;
using EchoRealm.AI; // VoiceCommandProcessor

namespace EchoRealm.Sandbox
{
    /// <summary>Registers a first-chance speech interceptor so ball phrases are handled locally and
    /// never reach the narrative AI / ActionCollector. This is the entire voice-isolation boundary.</summary>
    public class BallVoiceHook : MonoBehaviour
    {
        [SerializeField] private BallSpawner spawner;

        private void OnEnable()
        {
            if (spawner == null) spawner = FindObjectOfType<BallSpawner>();
            VoiceCommandProcessor.SpeechInterceptor = TryHandle;
        }

        private void OnDisable()
        {
            if (VoiceCommandProcessor.SpeechInterceptor == (System.Func<string, bool>)TryHandle)
                VoiceCommandProcessor.SpeechInterceptor = null;
        }

        /// <summary>Returns true (consumed) for ball phrases; false lets the utterance flow on normally.</summary>
        public bool TryHandle(string text)
        {
            switch (BallPhrases.Match(text))
            {
                case BallPhrases.Intent.Spawn:  spawner?.SpawnBall();  return true;
                case BallPhrases.Intent.Remove: spawner?.ClearBalls(); return true;
                default: return false;
            }
        }
    }
}
```

- [ ] **Step 3: Verify it compiles**

In Unity, Console shows no errors.

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/Sandbox/BallSpawner.cs EchoRealm/Assets/Scripts/Sandbox/BallVoiceHook.cs EchoRealm/Assets/Scripts/Sandbox/BallSpawner.cs.meta EchoRealm/Assets/Scripts/Sandbox/BallVoiceHook.cs.meta
git commit -m "feat(sandbox): BallSpawner (cap + co-located spawn) and BallVoiceHook (isolated voice)"
```

---

## Task 5: SpatialMeshManager

**Files:**
- Create: `EchoRealm/Assets/Scripts/Sandbox/SpatialMeshManager.cs`

- [ ] **Step 1: Implement `SpatialMeshManager`**

`EchoRealm/Assets/Scripts/Sandbox/SpatialMeshManager.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace EchoRealm.Sandbox
{
    /// <summary>Forces every AR-generated spatial mesh chunk onto the SpatialMesh layer with a
    /// MeshCollider + friction material, so the ball (and only the ball) bounces off the real room.
    /// Belt-and-suspenders even if the ARMeshManager.meshPrefab is misconfigured. Also toggles a
    /// debug wireframe.</summary>
    public class SpatialMeshManager : MonoBehaviour
    {
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private PhysicMaterial meshMaterial;   // low friction; bounce comes from the ball
        [SerializeField] private bool showWireframe = false;
        [SerializeField] private Material wireframeMaterial;

        private int _spatialLayer;

        private void Awake()
        {
            _spatialLayer = LayerMask.NameToLayer("SpatialMesh");
            if (_spatialLayer < 0) Debug.LogError("[BallSandbox] 'SpatialMesh' layer missing — run EchoRealm ▸ Ball Sandbox ▸ Setup Scene.");
            if (meshManager == null) meshManager = GetComponentInChildren<ARMeshManager>();
            if (meshManager != null) meshManager.meshesChanged += OnMeshesChanged;
        }

        private void OnDestroy()
        {
            if (meshManager != null) meshManager.meshesChanged -= OnMeshesChanged;
        }

        private void OnMeshesChanged(ARMeshesChangedEventArgs args)
        {
            Configure(args.added);
            Configure(args.updated);
        }

        private void Configure(List<MeshFilter> filters)
        {
            if (filters == null) return;
            foreach (var mf in filters)
            {
                if (mf == null) continue;
                var go = mf.gameObject;
                if (_spatialLayer >= 0) go.layer = _spatialLayer;

                var mc = go.GetComponent<MeshCollider>();
                if (mc == null) mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                if (meshMaterial != null) mc.material = meshMaterial;

                var r = go.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.enabled = showWireframe;
                    if (showWireframe && wireframeMaterial != null) r.sharedMaterial = wireframeMaterial;
                }
            }
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

In Unity, Console shows no errors. (`ARMeshManager`/`ARMeshesChangedEventArgs` resolve from AR Foundation 5.2, already installed.)

- [ ] **Step 3: Commit**

```bash
git add EchoRealm/Assets/Scripts/Sandbox/SpatialMeshManager.cs EchoRealm/Assets/Scripts/Sandbox/SpatialMeshManager.cs.meta
git commit -m "feat(sandbox): SpatialMeshManager (mesh colliders on SpatialMesh layer + wireframe)"
```

---

## Task 6: Editor setup script (layers, matrix, materials, prefabs, scene)

This builds all Unity assets/wiring that can't be done in C# runtime code. Run it once via menu, then complete the two clearly-marked manual steps.

**Files:**
- Create: `EchoRealm/Assets/Scripts/Editor/BallSandboxSetup.cs`

- [ ] **Step 1: Implement `BallSandboxSetup`**

`EchoRealm/Assets/Scripts/Editor/BallSandboxSetup.cs`:
```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Fusion;
using EchoRealm.Sandbox;

namespace EchoRealm.SandboxEditor
{
    /// <summary>One-click setup for the ball sandbox: layers, collision matrix, physic materials,
    /// the ball + mesh-chunk prefabs, and the scene objects. Idempotent — safe to re-run.</summary>
    public static class BallSandboxSetup
    {
        private const string MatDir = "Assets/Sandbox";
        private const string BallPrefabPath = "Assets/Sandbox/Ball.prefab";
        private const string ChunkPrefabPath = "Assets/Sandbox/SpatialMeshChunk.prefab";

        [MenuItem("EchoRealm/Ball Sandbox/Setup Scene")]
        public static void Setup()
        {
            EnsureLayer("SpatialMesh");
            EnsureLayer("BallSandbox");
            int ball = LayerMask.NameToLayer("BallSandbox");
            int mesh = LayerMask.NameToLayer("SpatialMesh");

            // Collision matrix: BallSandbox collides ONLY with SpatialMesh.
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(ball, i, true);       // ignore everything…
            }
            Physics.IgnoreLayerCollision(ball, mesh, false);       // …except the spatial mesh.

            EnsureDir(MatDir);
            var bouncy = EnsurePhysicMaterial("BallBouncy", 0.7f, 0.2f, PhysicMaterialCombine.Maximum, PhysicMaterialCombine.Minimum);
            var lowFric = EnsurePhysicMaterial("MeshLowFriction", 0f, 0.2f, PhysicMaterialCombine.Average, PhysicMaterialCombine.Minimum);

            var chunkPrefab = EnsureChunkPrefab(mesh, lowFric);
            var ballPrefab = EnsureBallPrefab(ball, bouncy);

            EnsureSceneObjects(ballPrefab, chunkPrefab, lowFric);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[BallSandbox] Setup complete. Now do the two MANUAL steps in the plan (ObjectManipulator events + OpenXR mesh feature).");
        }

        // ---- layers ----
        private static void EnsureLayer(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return; // already present
            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = name; tagManager.ApplyModifiedProperties(); return; }
            }
            Debug.LogError($"[BallSandbox] No free user layer slot for '{name}'.");
        }

        // ---- physic materials ----
        private static PhysicMaterial EnsurePhysicMaterial(string name, float bounce, float dynFric,
            PhysicMaterialCombine bounceCombine, PhysicMaterialCombine frictionCombine)
        {
            string path = $"{MatDir}/{name}.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
            if (m == null) { m = new PhysicMaterial(name); AssetDatabase.CreateAsset(m, path); }
            m.bounciness = bounce;
            m.dynamicFriction = dynFric;
            m.staticFriction = dynFric;
            m.bounceCombine = bounceCombine;
            m.frictionCombine = frictionCombine;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---- mesh chunk prefab (ARMeshManager.meshPrefab) ----
        private static GameObject EnsureChunkPrefab(int meshLayer, PhysicMaterial mat)
        {
            var go = new GameObject("SpatialMeshChunk", typeof(MeshFilter), typeof(MeshCollider));
            go.layer = meshLayer;
            go.GetComponent<MeshCollider>().material = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ChunkPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---- ball prefab ----
        private static GameObject EnsureBallPrefab(int ballLayer, PhysicMaterial mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); // sphere mesh + SphereCollider
            go.name = "Ball";
            go.layer = ballLayer;
            go.transform.localScale = Vector3.one * 0.12f;
            go.GetComponent<SphereCollider>().material = mat;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkedBall>();
            // NOTE: MRTK ObjectManipulator is added + its events wired in the MANUAL step below
            // (its API/event names vary by MRTK3 version; wiring 2 events by hand is the test-cube convention).

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, BallPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---- scene objects ----
        private static void EnsureSceneObjects(GameObject ballPrefab, GameObject chunkPrefab, PhysicMaterial meshMat)
        {
            var root = GameObject.Find("BallSandbox") ?? new GameObject("BallSandbox");

            var spawner = root.GetComponent<BallSpawner>() ?? root.AddComponent<BallSpawner>();
            SetPrivate(spawner, "ballPrefab", ballPrefab.GetComponent<NetworkObject>());

            var hook = root.GetComponent<BallVoiceHook>() ?? root.AddComponent<BallVoiceHook>();
            SetPrivate(hook, "spawner", spawner);

            // Spatial mesh holder. ARMeshManager is added in the MANUAL step (must sit under the XR Origin).
            var meshGo = GameObject.Find("SpatialMesh") ?? new GameObject("SpatialMesh");
            var smm = meshGo.GetComponent<SpatialMeshManager>() ?? meshGo.AddComponent<SpatialMeshManager>();
            SetPrivate(smm, "meshMaterial", meshMat);
        }

        // ---- helpers ----
        private static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Sandbox");
        }

        private static void SetPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop != null) { prop.objectReferenceValue = value; so.ApplyModifiedProperties(); }
            else Debug.LogWarning($"[BallSandbox] Field '{field}' not found on {target.GetType().Name}.");
        }
    }
}
```

- [ ] **Step 2: Run the setup menu**

In Unity: **EchoRealm ▸ Ball Sandbox ▸ Setup Scene**. Expected: layers created, `Assets/Sandbox/` has 2 physic materials + `Ball.prefab` + `SpatialMeshChunk.prefab`, and the scene has `BallSandbox` (with `BallSpawner`+`BallVoiceHook`) and `SpatialMesh` (with `SpatialMeshManager`) objects.

- [ ] **Step 3: MANUAL — wire the grab events (same as the test cube)**

Open `Assets/Sandbox/Ball.prefab`. Add the MRTK3 **ObjectManipulator** component. On it:
- `OnManipulationStarted` → drag the prefab root, choose `NetworkedBall ▸ OnGrabStart`.
- `OnManipulationEnded` → drag the prefab root, choose `NetworkedBall ▸ OnGrabEnd`.

Save the prefab. (This mirrors how `NetworkedTestCube` is wired; doing it by hand avoids MRTK version-specific event-wiring code.)

- [ ] **Step 4: MANUAL — AR mesh rig + OpenXR feature**

1. Select the `SpatialMesh` scene object, make it a child of your **XR Origin** (the MRTK XR Rig's origin), reset its local transform.
2. Add an **ARMeshManager** component to it. Set its `Mesh Prefab` = `Assets/Sandbox/SpatialMeshChunk.prefab`. Drag the ARMeshManager into the `SpatialMeshManager.meshManager` field.
3. **Project Settings ▸ XR Plug-in Management ▸ OpenXR** (UWP/HoloLens tab): enable the spatial-mesh / scene-understanding feature provided by the Microsoft OpenXR plugin so the mesh subsystem produces data on-device.

- [ ] **Step 5: Commit the scripts (you commit the assets/scene with your scene work)**

```bash
git add EchoRealm/Assets/Scripts/Editor/BallSandboxSetup.cs EchoRealm/Assets/Scripts/Editor/BallSandboxSetup.cs.meta
git commit -m "feat(sandbox): editor setup (layers, matrix, materials, ball/mesh prefabs, scene wiring)"
```
Then stage the generated assets when you're ready: `git add EchoRealm/Assets/Sandbox/` and your `MainScene.unity` / `ProjectSettings` changes — these intermix with your rewind work, as discussed.

---

## Task 7: In-Editor verification (stand-in floor)

No device needed. Proves spawn, gravity, bounce, throw arc, the cap, and isolation.

- [ ] **Step 1: Add a stand-in mesh.** In the scene, create a large Cube (e.g. scale 6×0.1×6) at the floor, set its layer to `SpatialMesh`, give its collider the `MeshLowFriction` material. Add a second Cube on the `Default` layer in front of the spawn point (the "scene prop" control).
- [ ] **Step 2: Enter Play mode** with networking started (your normal Fusion start flow).
- [ ] **Step 3: Trigger spawn.** Use `DebugVoiceInput` or a temporary call to `BallSpawner.SpawnBall()`. Expected: a ball appears in front of the camera, falls, and **bounces** on the stand-in floor.
- [ ] **Step 4: Throw.** Move the ball with the mouse/grab sim and release while moving (or set `_rb.velocity` via a debug button). Expected: a visible **parabolic arc**, then a bounce.
- [ ] **Step 5: Isolation — physical.** Expected: the ball passes **through** the `Default`-layer prop cube (no collision).
- [ ] **Step 6: Isolation — AI.** Before/after spawning several balls and saying a ball phrase through `DebugVoiceInput`, check `ActionCollector.Instance.Profile` counts (voice/world/nurture/chaos) are **unchanged** by ball activity. (Add a temporary `Debug.Log` of the profile if needed.)
- [ ] **Step 7: Cap.** Spawn 6 balls; expected only **5** live (oldest despawns). Say a remove phrase; expected all clear.
- [ ] **Step 8:** Remove the stand-in cubes (or keep them behind an editor-only flag). No commit required unless you scripted helpers.

---

## Task 8: On-device verification (two HoloLenses)

- [ ] **Step 1:** Deploy to both headsets; establish the shared session via the QR anchor as usual.
- [ ] **Step 2:** Look around each room area once so the spatial mesh scans.
- [ ] **Step 3:** Say **"drop a ball"** on headset A. Expected: the ball appears at the **same physical spot** on both headsets and bounces on the real floor.
- [ ] **Step 4:** Grab and throw it on A. Expected: B sees the motion and the bounce off real surfaces (off A's mesh — the accepted trade-off).
- [ ] **Step 5:** Grab it on B. Expected: authority transfers; A now sees B's motion.
- [ ] **Step 6:** Move the film/`SceneRoot` elsewhere. Expected: the ball **stays on the real floor** (anchor-framed), unaffected; the film is unaffected by the ball.
- [ ] **Step 7:** Say **"remove the ball."** Expected: balls despawn.
- [ ] **Step 8 (optional):** Toggle `SpatialMeshManager.showWireframe` to demo bouncing off the real room for the defense.

---

## Self-review (done at write time)

- **Spec coverage:** voice trigger (T1,T2,T4) · isolation: layers/matrix (T6), QR-anchor frame (T3), no ActionCollector/FilmSync (T2,T4 + verified T7) · networking spawn+authority+sync (T3,T4) · physics gravity/arc/bounce/continuous/throw-multiplier (T3,T6) · spatial mesh ARMeshManager (T5,T6) · defaults table (T6 values) · acceptance criteria (T7,T8). All spec sections map to a task.
- **Placeholder scan:** no TBD/TODO; every code step has complete code; manual steps give exact clicks, not "configure appropriately."
- **Type consistency:** `BallPhrases.Match`/`Intent`, `SandboxMath.ToAnchorRelative`/`FromAnchorRelative`/`SpawnPositionInFront`, `NetworkedBall.OnGrabStart`/`OnGrabEnd`/`AnchorRelPos`/`AnchorRelRot`, `BallSpawner.SpawnBall`/`ClearBalls`/`ballPrefab`, `BallVoiceHook.TryHandle`/`spawner`, `SpatialMeshManager.meshManager`/`meshMaterial`, `VoiceCommandProcessor.SpeechInterceptor` — used identically across tasks.
- **Known risk:** MRTK3 ObjectManipulator event wiring and the AR mesh rig / OpenXR feature are environment-specific → made explicit manual steps with verification, not code, to avoid version-fragile automation.
