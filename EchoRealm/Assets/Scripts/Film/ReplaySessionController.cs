using System.Collections.Generic;
using UnityEngine;
using EchoRealm.Interaction;
using EchoRealm.Characters;
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

        // Character motion replay: per-character pose tracks (SceneRoot-local), built from the saved
        // CharacterPose events. Empty for old saves → characters simply stay put (the previous behavior).
        private readonly List<TimelineEvent> _astroTrack = new List<TimelineEvent>();
        private readonly List<TimelineEvent> _oracleTrack = new List<TimelineEvent>();
        private bool _astroWalking;                             // replay-driven Walk/idle animation state

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
            BuildCharacterTracks();
            Index = Timeline.events.Count;           // start at the end (final diorama)
            Reconstruct();
        }

        // Split the saved CharacterPose events into per-character tracks (chronological). Old saves have
        // none → tracks stay empty → characters aren't moved (identical to the previous behavior).
        private void BuildCharacterTracks()
        {
            _astroTrack.Clear(); _oracleTrack.Clear();
            foreach (var e in Timeline.events)
            {
                if (e.kind != EventKind.CharacterPose) continue;
                if (e.id == "astronaut") _astroTrack.Add(e);
                else if (e.id == "oracle") _oracleTrack.Add(e);
            }
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

        // ---- Auto playback: replay the saved scene from the BEGINNING on a continuous clock, so the
        // characters MOVE smoothly along their recorded paths and the Oracle SPEAKS its lines again. ----
        private Coroutine _playback;

        /// <summary>True while the saved scene is auto-advancing from the start.</summary>
        public bool IsPlaying => _playback != null;

        /// <summary>Replay the saved scene from the very beginning. World/object/act events are
        /// re-applied (and the Oracle's lines spoken aloud) as a continuous clock crosses them, while
        /// the characters are interpolated along their recorded pose tracks. This is what
        /// "View saved scene" runs on open.</summary>
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
            var events = Timeline.events;
            float duration = events[events.Count - 1].t;

            TimelineReplayer.ApplyStateAt(Timeline, -1f, seeking: true, _target);  // world/props/act baseline
            PositionCharactersAt(0f, animate: false);                              // snap characters to their start
            int cursor = 0;
            Index = 0;
            OnChanged?.Invoke();
            yield return new WaitForSecondsRealtime(0.4f);

            float clock = 0f;
            while (clock <= duration + 0.5f)
            {
                clock += Time.unscaledDeltaTime;
                bool crossed = false;
                while (cursor < events.Count && events[cursor].t <= clock)
                {
                    ApplyForward(events[cursor]);     // world/object/act + speak Oracle lines (forward only)
                    cursor++;
                    crossed = true;
                }
                if (crossed) { Index = cursor; OnChanged?.Invoke(); }   // refresh transcript/status on change only
                PositionCharactersAt(clock, animate: true);            // smooth, continuous motion
                yield return null;
            }

            Index = events.Count;
            OnChanged?.Invoke();
            _playback = null;
        }

        // Apply one event forward (incrementally, like the live film). Momentary FX play too, since
        // forward playback should show them. CharacterPose is driven by PositionCharactersAt, not here.
        private void ApplyForward(TimelineEvent e)
        {
            switch (e.kind)
            {
                case EventKind.WorldCommand:  _target.ApplyWorldCommand(e.id); break;
                case EventKind.ObjectOp:      _target.ApplyObjectOp(e.id, e.i, e.f, e.v); break;
                case EventKind.ObjectState:   _target.SetObjectState(e.id, e.v2, e.v, e.q); break;
                case EventKind.ActTransition: _target.ApplyActState(e.i, e.text); break;
                case EventKind.AiUtterance:
                    if (e.id == "Oracle") OracleController.Instance?.Speak(e.text);   // speak it aloud again
                    break;
                case EventKind.CharacterPose: break;
            }
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
            PositionCharactersAt(TimeAtIndex(), animate: false);   // snap to this step (manual scrub never speaks)
            OnChanged?.Invoke();
        }

        // Playback time of the current step Index.
        private float TimeAtIndex()
        {
            int n = Timeline != null ? Timeline.events.Count : 0;
            if (n == 0 || Index <= 0) return 0f;
            return Timeline.events[Mathf.Clamp(Index - 1, 0, n - 1)].t;
        }

        // ---- Character motion replay ----
        private static Transform SceneRoot()
            => Networking.QRAnchorManager.Instance != null ? Networking.QRAnchorManager.Instance.SceneRoot : null;

        // Place both characters at playback time `time`. animate=true (auto-play) drives the astronaut's
        // Walk/idle animation from its per-frame motion; animate=false (scrub/seek) just snaps. No-op per
        // character whose track is empty (old saves), so those replay exactly as before.
        private void PositionCharactersAt(float time, bool animate)
        {
            var sr = SceneRoot();
            PlaceCharacter(_astroTrack, AstronautController.Instance != null ? AstronautController.Instance.transform : null, sr, time, animate, isAstronaut: true);
            PlaceCharacter(_oracleTrack, OracleController.Instance != null ? OracleController.Instance.transform : null, sr, time, animate, isAstronaut: false);
        }

        private void PlaceCharacter(List<TimelineEvent> track, Transform tr, Transform sr, float time, bool animate, bool isAstronaut)
        {
            if (tr == null || track.Count == 0) return;
            SampleTrack(track, time, out Vector3 lpos, out Quaternion lrot);
            Vector3 worldPos = sr != null ? sr.TransformPoint(lpos) : lpos;
            Quaternion worldRot = sr != null ? sr.rotation * lrot : lrot;

            // Disable the live mover so its Update can't fight the replay-driven pose.
            if (isAstronaut) AstronautController.Instance?.StopWalking();
            else OracleController.Instance?.StopGlide();

            // Astronaut: drive Walk/idle from per-frame motion so he animates instead of sliding.
            if (isAstronaut)
            {
                if (animate)
                {
                    float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                    bool moving = (worldPos - tr.position).magnitude / dt > 0.05f;   // m/s
                    if (moving && !_astroWalking) { AstronautController.Instance?.PlayAnimation("Walk"); _astroWalking = true; }
                    else if (!moving && _astroWalking) { AstronautController.Instance?.PlayAnimation("LookAround"); _astroWalking = false; }
                }
                else if (_astroWalking) { AstronautController.Instance?.PlayAnimation("LookAround"); _astroWalking = false; }
            }

            tr.SetPositionAndRotation(worldPos, worldRot);
        }

        // Interpolated SceneRoot-local pose at `time` from a chronological track (clamped at both ends).
        private static void SampleTrack(List<TimelineEvent> track, float time, out Vector3 pos, out Quaternion rot)
        {
            if (time <= track[0].t) { pos = track[0].v; rot = track[0].q; return; }
            int last = track.Count - 1;
            if (time >= track[last].t) { pos = track[last].v; rot = track[last].q; return; }
            for (int k = 1; k < track.Count; k++)
            {
                if (track[k].t >= time)
                {
                    var a = track[k - 1]; var b = track[k];
                    float u = (b.t > a.t) ? (time - a.t) / (b.t - a.t) : 0f;
                    pos = Vector3.Lerp(a.v, b.v, u);
                    rot = Quaternion.Slerp(a.q, b.q, u);
                    return;
                }
            }
            pos = track[last].v; rot = track[last].q;
        }

        // View-only: props can't be grabbed during playback.
        private void DisableManipulation()
        {
            foreach (var om in FindObjectsOfType<ObjectManipulator>(true))
                om.enabled = false;
        }
    }
}
