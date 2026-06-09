using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace EchoRealm.Sandbox
{
    /// <summary>
    /// Safety net for the optional room-mesh (layer "B"). Watches for Scene-Understanding / AR-mesh
    /// trouble in a sliding window and, past a threshold, DISABLES the ARMeshManager so the app
    /// degrades to floor-only instead of riding toward a native crash. Owns no colliders — it only
    /// watches logs and flips a flag. The SandboxFloor (A) is never touched, so the ball keeps bouncing.
    ///
    /// HONEST LIMITATION: a hard NATIVE "SU Work" thread crash the instant Scene Understanding is
    /// touched cannot be intercepted by managed code. This watchdog mitigates a GRADUAL failure (and
    /// isolates managed exceptions); the real no-crash guarantee is that B is opt-in — this object
    /// ships inactive, so the default film never touches Scene Understanding.
    /// </summary>
    public class SceneUnderstandingWatchdog : MonoBehaviour
    {
        [SerializeField] private ARMeshManager meshManager;
        [Tooltip("The SpatialMeshManager driving the mesh — disabled too on trip so it stops adding colliders.")]
        [SerializeField] private MonoBehaviour meshManagerComponent;
        [SerializeField] private int errorThreshold = 5;
        [SerializeField] private float windowSeconds = 10f;
        [SerializeField] private bool disableOnTrip = true;
        [SerializeField] private bool logVerbose = true;

        private readonly SlidingWindowCounter _counter = new SlidingWindowCounter();
        private bool _tripped;

        private void OnEnable() { Application.logMessageReceived += OnLog; }
        private void OnDisable() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (_tripped) return;
            if (!IsTroubleSignal(condition, stackTrace, type)) return;

            _counter.Add(Time.unscaledTime);
            if (_counter.CountWithin(Time.unscaledTime, windowSeconds) >= errorThreshold) Trip();
        }

        private void Trip()
        {
            _tripped = true;
            if (logVerbose)
                Debug.LogWarning("[BallSandbox][Watchdog] Scene-Understanding trouble threshold hit — " +
                                 "disabling room mesh, falling back to FLOOR-ONLY. Film unaffected.");
            if (!disableOnTrip) return;
            if (meshManager != null) meshManager.enabled = false;             // stop driving Scene Understanding
            if (meshManagerComponent != null) meshManagerComponent.enabled = false;
        }

        /// <summary>Classify a log line as genuine mesh/SU trouble. Hard-excludes the benign
        /// SceneComputer line (it appears in every working session). Counts managed mesh exceptions /
        /// errors that mention the mesh subsystem; everything else is ignored.</summary>
        public static bool IsTroubleSignal(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition)) return false;
            if (IsBenignSceneLog(condition)) return false;
            if (type != LogType.Exception && type != LogType.Error) return false;
            return Mentions(condition) || Mentions(stackTrace ?? string.Empty);
        }

        private static bool Mentions(string text)
            => text.Contains("ARMesh") || text.Contains("BakeMesh") || text.Contains("MeshCollider")
            || text.Contains("SceneUnderstanding") || text.Contains("SceneObserver")
            || text.Contains("XRMeshSubsystem") || text.Contains("meshesChanged");

        /// <summary>The benign, ever-present Microsoft-OpenXR scene-compute line — never counted.</summary>
        public static bool IsBenignSceneLog(string condition)
            => !string.IsNullOrEmpty(condition)
            && condition.Contains("SceneComputer_Update_ComputeCompletedWithError");
    }

    /// <summary>Pure sliding-window event counter (unit-tested by the editor self-check).</summary>
    public class SlidingWindowCounter
    {
        private readonly Queue<float> _stamps = new Queue<float>();

        public void Add(float timestamp) => _stamps.Enqueue(timestamp);

        /// <summary>Events within [now - window, now]; evicts older entries as a side effect.</summary>
        public int CountWithin(float now, float window)
        {
            float cutoff = now - window;
            while (_stamps.Count > 0 && _stamps.Peek() < cutoff) _stamps.Dequeue();
            return _stamps.Count;
        }

        public void Clear() => _stamps.Clear();
    }
}
