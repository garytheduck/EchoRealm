using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// Tracks how the players have interacted throughout a session across all modalities:
    /// voice, gesture, gaze, and cooperation. Updated by ActionCollector.
    ///
    /// Used by NarrativeDecisionEngine to build the behavior summary sent to the AI
    /// at each act transition, so the AI can choose the most fitting scene variant.
    /// </summary>
    public class PlayerBehaviorProfile
    {
        // ------------------------------------------------------------------
        // Counters (updated by ActionCollector)
        // ------------------------------------------------------------------

        public int VoiceCommandCount  { get; private set; }
        public int ManipulationCount  { get; private set; }
        public int CooperationCount   { get; private set; }
        public int GazeEventCount     { get; private set; }

        public int NurtureCount { get; private set; }
        public int ChaosCount   { get; private set; }

        private readonly HashSet<string> _interactedObjects = new HashSet<string>();

        /// <summary>Number of distinct objects the players touched or gazed at.</summary>
        public int UniqueObjectsInteracted => _interactedObjects.Count;

        /// <summary>Total interactions across all modalities.</summary>
        public int TotalInteractionCount =>
            VoiceCommandCount + ManipulationCount + CooperationCount + GazeEventCount;

        // ------------------------------------------------------------------
        // Record methods
        // ------------------------------------------------------------------

        public void RecordVoice() => VoiceCommandCount++;

        public void RecordManipulation(string objectName)
        {
            ManipulationCount++;
            if (!string.IsNullOrEmpty(objectName))
                _interactedObjects.Add(objectName);
        }

        public void RecordCooperation() => CooperationCount++;

        public void RecordGaze(string objectName)
        {
            GazeEventCount++;
            if (!string.IsNullOrEmpty(objectName))
                _interactedObjects.Add(objectName);
        }

        public void RecordWorldChange(CommandTone tone)
        {
            if (tone == CommandTone.Nurture) NurtureCount++;
            else if (tone == CommandTone.Chaos) ChaosCount++;
        }

        /// <summary>"nurturing", "chaotic", or "balanced" based on the world commands given.</summary>
        public string WorldTone
        {
            get
            {
                if (NurtureCount == 0 && ChaosCount == 0) return "untouched";
                if (NurtureCount >= ChaosCount * 2) return "nurturing";
                if (ChaosCount >= NurtureCount * 2) return "chaotic";
                return "balanced";
            }
        }

        // ------------------------------------------------------------------
        // Derived properties
        // ------------------------------------------------------------------

        /// <summary>
        /// The single behavioral archetype that dominated this session.
        /// Priority (when tied): Talker > Explorer > Cooperator > Observer.
        ///
        ///   Talker      — many voice commands relative to other inputs
        ///   Explorer    — many grab/manipulation events, diverse object contact
        ///   Cooperator  — many cooperation events between players
        ///   Observer    — passive, few interactions of any kind
        /// </summary>
        public string DominantArchetype
        {
            get
            {
                if (TotalInteractionCount == 0) return "Observer";

                int maxCount = Mathf.Max(VoiceCommandCount, ManipulationCount, CooperationCount);

                if (maxCount == 0)               return "Observer";
                if (maxCount == VoiceCommandCount) return "Talker";
                if (maxCount == ManipulationCount) return "Explorer";
                if (maxCount == CooperationCount)  return "Cooperator";
                return "Observer";
            }
        }

        /// <summary>
        /// Builds a human-readable + AI-readable summary of player behavior.
        /// Sent verbatim inside the AI scene-decision prompt.
        /// </summary>
        public string GetAISummary()
        {
            return
                $"Dominant archetype: {DominantArchetype}. " +
                $"Voice commands given: {VoiceCommandCount}. " +
                $"Object manipulations (grabs/taps): {ManipulationCount}. " +
                $"Cooperation events: {CooperationCount}. " +
                $"Gaze interactions: {GazeEventCount}. " +
                $"Unique objects touched or gazed at: {UniqueObjectsInteracted}. " +
                $"World tone: {WorldTone} (nurturing acts: {NurtureCount}, chaotic acts: {ChaosCount}). " +
                $"Total interactions: {TotalInteractionCount}.";
        }

        /// <summary>Reset all counters (called between acts if needed).</summary>
        public void Reset()
        {
            VoiceCommandCount  = 0;
            ManipulationCount  = 0;
            CooperationCount   = 0;
            GazeEventCount     = 0;
            NurtureCount       = 0;
            ChaosCount         = 0;
            _interactedObjects.Clear();
        }

        /// <summary>Copy the cumulative counters into a snapshot (for rewind rollback).</summary>
        public void CaptureInto(AiMemoryState m)
        {
            m.voice = VoiceCommandCount; m.manipulation = ManipulationCount;
            m.cooperation = CooperationCount; m.gaze = GazeEventCount;
            m.nurture = NurtureCount; m.chaos = ChaosCount;
            m.interactedObjects = new List<string>(_interactedObjects);
        }

        /// <summary>Restore the cumulative counters from a snapshot (rewind rollback).</summary>
        public void RestoreFrom(AiMemoryState m)
        {
            VoiceCommandCount = m.voice; ManipulationCount = m.manipulation;
            CooperationCount = m.cooperation; GazeEventCount = m.gaze;
            NurtureCount = m.nurture; ChaosCount = m.chaos;
            _interactedObjects.Clear();
            if (m.interactedObjects != null)
                foreach (var o in m.interactedObjects) _interactedObjects.Add(o);
        }
    }
}
