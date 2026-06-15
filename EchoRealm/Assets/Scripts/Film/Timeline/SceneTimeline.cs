using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Mirror of EchoRealm.Interaction.ObjOpType for the headless core (which cannot
    /// reference Assembly-CSharp). Values MUST match ObjOpType: Scale=0, Move=1, Rotate=2, Reset=3.</summary>
    internal enum ReplayObjOp { Scale = 0, Move = 1, Rotate = 2, Reset = 3 }

    /// <summary>Kinds of recorded events. WorldCommand/ObjectOp/ActTransition/ObjectState affect scene
    /// state on replay; AiUtterance is transcript-only. CharacterPose is read ONLY by the offline
    /// saved-scene viewer (to replay astronaut/Oracle movement) — the reconstruction engine
    /// (TimelineReplayer.ApplyStateAt) has no case for it and ignores it, so live rewind is unaffected.
    /// CharacterPose is appended LAST so existing saved files keep their integer kind values.</summary>
    public enum EventKind { WorldCommand, ObjectOp, ActTransition, AiUtterance, ObjectState, CharacterPose }

    /// <summary>One thing that happened, with its timestamp. Flat tagged record so Unity's
    /// JsonUtility can round-trip it (JsonUtility cannot serialize polymorphic subclasses).
    /// Only the fields relevant to <see cref="kind"/> are populated.</summary>
    [Serializable]
    public class TimelineEvent
    {
        public float t;          // seconds since recording start
        public EventKind kind;
        public bool transient;   // momentary FX (earthquake/lightning/anim triggers) — skipped on seek

        public string id;        // object id | world-command name | utterance speaker | character id
        public string text;      // AI utterance text | act variant
        public int i;            // object op type (ObjOpType) | act number
        public float f;          // scale factor (Scale) | yaw degrees (Rotate)
        public Vector3 v;        // move delta (Move) | ObjectState pos | CharacterPose: SceneRoot-local position
        public Vector3 v2;       // ObjectState: absolute local scale (v = pos, q = rot)
        public Quaternion q;     // ObjectState abs local rotation | CharacterPose: SceneRoot-local rotation
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
