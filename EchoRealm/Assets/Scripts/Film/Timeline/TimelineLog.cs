using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Pure append/truncate operations over a SceneTimeline. The MonoBehaviour
    /// TimelineRecorder wraps this and feeds it timestamps from Unity events.</summary>
    public class TimelineLog
    {
        public SceneTimeline Timeline { get; } = new SceneTimeline();

        public void AddWorldCommand(string command, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.WorldCommand,
                id = command, transient = TransientCommands.IsTransient(command)
            });
        }

        public void AddObjectOp(string id, int opType, float factor, Vector3 delta, float degrees, float t)
        {
            // One scalar field carries factor (Scale) OR degrees (Rotate); Move/Reset use the delta.
            float scalar = opType == (int)ReplayObjOp.Rotate ? degrees : factor;
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.ObjectOp, id = id, i = opType, f = scalar, v = delta
            });
        }

        public void AddActTransition(int act, string variant, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.ActTransition, i = act, text = variant
            });
        }

        public void AddUtterance(string speaker, string text, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.AiUtterance, id = speaker, text = text
            });
        }

        public void AddObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.ObjectState, id = id, v = pos, v2 = scale, q = rot
            });
        }

        /// <summary>Record a character's pose (astronaut/Oracle) in SceneRoot-LOCAL space so it is
        /// anchor-independent on replay. Read only by the offline viewer; the reconstruction engine
        /// ignores CharacterPose, so this is invisible to live rewind.</summary>
        public void AddCharacterPose(string id, Vector3 localPos, Quaternion localRot, float t)
        {
            Timeline.events.Add(new TimelineEvent
            {
                t = t, kind = EventKind.CharacterPose, id = id, v = localPos, q = localRot
            });
        }

        /// <summary>Drop every event with t &gt; cutoff (used by rewind).</summary>
        public void TruncateAfter(float cutoff)
        {
            var ev = Timeline.events;
            for (int k = ev.Count - 1; k >= 0; k--)
                if (ev[k].t > cutoff) ev.RemoveAt(k);
        }
    }
}
