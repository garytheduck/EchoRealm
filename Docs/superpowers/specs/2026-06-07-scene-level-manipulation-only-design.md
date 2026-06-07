# Scene-Level Manipulation Only — Design

**Date:** 2026-06-07
**Branch:** `feature/networked-object-manipulation`
**Status:** Approved (design), pending implementation

## Goal

Restrict **hand** manipulation to the **whole scene only**. A user can grab anywhere with
their hands and move / scale / rotate / pocket the entire diorama "at once" — but can no
longer hand-grab an *individual* prop. Individual props are manipulated **only** through the
existing "Claude, …" voice command + eye-tracking path.

Decision (confirmed): grabbing a prop with the hand **moves the whole scene** (option A) — no
dead zones. Looking at a prop and saying "Claude, make this bigger" still works.

## Why this is (almost) already done

Both interaction modes the user wants to keep are **already fully networked, bidirectionally**:

- **Whole-scene hand grab** — `FilmSync` streams `SceneRoot`'s QR-relative transform between
  master and client at ~20 Hz (`FixedUpdateNetwork`/`Render`/`RPC_PushSceneTransform`). Both
  headsets already see the scene move/scale together.
- **"Claude" + eye-tracking on a gazed prop** — gaze is resolved locally per device, but the
  resulting op rides `SubmitObjectOp → RPC_ApplyObjectOp` to *all* devices, is recorded for
  rewind (`OnObjectOpApplied`), and replays for late-joiners (see
  `2026-06-04-echorealm-gaze-object-manipulation-design.md`).

So **no new networking code is required.** The original "network the individual prop
hand-grabs" idea is dropped, because individual prop hand-grabs are being removed entirely.

## Current obstacle

Each prop currently carries its own `ObjectManipulator`, which is why a single cloud can be
hand-grabbed today. `SceneManipulationReporter` (on `SceneRoot`) deliberately *excludes* those
prop-owned manipulators from the whole-scene grab so per-prop grab and whole-scene grab can
coexist. To make hands move only the whole scene, those per-prop grabs must be turned off and
their colliders folded back into the whole-scene grab.

## Design

All changes are contained to **`SceneManipulationReporter`** (the component that already owns
"what does the whole-scene grab claim"). No other file changes for the core behaviour.

1. **Disable individual prop grab.** At startup, disable the `ObjectManipulator` on every
   non-protected prop under `SceneRoot` (everything except `SceneRoot`'s own world-grab
   manipulator and the protected subtrees). A disabled manipulator stops processing hand
   interactions, so the prop can no longer be grabbed on its own.

2. **Fold prop colliders into the whole-scene grab.** Refine the collider-keep rule so a
   prop's collider is excluded from the world grab *only* if it has an **enabled** manipulator.
   Since step 1 disables them, all non-protected colliders (props included) now belong to the
   whole-scene grab → grabbing a prop moves the whole scene.

3. **Reversible toggle.** Gate steps 1–2 behind a serialized `bool allowIndividualPropGrab`
   (default **false** = individual grab off). Set it **true** in the Inspector and the original
   per-prop + whole-scene coexistence returns. This satisfies the additive-isolation rule:
   flipping one flag restores the prior behaviour exactly.

## What stays exactly the same

- **Protected objects** (Oracle, Astronaut, HeartStone, portal, trial props) keep their own
  interactables — e.g. the cooperative HeartStone grab. They are never disabled and never folded
  into the world grab.
- **Voice + eye-tracking individual-object manipulation** is untouched and remains networked:
  - `ManipulableRegistry` still discovers the props — a disabled `ObjectManipulator` is still
    returned by `GetComponentsInChildren<ObjectManipulator>(true)` (the `enabled` flag does not
    hide a component from `GetComponents`).
  - The props' colliders still exist for the gaze raycast (`EyeTrackingManager`); being listed in
    the world-grab manipulator's `colliders` does not remove them from physics raycasts.
  - The op itself never used the manipulator — it calls `ApplyScale`/`ApplyMove`/`ApplyYaw`
    directly and syncs over RPC.
- The film, networking spine, save, and rewind systems are not touched.

## Out of scope

- No new networking (both kept modes are already networked).
- No changes to the rewind/recorder. The `TimelineRecorder` per-prop hand-grab capture hook
  (`OnPropManipulated`) becomes inert (there are no individual hand-grabs left to fire it). It is
  left in place — harmless, and individual-object rewind still works because it rolls back the
  recorded *voice* ops, not hand-grabs.

## Risks / verification

- MRTK collider resolution can be finicky (cf. the earlier "only one cloud grabbable" conflict).
  The key behaviour — grabbing a prop drives the **whole-scene** grab (not nothing, not the prop)
  — must be confirmed **on-device**.
- Confirm the voice + eye-tracking path still resolves and manipulates a gazed prop after the
  prop manipulators are disabled.
- Confirm on both headsets that a prop-initiated whole-scene grab still syncs (it rides the
  existing `SceneRoot` streaming, so it should).

## Acceptance criteria

1. Hand-grabbing **anywhere** — empty space or on a prop — moves/scales/rotates the whole scene,
   networked to the other headset.
2. No individual prop can be moved/scaled by hand.
3. "Claude, make this bigger" while gazing at a prop still works and still syncs to both headsets.
4. Protected objects (e.g. HeartStone cooperative grab) still respond to their own interactables.
5. Setting `allowIndividualPropGrab = true` restores the previous per-prop + whole-scene behaviour.
