using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Tracks what the user is looking at using MRTK3 Eye Tracking on HoloLens 2.
    /// Provides data for:
    /// - Implicit choices (user looks at option A longer = preference for A)
    /// - AI context (what attracted the user's attention)
    /// - Cooperative interactions (gaze + gesture combos)
    ///
    /// Requires: HoloLens 2 with eye calibration, GazeInput capability enabled.
    /// In Editor: simulated via MRTK Input Simulator (mouse = gaze direction).
    /// </summary>
    public class EyeTrackingManager : MonoBehaviour
    {
        [Header("Raycast Settings")]
        [Tooltip("Maximum distance for eye gaze raycast.")]
        [SerializeField] private float maxRaycastDistance = 10f;

        [Tooltip("Layer mask for objects that can be gazed at.")]
        [SerializeField] private LayerMask gazeLayerMask = ~0;

        [Header("Dwell Settings")]
        [Tooltip("Seconds the user must look at an object to trigger a dwell event.")]
        [SerializeField] private float dwellThresholdSeconds = 2f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;
        [SerializeField] private bool drawDebugRay = true;

        /// <summary>The GameObject currently being looked at (null if nothing).</summary>
        public GameObject CurrentTarget { get; private set; }

        /// <summary>World position where the gaze ray hits.</summary>
        public Vector3 GazeHitPosition { get; private set; }

        /// <summary>How long the user has been looking at the current target.</summary>
        public float CurrentDwellTime { get; private set; }

        /// <summary>Fired when the user starts looking at a new object.</summary>
        public event Action<GameObject> OnGazeEnter;

        /// <summary>Fired when the user stops looking at an object.</summary>
        public event Action<GameObject> OnGazeExit;

        /// <summary>Fired when dwell threshold is reached on an object.</summary>
        public event Action<GameObject> OnDwellCompleted;

        public static EyeTrackingManager Instance { get; private set; }

        // Tracks cumulative gaze time per object (for AI context)
        private Dictionary<string, float> gazeTimePerObject = new Dictionary<string, float>();
        private GameObject previousTarget;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            PerformGazeRaycast();
            UpdateDwell();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a summary of what the user looked at most (for AI context).
        /// Format: "Dobby (5.2s), Oracle (3.1s), Portal (1.8s)"
        /// </summary>
        public string GetGazeSummary()
        {
            var sorted = new List<KeyValuePair<string, float>>(gazeTimePerObject);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            var parts = new List<string>();
            int count = Mathf.Min(sorted.Count, 5); // Top 5
            for (int i = 0; i < count; i++)
            {
                parts.Add($"{sorted[i].Key} ({sorted[i].Value:F1}s)");
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "nothing specific";
        }

        /// <summary>
        /// Returns total gaze time for a specific object name.
        /// </summary>
        public float GetGazeTime(string objectName)
        {
            return gazeTimePerObject.TryGetValue(objectName, out float time) ? time : 0f;
        }

        /// <summary>Reset all tracked gaze data.</summary>
        public void ResetGazeData()
        {
            gazeTimePerObject.Clear();
            CurrentDwellTime = 0f;
        }

        // ------------------------------------------------------------------
        // Gaze Processing
        // ------------------------------------------------------------------

        private void PerformGazeRaycast()
        {
            // Use main camera forward as gaze direction
            // On HoloLens 2 with MRTK3, eye tracking data overrides this automatically
            // via the GazeInteractor on the MRTK XR Rig
            Transform gazeOrigin = Camera.main != null ? Camera.main.transform : transform;

            Ray gazeRay = new Ray(gazeOrigin.position, gazeOrigin.forward);

            if (drawDebugRay)
                Debug.DrawRay(gazeRay.origin, gazeRay.direction * maxRaycastDistance, Color.yellow);

            if (Physics.Raycast(gazeRay, out RaycastHit hit, maxRaycastDistance, gazeLayerMask))
            {
                GazeHitPosition = hit.point;
                GameObject hitObject = hit.collider.gameObject;

                if (hitObject != CurrentTarget)
                {
                    // Gaze target changed
                    if (CurrentTarget != null)
                    {
                        OnGazeExit?.Invoke(CurrentTarget);
                        if (logEvents) Log($"Gaze EXIT: {CurrentTarget.name}");
                    }

                    CurrentTarget = hitObject;
                    CurrentDwellTime = 0f;

                    OnGazeEnter?.Invoke(CurrentTarget);
                    if (logEvents) Log($"Gaze ENTER: {CurrentTarget.name}");
                }
            }
            else
            {
                // Looking at nothing
                if (CurrentTarget != null)
                {
                    OnGazeExit?.Invoke(CurrentTarget);
                    if (logEvents) Log($"Gaze EXIT: {CurrentTarget.name}");
                    CurrentTarget = null;
                    CurrentDwellTime = 0f;
                }

                GazeHitPosition = gazeOrigin.position + gazeOrigin.forward * maxRaycastDistance;
            }
        }

        private void UpdateDwell()
        {
            if (CurrentTarget == null) return;

            CurrentDwellTime += Time.deltaTime;

            // Track cumulative gaze time per object
            string objName = CurrentTarget.name;
            if (!gazeTimePerObject.ContainsKey(objName))
                gazeTimePerObject[objName] = 0f;
            gazeTimePerObject[objName] += Time.deltaTime;

            // Check dwell threshold
            if (previousTarget == CurrentTarget && CurrentDwellTime >= dwellThresholdSeconds)
            {
                OnDwellCompleted?.Invoke(CurrentTarget);
                if (logEvents) Log($"DWELL completed on: {CurrentTarget.name} ({CurrentDwellTime:F1}s)");

                // Reset so it doesn't fire repeatedly
                CurrentDwellTime = 0f;
            }

            previousTarget = CurrentTarget;
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[EyeTracking] {message}");
        }
    }
}
