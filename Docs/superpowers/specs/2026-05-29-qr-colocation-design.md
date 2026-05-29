# QR-Based Anchor-Relative Co-location — Design Spec

**Date:** 2026-05-29
**Project:** EchoRealm (MR interactive film, HoloLens 2, Unity 2022.3 + MRTK3 + Photon Fusion 2)
**Status:** Approved design — ready for implementation plan

---

## 1. Problem

Two HoloLens 2 headsets already connect to the same Photon Fusion 2 session (Shared Mode, region `eu`) and recognize each other (`Players: 2`, verified in logs). Networked objects replicate correctly.

But there is **no shared physical origin**. Each headset sets its Unity world origin wherever it booted. Fusion's `NetworkTransform` syncs **world** coordinates, so a networked object at world `(0, 0.3, 0.5)` lands in a *different real-world location* on each headset. Result: both users see the cube and its movements, but it floats in a different place in the room for each of them.

## 2. Goal / Success Criteria

**Everything the scene generates appears on the CLIENT headset in the same physical real-world spot as on the MASTER headset** (master = first device to run the scene). This applies to the test cube now, and to all future co-located content (Oracle, Dobby, weather, effects).

Concretely, success =
- Both headsets scan the same physical QR code.
- The master spawns the test cube; it appears at the **same physical location** on both headsets.
- Moving/grabbing the cube keeps it aligned on both.

## 3. Chosen Approach: A — Anchor-Relative Content

(Considered and rejected: **B — rebase world origin to QR** — universal but must rebase before any spawn, both devices consistently, and risks MRTK rig side-effects; **C — manual `[Networked]` anchor-relative coordinates per object** — explicit but most per-object code to maintain.)

**Core principle:** one `SceneRoot` transform per device, aligned to the shared physical QR code. **Everything that must co-locate is a child of `SceneRoot`.**

- **Networked objects** (cube, future networked content): Fusion 2's `NetworkTransform` stores TRS in **local space**, so once an object is parented under `SceneRoot`, its synced pose is anchor-relative. Both `SceneRoot`s sit at the same physical QR pose → identical physical placement.
- **Non-networked local content** (weather, future Oracle): co-locates simply by being a child of `SceneRoot`.
- **Late-scan tolerance:** because content is a *child* of `SceneRoot`, if a headset scans the QR late, `SceneRoot` moves and all children follow — content snaps into alignment whenever the scan happens.

## 4. Fusion 2 Specifics (verified against Photon docs)

1. **`NetworkTransform` stores TRS in local space** (Fusion 2 change from v1). This is what makes Approach A work without custom networking code.
2. **The "Sync Parent" toggle is NOT used.** It requires the parent transform to have a `NetworkBehaviour`; `SceneRoot` is a plain scene object. Instead, **each device re-parents the object under its own local `SceneRoot`** in `Spawned()`.
3. **Interest Management (AOI) caveat:** a `NetworkTransform` child of a non-networked parent at a non-zero position confuses AOI culling (child local pos used as global). AOI is **off by default** in a small 2-player Shared session, so we leave it off. If AOI is ever enabled, use `AreaOfInterestOverride`. Documented, not a blocker.

## 5. Components & Changes

### 5.1 `Assets/Scripts/Networking/QRAnchorManager.cs`
- Add a static `Instance` and a public `Transform SceneRoot { get; }` getter so networked objects can find the anchor.
- Keep existing detect → `AlignSceneToQR(pos, rot)` logic (sets `sceneRoot.position/rotation` to the QR pose, fires `OnAnchorEstablished`).
- Optionally lock onto the first solid detection (ignore later re-reads) to avoid jitter. The existing code already re-processes `updated` markers only while `!IsAnchored`, which is sufficient; locking is a nice-to-have.

### 5.2 `Assets/Scripts/Testing/NetworkedTestCube.cs` (key change)
- In `Spawned()`, on **every** device: parent under the local anchor:
  ```csharp
  var root = QRAnchorManager.Instance != null ? QRAnchorManager.Instance.SceneRoot : null;
  if (root != null) transform.SetParent(root, worldPositionStays: false);
  ```
- Rely on the prefab's local-space `NetworkTransform` (no "Sync Parent").
- Guard against a null/missing `SceneRoot` (log a warning, leave in world space).

### 5.3 `Assets/Scripts/Testing/TestCubeSpawner.cs`
- Master still spawns via `Runner.Spawn`. The cube parents itself under `SceneRoot` in its own `Spawned()` (5.2); the master sets `transform.localPosition = spawnOffset` so the offset is **anchor-relative** rather than a world position.
- `anchorTransform` should reference `SceneRoot` (verify in scene).

### 5.4 Scene wiring (`Assets/Scenes/MainScene.unity`)
- Universal rule: **co-located content is a child of `SceneRoot`.**
- Verify/parent the existing `WeatherEffects` and `EnvironmentEffects` under `SceneRoot`.
- Confirm `QRAnchorManager.sceneRoot` and `TestCubeSpawner.anchorTransform` both reference the `SceneRoot` object.
- Confirm the cube prefab has a `NetworkObject` + `NetworkTransform`.

### 5.5 Boot flow / QR requirement (`EchoRealmBootstrapper.cs`)
- Co-location **requires both headsets to scan the QR.** The existing 8s `QRTimeoutFallback` means "proceed un-anchored" → no co-location.
- Keep the timeout for solo dev testing, but make behavior explicit: a `requireQR` flag (default off for dev, on for real multi-device sessions) and clear status text distinguishing "anchored" vs "continuing without spatial anchor."

### 5.6 Physical QR code (user-side prerequisite)
- Print or display a QR code encoding the exact text **`EchoRealm-Anchor`** (matches `QRAnchorManager.expectedQRData`), at least ~5 cm wide (≥ `minQRSizeMeters` = 0.05 m), placed flat where both users can look at it during boot.

## 6. Data Flow

1. Boot → `QRAnchorManager` detects QR → `AlignSceneToQR` sets `SceneRoot` to the QR pose (both devices → same physical pose, different world coords).
2. Master spawns cube → cube's `Spawned()` parents it under master's `SceneRoot`; master sets `localPosition = spawnOffset`.
3. Fusion syncs the cube's **local** pose to the client.
4. Client receives cube → `Spawned()` parents it under client's `SceneRoot` → `NetworkTransform` applies the synced local pose → **same physical spot**.
5. Either user grabs → `RequestStateAuthority` → local pose syncs → stays co-located.

## 7. Testing

- **Editor:** `QRAnchorManager` already simulates an anchor at origin in play mode (unchanged).
- **Single device:** scan QR → confirm `SceneRoot` jumps to the code; cube spawns at the offset from it.
- **Two devices:** both scan the *same* code → master spawns → cube appears at the **same physical spot** on both; grab/move → stays aligned. Watch logs for spawn/parent confirmation.
- **Late scan:** scan on one device after content exists → content snaps into place (child-of-SceneRoot).

## 8. Edge Cases

- **No scan on a device:** that headset shows content relative to its own boot origin (graceful degradation), with a clear warning logged.
- **QR re-detected/moved:** only re-align while `!IsAnchored` (or lock after first solid detection) to avoid jitter.
- **Cube spawned before any scan:** parented under `SceneRoot` at origin; follows `SceneRoot` when the scan later aligns it.
- **SceneRoot missing/unassigned:** `QRAnchorManager` already logs an error; `NetworkedTestCube` guards and leaves the object in world space.

## 9. Out of Scope (this round)

- Networking the film content itself (Oracle/Dobby/acts/AI decisions) — separate future work; this spec only establishes the shared spatial frame they will later sit in.
- Anchor persistence across sessions (Azure Spatial Anchors / world-locking beyond the QR).
- Rebasing the world origin (Approach B) or per-object manual relative sync (Approach C).

## 10. Prerequisites Summary

1. A physical QR code encoding `EchoRealm-Anchor` (≥ 5 cm), visible to both users at boot.
2. Both headsets on the same Wi-Fi, same build deployed.
