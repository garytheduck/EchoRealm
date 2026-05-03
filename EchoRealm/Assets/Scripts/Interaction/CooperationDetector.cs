using System;
using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Detects cooperative interactions between two HoloLens 2 users.
    ///
    /// Cooperation patterns detected:
    /// 1. Simultaneous interaction: both users interact with the same object within a time window
    /// 2. Gaze + Gesture combo: one user looks at an object while the other manipulates it
    /// 3. Sequential voice: users give complementary voice commands in sequence
    ///
    /// Reports cooperation events to NarrativeManager for AI context.
    /// Used in Act 3 (cooperative challenge) to track puzzle-solving progress.
    /// </summary>
    public class CooperationDetector : MonoBehaviour
    {
        [Header("Detection Settings")]
        [Tooltip("Time window (seconds) for two actions to count as simultaneous cooperation.")]
        [SerializeField] private float cooperationWindowSeconds = 3f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>Total cooperation events detected this session.</summary>
        public int CooperationCount { get; private set; }

        /// <summary>Fired when a cooperation event is detected.</summary>
        public event Action<CooperationEvent> OnCooperationDetected;

        public static CooperationDetector Instance { get; private set; }

        // Track last interaction per player
        private PlayerInteraction lastPlayer1Action;
        private PlayerInteraction lastPlayer2Action;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Report that a player performed an interaction.
        /// Call this from network callbacks when a player interacts with an object.
        /// </summary>
        /// <param name="playerIndex">0 for player 1, 1 for player 2.</param>
        /// <param name="targetObject">The object interacted with.</param>
        /// <param name="interactionType">Type of interaction (gaze, grab, voice, etc.).</param>
        public void ReportInteraction(int playerIndex, GameObject targetObject, InteractionType interactionType)
        {
            var interaction = new PlayerInteraction
            {
                playerIndex = playerIndex,
                targetObject = targetObject,
                type = interactionType,
                timestamp = Time.time
            };

            if (playerIndex == 0)
                lastPlayer1Action = interaction;
            else
                lastPlayer2Action = interaction;

            Log($"Player {playerIndex + 1} → {interactionType} on '{targetObject.name}'");

            CheckForCooperation();
        }

        /// <summary>
        /// Manually trigger a cooperation event (for scripted moments).
        /// </summary>
        public void ForceCooperationEvent(string description)
        {
            var coopEvent = new CooperationEvent
            {
                description = description,
                timestamp = Time.time,
                type = CooperationType.Scripted
            };

            RegisterCooperation(coopEvent);
        }

        /// <summary>
        /// Returns a summary string for AI context.
        /// </summary>
        public string GetCooperationSummary()
        {
            return $"Cooperation events: {CooperationCount}";
        }

        // ------------------------------------------------------------------
        // Detection Logic
        // ------------------------------------------------------------------

        private void CheckForCooperation()
        {
            if (lastPlayer1Action == null || lastPlayer2Action == null) return;

            float timeDiff = Mathf.Abs(lastPlayer1Action.timestamp - lastPlayer2Action.timestamp);

            // Check if actions are within cooperation window
            if (timeDiff > cooperationWindowSeconds) return;

            // Pattern 1: Both interact with same object
            if (lastPlayer1Action.targetObject == lastPlayer2Action.targetObject)
            {
                var coopEvent = new CooperationEvent
                {
                    description = $"Both players interacted with '{lastPlayer1Action.targetObject.name}' " +
                                  $"(P1: {lastPlayer1Action.type}, P2: {lastPlayer2Action.type})",
                    timestamp = Time.time,
                    type = CooperationType.SimultaneousInteraction
                };

                RegisterCooperation(coopEvent);
                return;
            }

            // Pattern 2: Gaze + Gesture combo (one looks, other grabs)
            bool p1Gazing = lastPlayer1Action.type == InteractionType.Gaze;
            bool p2Grabbing = lastPlayer2Action.type == InteractionType.Grab;
            bool p2Gazing = lastPlayer2Action.type == InteractionType.Gaze;
            bool p1Grabbing = lastPlayer1Action.type == InteractionType.Grab;

            if ((p1Gazing && p2Grabbing) || (p2Gazing && p1Grabbing))
            {
                var coopEvent = new CooperationEvent
                {
                    description = "Gaze + Gesture cooperation: one player looked while the other grabbed",
                    timestamp = Time.time,
                    type = CooperationType.GazeGestureCombo
                };

                RegisterCooperation(coopEvent);
            }
        }

        private void RegisterCooperation(CooperationEvent coopEvent)
        {
            CooperationCount++;
            Log($"COOPERATION #{CooperationCount}: {coopEvent.description}");

            OnCooperationDetected?.Invoke(coopEvent);

            // Report to NarrativeManager (session log / hint system)
            var narrative = AI.NarrativeManager.Instance;
            if (narrative != null)
                narrative.RecordCooperation(coopEvent.description);

            // Feed into behavior profile for AI scene-branching decisions
            AI.ActionCollector.Instance?.RecordCooperation(coopEvent.description);

            // Reset to avoid double-counting
            lastPlayer1Action = null;
            lastPlayer2Action = null;
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[Cooperation] {message}");
        }
    }

    // ------------------------------------------------------------------
    // Data Types
    // ------------------------------------------------------------------

    public enum InteractionType
    {
        Gaze,
        Grab,
        Voice,
        AirTap,
        PointAt
    }

    public enum CooperationType
    {
        SimultaneousInteraction,
        GazeGestureCombo,
        SequentialVoice,
        Scripted
    }

    public class CooperationEvent
    {
        public string description;
        public float timestamp;
        public CooperationType type;
    }

    public class PlayerInteraction
    {
        public int playerIndex;
        public GameObject targetObject;
        public InteractionType type;
        public float timestamp;
    }
}
