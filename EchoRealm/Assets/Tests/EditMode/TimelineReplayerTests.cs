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
            public Dictionary<string, (Vector3 scale, Vector3 pos, Quaternion rot)> states
                = new Dictionary<string, (Vector3 scale, Vector3 pos, Quaternion rot)>();
            public int act; public string variant;

            public void ResetToBaseline()
            {
                calls.Add("reset"); flags.Clear(); scale.Clear(); states.Clear(); act = 0; variant = null;
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
            public void SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot)
            {
                calls.Add("state:" + id);
                states[id] = (scale, pos, rot);
            }
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

        [Test]
        public void ApplyStateAt_ObjectState_RestoresExactAbsoluteTransform()
        {
            var scale = new Vector3(1.7f, 1.7f, 1.7f);
            var pos = new Vector3(0.2f, -0.1f, 0.4f);
            var rot = Quaternion.Euler(0f, 35f, 0f);

            var tl = new SceneTimeline();
            tl.events.Add(new TimelineEvent
            {
                kind = EventKind.ObjectState, id = "Cloud", v = pos, v2 = scale, q = rot, t = 1f
            });
            var f = new FakeTarget();

            TimelineReplayer.ApplyStateAt(tl, 10f, true, f);

            Assert.IsTrue(f.states.ContainsKey("Cloud"));
            Assert.AreEqual(scale, f.states["Cloud"].scale); // v2 -> scale
            Assert.AreEqual(pos, f.states["Cloud"].pos);     // v  -> pos
            Assert.AreEqual(rot, f.states["Cloud"].rot);     // q  -> rot
        }

        [Test]
        public void ApplyStateAt_ObjectState_AfterObjectOp_AbsoluteOverridesRelative()
        {
            // A relative scale op, then an absolute hand-grab state for the SAME id.
            // The later ObjectState is authoritative — it restores an exact transform regardless
            // of the earlier relative op.
            var absScale = new Vector3(2.5f, 2.5f, 2.5f);
            var absPos = new Vector3(0.3f, 0f, 0.1f);
            var absRot = Quaternion.Euler(0f, 90f, 0f);

            var tl = new SceneTimeline();
            tl.events.Add(new TimelineEvent { kind = EventKind.ObjectOp, id = "Cloud", i = 0, f = 2f, t = 1f });
            tl.events.Add(new TimelineEvent
            {
                kind = EventKind.ObjectState, id = "Cloud", v = absPos, v2 = absScale, q = absRot, t = 2f
            });
            var f = new FakeTarget();

            TimelineReplayer.ApplyStateAt(tl, 10f, true, f);

            // Absolute state present and exact (it wins over the relative op).
            Assert.IsTrue(f.states.ContainsKey("Cloud"));
            Assert.AreEqual(absScale, f.states["Cloud"].scale);
            Assert.AreEqual(absPos, f.states["Cloud"].pos);
            Assert.AreEqual(absRot, f.states["Cloud"].rot);
            // The ObjectState was applied after the ObjectOp (chronological order preserved).
            Assert.Less(f.calls.IndexOf("op:Cloud"), f.calls.IndexOf("state:Cloud"));
        }
    }
}
