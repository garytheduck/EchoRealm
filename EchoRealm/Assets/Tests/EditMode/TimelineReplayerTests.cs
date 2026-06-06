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
