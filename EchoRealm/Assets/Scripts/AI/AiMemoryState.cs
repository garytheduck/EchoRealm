using System;
using System.Collections.Generic;

namespace EchoRealm.AI
{
    /// <summary>In-memory snapshot of the AI's accumulated "memory" at a moment in time: the
    /// cumulative PlayerBehaviorProfile counters + recent actions (ActionCollector) and the command
    /// logs/mood (NarrativeManager). TimelineRecorder keeps one per event and restores the one at T
    /// on rewind, so rewound commands stop influencing future AI decisions. Not saved to disk.</summary>
    [Serializable]
    public class AiMemoryState
    {
        public float t;
        public int voice, manipulation, cooperation, gaze, nurture, chaos;
        public List<string> interactedObjects = new List<string>();
        public List<string> recentActions = new List<string>();
        public List<string> voiceLog = new List<string>();
        public List<string> executedLog = new List<string>();
        public int narrativeCooperation;
        public string mood = "mysterious";
    }
}
