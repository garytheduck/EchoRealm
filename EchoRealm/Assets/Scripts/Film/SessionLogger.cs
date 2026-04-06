using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>
    /// Logs all significant events during a session for:
    /// 1. AI context (sent to Ollama as part of prompts)
    /// 2. User study data collection (exported after session)
    /// 3. Debugging and replay analysis
    ///
    /// Captures: voice commands, AI responses, cooperation events,
    /// act transitions, gaze data, gesture interactions.
    /// </summary>
    public class SessionLogger : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Maximum number of log entries to keep in memory.")]
        [SerializeField] private int maxEntries = 500;

        [Header("Debug")]
        [SerializeField] private bool logToConsole = true;

        /// <summary>All logged events.</summary>
        public List<SessionEvent> Events { get; private set; } = new List<SessionEvent>();

        /// <summary>Session start timestamp.</summary>
        public float SessionStartTime { get; private set; }

        /// <summary>Unique session identifier.</summary>
        public string SessionId { get; private set; }

        public static SessionLogger Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            SessionStartTime = Time.time;
            SessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            LogEvent(EventType.System, "Session started", $"ID: {SessionId}");
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Log a session event.</summary>
        public void LogEvent(EventType type, string action, string details = "")
        {
            var entry = new SessionEvent
            {
                timestamp = Time.time - SessionStartTime,
                type = type,
                action = action,
                details = details
            };

            Events.Add(entry);

            // Trim if too many entries
            if (Events.Count > maxEntries)
                Events.RemoveAt(0);

            if (logToConsole)
                Debug.Log($"[Session] [{entry.timestamp:F1}s] [{type}] {action}" +
                          (string.IsNullOrEmpty(details) ? "" : $" — {details}"));
        }

        /// <summary>
        /// Export the session log as a formatted string.
        /// Useful for saving to file or sending for analysis.
        /// </summary>
        public string ExportLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== EchoRealm Session Log ===");
            sb.AppendLine($"Session ID: {SessionId}");
            sb.AppendLine($"Duration: {(Time.time - SessionStartTime):F1}s");
            sb.AppendLine($"Events: {Events.Count}");
            sb.AppendLine("---");

            foreach (var e in Events)
            {
                sb.AppendLine($"[{e.timestamp:F1}s] [{e.type}] {e.action}" +
                              (string.IsNullOrEmpty(e.details) ? "" : $" — {e.details}"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Build a condensed summary for AI context (last N events).
        /// </summary>
        public string GetRecentSummary(int count = 10)
        {
            int start = Mathf.Max(0, Events.Count - count);
            var sb = new StringBuilder();

            for (int i = start; i < Events.Count; i++)
            {
                var e = Events[i];
                sb.Append($"[{e.type}] {e.action}; ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Count events of a specific type.
        /// </summary>
        public int CountEvents(EventType type)
        {
            int count = 0;
            foreach (var e in Events)
                if (e.type == type) count++;
            return count;
        }
    }

    // ------------------------------------------------------------------
    // Data Types
    // ------------------------------------------------------------------

    public enum EventType
    {
        System,
        VoiceCommand,
        AIResponse,
        CommandExecuted,
        ActTransition,
        Cooperation,
        GazeEvent,
        GestureEvent,
        HintGiven,
        DialogueShown
    }

    [Serializable]
    public class SessionEvent
    {
        public float timestamp;
        public EventType type;
        public string action;
        public string details;
    }
}
