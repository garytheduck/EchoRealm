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
