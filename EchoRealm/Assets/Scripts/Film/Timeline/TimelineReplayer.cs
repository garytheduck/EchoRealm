namespace EchoRealm.Film
{
    /// <summary>The one engine both features use. Resets to baseline, then re-applies every
    /// event with t &lt;= upTo in order. When seeking, one-shot FX are skipped. AiUtterance
    /// events have no scene effect (the transcript UI surfaces them).</summary>
    public static class TimelineReplayer
    {
        public static void ApplyStateAt(SceneTimeline tl, float upTo, bool seeking, IReplayTarget target)
        {
            if (tl == null || target == null) return;

            target.ResetToBaseline();

            var events = tl.events;
            for (int k = 0; k < events.Count; k++)
            {
                var e = events[k];
                if (e.t > upTo) break;                 // events are stored in chronological order
                if (seeking && e.transient) continue;  // skip one-shot FX when seeking/rewinding

                switch (e.kind)
                {
                    case EventKind.WorldCommand:
                        target.ApplyWorldCommand(e.id);
                        break;
                    case EventKind.ObjectOp:
                        target.ApplyObjectOp(e.id, e.i, e.f, e.v);
                        break;
                    case EventKind.ActTransition:
                        target.ApplyActState(e.i, e.text);
                        break;
                    case EventKind.AiUtterance:
                        break; // transcript-only
                }
            }
        }
    }
}
