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
