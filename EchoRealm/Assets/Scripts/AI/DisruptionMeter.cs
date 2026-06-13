using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// Scores how much the AUDIENCE is disturbing the film, and reacts proportionally:
    ///   • a disruptive BURST (many points within a short window) → the Oracle speaks a warning
    ///     (once per act, on every device — the input stream is identical everywhere);
    ///   • a heavy burst or a high total during Act 2 → the MASTER forces the act to complete
    ///     early, so the AI decides the branch NOW instead of waiting for the command quota;
    ///   • a LOW total by the end of Act 3 → FilmDirector skips the AI and plays the default
    ///     Passage ending (the film's guaranteed outcome when the audience mostly watched).
    ///
    /// Inputs (only USER actions — the film's own scripted ambient commands never count):
    ///   • spoken world commands via FilmSync.OnUserCommandsApplied, weighted by tone
    ///     (CommandSentiment: Chaos 3, Nurture 1, Neutral 0.5);
    ///   • "Claude" object manipulations via FilmSync.OnObjectOpApplied (1; reset 0.5);
    ///   • pocketing the world mid-film (+2) and miniaturizing the scene (palm-hold / extreme
    ///     shrink, detected from the networked scene scale so every device scores it) (+2).
    ///
    /// The TOTAL used for decisions is derived from ActionCollector's profile (which rewinds
    /// with the timeline) plus the local extras — so undone commands stop counting after a rewind.
    /// Auto-attached by FilmDirector; remove it and the film paces exactly as before.
    /// </summary>
    public class DisruptionMeter : MonoBehaviour
    {
        [Header("Points per action")]
        [SerializeField] private float chaosPoints = 3f;
        [SerializeField] private float nurturePoints = 1f;
        [SerializeField] private float neutralPoints = 0.5f;
        [SerializeField] private float objectOpPoints = 1f;
        [SerializeField] private float objectResetPoints = 0.5f;
        [Tooltip("Pocketing the world mid-film.")]
        [SerializeField] private float pocketPoints = 2f;
        [Tooltip("Scene miniaturized (palm-hold or an extreme shrink) mid-film.")]
        [SerializeField] private float miniaturizePoints = 2f;

        [Header("Thresholds")]
        [Tooltip("Burst points within the window that make the Oracle speak a warning (once per act).")]
        [SerializeField] private float warnBurstPoints = 6f;
        [Tooltip("Burst points that force Act 2 to complete early (AI decides immediately).")]
        [SerializeField] private float forceBurstPoints = 9f;
        [Tooltip("TOTAL points that force Act 2 to complete early.")]
        [SerializeField] private float forceTotalPoints = 12f;
        [Tooltip("Below this TOTAL at the end of Act 3, the AI is skipped and the default Passage ending plays.")]
        [SerializeField] private float defaultEndingFloor = 4f;
        [Tooltip("Sliding window (seconds) for burst detection.")]
        [SerializeField] private float burstWindowSeconds = 20f;

        [Header("Oracle warning")]
        [TextArea(2, 3)]
        [SerializeField] private string warningLine =
            "The grove trembles under your storms. Tread with care — it remembers.";

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        public static DisruptionMeter Instance { get; private set; }

        /// <summary>Floor below which FilmDirector plays the default ending without the AI.</summary>
        public float DefaultEndingFloor => defaultEndingFloor;

        /// <summary>Rewind-aware total: weighted ActionCollector profile + local extras.</summary>
        public float TotalPoints
        {
            get
            {
                float fromProfile = 0f;
                var profile = ActionCollector.Instance != null ? ActionCollector.Instance.Profile : null;
                if (profile != null)
                    fromProfile = chaosPoints * profile.ChaosCount + nurturePoints * profile.NurtureCount;
                return fromProfile + _objectOpTotal + _extraTotal;
            }
        }

        /// <summary>One-phrase description for the AI decision prompt.</summary>
        public string LevelDescription
        {
            get
            {
                float t = TotalPoints;
                if (t < defaultEndingFloor) return $"calm — the audience mostly watched ({t:F0} pts)";
                if (t < forceTotalPoints)   return $"engaged — moderate interventions ({t:F0} pts)";
                return $"stormy — heavy, disruptive interventions ({t:F0} pts)";
            }
        }

        // Burst window: (time, points) pairs, pruned past burstWindowSeconds.
        private readonly List<(float t, float pts)> _window = new List<(float, float)>();
        private float _objectOpTotal;   // object ops aren't in the profile — tracked locally
        private float _extraTotal;      // pocket / miniaturize — tracked locally
        private int _warnedAct = -1;
        private bool _wasPocketed;
        private bool _miniArmed = true;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            Networking.FilmSync.OnUserCommandsApplied += HandleUserCommands;
            Networking.FilmSync.OnObjectOpApplied += HandleObjectOp;
            Networking.FilmSync.OnRewindApplied += HandleRewind;
        }

        private void OnDisable()
        {
            Networking.FilmSync.OnUserCommandsApplied -= HandleUserCommands;
            Networking.FilmSync.OnObjectOpApplied -= HandleObjectOp;
            Networking.FilmSync.OnRewindApplied -= HandleRewind;
        }

        // A rewind rolls the cumulative profile back (chaos/nurture, on the master), but the meter's
        // LOCAL extras (object-op points, pocket/miniaturize points, the burst window) aren't in that
        // profile. Fires on every device — reset them so TotalPoints matches the rewound timeline and
        // the once-per-act warning can re-arm. Object ops aren't in the profile, so zero is the
        // closest-correct post-rewind value (the timeline itself reconstructs object/world state).
        private void HandleRewind(float t)
        {
            _window.Clear();
            _objectOpTotal = 0f;
            _extraTotal = 0f;
            _warnedAct = -1;
            _miniArmed = true;
            _wasPocketed = IsPocketedNow();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (!IsScoringWindow()) { _wasPocketed = IsPocketedNow(); return; }

            // Pocket transition (+pocketPoints once per pocket).
            bool pocketed = IsPocketedNow();
            if (pocketed && !_wasPocketed) AddPoints(pocketPoints, "world pocketed", countsInTotal: true);
            _wasPocketed = pocketed;

            // Scene miniaturized (palm-hold / extreme shrink) — read from the NETWORKED scale so
            // every device scores the same event no matter who is holding the scene.
            var sync = Networking.FilmSync.Instance;
            if (sync != null && sync.SceneScale > 0f)
            {
                if (_miniArmed && sync.SceneScale < 0.1f)
                {
                    _miniArmed = false;
                    AddPoints(miniaturizePoints, "scene miniaturized", countsInTotal: true);
                }
                else if (!_miniArmed && sync.SceneScale > 0.3f)
                {
                    _miniArmed = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // Inputs
        // ------------------------------------------------------------------

        private void HandleUserCommands(string[] commands)
        {
            if (!IsScoringWindow() || commands == null) return;
            foreach (var cmd in commands)
            {
                var tone = CommandSentiment.Classify(cmd);
                float pts = tone == CommandTone.Chaos ? chaosPoints
                          : tone == CommandTone.Nurture ? nurturePoints
                          : neutralPoints;
                // Chaos/Nurture already land in the (rewind-aware) profile on the master —
                // don't double-count them in the local extras.
                AddPoints(pts, $"command '{cmd}' ({tone})", countsInTotal: false);
            }
        }

        private void HandleObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
        {
            if (!IsScoringWindow()) return;
            float pts = opType == (int)Interaction.ObjOpType.Reset ? objectResetPoints : objectOpPoints;
            _objectOpTotal += pts;
            AddBurst(pts);
            CheckThresholds($"object op on '{id}'");
        }

        // ------------------------------------------------------------------
        // Scoring + thresholds
        // ------------------------------------------------------------------

        private void AddPoints(float pts, string why, bool countsInTotal)
        {
            // Spoken commands live in the (rewind-aware) ActionCollector profile; only the kinds
            // the profile doesn't know about (pocket, miniaturize) accumulate locally.
            if (countsInTotal) _extraTotal += pts;
            AddBurst(pts);
            CheckThresholds(why);
        }

        private void AddBurst(float pts)
        {
            float now = Time.time;
            _window.Add((now, pts));
            _window.RemoveAll(e => now - e.t > burstWindowSeconds);
        }

        private float BurstPoints()
        {
            float now = Time.time, sum = 0f;
            foreach (var e in _window)
                if (now - e.t <= burstWindowSeconds) sum += e.pts;
            return sum;
        }

        private void CheckThresholds(string why)
        {
            int act = Film.ActManager.Instance != null ? Film.ActManager.Instance.CurrentAct : 0;
            float burst = BurstPoints();
            float total = TotalPoints;
            if (logEvents) Debug.Log($"[Disruption] +{why} → burst {burst:F1}/{forceBurstPoints}, total {total:F1}/{forceTotalPoints}");

            // Oracle warning — once per act, locally on every device (same stream everywhere).
            if (burst >= warnBurstPoints && (act == 2 || act == 3) && _warnedAct != act)
            {
                _warnedAct = act;
                var oracle = Characters.OracleController.Instance;
                if (oracle != null)
                {
                    oracle.SetMood("warning");
                    oracle.Speak(warningLine);
                }
            }

            // Early AI decision — MASTER only, Act 2 only.
            bool isMaster = Networking.FusionNetworkManager.Instance == null
                            || Networking.FusionNetworkManager.Instance.IsMaster;
            if (isMaster && act == 2 && (burst >= forceBurstPoints || total >= forceTotalPoints))
            {
                Film.FilmDirector.Instance?.ForceCompleteAct2(
                    $"disruption (burst {burst:F1}, total {total:F1})");
            }
        }

        // Score only while the film is in acts 1-3 (the endings stage their own commands in Act 4).
        // Gate on ActManager.CurrentAct — it is set on EVERY device via RPC_StartAct — NOT on
        // FilmDirector.IsPlaying, which is a master-only flag (so the client would never score, and
        // the per-device Oracle warning would never fire there).
        private bool IsScoringWindow()
        {
            int act = Film.ActManager.Instance != null ? Film.ActManager.Instance.CurrentAct : 0;
            return act >= 1 && act <= 3;
        }

        private static bool IsPocketedNow()
            => Interaction.WorldPocket.Instance != null && Interaction.WorldPocket.Instance.IsPocketed;
    }
}
