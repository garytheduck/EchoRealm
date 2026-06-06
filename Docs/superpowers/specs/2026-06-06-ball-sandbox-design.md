# Ball Sandbox — Design Spec

- **Date:** 2026-06-06
- **Status:** Approved design, pre-implementation
- **Scope:** A — self-contained tech demo / thesis experiment
- **Branch context:** added on top of `feature/scene-save-rewind` (separate module, no interaction with rewind)

## Goal

Add a fully isolated, networked physics ball to the existing EchoRealm MR experience. On a
voice phrase, a ball appears in front of the speaker, obeys gravity, can be grabbed, dropped,
thrown, and caught, and bounces off the **real, HoloLens-scanned room surfaces**. It is shared
across every headset in the session (Photon Fusion 2 Shared Mode), co-located on the real floor
via the existing QR anchor.

The ball is a closed sandbox: it **cannot** physically touch EchoRealm scene content, and nothing
the user does with the ball can influence the AI's narrative/variant decisions.

## Non-goals (explicitly out of scope)

- No tie-in to the film, acts, rewind, pocket, cooperation, or narrative AI.
- No persistence across sessions; balls vanish on session end.
- Not a reusable physics framework — one object type (a ball), minimal polish.
- No per-device mesh reconciliation (see Known Limitations).

## The three isolation guarantees

1. **No physical contact with the scene.** Unity layer-gated collision. The ball is on a dedicated
   `BallSandbox` layer; the scan is on a `SpatialMesh` layer. The collision matrix allows **only**
   `BallSandbox ↔ SpatialMesh`. The ball passes straight through every film prop; no EchoRealm
   object or layer is modified.

2. **The scene is not constrained by the mesh — only the ball is.** EchoRealm content under
   `SceneRoot` is never physically simulated (it is hand-placed/scaled). The mesh colliders only
   interact with the `BallSandbox` layer, so the scene can be placed/scaled anywhere, overlapping
   real walls freely. The ball is the only thing that respects the room.

3. **The AI never observes the ball.** The narrative/variant decision is fed exclusively through
   `ActionCollector.Record*()` (see `Assets/Scripts/AI/ActionCollector.cs`), reached only via
   `FilmSync.SubmitSpeech` → master → `RecordVoiceCommand`/`RecordWorldChange`. The ball module
   calls `ActionCollector` **never**, routes through `FilmSync` narrative RPCs **never**, and never
   uses `HandleObjectCommand`. Its voice trigger is consumed before any of those branches. Grab,
   throw, and bounce never touch `ActionCollector`.

## Architecture

### Location

- New folder `Assets/Scripts/Sandbox/`, namespace `EchoRealm.Sandbox`.
- Self-contained. Allowed inbound references from existing code: **none** (decoupled via a generic
  delegate, below). Allowed outbound references: `QRAnchorManager` (read anchor pose),
  `FusionNetworkManager` (the `NetworkRunner`). No reference to Film/AI/Interaction logic.

### Components

| Component | Type | Responsibility |
|---|---|---|
| `BallVoiceHook` | MonoBehaviour | Registers a speech interceptor; matches trigger phrases; calls the spawner; reports "consumed" so the utterance dies before the AI sees it. |
| `BallSpawner` | MonoBehaviour | `SpawnBall()` / `ClearBalls()`. Computes spawn pose in front of the local user, enforces the cap, calls `Runner.Spawn`, tracks live balls. |
| `NetworkedBall` | NetworkBehaviour | The ball itself: gravity, grab/throw, bounce, anchor-relative pose sync, state-authority transfer. |
| `SpatialMeshManager` | MonoBehaviour | Owns/configures the AR Foundation `ARMeshManager`; assigns the `SpatialMesh` layer + `MeshCollider` + bouncy `PhysicMaterial` to generated meshes; dev wireframe toggle. |

### The single existing-file change (decoupled)

To keep existing code ignorant of the new module, add a **generic** interception point to the
speech dispatcher — not a reference to `EchoRealm.Sandbox`:

- In `Assets/Scripts/AI/VoiceCommandProcessor.cs`, add `public static System.Func<string,bool> SpeechInterceptor;`
- At the top of `ProcessSpeechText(text)` (after `OnSpeechRecognized`, before the pocket/START
  branches), add:
  ```csharp
  if (SpeechInterceptor != null && SpeechInterceptor(text)) return;
  ```
- `BallVoiceHook` assigns `VoiceCommandProcessor.SpeechInterceptor = TryHandle;` on enable and clears
  it on disable. This mirrors the existing meta-command pattern (catch a phrase, act locally, bypass
  the AI) without `VoiceCommandProcessor` depending on the ball. Stays within the additive-isolation
  rule.

## Behavior — the interaction loop

1. User says a spawn phrase → a ball appears ~0.5 m in front of the speaker at ~chest height,
   falls under gravity, bounces on the real floor.
2. User grabs it (MRTK3 `ObjectManipulator`) → ball follows the hand (kinematic while held).
3. User releases → ball drops and bounces. Toss up → it arcs and returns to be caught. Throw across
   the room → it flies on a true parabola and bounces off scanned walls/floor/furniture.
4. User says a remove phrase → all balls despawn.

## Networking & authority model

- **Mode:** Photon Fusion 2 Shared Mode (existing `FusionNetworkManager`, `ProvideInput = false`).
- **Spawn ownership:** the requesting device calls `Runner.Spawn` and holds **state authority** over
  its ball (Shared Mode permits client spawns). Replicates to peers immediately.
- **Co-location frame = the QR anchor, not `SceneRoot`.** The ball lives in world space; its pose is
  synced **relative to the QR anchor** via networked properties and reconstructed on peers — exactly
  the pattern `FilmSync` uses for `SceneRoot` (`Assets/Scripts/Networking/FilmSync.cs`,
  `PublishSceneTransform` / `Render`):
  - `[Networked] Vector3 AnchorRelPos`, `[Networked] Quaternion AnchorRelRot`.
  - Authority writes `relPos = inv(AnchorRot) * (world − AnchorPos)` each tick.
  - Non-authority reconstructs `world = AnchorPos + AnchorRot * AnchorRelPos` in `Render()` and
    displays it (interpolated). This keeps the ball on the same physical floor regardless of where
    each user placed the film.
- **Only the authority simulates.** Authority device: Rigidbody non-kinematic, runs physics against
  *its* mesh, writes the pose. Non-authority: Rigidbody kinematic, no local physics, follows the
  networked pose.
- **Authority transfer on grab.** Grab start → `Object.RequestStateAuthority()` (same idiom as
  `NetworkedTestCube.OnGrabStart`). On `StateAuthorityChanged`: gained → enable physics; lost →
  disable physics + follow. Transfers occur while the ball is in-hand (velocity ≈ 0), so no
  cross-authority velocity sync is needed for scope A.

## Physics

- **Gravity & arc:** Rigidbody + gravity. Throw velocity comes from MRTK3 `ObjectManipulator`
  release (3D linear + angular). Initial velocity + constant gravity ⇒ a true parabola (genuine
  projectile motion, not scripted).
- **Throw tuning:** a serialized `throwVelocityMultiplier` (default ~1.3) compensates for HoloLens
  hand-tracking under-reading release speed. The arc shape is physically exact regardless; only the
  launch magnitude is tuned.
- **Bounce:** a bouncy `PhysicMaterial` on the **ball** (bounciness ~0.7, low friction,
  **Bounce Combine = Maximum**) so the bounce holds regardless of the mesh collider's material.
  Energy decays naturally per bounce; spin is carried.
- **Fast-throw reliability:** Rigidbody `collisionDetectionMode = Continuous` to prevent tunneling
  through thin mesh colliders on hard throws.
- **Ball:** sphere, ~12 cm diameter, ~0.2 kg (mass is immaterial to the arc under gravity; it just
  keeps grab/throw stable).

## Spatial mesh

- **Source:** AR Foundation `ARMeshManager` (com.unity.xr.arfoundation **5.2.0**, already resolved
  via the Microsoft OpenXR plugin 1.11.2 — **no new package**).
- **Colliders:** ARMeshManager generates mesh GameObjects; configure it to add `MeshCollider`s.
  `SpatialMeshManager` assigns them the `SpatialMesh` layer and a shared low-friction `PhysicMaterial`
  (bounce comes from the ball's material via Bounce Combine = Maximum).
- **Visibility:** invisible by default (the ball appears to bounce off the real room). Dev toggle
  renders a wireframe for demos/debugging.
- **Coverage:** the mesh persists within a session, so colliders remain for areas already scanned
  even when the user looks away.

## Voice trigger

- **Mechanism:** dedicated phrases, no AI/NLU, consumed via `SpeechInterceptor` before the narrative
  path. Matching is forgiving (substring, lowercased), like the existing pocket/unpocket handling.
- **Spawn phrases:** "drop a ball", "spawn a ball", "spawn ball", "new ball".
- **Remove phrases:** "remove the ball", "remove ball", "delete the ball", "clear balls".

## Layers & collision matrix (concrete)

- Add two layers: `SpatialMesh`, `BallSandbox`.
- Collision matrix: enable `BallSandbox × SpatialMesh` only. Disable `BallSandbox × Default`,
  `BallSandbox × BallSandbox` (balls pass through each other — acceptable for scope A; can revisit),
  and all `SpatialMesh × *` except `BallSandbox`.
- No existing layer's collision settings are changed.

## Configurable defaults (serialized, vetoable later)

| Setting | Default |
|---|---|
| Max live balls | 5 (excess spawn evicts oldest) |
| Ball diameter | 0.12 m |
| Bounciness | 0.7 |
| Throw velocity multiplier | 1.3 |
| Spawn offset | 0.5 m forward, chest height, in front of speaker |
| Mesh wireframe | off |
| Collision detection | Continuous |

## Known limitations (accepted for scope A)

- **Per-device mesh:** the ball bounces off the **authority device's** scan. Co-located rooms overlap
  closely, so both users see a correct-looking bounce; exact contact points can differ slightly.
- **Scan coverage / resolution:** only mapped geometry has colliders; unscanned gaps or sub-mesh-
  resolution objects (thin/small) may be missed. Scanning the room once by looking around resolves
  this in practice.
- **Throw feel:** launch speed depends on hand-tracking release estimation; tuned via the multiplier,
  but not frame-perfect.

## Acceptance criteria

Editor-verifiable (with temporary `SpatialMesh`-layer floor/box stand-ins):
- Voice phrase (via `DebugVoiceInput`/text) spawns a ball; remove phrase despawns all.
- Ball falls, bounces, and comes to rest; thrown ball follows a visible arc and bounces.
- Ball passes through a Default-layer object placed in its path (isolation proof).
- A simulated voice command does not increment `ActionCollector` counts (isolation proof).
- Cap enforced at 5.

Device-verifiable (two HoloLenses):
- Ball appears at the same physical spot on both headsets and stays put when the film/`SceneRoot`
  is moved.
- Grab transfers authority; the other headset sees the motion; throw + room bounce replicate.

## Future (not now)

- Per-device mesh blending for exact local bounces.
- Ball-vs-ball collisions; other sandbox object types.
- Hand-menu spawn button alongside the voice trigger.
