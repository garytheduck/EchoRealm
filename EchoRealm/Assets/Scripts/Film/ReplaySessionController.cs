using System.Collections.Generic;
using UnityEngine;
using EchoRealm.Interaction;
using MixedReality.Toolkit.SpatialManipulation;

namespace EchoRealm.Film
{
    /// <summary>Drives offline, view-only playback of a saved scene. No Fusion/voice/film run in
    /// replay mode, so nothing mutates the world but this controller. Exposes scrub controls used
    /// by ReplayUI. Reconstructs state with the same engine the live game uses.</summary>
    public class ReplaySessionController : MonoBehaviour
    {
        public SceneTimeline Timeline { get; private set; }
        public int Index { get; private set; }                  // current step (event index)
        public bool Loaded => Timeline != null && Timeline.events.Count > 0;

        private readonly UnityReplayTarget _target = new UnityReplayTarget();
        public System.Action OnChanged;                         // ReplayUI subscribes to refresh

        /// <summary>Called by ReplayModeGate when the user chooses "View Saved Scene".</summary>
        public void Begin()
        {
            DisableManipulation();
            // ReplayUI shows the save picker and calls LoadFile() with the chosen path.
            var ui = FindObjectOfType<ReplayUI>(true);
            if (ui != null) ui.ShowPicker();
            else Debug.LogError("[Replay] No ReplayUI in scene.");
        }

        public List<string> ListSaves() => SceneArchive.List();

        public void LoadFile(string path)
        {
            Timeline = SceneArchive.Load(path);
            if (Timeline == null) { Debug.LogWarning($"[Replay] Could not load {path}"); return; }
            Index = Timeline.events.Count;           // start at the end (final diorama)
            Reconstruct();
        }

        // ---- Scrub controls (used by ReplayUI) ----
        public int StepCount => Timeline != null ? Timeline.events.Count : 0;

        public void SeekToIndex(int index)
        {
            if (Timeline == null) return;
            Index = Mathf.Clamp(index, 0, Timeline.events.Count);
            Reconstruct();
        }

        public void StepForward() => SeekToIndex(Index + 1);
        public void StepBack()    => SeekToIndex(Index - 1);

        public void RewindSeconds(float seconds)
        {
            if (Timeline == null) return;
            float curT = Index > 0 && Index <= Timeline.events.Count
                ? Timeline.events[Mathf.Clamp(Index - 1, 0, Timeline.events.Count - 1)].t : 0f;
            float targetT = Mathf.Max(0f, curT - seconds);
            int idx = 0;
            for (int k = 0; k < Timeline.events.Count; k++)
            {
                if (Timeline.events[k].t <= targetT) idx = k + 1; else break;
            }
            SeekToIndex(idx);
        }

        /// <summary>Utterances at or before the current step (for the transcript panel).</summary>
        public IEnumerable<TimelineEvent> UtterancesUpToNow()
        {
            if (Timeline == null) yield break;
            for (int k = 0; k < Index && k < Timeline.events.Count; k++)
                if (Timeline.events[k].kind == EventKind.AiUtterance) yield return Timeline.events[k];
        }

        private void Reconstruct()
        {
            float upTo = (Index <= 0) ? -1f
                : (Index >= Timeline.events.Count ? float.MaxValue : Timeline.events[Index - 1].t);
            TimelineReplayer.ApplyStateAt(Timeline, upTo, seeking: true, _target);
            OnChanged?.Invoke();
        }

        // View-only: props can't be grabbed during playback.
        private void DisableManipulation()
        {
            foreach (var om in FindObjectsOfType<ObjectManipulator>(true))
                om.enabled = false;
        }
    }
}
