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

        public void AddObjectOp(string id, int opType, float factor, Vector3 delta, float degrees, float t = 0f)
        {
            // One scalar field carries factor (Scale) OR degrees (Rotate); Move uses the delta.
            float scalar = opType == 2 /*Rotate*/ ? degrees : factor;
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

        /// <summary>Drop every event with t &gt; cutoff (used by rewind).</summary>
        public void TruncateAfter(float cutoff)
        {
            var ev = Timeline.events;
            for (int k = ev.Count - 1; k >= 0; k--)
                if (ev[k].t > cutoff) ev.RemoveAt(k);
        }
    }
}
