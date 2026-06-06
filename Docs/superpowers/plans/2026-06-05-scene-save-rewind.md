# Scene Save & Rewind — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record everything that happens in an EchoRealm scene as a timestamped event timeline so the live scene can be rewound to any earlier state, and a finished scene can be saved and re-loaded on HoloLens as a frozen, view-only diorama you can step/rewind through.

**Architecture:** One ordered event timeline is the single source of truth. State at any time T = reset-to-baseline + replay events ≤ T (object ops are relative, world commands are idempotent toggles, so ordered replay is exact). A **pure core** (own asmdef, headless-unit-tested) holds the data types + replay engine + archive; thin **Unity glue** (Assembly-CSharp) observes existing events to record, applies replay to the real scene, drives live rewind over Fusion, and runs offline playback. The whole subsystem is additive — remove it and the film behaves byte-for-byte as today.

**Tech Stack:** Unity 2022.3.62f3, C#, Unity Test Framework (EditMode/NUnit), Photon Fusion 2 (Shared Mode), MRTK3 (`MixedReality.Toolkit`, `StatefulInteractable`), `JsonUtility`.

**Spec:** `Docs/superpowers/specs/2026-06-05-scene-save-rewind-design.md`

---

## Conventions & ground rules

- **Isolation (hard constraint):** new files only, plus tiny *additive* edits to existing files (each a one-line event raise or a new method). Never change existing method behavior. The three new events no-op without subscribers.
- **Two assemblies:**
  - **Pure core** → new asmdef `EchoRealm.Timeline` (auto-referenced, no game-code dependencies). Contains data types, `TransientCommands`, `TimelineLog`, `IReplayTarget`, `TimelineReplayer`, `SceneArchive`. Depends only on `UnityEngine` (for `Vector3`/`Quaternion`/`JsonUtility`).
  - **Unity glue** → stays in Assembly-CSharp (recorder, replay target, rewind RPC, UI, bootstrap branch). Auto-references the core.
- **Why an asmdef:** game scripts are in Assembly-CSharp, which a test assembly cannot reference. Putting the pure logic in its own auto-referenced asmdef lets Assembly-CSharp use it AND lets an EditMode test assembly reference it for headless tests.
- **`ObjOpType`** (existing, `EchoRealm.Interaction`) values are used as `int` in the core (opaque) and cast back in Assembly-CSharp. Mapping: Scale/Move/Rotate/Reset. Always use `(int)EchoRealm.Interaction.ObjOpType.X` in glue code — never hardcode the int.
- **Single object-op scalar:** `TimelineEvent.f` carries the *factor* for Scale, the *degrees* for Rotate, and is unused for Move/Reset; `TimelineEvent.v` carries the Move delta. They're mutually exclusive per op.

### How to run tests (used by every core task)

**Primary (editor open):** `Window → General → Test Runner → EditMode → Run All` (or run a single test via right-click). Green = pass.

**CLI (editor must be CLOSED — the project is locked while open):**
```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -projectPath "C:\Users\Samuel\Desktop\DisertatieVatavu2026\EchoRealm" -runTests -testPlatform EditMode -testResults "$env:TEMP\echorealm_editmode.xml" -logFile -
```
The result XML lists `<test-case ... result="Passed|Failed">`. After Unity creates a new `.cs` file you must let the editor recompile (focus it) before tests see it.

---

## Phase A — Pure core (TDD, headless)

### Task 1: Create the core assembly + data types

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/EchoRealm.Timeline.asmdef`
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/SceneTimeline.cs`
- Create: `EchoRealm/Assets/Tests/EditMode/EchoRealm.Timeline.Tests.asmdef`
- Test: `EchoRealm/Assets/Tests/EditMode/SceneTimelineTests.cs`

- [ ] **Step 1: Create the core asmdef**

`EchoRealm/Assets/Scripts/Film/Timeline/EchoRealm.Timeline.asmdef`:
```json
{
    "name": "EchoRealm.Timeline",
    "rootNamespace": "EchoRealm.Film",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create the test asmdef**

`EchoRealm/Assets/Tests/EditMode/EchoRealm.Timeline.Tests.asmdef`:
```json
{
    "name": "EchoRealm.Timeline.Tests",
    "rootNamespace": "EchoRealm.Film.Tests",
    "references": [
        "EchoRealm.Timeline",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Write the failing test**

`EchoRealm/Assets/Tests/EditMode/SceneTimelineTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using EchoRealm.Film;

namespace EchoRealm.Film.Tests
{
    public class SceneTimelineTests
    {
        [Test]
        public void NewTimeline_IsEmpty_WithNonNullCollections()
        {
            var tl = new SceneTimeline();
            Assert.IsNotNull(tl.events);
            Assert.IsNotNull(tl.meta);
            Assert.AreEqual(0, tl.events.Count);
        }

        [Test]
        public void TimelineEvent_HoldsObjectOpPayload()
        {
            var e = new TimelineEvent
            {
                t = 1.5f, kind = EventKind.ObjectOp, id = "Cloud01",
                i = 0, f = 1.2f, v = new Vector3(0.1f, 0f, 0f)
            };
            Assert.AreEqual(EventKind.ObjectOp, e.kind);
            Assert.AreEqual("Cloud01", e.id);
            Assert.AreEqual(1.2f, e.f);
        }
    }
}
```

- [ ] **Step 4: Run the test — verify it FAILS**

Run via Test Runner (or CLI). Expected: compile error / FAIL — `SceneTimeline` and `TimelineEvent` do not exist yet.

- [ ] **Step 5: Implement the data types**

`EchoRealm/Assets/Scripts/Film/Timeline/SceneTimeline.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Mirror of EchoRealm.Interaction.ObjOpType for the headless core (which cannot
    /// reference Assembly-CSharp). Values MUST match ObjOpType: Scale=0, Move=1, Rotate=2, Reset=3.</summary>
    internal enum ReplayObjOp { Scale = 0, Move = 1, Rotate = 2, Reset = 3 }

    /// <summary>Kinds of recorded events. WorldCommand/ObjectOp/ActTransition affect scene
    /// state on replay; AiUtterance is transcript-only (no scene effect).</summary>
    public enum EventKind { WorldCommand, ObjectOp, ActTransition, AiUtterance }

    /// <summary>One thing that happened, with its timestamp. Flat tagged record so Unity's
    /// JsonUtility can round-trip it (JsonUtility cannot serialize polymorphic subclasses).
    /// Only the fields relevant to <see cref="kind"/> are populated.</summary>
    [Serializable]
    public class TimelineEvent
    {
        public float t;          // seconds since recording start
        public EventKind kind;
        public bool transient;   // momentary FX (earthquake/lightning/anim triggers) — skipped on seek

        public string id;        // object id | world-command name | utterance speaker
        public string text;      // AI utterance text | act variant
        public int i;            // object op type (ObjOpType) | act number
        public float f;          // scale factor (Scale) | yaw degrees (Rotate)
        public Vector3 v;        // move delta (Move)
    }

    /// <summary>Header info saved alongside the events.</summary>
    [Serializable]
    public class TimelineMeta
    {
        public string sessionId;
        public string savedAtIso;
        public float durationSec;
        public int finalAct;
        public string sceneVersion = "1";
        public int eventCount;
    }

    /// <summary>The full recording: header + ordered events. Single source of truth for
    /// both rewind and saved-scene playback.</summary>
    [Serializable]
    public class SceneTimeline
    {
        public TimelineMeta meta = new TimelineMeta();
        public List<TimelineEvent> events = new List<TimelineEvent>();
    }
}
```

- [ ] **Step 6: Run the test — verify it PASSES**

Expected: both tests green.

- [ ] **Step 7: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/Timeline/EchoRealm.Timeline.asmdef" "EchoRealm/Assets/Scripts/Film/Timeline/SceneTimeline.cs" "EchoRealm/Assets/Tests/EditMode/EchoRealm.Timeline.Tests.asmdef" "EchoRealm/Assets/Tests/EditMode/SceneTimelineTests.cs"
git commit -m "feat(timeline): scene timeline data types + headless test assembly"
```

---

### Task 2: Transient-command classifier

Some world commands are one-shot FX with no lasting state (`earthquake`, `lightning`, and all character-animation triggers). When seeking they must be skipped.

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/TransientCommands.cs`
- Test: `EchoRealm/Assets/Tests/EditMode/TransientCommandsTests.cs`

- [ ] **Step 1: Write the failing test**

`EchoRealm/Assets/Tests/EditMode/TransientCommandsTests.cs`:
```csharp
using NUnit.Framework;
using EchoRealm.Film;

namespace EchoRealm.Film.Tests
{
    public class TransientCommandsTests
    {
        [Test]
        public void Earthquake_And_Lightning_AreTransient()
        {
            Assert.IsTrue(TransientCommands.IsTransient("earthquake"));
            Assert.IsTrue(TransientCommands.IsTransient("lightning"));
        }

        [Test]
        public void CharacterAnimations_AreTransient()
        {
            Assert.IsTrue(TransientCommands.IsTransient("dobby_dance"));
            Assert.IsTrue(TransientCommands.IsTransient("astronaut_jump"));
        }

        [Test]
        public void PersistentToggles_AreNotTransient()
        {
            Assert.IsFalse(TransientCommands.IsTransient("rain"));
            Assert.IsFalse(TransientCommands.IsTransient("night"));
            Assert.IsFalse(TransientCommands.IsTransient("grow_tree"));
        }

        [Test]
        public void IsTransient_IsCaseInsensitive_AndNullSafe()
        {
            Assert.IsTrue(TransientCommands.IsTransient("EARTHQUAKE"));
            Assert.IsFalse(TransientCommands.IsTransient(null));
        }
    }
}
```

- [ ] **Step 2: Run the test — verify it FAILS** (`TransientCommands` undefined).

- [ ] **Step 3: Implement**

`EchoRealm/Assets/Scripts/Film/Timeline/TransientCommands.cs`:
```csharp
using System.Collections.Generic;

namespace EchoRealm.Film
{
    /// <summary>Classifies world commands that are one-shot (no lasting scene state).
    /// These are skipped when seeking/rewinding so we reconstruct resting state, not a
    /// re-fired earthquake. Mirrors the momentary cases in CommandExecutor.ExecuteCommand.</summary>
    public static class TransientCommands
    {
        private static readonly HashSet<string> _transient = new HashSet<string>
        {
            "earthquake", "lightning",
            "dobby_dance", "dobby_wave", "dobby_scared", "dobby_celebrate",
            "astronaut_jump", "astronaut_wave", "astronaut_look_around",
        };

        public static bool IsTransient(string command)
            => command != null && _transient.Contains(command.Trim().ToLowerInvariant());
    }
}
```

- [ ] **Step 4: Run the test — verify it PASSES.**

- [ ] **Step 5: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/Timeline/TransientCommands.cs" "EchoRealm/Assets/Tests/EditMode/TransientCommandsTests.cs"
git commit -m "feat(timeline): transient-command classifier"
```

---

### Task 3: TimelineLog — append + truncate

The pure append/truncate logic the recorder will wrap (kept separate from the MonoBehaviour so it's testable).

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/TimelineLog.cs`
- Test: `EchoRealm/Assets/Tests/EditMode/TimelineLogTests.cs`

- [ ] **Step 1: Write the failing test**

`EchoRealm/Assets/Tests/EditMode/TimelineLogTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using EchoRealm.Film;

namespace EchoRealm.Film.Tests
{
    public class TimelineLogTests
    {
        [Test]
        public void AddWorldCommand_AppendsEvent_AndMarksTransient()
        {
            var log = new TimelineLog();
            log.AddWorldCommand("rain", 1f);
            log.AddWorldCommand("earthquake", 2f);

            Assert.AreEqual(2, log.Timeline.events.Count);
            Assert.AreEqual(EventKind.WorldCommand, log.Timeline.events[0].kind);
            Assert.IsFalse(log.Timeline.events[0].transient);
            Assert.IsTrue(log.Timeline.events[1].transient);
        }

        [Test]
        public void AddObjectOp_StoresScalarAndDelta()
        {
            var log = new TimelineLog();
            log.AddObjectOp("Cloud01", 0, 1.2f, new Vector3(0.1f, 0, 0), 0f, 2f);
            var e = log.Timeline.events[0];
            Assert.AreEqual(EventKind.ObjectOp, e.kind);
            Assert.AreEqual("Cloud01", e.id);
            Assert.AreEqual(1.2f, e.f, 1e-5f);
            Assert.AreEqual(new Vector3(0.1f, 0, 0), e.v);
            Assert.AreEqual(2f, e.t, 1e-5f);
        }

        [Test]
        public void AddObjectOp_Rotate_RoutesDegreesToScalar()
        {
            var log = new TimelineLog();
            log.AddObjectOp("Cloud01", 2 /*Rotate*/, 1f, Vector3.zero, 45f, 3f);
            var e = log.Timeline.events[0];
            Assert.AreEqual(45f, e.f, 1e-5f); // degrees routed into the scalar field for Rotate
            Assert.AreEqual(3f, e.t, 1e-5f);
        }

        [Test]
        public void TruncateAfter_RemovesLaterEvents_KeepsEqualOrEarlier()
        {
            var log = new TimelineLog();
            log.AddWorldCommand("rain", 1f);
            log.AddWorldCommand("night", 2f);
            log.AddWorldCommand("fog", 3f);

            log.TruncateAfter(2f);

            Assert.AreEqual(2, log.Timeline.events.Count);
            Assert.AreEqual("night", log.Timeline.events[1].id);
        }
    }
}
```

- [ ] **Step 2: Run the test — verify it FAILS** (`TimelineLog` undefined).

- [ ] **Step 3: Implement**

`EchoRealm/Assets/Scripts/Film/Timeline/TimelineLog.cs`:
```csharp
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Pure append/truncate operations over a SceneTimeline. The MonoBehaviour
    /// TimelineRecorder wraps this and feeds it timestamps from Unity events.</summary>
    public class TimelineLog
    {
        public SceneTimeline Timeline { get; } = new SceneTimeline();

        public void AddWorldCommand(string command, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.WorldCommand,
                id = command, transient = TransientCommands.IsTransient(command)
            });
        }

        public void AddObjectOp(string id, int opType, float factor, Vector3 delta, float degrees, float t)
        {
            // One scalar field carries factor (Scale) OR degrees (Rotate); Move/Reset use the delta.
            float scalar = opType == (int)ReplayObjOp.Rotate ? degrees : factor;
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.ObjectOp, id = id, i = opType, f = scalar, v = delta
            });
        }

        public void AddActTransition(int act, string variant, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.ActTransition, i = act, text = variant
            });
        }

        public void AddUtterance(string speaker, string text, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.AiUtterance, id = speaker, text = text
            });
        }

        /// <summary>Drop every event with t &gt; cutoff (used by rewind).</summary>
        public void TruncateAfter(float cutoff)
        {
            var ev = Timeline.events;
            for (int k = ev.Count - 1; k >= 0; k--)
                if (ev[k].t > cutoff) ev.RemoveAt(k);
        }
    }
}
```

> Note: `AddObjectOp` resolves the op type via the core-local `ReplayObjOp` enum (defined in `SceneTimeline.cs`) to avoid a hard dependency on the Assembly-CSharp `ObjOpType`. The recorder (Task 7) passes `(int)ObjOpType.X`. `ReplayObjOp` MUST mirror `ObjOpType` (Scale=0, Move=1, Rotate=2, Reset=3) — Task 3 Step 5 verifies this. `t` is required (no default) so events can never be silently recorded at t=0.

- [ ] **Step 4: Run the test — verify it PASSES.**

- [ ] **Step 5: Verify the ObjOpType mapping** — open `EchoRealm/Assets/Scripts/Interaction/` (search for `enum ObjOpType`) and confirm order is `Scale, Move, Rotate, Reset`. If not, fix the `2` in `AddObjectOp` and the Rotate branch in Task 9's `UnityReplayTarget`.

- [ ] **Step 6: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/Timeline/TimelineLog.cs" "EchoRealm/Assets/Tests/EditMode/TimelineLogTests.cs"
git commit -m "feat(timeline): append/truncate log operations"
```

---

### Task 4: The replay engine (`IReplayTarget` + `TimelineReplayer`)

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/IReplayTarget.cs`
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/TimelineReplayer.cs`
- Test: `EchoRealm/Assets/Tests/EditMode/TimelineReplayerTests.cs`

- [ ] **Step 1: Write the failing test** (includes a fake target modelling relative scale + flag toggles)

`EchoRealm/Assets/Tests/EditMode/TimelineReplayerTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using EchoRealm.Film;

namespace EchoRealm.Film.Tests
{
    public class TimelineReplayerTests
    {
        // Headless model: relative scale, idempotent flag toggles, act state.
        private class FakeTarget : IReplayTarget
        {
            public List<string> calls = new List<string>();
            public HashSet<string> flags = new HashSet<string>();
            public Dictionary<string, float> scale = new Dictionary<string, float>();
            public int act; public string variant;

            public void ResetToBaseline()
            {
                calls.Add("reset"); flags.Clear(); scale.Clear(); act = 0; variant = null;
            }
            public void ApplyWorldCommand(string c)
            {
                calls.Add("cmd:" + c);
                if (c.StartsWith("stop_")) flags.Remove(c.Substring(5));
                else flags.Add(c);
            }
            public void ApplyObjectOp(string id, int opType, float scalar, Vector3 delta)
            {
                calls.Add("op:" + id);
                if (opType == 0) { float s = scale.ContainsKey(id) ? scale[id] : 1f; scale[id] = s * scalar; }
            }
            public void ApplyActState(int a, string v) { calls.Add("act:" + a); act = a; variant = v; }
        }

        private static TimelineEvent Cmd(string id, float t, bool transient = false)
            => new TimelineEvent { kind = EventKind.WorldCommand, id = id, t = t, transient = transient };

        [Test]
        public void ApplyStateAt_AlwaysResetsFirst()
        {
            var tl = new SceneTimeline();
            var f = new FakeTarget();
            TimelineReplayer.ApplyStateAt(tl, 100f, true, f);
            Assert.AreEqual("reset", f.calls[0]);
        }

        [Test]
        public void ApplyStateAt_OnlyAppliesEventsUpToT()
        {
            var tl = new SceneTimeline();
            tl.events.Add(Cmd("rain", 1f));
            tl.events.Add(Cmd("night", 2f));
            tl.events.Add(Cmd("fog", 3f));
            var f = new FakeTarget();

            TimelineReplayer.ApplyStateAt(tl, 2f, true, f);

            Assert.IsTrue(f.flags.Contains("rain"));
            Assert.IsTrue(f.flags.Contains("night"));
            Assert.IsFalse(f.flags.Contains("fog"));
        }

        [Test]
        public void ApplyStateAt_SkipsTransient_WhenSeeking_AppliesWhenNot()
        {
            var tl = new SceneTimeline();
            tl.events.Add(Cmd("earthquake", 1f, transient: true));
            var seeking = new FakeTarget();
            var live = new FakeTarget();

            TimelineReplayer.ApplyStateAt(tl, 5f, true, seeking);
            TimelineReplayer.ApplyStateAt(tl, 5f, false, live);

            Assert.IsFalse(seeking.calls.Contains("cmd:earthquake"));
            Assert.IsTrue(live.calls.Contains("cmd:earthquake"));
        }

        [Test]
        public void ApplyStateAt_RelativeScale_Accumulates()
        {
            var tl = new SceneTimeline();
            tl.events.Add(new TimelineEvent { kind = EventKind.ObjectOp, id = "Cloud", i = 0, f = 2f, t = 1f });
            tl.events.Add(new TimelineEvent { kind = EventKind.ObjectOp, id = "Cloud", i = 0, f = 3f, t = 2f });
            var f = new FakeTarget();

            TimelineReplayer.ApplyStateAt(tl, 10f, true, f);

            Assert.AreEqual(6f, f.scale["Cloud"]); // (1 * 2) * 3
        }

        [Test]
        public void ApplyStateAt_IgnoresUtterances_ForState()
        {
            var tl = new SceneTimeline();
            tl.events.Add(new TimelineEvent { kind = EventKind.AiUtterance, id = "Oracle", text = "Hi", t = 1f });
            var f = new FakeTarget();

            TimelineReplayer.ApplyStateAt(tl, 10f, true, f);

            Assert.AreEqual(1, f.calls.Count); // only "reset"
        }

        [Test]
        public void RewindThenContinue_DropsDiscardedFuture_KeepsNewFuture()
        {
            // Record rain@1, night@2, fog@3; rewind to t=2 (drop fog); then a new event fire@2.5.
            var log = new TimelineLog();
            log.AddWorldCommand("rain", 1f);
            log.AddWorldCommand("night", 2f);
            log.AddWorldCommand("fog", 3f);
            log.TruncateAfter(2f);              // rewind: discard the future after t=2
            log.AddWorldCommand("fire", 2.5f); // a different future unfolds

            var f = new FakeTarget();
            TimelineReplayer.ApplyStateAt(log.Timeline, 100f, true, f);

            Assert.IsTrue(f.flags.Contains("rain"));
            Assert.IsTrue(f.flags.Contains("night"));
            Assert.IsFalse(f.flags.Contains("fog"));  // discarded by rewind
            Assert.IsTrue(f.flags.Contains("fire"));  // new post-rewind future
        }
    }
}
```

- [ ] **Step 2: Run — verify it FAILS** (`IReplayTarget`/`TimelineReplayer` undefined).

- [ ] **Step 3: Implement the interface**

`EchoRealm/Assets/Scripts/Film/Timeline/IReplayTarget.cs`:
```csharp
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>What the replay engine drives. Implemented for real by UnityReplayTarget
    /// (Assembly-CSharp) and by fakes in tests. opType matches EchoRealm.Interaction.ObjOpType
    /// (Scale=0, Move=1, Rotate=2, Reset=3); scalar = factor for Scale / degrees for Rotate.</summary>
    public interface IReplayTarget
    {
        void ResetToBaseline();
        void ApplyWorldCommand(string command);
        void ApplyObjectOp(string id, int opType, float scalar, Vector3 delta);
        void ApplyActState(int act, string variant);
    }
}
```

- [ ] **Step 4: Implement the engine**

`EchoRealm/Assets/Scripts/Film/Timeline/TimelineReplayer.cs`:
```csharp
namespace EchoRealm.Film
{
    /// <summary>The one engine both features use. Resets to baseline, then re-applies every
    /// event with t &lt;= upTo in order. When seeking, one-shot FX are skipped. AiUtterance
    /// events have no scene effect (the transcript UI surfaces them).</summary>
    public static class TimelineReplayer
    {
        public static void ApplyStateAt(SceneTimeline tl, float upTo, bool seeking, IReplayTarget target)
        {
            if (tl == null || target == null) return;

            target.ResetToBaseline();

            var events = tl.events;
            for (int k = 0; k < events.Count; k++)
            {
                var e = events[k];
                if (e.t > upTo) break;                 // events are stored in chronological order
                if (seeking && e.transient) continue;  // skip one-shot FX when seeking/rewinding

                switch (e.kind)
                {
                    case EventKind.WorldCommand:
                        target.ApplyWorldCommand(e.id);
                        break;
                    case EventKind.ObjectOp:
                        target.ApplyObjectOp(e.id, e.i, e.f, e.v);
                        break;
                    case EventKind.ActTransition:
                        target.ApplyActState(e.i, e.text);
                        break;
                    case EventKind.AiUtterance:
                        break; // transcript-only
                }
            }
        }
    }
}
```

- [ ] **Step 5: Run — verify all tests PASS.**

- [ ] **Step 6: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/Timeline/IReplayTarget.cs" "EchoRealm/Assets/Scripts/Film/Timeline/TimelineReplayer.cs" "EchoRealm/Assets/Tests/EditMode/TimelineReplayerTests.cs"
git commit -m "feat(timeline): replay engine over IReplayTarget"
```

---

### Task 5: SceneArchive — JSON save/list/load round-trip

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/Timeline/SceneArchive.cs`
- Test: `EchoRealm/Assets/Tests/EditMode/SceneArchiveTests.cs`

- [ ] **Step 1: Write the failing test** (uses a temp dir, cleans up)

`EchoRealm/Assets/Tests/EditMode/SceneArchiveTests.cs`:
```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;
using EchoRealm.Film;

namespace EchoRealm.Film.Tests
{
    public class SceneArchiveTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "archive_test");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private SceneTimeline Sample()
        {
            var tl = new SceneTimeline();
            tl.meta.sessionId = "20260605_120000";
            tl.meta.finalAct = 4;
            tl.events.Add(new TimelineEvent { kind = EventKind.WorldCommand, id = "rain", t = 1f });
            tl.events.Add(new TimelineEvent { kind = EventKind.ObjectOp, id = "Cloud", i = 0, f = 1.5f, t = 2f });
            return tl;
        }

        [Test]
        public void Save_ThenLoad_RoundTripsEvents()
        {
            var path = SceneArchive.Save(Sample(), "log text", _dir);
            Assert.IsTrue(File.Exists(path));

            var loaded = SceneArchive.Load(path);
            Assert.AreEqual(2, loaded.events.Count);
            Assert.AreEqual("rain", loaded.events[0].id);
            Assert.AreEqual(1.5f, loaded.events[1].f);
            Assert.AreEqual(2, loaded.meta.eventCount);
        }

        [Test]
        public void Save_WritesSessionLogSidecar()
        {
            var path = SceneArchive.Save(Sample(), "the session log", _dir);
            var sidecar = Path.ChangeExtension(path, ".txt");
            Assert.IsTrue(File.Exists(sidecar));
            Assert.AreEqual("the session log", File.ReadAllText(sidecar));
        }

        [Test]
        public void List_ReturnsSavedFiles_NewestFirst()
        {
            SceneArchive.Save(Sample(), "", _dir, fileName: "scene_a.json");
            SceneArchive.Save(Sample(), "", _dir, fileName: "scene_b.json");
            var files = SceneArchive.List(_dir);
            Assert.AreEqual(2, files.Count);
        }

        [Test]
        public void Load_BadFile_ReturnsNull_NoThrow()
        {
            Directory.CreateDirectory(_dir);
            var bad = Path.Combine(_dir, "corrupt.json");
            File.WriteAllText(bad, "{ not valid json ");
            Assert.IsNull(SceneArchive.Load(bad));
        }
    }
}
```

- [ ] **Step 2: Run — verify it FAILS** (`SceneArchive` undefined).

- [ ] **Step 3: Implement**

`EchoRealm/Assets/Scripts/Film/Timeline/SceneArchive.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Persists a SceneTimeline to JSON (plus a .txt session-log sidecar) and reads
    /// it back. Pure of game code: callers pass the timeline and the session-log text.
    /// Default directory is persistentDataPath/EchoRealmSaves.</summary>
    public static class SceneArchive
    {
        public static string DefaultDir =>
            Path.Combine(Application.persistentDataPath, "EchoRealmSaves");

        /// <summary>Write the timeline as JSON and the log as a .txt sidecar. Returns the json path.</summary>
        public static string Save(SceneTimeline tl, string logText, string dir = null, string fileName = null)
        {
            dir = dir ?? DefaultDir;
            Directory.CreateDirectory(dir);

            tl.meta.eventCount = tl.events.Count;
            if (string.IsNullOrEmpty(tl.meta.savedAtIso))
                tl.meta.savedAtIso = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            if (tl.events.Count > 0)
                tl.meta.durationSec = tl.events[tl.events.Count - 1].t;

            if (string.IsNullOrEmpty(fileName))
            {
                string id = string.IsNullOrEmpty(tl.meta.sessionId)
                    ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : tl.meta.sessionId;
                fileName = $"scene_{id}_act{tl.meta.finalAct}.json";
            }

            string jsonPath = Path.Combine(dir, fileName);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(tl, prettyPrint: true));
            File.WriteAllText(Path.ChangeExtension(jsonPath, ".txt"), logText ?? "");
            Debug.Log($"[SceneArchive] Saved {tl.events.Count} events → {jsonPath}");
            return jsonPath;
        }

        /// <summary>All saved .json files, newest first. Empty list if the dir is missing.</summary>
        public static List<string> List(string dir = null)
        {
            dir = dir ?? DefaultDir;
            var result = new List<string>();
            if (!Directory.Exists(dir)) return result;
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            result.AddRange(files);
            return result;
        }

        /// <summary>Deserialize a saved timeline, or null on any failure (corrupt/missing).</summary>
        public static SceneTimeline Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var tl = JsonUtility.FromJson<SceneTimeline>(File.ReadAllText(path));
                if (tl == null || tl.events == null) return null;
                return tl;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SceneArchive] Could not load '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
```

> `JsonUtility.FromJson` returns a non-null object for `"{ not valid json "`? No — malformed JSON throws, caught above → null. Valid-but-empty JSON (`{}`) yields a `SceneTimeline` with null `events` only if the field were absent; our class initializes `events`, but `FromJson` overwrites with null when the key is missing, hence the explicit `tl.events == null` guard.

- [ ] **Step 4: Run — verify all tests PASS.**

- [ ] **Step 5: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/Timeline/SceneArchive.cs" "EchoRealm/Assets/Tests/EditMode/SceneArchiveTests.cs"
git commit -m "feat(timeline): JSON scene archive save/list/load"
```

---

## Phase B — Capture hooks + recorder (additive)

### Task 6: Add the three thin event hooks to existing classes

Each is a new event + a single raise. No existing behavior changes.

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Characters/OracleController.cs`
- Modify: `EchoRealm/Assets/Scripts/Networking/FilmSync.cs`
- Modify: `EchoRealm/Assets/Scripts/Film/ActManager.cs`

- [ ] **Step 1: `OracleController.OnSpoke`** — add the event near the other public members (after `CurrentMood`, ~line 64):

```csharp
        /// <summary>Fired whenever the Oracle speaks a line (text, mood). Observational — used
        /// by TimelineRecorder to capture "what the AI said". No effect if unsubscribed.</summary>
        public static event System.Action<string, string> OnSpoke;
```

Raise it inside `Speak` (end of method, after the existing `Log(...)`, ~line 123):
```csharp
            OnSpoke?.Invoke(text, CurrentMood);
```

And inside `SpeakDramatic` (right after `if (voice != null) voice.Speak(text);`, ~line 132):
```csharp
            OnSpoke?.Invoke(text, CurrentMood);
```

- [ ] **Step 2: `ActManager.OnActStarted`** — add the event near `OnActCompleted` (~line 54):

```csharp
        /// <summary>Fired when an act starts (act number, variant). Observational — used by
        /// TimelineRecorder. No effect if unsubscribed.</summary>
        public static event System.Action<int, string> OnActStarted;
```

Raise it inside `StartAct`, right after `CurrentDecision = decision;` (~line 83):
```csharp
            OnActStarted?.Invoke(actNumber, decision?.chosen_variant ?? "default");
```

- [ ] **Step 3: `FilmSync.OnObjectOpApplied`** — add the event near `Instance` (~line 22):

```csharp
        /// <summary>Fired on every device after an object op is applied (id, opType, factor,
        /// delta, degrees). Observational — used by TimelineRecorder on the master. No effect
        /// if unsubscribed.</summary>
        public static event System.Action<string, int, float, Vector3, float> OnObjectOpApplied;
```

Raise it at the end of `RPC_ApplyObjectOp`, after the `_objStates[id] = ...` block (~line 383):
```csharp
            OnObjectOpApplied?.Invoke(id, opType, factor, delta, degrees);
```

- [ ] **Step 4: Compile check** — focus the Unity editor, wait for recompile, confirm **no console errors**. (No automated test; these are one-line raises.)

- [ ] **Step 5: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Characters/OracleController.cs" "EchoRealm/Assets/Scripts/Networking/FilmSync.cs" "EchoRealm/Assets/Scripts/Film/ActManager.cs"
git commit -m "feat(capture): additive observation events (Oracle/Act/ObjectOp)"
```

---

### Task 7: TimelineRecorder — observe & record

Subscribes to the existing `CommandExecutor.OnCommandExecuted` (instance) and `VoiceCommandProcessor.OnAIResponseReceived` (instance), plus the three new static events. Lazily starts its clock on the first recorded event.

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/TimelineRecorder.cs`

- [ ] **Step 1: Implement**

`EchoRealm/Assets/Scripts/Film/TimelineRecorder.cs`:
```csharp
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Characters;
using EchoRealm.Networking;

namespace EchoRealm.Film
{
    /// <summary>Master-side observer that records everything that happens into a TimelineLog.
    /// Pure observer: subscribes to existing/added events and appends — never calls back into
    /// the film. Remove this component and the film is unchanged. Attach to a persistent object
    /// (e.g. GameManager) in MainScene.</summary>
    public class TimelineRecorder : MonoBehaviour
    {
        public static TimelineRecorder Instance { get; private set; }

        public TimelineLog Log { get; } = new TimelineLog();
        public SceneTimeline Timeline => Log.Timeline;

        private float _startTime = -1f;
        private bool _subscribedInstances;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            OracleController.OnSpoke += HandleSpoke;
            ActManager.OnActStarted += HandleActStarted;
            FilmSync.OnObjectOpApplied += HandleObjectOp;
        }

        private void OnDisable()
        {
            OracleController.OnSpoke -= HandleSpoke;
            ActManager.OnActStarted -= HandleActStarted;
            FilmSync.OnObjectOpApplied -= HandleObjectOp;
            UnsubscribeInstances();
        }

        private void Start() => SubscribeInstances();

        private void SubscribeInstances()
        {
            if (_subscribedInstances) return;
            if (CommandExecutor.Instance != null)
                CommandExecutor.Instance.OnCommandExecuted += HandleWorldCommand;
            if (VoiceCommandProcessor.Instance != null)
            {
                VoiceCommandProcessor.Instance.OnAIResponseReceived += HandleAIResponse;
                VoiceCommandProcessor.Instance.OnSpeechRecognized += HandleSpeech;
            }
            _subscribedInstances = true;
        }

        private void UnsubscribeInstances()
        {
            if (!_subscribedInstances) return;
            if (CommandExecutor.Instance != null)
                CommandExecutor.Instance.OnCommandExecuted -= HandleWorldCommand;
            if (VoiceCommandProcessor.Instance != null)
            {
                VoiceCommandProcessor.Instance.OnAIResponseReceived -= HandleAIResponse;
                VoiceCommandProcessor.Instance.OnSpeechRecognized -= HandleSpeech;
            }
            _subscribedInstances = false;
        }

        // Seconds since the first recorded event (lazy clock start).
        private float Now()
        {
            if (_startTime < 0f) _startTime = Time.time;
            return Time.time - _startTime;
        }

        private void HandleWorldCommand(string command) => Log.AddWorldCommand(command, Now());

        private void HandleObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
            => Log.AddObjectOp(id, opType, factor, delta, degrees, Now());

        private void HandleActStarted(int act, string variant)
        {
            Log.AddActTransition(act, variant, Now());
            Log.Timeline.meta.finalAct = act;
        }

        private void HandleSpoke(string text, string mood) => Log.AddUtterance("Oracle", text, Now());

        private void HandleSpeech(string text) => Log.AddUtterance("User", text, Now());

        private void HandleAIResponse(AICommandResponse r)
        {
            if (r == null) return;
            string cmds = (r.commands != null) ? string.Join(",", r.commands) : "";
            Log.AddUtterance("AI", $"decided [{cmds}] mood={r.mood}", Now());
        }

        /// <summary>Set the session id used in the saved filename (call once when the film starts).</summary>
        public void SetSessionId(string id) => Log.Timeline.meta.sessionId = id;

        /// <summary>Rewind support: drop events after the cutoff.</summary>
        public void TruncateAfter(float cutoff) => Log.TruncateAfter(cutoff);

        /// <summary>Current recording time, for the rewind controller.</summary>
        public float CurrentTime => Now();

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
```

- [ ] **Step 2: Compile check** — recompile in the editor, no errors.

- [ ] **Step 3: Editor smoke test (manual)** — in `MainScene`, add a `TimelineRecorder` component to the GameManager object. Enter Play mode in the editor; trigger a debug voice command via `VoiceCommandProcessor.ProcessDebugInput("make it rain")` (or your usual editor flow). In the inspector/console confirm `TimelineRecorder.Instance.Timeline.events.Count` grows. Exit Play mode.

- [ ] **Step 4: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/TimelineRecorder.cs"
git commit -m "feat(capture): TimelineRecorder observes and records the session"
```

---

## Phase C — Reset baseline + Unity replay target

### Task 8: Expose registered props + add quiet act state

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Interaction/ManipulableRegistry.cs`
- Modify: `EchoRealm/Assets/Scripts/Film/ActManager.cs`

- [ ] **Step 1: `ManipulableRegistry` — additive enumeration** (after `FindById`, ~line 59):

```csharp
        /// <summary>All registered manipulable props (read-only). Used by replay to reset
        /// every prop to its baseline. Additive — existing lookups are unchanged.</summary>
        public System.Collections.Generic.IEnumerable<ManipulableObject> All => _byId.Values;
```

- [ ] **Step 2: `ActManager.ApplyActState` — quiet visual setup** (new public method; does NOT run coroutines or TTS). Add after `StartAct` (~line 96):

```csharp
        /// <summary>Replay/rewind helper: set an act's VISUAL state only — no coroutines, no
        /// Oracle narration, no waits. Mirrors the obstacle/portal setup that the act coroutines
        /// perform, so a reconstructed scene matches without re-performing the act.</summary>
        public void ApplyActState(int actNumber, string variant)
        {
            StopAllCoroutines();
            DeactivateTrialObstacles();
            CurrentAct = actNumber;

            if (actNumber == 3)
            {
                var obstacle = PickObstacleForVariant(variant ?? "default");
                if (obstacle != null) obstacle.SetActive(true);
            }
            else if (actNumber == 4)
            {
                if (portalEffect != null) portalEffect.SetActive(true);
            }
        }
```

> This reuses the existing private `DeactivateTrialObstacles()` and `PickObstacleForVariant(...)`. `StartAct` is untouched; live acts still perform fully.

- [ ] **Step 3: Compile check** — no errors.

- [ ] **Step 4: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Interaction/ManipulableRegistry.cs" "EchoRealm/Assets/Scripts/Film/ActManager.cs"
git commit -m "feat(replay): registry enumeration + quiet ApplyActState"
```

---

### Task 9: UnityReplayTarget — apply replay to the real scene

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/UnityReplayTarget.cs`

- [ ] **Step 1: Implement**

`EchoRealm/Assets/Scripts/Film/UnityReplayTarget.cs`:
```csharp
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Interaction;

namespace EchoRealm.Film
{
    /// <summary>Applies replayed timeline events to the live scene via the existing systems.
    /// Used by both live rewind and offline playback. Object ops are re-applied relatively
    /// (exactly as during play), so ordered replay from baseline reproduces the exact state.</summary>
    public class UnityReplayTarget : IReplayTarget
    {
        public void ResetToBaseline()
        {
            // Props back to their captured originals.
            var reg = ManipulableRegistry.Instance;
            if (reg != null)
                foreach (var mo in reg.All)
                    if (mo != null) mo.ResetTransform();

            // World back to defaults.
            CommandExecutor.Instance?.ResetWorldToDefaults();

            // Acts: clear obstacles/portal (act 0 = pre-start visual state).
            ActManager.Instance?.ApplyActState(0, null);
        }

        public void ApplyWorldCommand(string command)
            => CommandExecutor.Instance?.ExecuteCommand(command);

        public void ApplyObjectOp(string id, int opType, float scalar, Vector3 delta)
        {
            var mo = ManipulableRegistry.Instance?.FindById(id);
            if (mo == null) return;
            switch ((ObjOpType)opType)
            {
                case ObjOpType.Scale:  mo.ApplyScale(scalar); break;
                case ObjOpType.Move:   mo.ApplyMove(delta);   break;
                case ObjOpType.Rotate: mo.ApplyYaw(scalar);   break;
                case ObjOpType.Reset:  mo.ResetTransform();   break;
            }
        }

        public void ApplyActState(int act, string variant)
            => ActManager.Instance?.ApplyActState(act, variant);
    }
}
```

- [ ] **Step 2: Add `CommandExecutor.ResetWorldToDefaults`** — the replay target needs a single call that returns the world to its authored baseline (turns every effect off and resets flags). Add to `EchoRealm/Assets/Scripts/AI/CommandExecutor.cs` after `ExecuteCommand` (~line 257):

```csharp
        /// <summary>Replay/rewind helper: turn every persistent effect OFF and reset state flags
        /// to their authored defaults. Reuses ExecuteCommand so behavior matches exactly.
        /// Additive — never called by the live film.</summary>
        public void ResetWorldToDefaults()
        {
            ExecuteCommand("stop_rain");
            ExecuteCommand("day");
            ExecuteCommand("stop_fire");
            ExecuteCommand("stop_wind");
            ExecuteCommand("stop_fog");
            ExecuteCommand("stop_butterflies");
            ExecuteCommand("stop_fireflies");
            ExecuteCommand("open_path");
            SetGameObject(forestGroup, false);
            SetGameObject(flowersGroup, false);
            hasForest = false;
        }
```

- [ ] **Step 3: Compile check** — no errors.

- [ ] **Step 4: PlayMode smoke test (manual)** — in `MainScene` Play mode, build a small `SceneTimeline` in code (e.g. via a temporary debug key) with `rain` then a `Cloud` scale op, call `TimelineReplayer.ApplyStateAt(tl, 100f, true, new UnityReplayTarget())`, and confirm the cloud is scaled and rain is on. Then `ApplyStateAt(tl, 0f, ...)` and confirm baseline (no rain, cloud original size).

- [ ] **Step 5: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/UnityReplayTarget.cs" "EchoRealm/Assets/Scripts/AI/CommandExecutor.cs"
git commit -m "feat(replay): UnityReplayTarget + CommandExecutor world reset"
```

---

### Task 9b: AI-memory rollback (rewound commands must not influence future AI decisions)

**Why:** The AI's variant choice and final monologue read a *separate* cumulative store — `ActionCollector.Profile` (a `PlayerBehaviorProfile`: voice/manipulation/cooperation/gaze/nurture/chaos counts) and `NarrativeManager`'s command logs — NOT the visual timeline. `ActionCollector.ResetForNewAct()` clears only the rolling recent-actions list, never the cumulative profile ([ActionCollector.cs:133](EchoRealm/Assets/Scripts/AI/ActionCollector.cs)). So truncating the timeline alone leaves rewound commands influencing the next AI decision. This task snapshots that memory alongside the timeline and restores it on rewind (spec R9). In-memory only — not part of the saved file (offline playback makes no new AI decisions).

**Files:**
- Create: `EchoRealm/Assets/Scripts/AI/AiMemoryState.cs`
- Modify: `EchoRealm/Assets/Scripts/AI/PlayerBehaviorProfile.cs`
- Modify: `EchoRealm/Assets/Scripts/AI/ActionCollector.cs`
- Modify: `EchoRealm/Assets/Scripts/AI/NarrativeManager.cs`
- Modify: `EchoRealm/Assets/Scripts/Film/TimelineRecorder.cs` (from Task 7)
- Modify: `EchoRealm/Assets/Scripts/Film/FilmDirector.cs`

- [ ] **Step 1: Create the snapshot type**

`EchoRealm/Assets/Scripts/AI/AiMemoryState.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace EchoRealm.AI
{
    /// <summary>In-memory snapshot of the AI's accumulated "memory" at a moment in time: the
    /// cumulative PlayerBehaviorProfile counters + recent actions (ActionCollector) and the command
    /// logs/mood (NarrativeManager). TimelineRecorder keeps one per event and restores the one at T
    /// on rewind, so rewound commands stop influencing future AI decisions. Not saved to disk.</summary>
    [Serializable]
    public class AiMemoryState
    {
        public float t;
        public int voice, manipulation, cooperation, gaze, nurture, chaos;
        public List<string> interactedObjects = new List<string>();
        public List<string> recentActions = new List<string>();
        public List<string> voiceLog = new List<string>();
        public List<string> executedLog = new List<string>();
        public int narrativeCooperation;
        public string mood = "mysterious";
    }
}
```

- [ ] **Step 2: `PlayerBehaviorProfile` capture/restore** — add inside the class (after `Reset()`, ~line 132). These set the private-setter properties from within the class.

```csharp
        /// <summary>Copy the cumulative counters into a snapshot (for rewind rollback).</summary>
        public void CaptureInto(AiMemoryState m)
        {
            m.voice = VoiceCommandCount; m.manipulation = ManipulationCount;
            m.cooperation = CooperationCount; m.gaze = GazeEventCount;
            m.nurture = NurtureCount; m.chaos = ChaosCount;
            m.interactedObjects = new List<string>(_interactedObjects);
        }

        /// <summary>Restore the cumulative counters from a snapshot (rewind rollback).</summary>
        public void RestoreFrom(AiMemoryState m)
        {
            VoiceCommandCount = m.voice; ManipulationCount = m.manipulation;
            CooperationCount = m.cooperation; GazeEventCount = m.gaze;
            NurtureCount = m.nurture; ChaosCount = m.chaos;
            _interactedObjects.Clear();
            if (m.interactedObjects != null)
                foreach (var o in m.interactedObjects) _interactedObjects.Add(o);
        }
```

- [ ] **Step 3: `ActionCollector` capture/restore** — add inside the class (after `ResetForNewAct`, ~line 139):

```csharp
        /// <summary>Snapshot the cumulative profile + recent actions at time t (for rewind).</summary>
        public AiMemoryState CaptureMemory(float t)
        {
            var m = new AiMemoryState { t = t };
            Profile.CaptureInto(m);
            m.recentActions = new List<string>(_recentActions);
            return m;
        }

        /// <summary>Roll the profile + recent actions back to a snapshot (rewind).</summary>
        public void RestoreMemory(AiMemoryState m)
        {
            Profile.RestoreFrom(m);
            _recentActions.Clear();
            if (m.recentActions != null) _recentActions.AddRange(m.recentActions);
        }
```

- [ ] **Step 4: `NarrativeManager` capture/restore** — add inside the class (after `BuildSessionSummary`, ~line 150). Sets its own private-setter logs from within the class.

```csharp
        /// <summary>Snapshot the session command logs + mood (for rewind).</summary>
        public void CaptureInto(AiMemoryState m)
        {
            m.voiceLog = new List<string>(VoiceCommandLog);
            m.executedLog = new List<string>(ExecutedCommandLog);
            m.narrativeCooperation = CooperationCount;
            m.mood = CurrentMood;
        }

        /// <summary>Roll the session command logs + mood back to a snapshot (rewind).</summary>
        public void RestoreFrom(AiMemoryState m)
        {
            VoiceCommandLog = m.voiceLog != null ? new List<string>(m.voiceLog) : new List<string>();
            ExecutedCommandLog = m.executedLog != null ? new List<string>(m.executedLog) : new List<string>();
            CooperationCount = m.narrativeCooperation;
            CurrentMood = m.mood;
        }
```

- [ ] **Step 5: `FilmDirector.RewindToAct`** — re-arm the act flow when a rewind lands in an earlier act. Add inside the class (after `SkipToAct`, ~line 155):

```csharp
        /// <summary>Rewind helper: re-arm the act-flow state machine to the act active at T, so the
        /// film resumes correctly (and re-makes later AI decisions). Decisions for acts not yet
        /// reached are cleared. Additive — never called by the live flow.</summary>
        public void RewindToAct(int act)
        {
            IsPlaying = true; IsFinished = false;
            if (act < 4) _act4Decision = null;
            if (act < 3) _act3Decision = null;
            act2Active = (act == 2);
            if (act == 2) act2StartTime = Time.time;
        }
```

- [ ] **Step 6: Extend `TimelineRecorder`** — snapshot AI memory on every event and add the restore. Add `using System.Collections.Generic;` to the top if absent. Add the field (near `Log`):

```csharp
        private readonly List<AiMemoryState> _aiSnapshots = new List<AiMemoryState>();
```

Replace the `Handle*` block from Task 7 with these versions (each also snapshots), and add `SnapshotAiMemory` + `RestoreAiMemoryAt`:

```csharp
        private void HandleWorldCommand(string command) { Log.AddWorldCommand(command, Now()); SnapshotAiMemory(); }

        private void HandleObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
        { Log.AddObjectOp(id, opType, factor, delta, degrees, Now()); SnapshotAiMemory(); }

        private void HandleActStarted(int act, string variant)
        {
            Log.AddActTransition(act, variant, Now());
            Log.Timeline.meta.finalAct = act;
            SnapshotAiMemory();
        }

        private void HandleSpoke(string text, string mood) { Log.AddUtterance("Oracle", text, Now()); SnapshotAiMemory(); }

        private void HandleSpeech(string text) { Log.AddUtterance("User", text, Now()); SnapshotAiMemory(); }

        private void HandleAIResponse(AICommandResponse r)
        {
            if (r == null) return;
            string cmds = (r.commands != null) ? string.Join(",", r.commands) : "";
            Log.AddUtterance("AI", $"decided [{cmds}] mood={r.mood}", Now());
            SnapshotAiMemory();
        }

        // Capture the AI's cumulative memory at the current time (called after each recorded event).
        private void SnapshotAiMemory()
        {
            var ac = EchoRealm.AI.ActionCollector.Instance;
            var m = ac != null ? ac.CaptureMemory(Now()) : new AiMemoryState { t = Now() };
            NarrativeManager.Instance?.CaptureInto(m);
            _aiSnapshots.Add(m);
        }

        /// <summary>Rewind: restore the AI's memory to the latest snapshot at or before t (empty
        /// baseline if none), and drop snapshots after t. Called by FilmSync.DoRewind.</summary>
        public void RestoreAiMemoryAt(float t)
        {
            AiMemoryState chosen = null;
            for (int k = 0; k < _aiSnapshots.Count; k++)
            {
                if (_aiSnapshots[k].t <= t) chosen = _aiSnapshots[k];
                else break;
            }
            var state = chosen ?? new AiMemoryState { t = 0f }; // before first command → empty
            EchoRealm.AI.ActionCollector.Instance?.RestoreMemory(state);
            NarrativeManager.Instance?.RestoreFrom(state);

            for (int k = _aiSnapshots.Count - 1; k >= 0; k--)
                if (_aiSnapshots[k].t > t) _aiSnapshots.RemoveAt(k);
        }
```

- [ ] **Step 7: Compile check** — recompile in the editor, no errors.

- [ ] **Step 8: Manual integration test** — in `MainScene` Play mode with `TimelineRecorder` present: give 5 nurturing debug voice commands (e.g. `ProcessDebugInput("grow a tree")` ×5). Confirm `ActionCollector.Instance.Profile.VoiceCommandCount == 5`. Then call `TimelineRecorder.Instance.RestoreAiMemoryAt(t3)` where `t3` is the timestamp around the 3rd command (read it from `TimelineRecorder.Instance.Timeline.events`). Confirm `VoiceCommandCount` drops to `3` and `NurtureCount` reflects only the first 3. This proves rewound commands stop counting toward AI decisions.

- [ ] **Step 9: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/AI/AiMemoryState.cs" "EchoRealm/Assets/Scripts/AI/PlayerBehaviorProfile.cs" "EchoRealm/Assets/Scripts/AI/ActionCollector.cs" "EchoRealm/Assets/Scripts/AI/NarrativeManager.cs" "EchoRealm/Assets/Scripts/Film/TimelineRecorder.cs" "EchoRealm/Assets/Scripts/Film/FilmDirector.cs"
git commit -m "feat(rewind): roll back AI behavioral memory on rewind (R9)"
```

---

## Phase D — Live rewind (networked)

### Task 10: FilmSync rewind entry point + broadcast

Reuses the existing idempotent `RPC_SetObjectState` and `WorldStateCsv` path. The master replays to T locally, then broadcasts absolute state.

**Files:**
- Modify: `EchoRealm/Assets/Scripts/Networking/FilmSync.cs`

- [ ] **Step 1: Add a rewind guard field** near the other private fields (~line 58):

```csharp
        // True only during a rewind apply, so live writes/AI don't fight the reconstruction.
        public bool IsRewinding { get; private set; }
```

- [ ] **Step 2: Add the public entry + RPCs** (after `RequestPocket`/`RPC_SetPocket`, end of class ~line 441):

```csharp
        // ------------------------------------------------------------------
        // Rewind — jump the shared world back to its state N seconds ago
        // ------------------------------------------------------------------

        /// <summary>Any device asks to rewind; only the master computes + broadcasts it.</summary>
        public void RequestRewind(float seconds)
        {
            if (HasStateAuthority) DoRewind(seconds);
            else RPC_RequestRewind(seconds);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestRewind(float seconds) => DoRewind(seconds);

        // Master only: truncate the timeline to T, replay locally, broadcast absolute state.
        private void DoRewind(float seconds)
        {
            if (!HasStateAuthority) return;
            var recorder = EchoRealm.Film.TimelineRecorder.Instance;
            if (recorder == null) return;

            float t = Mathf.Max(0f, recorder.CurrentTime - seconds);
            IsRewinding = true;

            recorder.TruncateAfter(t);
            recorder.RestoreAiMemoryAt(t);   // roll the AI's behavioral memory back to T (spec R9)
            var target = new EchoRealm.Film.UnityReplayTarget();
            EchoRealm.Film.TimelineReplayer.ApplyStateAt(recorder.Timeline, t, seeking: true, target);

            // Rebuild authoritative per-object state from the reconstructed scene, then push to peers.
            _objStates.Clear();
            var reg = ManipulableRegistry.Instance;
            if (reg != null)
            {
                foreach (var mo in reg.All)
                {
                    if (mo == null) continue;
                    mo.GetLocal(out var s, out var p, out var r);
                    _objStates[mo.Id] = new ObjState { scale = s, pos = p, rot = r };
                }
            }

            // Recompute the world-state CSV from the surviving WorldCommand events.
            _worldCommandLog.Clear();
            foreach (var e in recorder.Timeline.events)
                if (e.kind == EchoRealm.Film.EventKind.WorldCommand && !e.transient)
                    _worldCommandLog.Add(e.id);
            WorldStateCsv = TrimWorldCsv();

            CurrentAct = ActManager.Instance != null ? ActManager.Instance.CurrentAct : CurrentAct;
            EchoRealm.Film.FilmDirector.Instance?.RewindToAct(CurrentAct); // re-arm the act flow to T (spec R9)

            RPC_RewindApply(t, CurrentAct, ChosenVariant.ToString(), WorldStateCsv.ToString());
            foreach (var kv in _objStates)
                RPC_SetObjectState(kv.Key, kv.Value.scale, kv.Value.pos, kv.Value.rot);

            IsRewinding = false;
            Debug.Log($"[FilmSync] Rewound to t={t:F1}s ({seconds:F0}s back).");
        }

        // Every device: reset to baseline, replay world + act state to T. Objects arrive via RPC_SetObjectState.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RewindApply(float t, int act, string variant, string worldCsv)
        {
            var reg = ManipulableRegistry.Instance;
            if (reg != null)
                foreach (var mo in reg.All)
                    if (mo != null) mo.ResetTransform();

            CommandExecutor.Instance?.ResetWorldToDefaults();
            ActManager.Instance?.ApplyActState(act, variant);

            var exec = CommandExecutor.Instance;
            if (exec != null && !string.IsNullOrEmpty(worldCsv))
                foreach (var raw in worldCsv.Split(','))
                {
                    string cmd = raw.Trim();
                    if (cmd.Length > 0) exec.ExecuteCommand(cmd);
                }
        }

        // Cap the world CSV to the networked-string budget (most-recent wins) — same rule as RecordAndPublishWorldState.
        private NetworkString<_512> TrimWorldCsv()
        {
            int start = 0;
            string csv = string.Join(",", _worldCommandLog);
            while (csv.Length > MaxWorldStateChars && start < _worldCommandLog.Count - 1)
            {
                start++;
                csv = string.Join(",", _worldCommandLog.GetRange(start, _worldCommandLog.Count - start));
            }
            return csv;
        }
```

- [ ] **Step 3: Guard live writes during rewind** — in `FixedUpdateNetwork` (~line 106) add after the existing early-returns:

```csharp
            if (IsRewinding) return;
```

- [ ] **Step 4: Compile check** — no errors. (`NetworkString<_512>`, `_worldCommandLog`, `MaxWorldStateChars`, `_objStates`, `ObjState` already exist in this file.)

- [ ] **Step 5: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Networking/FilmSync.cs"
git commit -m "feat(rewind): networked RequestRewind + broadcast on FilmSync"
```

---

### Task 11: Live rewind hand-menu

A runtime-built MRTK panel (following the `ResumeButton` pattern) with two buttons.

**Files:**
- Create: `EchoRealm/Assets/Scripts/Interaction/RewindMenu.cs`

- [ ] **Step 1: Implement**

`EchoRealm/Assets/Scripts/Interaction/RewindMenu.cs`:
```csharp
using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Interaction
{
    /// <summary>A small head-following panel with "Rewind 20s" / "Rewind 1m" buttons that call
    /// FilmSync.RequestRewind (networked → both headsets jump together). Runtime-built like
    /// ResumeButton. Attach to a persistent object (GameManager) in MainScene.</summary>
    public class RewindMenu : MonoBehaviour
    {
        [SerializeField] private float distance = 0.6f;
        [SerializeField] private float verticalOffset = -0.18f;
        [Range(0.02f, 1f)][SerializeField] private float followLerp = 0.12f;

        private GameObject _go;

        private void Start() => Build();

        private void LateUpdate()
        {
            if (_go == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 target = cam.transform.position + cam.transform.forward * distance
                             + cam.transform.up * verticalOffset;
            _go.transform.position = Vector3.Lerp(_go.transform.position, target, followLerp);
            _go.transform.rotation = Quaternion.LookRotation(_go.transform.position - cam.transform.position, cam.transform.up);
        }

        private void Build()
        {
            _go = new GameObject("RewindMenu(Runtime)");
            MakeButton("⟲ 20s", new Vector3(-0.13f, 0, 0), () => Rewind(20f));
            MakeButton("⟲ 1m", new Vector3(0.13f, 0, 0), () => Rewind(60f));
        }

        private void Rewind(float seconds)
        {
            var sync = EchoRealm.Networking.FilmSync.Instance;
            if (sync != null) sync.RequestRewind(seconds);
            else Debug.LogWarning("[RewindMenu] No FilmSync — rewind needs a session.");
        }

        private void MakeButton(string label, Vector3 localPos, UnityEngine.Events.UnityAction onClick)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(_go.transform, false);
            root.transform.localPosition = localPos;

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, 0.10f, 0.02f);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.22f, 0.10f, 0.012f);
            var bgCol = bg.GetComponent<Collider>();
            if (bgCol != null) Destroy(bgCol);
            var mat = bg.GetComponent<MeshRenderer>().material;
            mat.color = new Color(0.30f, 0.10f, 0.45f, 1f);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.09f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.05f;

            var si = root.AddComponent<StatefulInteractable>();
            si.OnClicked.AddListener(onClick);
        }
    }
}
```

- [ ] **Step 2: Compile check** — no errors.

- [ ] **Step 3: Manual on-device test (2 headsets)** — run a session, advance a bit (rain + scale a cloud), tap **⟲ 20s**. Both headsets snap the world back ~20s (rain/cloud as they were); the film continues from there. Tap **⟲ 1m** early in the scene → lands at baseline (clamped).

- [ ] **Step 4: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Interaction/RewindMenu.cs"
git commit -m "feat(rewind): live hand-menu (Rewind 20s / 1m)"
```

---

### Task 11c: Record hand-grab manipulations (so they save & rewind)

**Why:** Only voice-"Claude" object ops (via `FilmSync.RPC_ApplyObjectOp`) are captured today. A direct MRTK hand-grab of a prop moves its transform locally and is invisible to the timeline — so it neither saves nor rewinds (spec R10). This records each hand-manipulation's **resulting absolute transform** as a new `ObjectState` event; replay/rewind restores it via `ManipulableObject.SetLocal`. Absolute (not relative) because a hand-grab is free-form. **Depends on `ManipulableRegistry` being live** (provides props + ids).

**Files:** `Film/Timeline/SceneTimeline.cs`, `TimelineLog.cs`, `IReplayTarget.cs`, `TimelineReplayer.cs`, `Film/UnityReplayTarget.cs`, `Film/TimelineRecorder.cs`, `Tests/EditMode/TimelineReplayerTests.cs`.

- [ ] **Step 1 — data model (`SceneTimeline.cs`):** add `ObjectState` to `EventKind`, and two fields to `TimelineEvent`:
```csharp
    public enum EventKind { WorldCommand, ObjectOp, ActTransition, AiUtterance, ObjectState }
```
```csharp
        public Vector3 v2;       // ObjectState: absolute local scale (v = pos, q = rot)
        public Quaternion q;     // ObjectState: absolute local rotation
```

- [ ] **Step 2 — `TimelineLog.cs`:** add
```csharp
        public void AddObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.ObjectState, id = id, v = pos, v2 = scale, q = rot
            });
        }
```

- [ ] **Step 3 — `IReplayTarget.cs`:** add `void SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot);`

- [ ] **Step 4 — `TimelineReplayer.cs`:** add a case to the switch:
```csharp
                    case EventKind.ObjectState:
                        target.SetObjectState(e.id, e.v2, e.v, e.q);
                        break;
```

- [ ] **Step 5 — tests (`TimelineReplayerTests.cs`):** add `SetObjectState` to `FakeTarget` (record into a `Dictionary<string,(Vector3 scale,Vector3 pos,Quaternion rot)>`), and add a test: an `ObjectState` event replays to the exact absolute transform, and a later `ObjectState` for the same id overrides an earlier `ObjectOp`. Run EditMode → green.

- [ ] **Step 6 — `UnityReplayTarget.cs`:** implement
```csharp
        public void SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot)
        {
            var mo = ManipulableRegistry.Instance?.FindById(id);
            if (mo != null) mo.SetLocal(scale, pos, rot);
        }
```

- [ ] **Step 7 — `TimelineRecorder.cs`:** add `using MixedReality.Toolkit.SpatialManipulation;`. In `Start()`, also `StartCoroutine(HookManipulablesWhenReady());`. Add:
```csharp
        private System.Collections.IEnumerator HookManipulablesWhenReady()
        {
            // ManipulableRegistry registers props in its own Start(); wait until it's populated.
            float timeout = 5f;
            while (timeout > 0f && (ManipulableRegistry.Instance == null))
            { timeout -= Time.deltaTime; yield return null; }
            yield return null; // let registration finish
            var reg = ManipulableRegistry.Instance;
            if (reg == null) yield break;
            foreach (var mo in reg.All)
            {
                if (mo == null) continue;
                var om = mo.GetComponent<ObjectManipulator>();
                if (om == null) continue;
                var captured = mo;
                om.lastSelectExited.AddListener(_ => OnPropManipulated(captured));
            }
        }

        // A hand-grab of a prop ended — record its resulting absolute local transform.
        private void OnPropManipulated(ManipulableObject mo)
        {
            if (RewindInProgress || mo == null) return;
            mo.GetLocal(out var s, out var p, out var r);
            Log.AddObjectState(mo.Id, s, p, r, Now());
            SnapshotAiMemory();
        }
```
> `ManipulableObject` is in `EchoRealm.Interaction` — add `using EchoRealm.Interaction;` if not present. `lastSelectExited` is the MRTK/XRI manipulation-end event (same one `SceneManipulationReporter` uses).

- [ ] **Step 8 — commit:** `git add` the 7 files; `git commit -m "feat(rewind): record hand-grab manipulations as ObjectState events (R10)"`.

---

## Phase E — Save at end of scene

### Task 12: End-of-scene Save/Discard prompt + EndFilm hook

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/SceneSavePrompt.cs`
- Modify: `EchoRealm/Assets/Scripts/Film/FilmDirector.cs`

- [ ] **Step 1: Implement the prompt** (runtime panel; Save persists the recorder's timeline + the session log)

`EchoRealm/Assets/Scripts/Film/SceneSavePrompt.cs`:
```csharp
using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Film
{
    /// <summary>Shown once when the scene ends: "Save this scene?" with Save / Discard.
    /// Save writes the recorded timeline + SessionLogger text via SceneArchive. Master-only
    /// (the master owns the authoritative timeline). Attach to GameManager; call Show().</summary>
    public class SceneSavePrompt : MonoBehaviour
    {
        public static SceneSavePrompt Instance { get; private set; }
        private GameObject _go;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>Show the prompt (only meaningful on the device that recorded — the master).</summary>
        public void Show()
        {
            if (TimelineRecorder.Instance == null)
            {
                Debug.Log("[SceneSavePrompt] No recorder on this device — nothing to save.");
                return;
            }
            if (_go == null) Build();
            _go.SetActive(true);
            Place();
        }

        private void Save()
        {
            var rec = TimelineRecorder.Instance;
            string log = SessionLogger.Instance != null ? SessionLogger.Instance.ExportLog() : "";
            if (SessionLogger.Instance != null) rec.SetSessionId(SessionLogger.Instance.SessionId);
            string path = SceneArchive.Save(rec.Timeline, log);
            Debug.Log($"[SceneSavePrompt] Scene saved → {path}");
            Hide();
        }

        private void Hide() { if (_go != null) _go.SetActive(false); }

        private void Place()
        {
            var cam = Camera.main; if (cam == null) return;
            _go.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
            // Face the user (+Z toward the camera) so the prompt reads correctly. Placed once, then fixed.
            _go.transform.rotation = Quaternion.LookRotation(cam.transform.position - _go.transform.position, Vector3.up);
        }

        private void Build()
        {
            _go = new GameObject("SceneSavePrompt(Runtime)");
            MakeLabel("Save this scene?", new Vector3(0, 0.10f, 0));
            MakeButton("Save", new Vector3(-0.13f, 0, 0), Save, new Color(0.10f, 0.45f, 0.20f));
            MakeButton("Discard", new Vector3(0.13f, 0, 0), Hide, new Color(0.45f, 0.12f, 0.12f));
        }

        private void MakeLabel(string text, Vector3 localPos)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(_go.transform, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.08f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.05f;
        }

        private void MakeButton(string label, Vector3 localPos, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(_go.transform, false);
            root.transform.localPosition = localPos;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, 0.10f, 0.02f);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.22f, 0.10f, 0.012f);
            var bgCol = bg.GetComponent<Collider>(); if (bgCol != null) Destroy(bgCol);
            bg.GetComponent<MeshRenderer>().material.color = color;
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.09f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.05f;
            var si = root.AddComponent<StatefulInteractable>();
            si.OnClicked.AddListener(onClick);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
```

- [ ] **Step 2: Trigger it from `EndFilm`** — in `EchoRealm/Assets/Scripts/Film/FilmDirector.cs`, at the end of `EndFilm()` after the existing log block (~line 184):

```csharp
            // Offer to save the recorded scene (master holds the authoritative timeline).
            if (IsMaster) SceneSavePrompt.Instance?.Show();
```

- [ ] **Step 3: Compile check** — no errors.

- [ ] **Step 4: Manual test** — in `MainScene`, add `TimelineRecorder` + `SceneSavePrompt` to GameManager. Run a short session to the end (or call `FilmDirector.Instance.EndFilm()` from a debug key). Prompt appears → **Save** → confirm a `scene_*.json` + `.txt` appear under `Application.persistentDataPath/EchoRealmSaves` (log the path; on device it's the app's LocalState folder).

- [ ] **Step 5: Commit**

```powershell
git add "EchoRealm/Assets/Scripts/Film/SceneSavePrompt.cs" "EchoRealm/Assets/Scripts/Film/FilmDirector.cs"
git commit -m "feat(save): end-of-scene Save/Discard prompt"
```

---

## Phase F — Offline view-only playback

### Task 13: Replay-mode boot branch + startup choice

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/ReplayModeGate.cs`
- Modify: `EchoRealm/Assets/Scripts/Film/EchoRealmBootstrapper.cs`

- [ ] **Step 1: Implement the gate** (a static flag + a tiny startup chooser)

`EchoRealm/Assets/Scripts/Film/ReplayModeGate.cs`:
```csharp
using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Film
{
    /// <summary>Decides at startup whether to run the live film or load a saved scene. When
    /// "View Saved Scene" is chosen, ReplayMode is set true BEFORE the normal boot proceeds, so
    /// the bootstrapper skips QR/Fusion/voice/film entirely and hands off to ReplaySessionController.</summary>
    public class ReplayModeGate : MonoBehaviour
    {
        public static bool ReplayMode { get; private set; }

        [SerializeField] private ReplaySessionController replayController;
        private GameObject _go;

        /// <summary>Called by the bootstrapper at the very start. Returns true if it showed the
        /// chooser (boot should pause); false to proceed with the live film immediately.</summary>
        public bool ShowChooser()
        {
            if (replayController == null) replayController = FindObjectOfType<ReplaySessionController>(true);
            Build();
            return true;
        }

        private void ChooseLive()
        {
            ReplayMode = false;
            Hide();
            var boot = FindObjectOfType<EchoRealmBootstrapper>();
            if (boot != null) boot.BeginLiveBoot();
        }

        private void ChooseReplay()
        {
            ReplayMode = true;
            Hide();
            if (replayController != null) replayController.Begin();
            else Debug.LogError("[ReplayModeGate] No ReplaySessionController assigned.");
        }

        private void Hide() { if (_go != null) _go.SetActive(false); }

        private void Build()
        {
            _go = new GameObject("StartChooser(Runtime)");
            var cam = Camera.main;
            if (cam != null)
            {
                _go.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
                _go.transform.rotation = Quaternion.LookRotation(_go.transform.position - cam.transform.position);
            }
            Button("Start Live Film", new Vector3(-0.14f, 0, 0), ChooseLive, new Color(0.10f, 0.35f, 0.45f));
            Button("View Saved Scene", new Vector3(0.14f, 0, 0), ChooseReplay, new Color(0.32f, 0.20f, 0.45f));
        }

        private void Button(string label, Vector3 localPos, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(_go.transform, false);
            root.transform.localPosition = localPos;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.26f, 0.10f, 0.02f);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.26f, 0.10f, 0.012f);
            var bgc = bg.GetComponent<Collider>(); if (bgc != null) Destroy(bgc);
            bg.GetComponent<MeshRenderer>().material.color = color;
            var t = new GameObject("Label"); t.transform.SetParent(root.transform, false);
            t.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = label; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.24f, 0.09f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.045f;
            root.AddComponent<StatefulInteractable>().OnClicked.AddListener(onClick);
        }
    }
}
```

- [ ] **Step 2: Branch the bootstrapper** — in `EchoRealmBootstrapper.cs`, refactor `Start()` so its body becomes `BeginLiveBoot()`, and `Start()` shows the chooser first:

Replace the current `private void Start()` signature/opening (~line 60) so the existing body lives in a new public method, and add the gate. Concretely, rename `Start` to `BeginLiveBoot` (make it `public void BeginLiveBoot()`), then add:

```csharp
        [SerializeField] private ReplayModeGate replayGate;

        private void Start()
        {
            if (replayGate == null) replayGate = FindObjectOfType<ReplayModeGate>(true);
            if (replayGate != null) { replayGate.ShowChooser(); return; } // user picks Live vs Saved
            BeginLiveBoot(); // no gate present → behave exactly as before
        }
```

> If no `ReplayModeGate` is in the scene, boot is unchanged (isolation preserved). `BeginLiveBoot()` is the original `Start()` body verbatim.

- [ ] **Step 3: Compile check** — `ReplaySessionController` is created in Task 14; this won't compile until then. Proceed to Task 14, then compile both together.

- [ ] **Step 4: Commit (after Task 14 compiles)** — deferred; commit together with Task 14.

---

### Task 14: ReplaySessionController — load & reconstruct

**Files:**
- Create: `EchoRealm/Assets/Scripts/Film/ReplaySessionController.cs`

- [ ] **Step 1: Implement**

`EchoRealm/Assets/Scripts/Film/ReplaySessionController.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using EchoRealm.Interaction;
using MixedReality.Toolkit.SpatialManipulation;

namespace EchoRealm.Film
{
    /// <summary>Drives offline, view-only playback of a saved scene. No Fusion/voice/film run in
    /// replay mode, so nothing mutates the world but this controller. Exposes scrub controls used
    /// by ReplayUI. Reconstructs state with the same engine the live game uses.</summary>
    public class ReplaySessionController : MonoBehaviour
    {
        public SceneTimeline Timeline { get; private set; }
        public int Index { get; private set; }                 // current step (event index)
        public bool Loaded => Timeline != null && Timeline.events.Count > 0;

        private readonly UnityReplayTarget _target = new UnityReplayTarget();
        public System.Action OnChanged;                        // ReplayUI subscribes to refresh

        /// <summary>Called by ReplayModeGate when the user chooses "View Saved Scene".</summary>
        public void Begin()
        {
            DisableManipulation();
            // ReplayUI shows the save picker and calls LoadFile() with the chosen path.
            var ui = FindObjectOfType<ReplayUI>(true);
            if (ui != null) ui.ShowPicker();
            else Debug.LogError("[Replay] No ReplayUI in scene.");
        }

        public List<string> ListSaves() => SceneArchive.List();

        public void LoadFile(string path)
        {
            Timeline = SceneArchive.Load(path);
            if (Timeline == null) { Debug.LogWarning($"[Replay] Could not load {path}"); return; }
            Index = Timeline.events.Count;           // start at the end (final diorama)
            Reconstruct();
        }

        // ---- Scrub controls (used by ReplayUI) ----
        public int StepCount => Timeline != null ? Timeline.events.Count : 0;

        public void SeekToIndex(int index)
        {
            if (Timeline == null) return;
            Index = Mathf.Clamp(index, 0, Timeline.events.Count);
            Reconstruct();
        }

        public void StepForward() => SeekToIndex(Index + 1);
        public void StepBack() => SeekToIndex(Index - 1);

        public void RewindSeconds(float seconds)
        {
            if (Timeline == null) return;
            float curT = Index > 0 && Index <= Timeline.events.Count
                ? Timeline.events[Mathf.Clamp(Index - 1, 0, Timeline.events.Count - 1)].t : 0f;
            float targetT = Mathf.Max(0f, curT - seconds);
            int idx = 0;
            for (int k = 0; k < Timeline.events.Count; k++)
            {
                if (Timeline.events[k].t <= targetT) idx = k + 1; else break;
            }
            SeekToIndex(idx);
        }

        /// <summary>Utterances at or before the current step (for the transcript panel).</summary>
        public IEnumerable<TimelineEvent> UtterancesUpToNow()
        {
            if (Timeline == null) yield break;
            for (int k = 0; k < Index && k < Timeline.events.Count; k++)
                if (Timeline.events[k].kind == EventKind.AiUtterance) yield return Timeline.events[k];
        }

        private void Reconstruct()
        {
            float upTo = (Index <= 0) ? -1f
                : (Index >= Timeline.events.Count ? float.MaxValue : Timeline.events[Index - 1].t);
            TimelineReplayer.ApplyStateAt(Timeline, upTo, seeking: true, _target);
            OnChanged?.Invoke();
        }

        // View-only: props can't be grabbed during playback.
        private void DisableManipulation()
        {
            foreach (var om in FindObjectsOfType<ObjectManipulator>(true))
                om.enabled = false;
        }
    }
}
```

> `Reconstruct` uses `upTo = events[Index-1].t` so "Index = number of applied events." When `Index == count`, `upTo = MaxValue` (all events). When `Index == 0`, `upTo = -1` (baseline only).

- [ ] **Step 2: Compile check** — `ReplayUI` is created in Task 15; compile after Task 15.

- [ ] **Step 3: Commit (after Task 15)** — deferred.

---

### Task 15: ReplayUI — picker, scrubber, transcript, banner

**Files:**
- Create: `EchoRealm/Assets/Scripts/Interaction/ReplayUI.cs`

- [ ] **Step 1: Implement** (runtime panel; picker list + prev/next + Rewind 20s/1m + transcript + read-only banner)

`EchoRealm/Assets/Scripts/Interaction/ReplayUI.cs`:
```csharp
using UnityEngine;
using TMPro;
using MixedReality.Toolkit;
using EchoRealm.Film;
using System.IO;

namespace EchoRealm.Interaction
{
    /// <summary>The offline playback UI: a save-picker, then a transport bar (Prev/Next,
    /// Rewind 20s/1m), an AI transcript panel, and a persistent read-only banner.</summary>
    public class ReplayUI : MonoBehaviour
    {
        [SerializeField] private ReplaySessionController controller;

        private GameObject _picker, _transport, _banner;
        private TextMeshPro _transcript, _status;

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<ReplaySessionController>(true);
        }

        // ---- Save picker ----
        public void ShowPicker()
        {
            _picker = new GameObject("ReplayPicker(Runtime)");
            PlaceInFront(_picker.transform, 0.8f);
            var files = controller.ListSaves();
            MakeLabel(_picker.transform, "Saved scenes", new Vector3(0, 0.22f, 0), 0.05f);
            if (files.Count == 0)
                MakeLabel(_picker.transform, "(none found)", Vector3.zero, 0.04f);

            float y = 0.10f;
            foreach (var path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string captured = path; // closure capture
                MakeButton(_picker.transform, name, new Vector3(0, y, 0), () => Pick(captured),
                           new Color(0.18f, 0.22f, 0.35f), width: 0.5f);
                y -= 0.12f;
            }
        }

        private void Pick(string path)
        {
            if (_picker != null) Destroy(_picker);
            controller.OnChanged += Refresh;
            controller.LoadFile(path);
            BuildTransport();
            BuildBanner();
            Refresh();
        }

        // ---- Transport ----
        private void BuildTransport()
        {
            _transport = new GameObject("ReplayTransport(Runtime)");
            MakeButton(_transport.transform, "◀ Prev", new Vector3(-0.30f, 0, 0), () => controller.StepBack(), Teal());
            MakeButton(_transport.transform, "Next ▶", new Vector3(-0.10f, 0, 0), () => controller.StepForward(), Teal());
            MakeButton(_transport.transform, "⟲ 20s", new Vector3(0.10f, 0, 0), () => controller.RewindSeconds(20f), Purple());
            MakeButton(_transport.transform, "⟲ 1m", new Vector3(0.30f, 0, 0), () => controller.RewindSeconds(60f), Purple());

            var s = new GameObject("Status"); s.transform.SetParent(_transport.transform, false);
            s.transform.localPosition = new Vector3(0, 0.10f, 0);
            _status = s.AddComponent<TextMeshPro>();
            _status.alignment = TextAlignmentOptions.Center; _status.color = Color.white;
            _status.rectTransform.sizeDelta = new Vector2(0.7f, 0.06f);
            _status.enableAutoSizing = true; _status.fontSizeMin = 0.01f; _status.fontSizeMax = 0.04f;

            var tr = new GameObject("Transcript"); tr.transform.SetParent(_transport.transform, false);
            tr.transform.localPosition = new Vector3(0, 0.28f, 0);
            _transcript = tr.AddComponent<TextMeshPro>();
            _transcript.alignment = TextAlignmentOptions.Top; _transcript.color = new Color(0.85f, 0.85f, 1f);
            _transcript.rectTransform.sizeDelta = new Vector2(0.8f, 0.30f);
            _transcript.fontSize = 0.028f;
        }

        private void BuildBanner()
        {
            _banner = new GameObject("ReadOnlyBanner(Runtime)");
            var tmp = _banner.AddComponent<TextMeshPro>();
            tmp.text = "VIEWING SAVED SCENE — read only";
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = new Color(1f, 0.85f, 0.3f);
            tmp.rectTransform.sizeDelta = new Vector2(0.8f, 0.06f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.045f;
        }

        private void Refresh()
        {
            if (_status != null)
                _status.text = $"Step {controller.Index} / {controller.StepCount}";
            if (_transcript != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var u in controller.UtterancesUpToNow())
                    sb.AppendLine($"<b>{u.id}:</b> {u.text}");
                _transcript.text = sb.ToString();
            }
        }

        private void LateUpdate()
        {
            if (_transport != null) PlaceInFront(_transport.transform, 0.75f, lerp: 0.1f);
            if (_banner != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    _banner.transform.position = cam.transform.position + cam.transform.forward * 0.7f + cam.transform.up * 0.22f;
                    _banner.transform.rotation = Quaternion.LookRotation(_banner.transform.position - cam.transform.position);
                }
            }
        }

        // ---- helpers ----
        private static Color Teal() => new Color(0.05f, 0.42f, 0.5f);
        private static Color Purple() => new Color(0.32f, 0.18f, 0.45f);

        private void PlaceInFront(Transform t, float dist, float lerp = -1f)
        {
            var cam = Camera.main; if (cam == null) return;
            Vector3 target = cam.transform.position + cam.transform.forward * dist + cam.transform.up * -0.12f;
            t.position = lerp < 0 ? target : Vector3.Lerp(t.position, target, lerp);
            t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, cam.transform.up);
        }

        private void MakeLabel(Transform parent, string text, Vector3 localPos, float maxFont)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.06f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = maxFont;
        }

        private void MakeButton(Transform parent, string label, Vector3 localPos,
                                UnityEngine.Events.UnityAction onClick, Color color, float width = 0.18f)
        {
            var root = new GameObject($"Btn_{label}"); root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var col = root.AddComponent<BoxCollider>(); col.size = new Vector3(width, 0.09f, 0.02f);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(width, 0.09f, 0.012f);
            var bgc = bg.GetComponent<Collider>(); if (bgc != null) Destroy(bgc);
            bg.GetComponent<MeshRenderer>().material.color = color;
            var t = new GameObject("Label"); t.transform.SetParent(root.transform, false);
            t.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = label; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(width * 0.9f, 0.08f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.035f;
            root.AddComponent<StatefulInteractable>().OnClicked.AddListener(onClick);
        }
    }
}
```

- [ ] **Step 2: Compile check** — focus the editor; Tasks 13–15 now compile together. Fix any namespace mismatches.

- [ ] **Step 3: Scene wiring (manual)** — in `MainScene`, add to GameManager: `ReplaySessionController`, `ReplayUI`, `ReplayModeGate`. Assign `ReplayModeGate.replayController` → the controller, `ReplayUI.controller` → the controller, and `EchoRealmBootstrapper.replayGate` → the gate.

- [ ] **Step 4: Manual end-to-end test (device or editor)** — launch → **View Saved Scene** → pick a save from Task 12 → the grove appears in its final state with the read-only banner; **◀ Prev / Next ▶** step through changes (transcript updates with Oracle/AI lines); **⟲ 20s / 1m** jump back in time; props are not grabbable.

- [ ] **Step 5: Commit (Tasks 13–15 together)**

```powershell
git add "EchoRealm/Assets/Scripts/Film/ReplayModeGate.cs" "EchoRealm/Assets/Scripts/Film/ReplaySessionController.cs" "EchoRealm/Assets/Scripts/Interaction/ReplayUI.cs" "EchoRealm/Assets/Scripts/Film/EchoRealmBootstrapper.cs"
git commit -m "feat(replay): offline view-only playback (gate, controller, UI)"
```

---

## Final verification checklist

- [ ] All EditMode tests pass (`Test Runner → EditMode → Run All`).
- [ ] With NO `TimelineRecorder`/`ReplayModeGate` in the scene, the film boots and plays exactly as before (isolation/regression check).
- [ ] After a live rewind, `ActionCollector.Profile.VoiceCommandCount` (and nurture/chaos counts) reflect only commands up to T — rewound commands no longer sway the next AI variant decision (spec R9).
- [ ] Live: rewind 20s/1m on the master syncs both headsets; film continues from T.
- [ ] Save at end writes `scene_*.json` + `.txt` to `EchoRealmSaves`.
- [ ] Offline: pick a save → final diorama + read-only banner; step/rewind works; transcript shows what the AI said/decided; nothing is grabbable.
- [ ] `.meta` files generated for every new script/asmdef are committed (Unity creates them on import — `git add` them).

> **Unity `.meta` note:** when Unity imports the new files it generates `.meta` siblings. Commit them alongside each file (e.g. `git add EchoRealm/Assets/Scripts/Film/Timeline/*.meta`). The repo already tracks `.meta` files.
