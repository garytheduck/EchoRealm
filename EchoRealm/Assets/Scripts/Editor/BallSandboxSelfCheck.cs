using UnityEngine;
using UnityEditor;
using EchoRealm.Sandbox;

namespace EchoRealm.SandboxEditor
{
    /// <summary>Editor-only assertions for the sandbox's pure logic. Run via the menu.
    /// This is the unit-test substitute for code that can run without a device.</summary>
    public static class BallSandboxSelfCheck
    {
        [MenuItem("EchoRealm/Ball Sandbox/Run Self-Check")]
        public static void Run()
        {
            int fail = 0;

            // --- BallPhrases ---
            Check(ref fail, BallPhrases.Match("Drop a ball now") == BallPhrases.Intent.Spawn, "spawn: 'drop a ball'");
            Check(ref fail, BallPhrases.Match("spawn ball") == BallPhrases.Intent.Spawn, "spawn: 'spawn ball'");
            Check(ref fail, BallPhrases.Match("please remove the ball") == BallPhrases.Intent.Remove, "remove: 'remove the ball'");
            Check(ref fail, BallPhrases.Match("clear balls") == BallPhrases.Intent.Remove, "remove: 'clear balls'");
            Check(ref fail, BallPhrases.Match("make it rain") == BallPhrases.Intent.None, "none: unrelated");
            Check(ref fail, BallPhrases.Match("") == BallPhrases.Intent.None, "none: empty");

            // --- SandboxMath round-trip: world -> anchor-relative -> world is identity ---
            var anchorPos = new Vector3(1f, 2f, 3f);
            var anchorRot = Quaternion.Euler(10f, 45f, 0f);
            var worldPos = new Vector3(-2f, 0.5f, 4f);
            var worldRot = Quaternion.Euler(0f, 90f, 15f);
            SandboxMath.ToAnchorRelative(anchorPos, anchorRot, worldPos, worldRot, out var rp, out var rr);
            SandboxMath.FromAnchorRelative(anchorPos, anchorRot, rp, rr, out var wp, out var wr);
            Check(ref fail, (wp - worldPos).magnitude < 1e-3f, "math: pos round-trip");
            Check(ref fail, Quaternion.Angle(wr, worldRot) < 0.05f, "math: rot round-trip");

            // --- SandboxMath spawn placement: in front, flattened, lowered ---
            var p = SandboxMath.SpawnPositionInFront(Vector3.zero, Quaternion.Euler(30f, 0f, 0f), 0.5f, 0.2f);
            Check(ref fail, Mathf.Abs(p.z - 0.5f) < 1e-3f, "math: forward is flattened to horizontal");
            Check(ref fail, Mathf.Abs(p.y + 0.2f) < 1e-3f, "math: lowered by 'down'");

            // --- SandboxFloorMath: floor top is straight down from the anchor under gravity ---
            var fAnchor = new Vector3(1f, 2.5f, -3f);
            var fTop = SandboxFloorMath.FloorTopCenter(fAnchor, 1.0f);
            Check(ref fail, Mathf.Abs(fTop.y - (fAnchor.y - 1.0f)) < 1e-4f, "floor: top Y = anchorY - drop");
            Check(ref fail, Mathf.Abs(fTop.x - fAnchor.x) < 1e-4f && Mathf.Abs(fTop.z - fAnchor.z) < 1e-4f, "floor: centred under anchor X/Z");

            // --- SlidingWindowCounter: counts within window, evicts stale, reaches threshold at the Nth event ---
            var win = new SlidingWindowCounter();
            win.Add(0f); win.Add(1f); win.Add(2f); win.Add(3f);
            Check(ref fail, win.CountWithin(3f, 10f) == 4, "watchdog: 4 events within window counted");
            Check(ref fail, win.CountWithin(100f, 10f) == 0, "watchdog: stale events evicted");
            var win2 = new SlidingWindowCounter();
            for (int i = 0; i < 4; i++) win2.Add(i);
            Check(ref fail, win2.CountWithin(3f, 10f) < 5, "watchdog: 4 events do NOT reach threshold 5");
            win2.Add(4f);
            Check(ref fail, win2.CountWithin(4f, 10f) >= 5, "watchdog: 5th event reaches threshold 5");

            // --- benign Scene-Understanding line must NOT be treated as trouble ---
            const string benign = "[MROpenXR][Error] SceneComputer_Update_ComputeCompletedWithError";
            Check(ref fail, SceneUnderstandingWatchdog.IsBenignSceneLog(benign), "watchdog: benign SceneComputer line classified benign");
            Check(ref fail, !SceneUnderstandingWatchdog.IsTroubleSignal(benign, "", LogType.Error), "watchdog: benign line is NOT a trouble signal");
            Check(ref fail, SceneUnderstandingWatchdog.IsTroubleSignal("NullReferenceException", "at SpatialMeshManager.ConfigureOne ARMesh", LogType.Exception), "watchdog: a mesh exception IS a trouble signal");

            if (fail == 0) Debug.Log("[BallSandbox] Self-Check PASSED (all assertions).");
            else Debug.LogError($"[BallSandbox] Self-Check FAILED: {fail} assertion(s).");
        }

        private static void Check(ref int fail, bool ok, string label)
        {
            if (ok) Debug.Log($"[BallSandbox]   PASS: {label}");
            else { fail++; Debug.LogError($"[BallSandbox]   FAIL: {label}"); }
        }
    }
}
