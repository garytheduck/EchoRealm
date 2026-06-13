using MixedReality.Toolkit;
using MixedReality.Toolkit.Subsystems;
using UnityEngine;
using UnityEngine.XR;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// "Bring the world to my palm." On the voice command (routed here by VoiceCommandProcessor),
    /// if the speaker's open palm is extended in front at chest level, the whole scene shrinks to
    /// palm size and FOLLOWS the palm each frame; "make it big again" restores the exact pre-hold
    /// pose. Networked for free: while holding, SceneManipulationReporter.ExternalDrive is set, so
    /// FilmSync streams the scene transform to every headset exactly as during a hand grab — the
    /// other HoloLens sees the scene sitting in this user's palm, co-located via the QR anchor.
    ///
    /// Attach to a persistent object (GameManager) — NOT under SceneRoot. Pure opt-in: without this
    /// component, VoiceCommandProcessor's palm phrases are null-safe no-ops and nothing changes.
    /// </summary>
    public class PalmHold : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Whole-scene root. Auto-found by name when left empty (same convention as WorldPocket).")]
        [SerializeField] private Transform sceneRoot;

        [Header("Palm pose conditions (palm extended in front, chest level)")]
        [Tooltip("Palm must be this far BELOW eye level (m) — lower bound of the chest window.")]
        [SerializeField] private float minBelowEyes = 0.10f;
        [Tooltip("Palm must be no more than this far below eye level (m).")]
        [SerializeField] private float maxBelowEyes = 0.70f;
        [Tooltip("Max horizontal distance from the head to the palm (m) — 'in front of me'.")]
        [SerializeField] private float maxForward = 0.80f;

        [Header("Hold behavior")]
        [Tooltip("Uniform scene scale while held in the palm (~the size of a hand). Smaller = tinier.")]
        [SerializeField] private float palmScale = 0.03f;
        [Tooltip("Scene floats this many metres above the palm joint.")]
        [SerializeField] private float liftAbovePalm = 0.04f;
        [Tooltip("Follow smoothing — higher snaps faster.")]
        [SerializeField] private float followLerp = 12f;
        [Tooltip("Seconds of lost hand tracking before the hold auto-releases (restores the scene).")]
        [SerializeField] private float lostTrackingRelease = 2.5f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True while the scene is held in (and following) a palm on THIS device.</summary>
        public bool IsHolding { get; private set; }

        public static PalmHold Instance { get; private set; }

        private XRNode _hand = XRNode.RightHand;
        private Vector3 _savedScale;
        private Vector3 _savedPos;
        private Quaternion _savedRot;
        private float _lostSince = -1f;
        private Coroutine _releaseGrace;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            if (sceneRoot == null)
            {
                var go = GameObject.Find("SceneRoot");
                if (go != null) sceneRoot = go.transform;
            }
        }

        private void OnDestroy()
        {
            // Only the LIVE instance may clear the global drive flags — a rejected duplicate's
            // OnDestroy must not wipe an active hold's streaming/suppression state.
            if (Instance != this) return;
            Instance = null;
            ClearDriveFlags();
        }

        // ------------------------------------------------------------------
        // Public API (called by VoiceCommandProcessor)
        // ------------------------------------------------------------------

        /// <summary>Start holding: requires a tracked open palm in front at chest level. Returns
        /// false (with a log) when the pose conditions aren't met, so nothing happens.</summary>
        public bool TryHold()
        {
            if (IsHolding || sceneRoot == null) return false;
            if (WorldPocket.Instance != null && WorldPocket.Instance.IsPocketed) return false;

            if (!FindValidPalm(out _hand, out Vector3 palmPos))
            {
                Log("Palm not found — extend your open palm in front of you at chest level, then repeat.");
                return false;
            }

            _savedScale = sceneRoot.localScale;
            _savedPos   = sceneRoot.position;
            _savedRot   = sceneRoot.rotation;

            IsHolding = true;
            _lostSince = -1f;
            SetDriveFlags(true);
            // Snap most of the way immediately so the response feels instant.
            sceneRoot.position = palmPos + Vector3.up * liftAbovePalm;
            sceneRoot.localScale = Vector3.one * palmScale;

            Log($"Holding the scene in the {(_hand == XRNode.RightHand ? "right" : "left")} palm.");
            return true;
        }

        /// <summary>Stop holding and restore the exact pre-hold pose (same convention as unpocket,
        /// so the world returns to its shared, QR-anchored place for everyone).</summary>
        public void Release()
        {
            if (!IsHolding) return;
            IsHolding = false;
            if (sceneRoot != null)
            {
                sceneRoot.localScale = _savedScale;
                sceneRoot.position   = _savedPos;
                sceneRoot.rotation   = _savedRot;
            }
            // Make the RESTORED pose the networked truth BEFORE letting go of the drive flag —
            // otherwise Render() snaps the scene back to the stale streamed palm pose everywhere.
            var sync = Networking.FilmSync.Instance;
            sync?.RepublishSceneTransform();
            if (sync != null && !sync.HasStateAuthority)
            {
                // A client's restore round-trips through the master before it replicates back; keep
                // the drive flag briefly so Render() doesn't snap to the stale palm pose meanwhile.
                if (_releaseGrace != null) StopCoroutine(_releaseGrace);
                _releaseGrace = StartCoroutine(ReleaseDriveAfterRoundTrip());
            }
            else
            {
                SetDriveFlags(false);
            }
            Log("Released — scene restored to its pre-hold pose.");
        }

        // ------------------------------------------------------------------
        // Follow loop
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!IsHolding || sceneRoot == null) return;

            // If something pocketed/hid the world meanwhile, drop the hold without moving anything.
            if ((WorldPocket.Instance != null && WorldPocket.Instance.IsPocketed) ||
                !sceneRoot.gameObject.activeInHierarchy)
            {
                IsHolding = false;
                SetDriveFlags(false);
                Log("Hold aborted (world was pocketed/hidden).");
                return;
            }

            if (TryGetPalm(_hand, out Vector3 palmPos))
            {
                _lostSince = -1f;
                Vector3 target = palmPos + Vector3.up * liftAbovePalm;
                float k = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
                sceneRoot.position = Vector3.Lerp(sceneRoot.position, target, k);
                sceneRoot.localScale = Vector3.Lerp(sceneRoot.localScale, Vector3.one * palmScale, k);
                // Rotation is left alone: the grove stays upright and keeps its facing.
            }
            else
            {
                // Hand briefly untracked: freeze in place; after a grace period, restore the world
                // rather than stranding a tiny scene in mid-air.
                if (_lostSince < 0f) _lostSince = Time.time;
                else if (Time.time - _lostSince > lostTrackingRelease)
                {
                    Log("Hand tracking lost — auto-releasing.");
                    Release();
                }
            }
        }

        // ------------------------------------------------------------------
        // Palm lookup (MRTK3 HandsAggregator; simulated in the Editor)
        // ------------------------------------------------------------------

        private bool FindValidPalm(out XRNode hand, out Vector3 palmPos)
        {
            // Prefer the right hand, fall back to the left.
            if (TryGetPalm(XRNode.RightHand, out palmPos) && PalmInChestWindow(palmPos))
            { hand = XRNode.RightHand; return true; }
            if (TryGetPalm(XRNode.LeftHand, out palmPos) && PalmInChestWindow(palmPos))
            { hand = XRNode.LeftHand; return true; }
            hand = XRNode.RightHand;
            palmPos = default;
            return false;
        }

        private bool PalmInChestWindow(Vector3 palmPos)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 head = cam.transform.position;
            float below = head.y - palmPos.y;
            Vector3 flat = palmPos - head; flat.y = 0f;
            return below >= minBelowEyes && below <= maxBelowEyes && flat.magnitude <= maxForward;
        }

        private static bool TryGetPalm(XRNode hand, out Vector3 worldPos)
        {
#if UNITY_EDITOR
            // No hands in the Editor: simulate a palm at chest level in front of the camera so the
            // whole flow is testable with ProcessDebugInput("bring the world to my palm").
            var cam = Camera.main;
            if (cam != null)
            {
                worldPos = cam.transform.position + cam.transform.forward * 0.40f - Vector3.up * 0.30f;
                return true;
            }
            worldPos = default;
            return false;
#else
            var agg = XRSubsystemHelpers.HandsAggregator;
            if (agg != null && agg.TryGetJoint(TrackedHandJoint.Palm, hand, out HandJointPose pose))
            {
                // HandsAggregator already returns joints in Unity WORLD space (the hands subsystems
                // apply the playspace transform internally) — do NOT transform again, or the palm
                // lands offset by the rig pose and the chest-window test never passes on device.
                worldPos = pose.Position;
                return true;
            }
            worldPos = default;
            return false;
#endif
        }

        // ------------------------------------------------------------------
        // Drive flags: stream the transform + keep the auto-pocket gesture out of the way
        // ------------------------------------------------------------------

        // Client-only: hold the drive flag through the master round-trip, re-pushing once for safety.
        private System.Collections.IEnumerator ReleaseDriveAfterRoundTrip()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (!IsHolding) Networking.FilmSync.Instance?.RepublishSceneTransform();
            yield return new WaitForSecondsRealtime(0.35f);
            if (!IsHolding) SetDriveFlags(false);
            _releaseGrace = null;
        }

        private void SetDriveFlags(bool on)
        {
            if (SceneManipulationReporter.Instance != null)
                SceneManipulationReporter.Instance.ExternalDrive = on;
            WorldPocket.SuppressAutoPocket = on;
        }

        private void ClearDriveFlags() => SetDriveFlags(false);

        private void Log(string msg)
        {
            if (logEvents) Debug.Log($"[PalmHold] {msg}");
        }
    }
}
