using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Kinds of recorded events. WorldCommand/ObjectOp/ActTransition affect scene
    /// state on replay; AiUtterance is transcript-only (no scene effect).</summary>
    public enum EventKind { WorldCommand, ObjectOp, ActTransition, AiUtterance }

    /// <summary>One thing that happened, with its timestamp. Flat tagged record so Unity's
    /// JsonUtility can round-trip it (JsonUtility cannot serialize polymorphic subclasses).
    /// Only the fields relevant to <see cref="kind"/> are populated.</summary>
    [Serializable]
    public class TimelineEvent
    {
        public float t;          // seconds since recording start
        public EventKind kind;
        public bool transient;   // momentary FX (earthquake/lightning/anim triggers) — skipped on seek

        public string id;        // object id | world-command name | utterance speaker
        public string text;      // AI utterance text | act variant
        public int i;            // object op type (ObjOpType) | act number
        public float f;          // scale factor (Scale) | yaw degrees (Rotate)
        public Vector3 v;        // move delta (Move)
    }

    /// <summary>Header info saved alongside the events.</summary>
    [Serializable]
    public class TimelineMeta
    {
        public string sessionId;
        public string savedAtIso;
        public float durationSec;
        public int finalAct;
        public string sceneVersion = "1";
        public int eventCount;
    }

    /// <summary>The full recording: header + ordered events. Single source of truth for
    /// both rewind and saved-scene playback.</summary>
    [Serializable]
    public class SceneTimeline
    {
        public TimelineMeta meta = new TimelineMeta();
        public List<TimelineEvent> events = new List<TimelineEvent>();
    }
}
