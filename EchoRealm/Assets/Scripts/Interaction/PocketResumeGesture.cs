using MixedReality.Toolkit;
using MixedReality.Toolkit.Subsystems;
using UnityEngine;
using UnityEngine.XR;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Unpocket the world with a "push from the chest forward" gesture instead of a Resume button.
    /// While the world is pocketed, the user draws an open palm in near the chest, then pushes it
    /// forward; that calls WorldPocket.Unpocket() (networked → resumes every headset). Any headset's
    /// push resumes the shared film.
    ///
    /// Auto-attached by WorldPocket (persistent object, NOT under SceneRoot, which is hidden while
    /// pocketed). Reads the palm from MRTK3 HandsAggregator. Voice-resume (any speech while pocketed)
    /// still works as a fallback; the tappable Resume button is off by default (WorldPocket.useResumeButton).
    /// </summary>
    public class PocketResumeGesture : MonoBehaviour
    {
        [Header("Chest zone (palm drawn in, ready to push)")]
        [Tooltip("Palm must be at least this far BELOW eye level (m).")]
        [SerializeField] private float chestBelowMin = 0.05f;
        [Tooltip("Palm must be no more than this far below eye level (m).")]
        [SerializeField] private float chestBelowMax = 0.65f;
        [Tooltip("Palm counts as 'at the chest' when it is within this distance in FRONT of the head (m).")]
        [SerializeField] private float armForward = 0.25f;
        [Tooltip("Max sideways distance from the forward axis, so the push is roughly straight ahead (m).")]
        [SerializeField] private float maxLateral = 0.45f;

        [Header("Push")]
        [Tooltip("Forward distance the palm must reach to fire the resume (m).")]
        [SerializeField] private float triggerForward = 0.50f;
        [Tooltip("The push must complete within this long after the palm armed at the chest (s).")]
        [SerializeField] private float pushWindow = 1.2f;
        [Tooltip("Ignore further gestures for this long after one fires (s).")]
        [SerializeField] private float cooldown = 1.0f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        public static PocketResumeGesture Instance { get; private set; }

        [Tooltip("Tolerate this long of lost hand tracking mid-push before disarming (HoloLens hand " +
                 "tracking flickers).")]
        [SerializeField] private float lostTrackingGrace = 0.25f;

        private bool _armed;
        private XRNode _armHand = XRNode.RightHand;
        private float _armTime;
        private float _lostSince = -1f;
        private float _cooldownUntil = -1f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            // Only while the world is pocketed; reset arming otherwise.
            var pocket = WorldPocket.Instance;
            if (pocket == null || !pocket.IsPocketed) { _armed = false; return; }
            if (Time.unscaledTime < _cooldownUntil) return;

            var cam = Camera.main;
            if (cam == null) { _armed = false; return; }
            Vector3 head = cam.transform.position;
            Vector3 fwd = cam.transform.forward;

            if (_armed)
            {
                // Armed: follow the SAME hand until it pushes out, drifts back, the window expires,
                // or tracking is lost for longer than the flicker grace.
                if (!TryGetPalm(_armHand, out Vector3 palm))
                {
                    if (_lostSince < 0f) _lostSince = Time.unscaledTime;
                    if (Time.unscaledTime - _lostSince > lostTrackingGrace) _armed = false;
                    return;
                }
                _lostSince = -1f;
                if (Time.unscaledTime - _armTime > pushWindow) { _armed = false; return; }

                float forwardDist = Vector3.Dot(palm - head, fwd);
                float lateral = Vector3.ProjectOnPlane(palm - head, fwd).magnitude;
                if (forwardDist >= triggerForward && lateral <= maxLateral)
                {
                    _armed = false;
                    _cooldownUntil = Time.unscaledTime + cooldown;
                    if (logEvents) Debug.Log("[PocketResume] Push-forward gesture — resuming the world.");
                    pocket.Unpocket();   // networked → resumes every headset
                }
                return;
            }

            // Not armed: arm with whichever hand is currently drawn in at the chest.
            if (TryArm(XRNode.RightHand, head, fwd)) return;
            TryArm(XRNode.LeftHand, head, fwd);
        }

        private bool TryArm(XRNode hand, Vector3 head, Vector3 fwd)
        {
            if (!TryGetPalm(hand, out Vector3 palm)) return false;
            float below = head.y - palm.y;
            float forwardDist = Vector3.Dot(palm - head, fwd);
            float lateral = Vector3.ProjectOnPlane(palm - head, fwd).magnitude;
            bool inChest = below >= chestBelowMin && below <= chestBelowMax
                           && forwardDist <= armForward && lateral <= maxLateral;
            if (!inChest) return false;
            _armed = true;
            _armHand = hand;
            _armTime = Time.unscaledTime;
            _lostSince = -1f;
            return true;
        }

        private static bool TryGetPalm(XRNode hand, out Vector3 worldPos)
        {
#if UNITY_EDITOR
            // No hand tracking in the Editor — the push gesture is device-only. Test unpocket in the
            // Editor via voice (any speech resumes) or by enabling WorldPocket.useResumeButton.
            worldPos = default;
            return false;
#else
            var agg = XRSubsystemHelpers.HandsAggregator;
            if (agg != null && agg.TryGetJoint(TrackedHandJoint.Palm, hand, out HandJointPose pose))
            {
                // HandsAggregator returns joints already in Unity world space — do not transform again.
                worldPos = pose.Position;
                return true;
            }
            worldPos = default;
            return false;
#endif
        }
    }
}
