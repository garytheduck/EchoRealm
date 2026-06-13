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
            HideLiveUI();
            // ReplayUI shows the save picker and calls LoadFile() with the chosen path.
            var ui = FindObjectOfType<ReplayUI>(true);
            if (ui != null) ui.ShowPicker();
            else Debug.LogError("[Replay] No ReplayUI in scene.");
        }

        // View-only playback: silence the live recorder and hide the live rewind panel so they
        // don't clutter the saved-scene view (they're meaningless without a live session). Also
        // MUTE the voice pipeline — if the viewer is entered in-session (Photon/recognizer still
        // live, e.g. straight after the Save prompt), an unmuted spoken command would route through
        // FilmSync and overwrite the reconstructed, read-only scene. Mute makes it truly read-only.
        private void HideLiveUI()
        {
            if (TimelineRecorder.Instance != null) TimelineRecorder.Instance.enabled = false;
            var rewind = FindObjectOfType<RewindMenu>(true);
            if (rewind != null) rewind.HideMenu();
            AI.VoiceCommandProcessor.Instance?.SetMuted(true);
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

        // ---- Auto playback (so "View saved scene" replays from the BEGINNING, not the diorama) ----
        private Coroutine _playback;

        /// <summary>True while the saved scene is auto-advancing from the start.</summary>
        public bool IsPlaying => _playback != null;

        /// <summary>Replay the saved scene from the very beginning, advancing through the recorded
        /// events at (clamped) original pace. This is what "View saved scene" runs on open, so the
        /// viewer shows the run unfold instead of opening on the final diorama.</summary>
        public void PlayFromStart()
        {
            StopPlayback();
            if (Timeline == null || Timeline.events.Count == 0) { SeekToIndex(0); return; }
            _playback = StartCoroutine(PlayRoutine());
        }

        /// <summary>Stop the auto playback (called when the user scrubs manually).</summary>
        public void StopPlayback()
        {
            if (_playback != null) { StopCoroutine(_playback); _playback = null; }
        }

        private System.Collections.IEnumerator PlayRoutine()
        {
            SeekToIndex(0);                                   // empty/initial grove
            yield return new WaitForSecondsRealtime(0.6f);
            for (int i = 1; i <= Timeline.events.Count; i++)
            {
                SeekToIndex(i);                               // apply events[i-1]
                if (i < Timeline.events.Count)
                {
                    float gap = Timeline.events[i].t - Timeline.events[i - 1].t;
                    yield return new WaitForSecondsRealtime(Mathf.Clamp(gap, 0.4f, 2.5f));
                }
            }
            _playback = null;
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
