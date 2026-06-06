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
