# QR Anchor-Relative Co-location Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make everything the scene generates appear at the same physical real-world location on both HoloLens headsets, by anchoring all co-located content to a shared QR code.

**Architecture:** Each device aligns a single `SceneRoot` transform to a shared physical QR code (already coded in `QRAnchorManager`). All co-located content becomes a child of `SceneRoot`. Networked objects rely on Fusion 2's local-space `NetworkTransform`, so their synced pose is anchor-relative and lands at the same physical spot on every headset.

**Tech Stack:** Unity 2022.3 LTS (Built-In RP), MRTK3 3.3, Photon Fusion 2 (Shared Mode), Mixed Reality OpenXR Plugin (`ARMarkerManager` for QR), UWP/ARM64 for HoloLens 2.

**Verification note:** There is no CLI test runner for this project. "Verify" means one of: (a) **Compile** — Unity Editor recompiles and the Console shows no errors; (b) **Editor** — enter Play Mode and observe logged behavior (the editor simulates the anchor at origin); (c) **Device** — deploy to HoloLens and follow the manual test procedure. Each task states which.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Scripts/Networking/QRAnchorManager.cs` | Detect QR, align `SceneRoot`, expose anchor | Add static `Instance` + public `SceneRoot` getter |
| `Assets/Scripts/Testing/NetworkedTestCube.cs` | Networked cube; co-location parenting | Parent under `SceneRoot` in `Spawned()`; local-space `RandomizePosition` |
| `Assets/Scripts/Testing/TestCubeSpawner.cs` | Master spawns the cube | Spawn anchor-relative (set `localPosition`) |
| `Assets/Scripts/Film/EchoRealmBootstrapper.cs` | Boot sequence / QR gate | Add `requireQR` flag + clearer status |
| `Assets/Scenes/MainScene.unity` | Scene wiring | Parent co-located content under `SceneRoot`; verify references + prefab |

---

## Task 1: Expose the anchor from QRAnchorManager

**Files:**
- Modify: `Assets/Scripts/Networking/QRAnchorManager.cs`

- [ ] **Step 1: Add a static `Instance` and a public `SceneRoot` getter**

In `QRAnchorManager.cs`, find the properties block:

```csharp
        /// <summary>True after the anchor has been established from a QR code.</summary>
        public bool IsAnchored { get; private set; }

        /// <summary>Fired when the QR anchor is successfully established.</summary>
        public event System.Action OnAnchorEstablished;
```

Replace it with:

```csharp
        /// <summary>True after the anchor has been established from a QR code.</summary>
        public bool IsAnchored { get; private set; }

        /// <summary>Fired when the QR anchor is successfully established.</summary>
        public event System.Action OnAnchorEstablished;

        /// <summary>Singleton access so networked objects can find the anchor to parent under.</summary>
        public static QRAnchorManager Instance { get; private set; }

        /// <summary>The shared anchor transform. All co-located content must be a child of this.</summary>
        public Transform SceneRoot => sceneRoot;
```

- [ ] **Step 2: Set `Instance` in `Awake` (before any `Start`/`Spawned` runs)**

`QRAnchorManager` currently has no `Awake` method (it starts in `Start`). Add one immediately above the existing `private void Start()`:

```csharp
        private void Awake()
        {
            Instance = this;
        }

        private void Start()
```

- [ ] **Step 3: Verify (Compile)**

Switch to the Unity Editor and let it recompile. Open **Window → General → Console**.
Expected: no compile errors. `QRAnchorManager.Instance` and `.SceneRoot` now resolve.

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/Networking/QRAnchorManager.cs
git commit -m "feat(qr): expose QRAnchorManager.Instance and SceneRoot for co-location"
```

---

## Task 2: Parent the networked cube under the anchor

**Files:**
- Modify: `Assets/Scripts/Testing/NetworkedTestCube.cs`

- [ ] **Step 1: Re-parent under the local SceneRoot in `Spawned()`**

In `NetworkedTestCube.cs`, replace the entire `Spawned()` method:

```csharp
        public override void Spawned()
        {
            Log($"Cube spawned. HasStateAuthority={HasStateAuthority}, InputAuthority={Object.InputAuthority}");

            // Tint differently on master vs. client so you can visually tell
            // which machine is the "authoritative" spawner at a glance.
            if (cubeRenderer != null)
            {
                bool isMaster = Runner != null && Runner.IsSharedModeMasterClient;
                cubeRenderer.material.color = isMaster ? masterColor : clientColor;
            }
        }
```

with:

```csharp
        public override void Spawned()
        {
            Log($"Cube spawned. HasStateAuthority={HasStateAuthority}, InputAuthority={Object.InputAuthority}");

            // CO-LOCATION: parent under the local SceneRoot (aligned to the shared QR code).
            // Fusion 2's NetworkTransform syncs LOCAL space, so the synced pose becomes
            // anchor-relative and lands at the same physical spot on every headset.
            var anchor = EchoRealm.Networking.QRAnchorManager.Instance;
            if (anchor != null && anchor.SceneRoot != null)
            {
                transform.SetParent(anchor.SceneRoot, worldPositionStays: false);
                Log($"Parented under SceneRoot '{anchor.SceneRoot.name}'. localPos={transform.localPosition}");
            }
            else
            {
                Log("QRAnchorManager/SceneRoot not found — cube stays in world space (NOT co-located).");
            }

            // Tint differently on master vs. client so you can visually tell
            // which machine is the "authoritative" spawner at a glance.
            if (cubeRenderer != null)
            {
                bool isMaster = Runner != null && Runner.IsSharedModeMasterClient;
                cubeRenderer.material.color = isMaster ? masterColor : clientColor;
            }
        }
```

- [ ] **Step 2: Make `RandomizePosition` move in local (anchor-relative) space**

In `NetworkedTestCube.cs`, find in `RandomizePosition()`:

```csharp
            transform.position += offset;
            Log($"Randomized position to {transform.position}");
```

Replace with:

```csharp
            transform.localPosition += offset; // local space keeps it anchor-relative
            Log($"Randomized local position to {transform.localPosition}");
```

- [ ] **Step 3: Verify (Compile)**

Return to Unity, let it recompile, check the Console.
Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/Testing/NetworkedTestCube.cs
git commit -m "feat(coloc): parent networked cube under SceneRoot; randomize in local space"
```

---

## Task 3: Spawn the cube at an anchor-relative offset

**Files:**
- Modify: `Assets/Scripts/Testing/TestCubeSpawner.cs`

- [ ] **Step 1: Replace world-space spawn with anchor-relative offset**

In `TestCubeSpawner.cs`, find this block inside `SpawnCube()`:

```csharp
            Vector3 worldPos = anchorTransform != null
                ? anchorTransform.TransformPoint(spawnOffset)
                : spawnOffset;

            Quaternion rot = anchorTransform != null
                ? anchorTransform.rotation
                : Quaternion.identity;

            spawnedCube = network.Runner.Spawn(cubePrefab, worldPos, rot, network.Runner.LocalPlayer);
            Log($"Spawned cube at {worldPos} (anchor: {(anchorTransform != null ? anchorTransform.name : "world origin")})");
```

Replace with:

```csharp
            // Spawn at origin; NetworkedTestCube.Spawned() parents it under SceneRoot on
            // every device. As the state authority, set the anchor-relative offset here —
            // Fusion's local-space NetworkTransform syncs it to all clients.
            spawnedCube = network.Runner.Spawn(cubePrefab, Vector3.zero, Quaternion.identity, network.Runner.LocalPlayer);

            if (spawnedCube != null)
            {
                spawnedCube.transform.localPosition = spawnOffset;
                spawnedCube.transform.localRotation = Quaternion.identity;
                Log($"Spawned cube at SceneRoot-local offset {spawnOffset}");
            }
```

- [ ] **Step 2: Verify (Compile)**

Return to Unity, recompile, check the Console.
Expected: no compile errors. (`anchorTransform` is still used for the null-check elsewhere; no unused-variable warnings expected since we removed `worldPos`/`rot`.)

- [ ] **Step 3: Commit**

```bash
git add EchoRealm/Assets/Scripts/Testing/TestCubeSpawner.cs
git commit -m "feat(coloc): spawn test cube at anchor-relative offset under SceneRoot"
```

---

## Task 4: Add a requireQR gate to the bootstrapper

**Files:**
- Modify: `Assets/Scripts/Film/EchoRealmBootstrapper.cs`

- [ ] **Step 1: Add the `requireQR` serialized field**

In `EchoRealmBootstrapper.cs`, find:

```csharp
        [Tooltip("If QR code isn't detected after this many seconds, skip QR anchoring and continue with default origin.")]
        [SerializeField] private float qrTimeoutSeconds = 8f;
```

Add immediately below it:

```csharp
        [Tooltip("If true, the app will NOT proceed until a QR code is scanned (required for multi-device co-location). " +
                 "If false, it skips QR after the timeout and runs un-anchored (solo/dev testing).")]
        [SerializeField] private bool requireQR = false;
```

- [ ] **Step 2: Honor the flag in the timeout fallback**

In `EchoRealmBootstrapper.cs`, replace the entire `QRTimeoutFallback` coroutine:

```csharp
        private System.Collections.IEnumerator QRTimeoutFallback()
        {
            yield return new WaitForSeconds(qrTimeoutSeconds);
            if (!qrAnchorHandled)
            {
                Debug.LogWarning($"[Boot] QR code not detected after {qrTimeoutSeconds}s. Skipping QR anchoring and continuing with default origin.");
                SetStatus("No QR code detected.\nContinuing without spatial anchor...");
                OnQRAnchorEstablished();
            }
        }
```

with:

```csharp
        private System.Collections.IEnumerator QRTimeoutFallback()
        {
            yield return new WaitForSeconds(qrTimeoutSeconds);
            if (qrAnchorHandled) yield break;

            if (requireQR)
            {
                // Multi-device co-located session: do NOT proceed un-anchored.
                Debug.LogWarning($"[Boot] QR not detected after {qrTimeoutSeconds}s. requireQR=true → still waiting for the shared QR anchor.");
                SetStatus("Still scanning for QR code...\nCo-location needs the shared QR anchor.\nLook at the EchoRealm-Anchor code.");
                yield break; // a real QR detection will fire OnQRAnchorEstablished and continue boot
            }

            Debug.LogWarning($"[Boot] QR code not detected after {qrTimeoutSeconds}s. requireQR=false → continuing un-anchored (NOT co-located).");
            SetStatus("No QR code detected.\nContinuing without spatial anchor...");
            OnQRAnchorEstablished();
        }
```

- [ ] **Step 3: Verify (Compile)**

Return to Unity, recompile, check the Console.
Expected: no compile errors. New **Require QR** checkbox appears on the `EchoRealmBootstrapper` component (default unchecked).

- [ ] **Step 4: Commit**

```bash
git add EchoRealm/Assets/Scripts/Film/EchoRealmBootstrapper.cs
git commit -m "feat(boot): add requireQR gate so co-located sessions wait for the QR anchor"
```

---

## Task 5: Scene wiring + prefab verification (Unity Editor, manual)

**Files:**
- Modify: `Assets/Scenes/MainScene.unity` (via the Editor — do not hand-edit YAML)

- [ ] **Step 1: Confirm the anchor references point at SceneRoot**

In the Unity **Hierarchy**, select the object holding **QRAnchorManager**.
- In the Inspector, confirm **Scene Root** = the `SceneRoot` GameObject. If empty, drag `SceneRoot` in.

Select the **TestCubeSpawner** object.
- Confirm **Anchor Transform** = `SceneRoot`. If empty, drag it in.

- [ ] **Step 2: Verify the cube prefab has the networking components**

In the Project window, locate the cube prefab assigned to `TestCubeSpawner.cubePrefab` (double-click it to open). Confirm it has:
- `NetworkObject`
- `NetworkTransform` — and confirm **Sync Parent is NOT enabled** (we re-parent manually; SceneRoot has no NetworkBehaviour)
- `NetworkedTestCube`
- A `BoxCollider` + MRTK `ObjectManipulator` (for grabbing)

No change needed if all present; this is a confirmation step.

- [ ] **Step 3: Parent co-located content under SceneRoot**

In the Hierarchy, drag these under `SceneRoot` (so they co-locate too), if they aren't already children:
- `WeatherEffects`
- `EnvironmentEffects`

Do NOT parent: `GameManager`, `Canvas`, `EventSystem`, the MRTK XR Rig, or `QRAnchorManager` — those are app/UI infrastructure, not world content.

- [ ] **Step 4: Save the scene**

`Ctrl+S`.

- [ ] **Step 5: Verify (Editor Play Mode)**

Press **Play** in the Editor. In the Console, expect:
- `[QRAnchor] Running in Editor — simulating QR anchor at world origin.`
- No NullReference errors from `QRAnchorManager.Instance` or `NetworkedTestCube`.

Stop Play Mode.

- [ ] **Step 6: Commit**

```bash
git add EchoRealm/Assets/Scenes/MainScene.unity
git commit -m "chore(scene): parent world content under SceneRoot; wire anchor references"
```

---

## Task 6: Build, deploy, and on-device co-location test

**Files:** none (build + manual test)

- [ ] **Step 1: Prepare the physical QR code**

Generate a QR code encoding the exact text **`EchoRealm-Anchor`** (any free QR generator). Print or display it at least **5 cm** wide. Place it flat (table/wall) where both users can look at it.

- [ ] **Step 2: (Optional) Enable requireQR for a strict co-located test**

In the Editor, select the `EchoRealmBootstrapper` object → check **Require QR** → `Ctrl+S`. (Leave unchecked if you want the 8s fallback for a quick solo run.)

- [ ] **Step 3: Build in Unity**

`File → Build Settings → Build` → output to `Builds/Hololens`. Wait for completion.

- [ ] **Step 4: Deploy to HoloLens A (master)**

Open `Builds/Hololens/EchoRealm.sln` in Visual Studio → `Release` / `ARM64` / `Remote Machine`. Set Machine Name to HoloLens A's IP. **Ctrl+F5** (run without debugger so it keeps running).

- [ ] **Step 5: Deploy to HoloLens B (client)**

Change the project's **Machine Name** to HoloLens B's IP. **F5** (with debugger, to watch B's logs in the VS Output window).

- [ ] **Step 6: Both scan the QR**

Have both headsets look at the `EchoRealm-Anchor` code. Expect in each log:
```
[QRAnchor] QR detected: data='EchoRealm-Anchor', size=...
[QRAnchor] QR Anchor ESTABLISHED. Scene is now spatially aligned.
```

- [ ] **Step 7: Spawn and verify co-location**

On HoloLens A (master), press **Spawn** (the buttons should now be pressable after the earlier raycaster fix). Verify:
- The cube appears on **both** headsets.
- It is in the **same physical location** in the room for both users (stand side by side to confirm).
- Press **Randomize** on the master → the cube jumps to the **same new physical spot** on both.
- Reach out and **grab** the cube on either headset → it moves on both and stays co-located.

Expected client log:
```
[TestCube] Cube spawned. ...
[TestCube] Parented under SceneRoot 'SceneRoot'. localPos=(0.0, 0.3, 0.5)
```

- [ ] **Step 8: Commit any tuning**

If you adjusted `spawnOffset`, `minQRSizeMeters`, or `requireQR` during testing, commit:

```bash
git add EchoRealm/Assets/Scenes/MainScene.unity EchoRealm/Assets/Scripts/Testing/TestCubeSpawner.cs
git commit -m "chore(coloc): tune spawn offset / QR settings after on-device test"
```

---

## Self-Review

**Spec coverage:**
- §5.1 QRAnchorManager Instance/SceneRoot → Task 1 ✓
- §5.2 NetworkedTestCube re-parent in Spawned() → Task 2 ✓
- §5.3 TestCubeSpawner anchor-relative spawn → Task 3 ✓
- §5.4 Scene wiring (parent content, verify refs + prefab) → Task 5 ✓
- §5.5 Boot flow requireQR → Task 4 ✓
- §5.6 Physical QR code → Task 6 Step 1 ✓
- §7 Testing → Task 5 Step 5 (Editor) + Task 6 (device) ✓
- §4 Fusion specifics (no Sync Parent) → Task 5 Step 2 (confirm Sync Parent off) ✓

**Placeholder scan:** No TBD/TODO; every code step shows full before/after. ✓

**Type/name consistency:** `QRAnchorManager.Instance`, `QRAnchorManager.SceneRoot`, `requireQR`, `spawnOffset`, `cubePrefab`, `anchorTransform` used consistently across tasks and match existing source. ✓
