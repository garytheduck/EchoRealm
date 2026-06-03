using System.Collections.Generic;
using UnityEngine;
using EchoRealm.Interaction;

namespace EchoRealm.AI
{
    /// <summary>
    /// Central hub that receives ALL player interactions (voice, gesture, gaze, cooperation)
    /// from across the codebase and feeds them into PlayerBehaviorProfile.
    ///
    /// Every system that captures input calls one of this class's Record* methods:
    ///   • VoiceCommandProcessor  → RecordVoiceCommand()
    ///   • GestureManager         → RecordGesture()
    ///   • CooperationDetector    → RecordCooperation()
    ///   • EyeTrackingManager     → RecordGaze()
    ///
    /// NarrativeDecisionEngine calls GetBehaviorSummary() when it needs to ask the AI
    /// which scene variant to play next.
    ///
    /// In a multi-HoloLens session, only the MASTER device should make AI decisions.
    /// ActionCollector aggregates local data; the master's decision is then broadcast
    /// to all devices via FusionNetworkManager (see NarrativeDecisionEngine).
    /// </summary>
    public class ActionCollector : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Maximum recent actions kept for AI context window.")]
        [SerializeField] private int maxRecentActions = 20;

        [Header("Debug")]
        [SerializeField] private bool logActions = false;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        /// <summary>Cumulative behavioral profile for the entire session.</summary>
        public PlayerBehaviorProfile Profile { get; private set; } = new PlayerBehaviorProfile();

        /// <summary>Rolling log of the last N actions as human-readable strings.</summary>
        private readonly List<string> _recentActions = new List<string>();

        public static ActionCollector Instance { get; private set; }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

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
        // Record API — called by input systems
        // ------------------------------------------------------------------

        /// <summary>
        /// Called by VoiceCommandProcessor each time a voice command is sent to the AI.
        /// </summary>
        public void RecordVoiceCommand(string text)
        {
            Profile.RecordVoice();
            AddRecent($"Voice: \"{text}\"");
        }

        /// <summary>Classify an executed world command and tally its nurture/chaos tone.</summary>
        public void RecordWorldChange(string command)
        {
            var tone = CommandSentiment.Classify(command);
            Profile.RecordWorldChange(tone);
            if (tone != CommandTone.Neutral) AddRecent($"World: {command} ({tone})");
        }

        /// <summary>
        /// Called by GestureManager on grab/tap events.
        /// </summary>
        public void RecordGesture(InteractionType type, string objectName)
        {
            Profile.RecordManipulation(objectName);
            AddRecent($"Gesture: {type} on '{objectName}'");
        }

        /// <summary>
        /// Called by CooperationDetector when two players cooperate.
        /// </summary>
        public void RecordCooperation(string description)
        {
            Profile.RecordCooperation();
            AddRecent($"Cooperation: {description}");
        }

        /// <summary>
        /// Called by EyeTrackingManager when the player dwells on an object.
        /// </summary>
        public void RecordGaze(string objectName)
        {
            Profile.RecordGaze(objectName);
            AddRecent($"Gaze: '{objectName}'");
        }

        // ------------------------------------------------------------------
        // Query API — called by NarrativeDecisionEngine
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the full behavior summary string sent inside the AI scene-decision prompt.
        /// Includes cumulative profile + last 5 actions for temporal context.
        /// </summary>
        public string GetBehaviorSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Profile.GetAISummary());

            if (_recentActions.Count > 0)
            {
                int start = Mathf.Max(0, _recentActions.Count - 5);
                var recent = _recentActions.GetRange(start, _recentActions.Count - start);
                sb.Append($" Last {recent.Count} actions: [{string.Join("; ", recent)}].");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Clears the rolling recent-actions list at the start of each act
        /// so the AI gets act-scoped context rather than the entire session.
        /// Does NOT reset the cumulative profile — that persists until session end.
        /// </summary>
        public void ResetForNewAct()
        {
            _recentActions.Clear();
            Log("Recent actions cleared for new act.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void AddRecent(string action)
        {
            _recentActions.Add(action);
            if (_recentActions.Count > maxRecentActions)
                _recentActions.RemoveAt(0);
            Log(action);
        }

        private void Log(string msg)
        {
            if (logActions)
                Debug.Log($"[ActionCollector] {msg}");
        }
    }
}
