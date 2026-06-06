using Fusion;
using UnityEngine;
using EchoRealm.Networking; // QRAnchorManager

namespace EchoRealm.Sandbox
{
    /// <summary>
    /// A networked physics ball. Only the state authority simulates PhysX; its pose is published
    /// relative to the QR anchor and every other device displays that pose kinematically (same
    /// anchor-relative scheme as FilmSync). Authority transfers to whoever grabs it (same idiom as
    /// NetworkedTestCube). Throw velocity is tracked locally and applied on release.
    ///
    /// Prefab requirements (built by BallSandboxSetup): NetworkObject, Rigidbody, SphereCollider,
    /// MeshFilter+MeshRenderer, MRTK ObjectManipulator (events wired to OnGrabStart/OnGrabEnd),
    /// layer = BallSandbox.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkedBall : NetworkBehaviour
    {
        [Tooltip("Compensates for HoloLens hand-tracking under-reading release speed.")]
        [SerializeField] private float throwVelocityMultiplier = 1.3f;

        [Networked] public Vector3 AnchorRelPos { get; set; }
        [Networked] public Quaternion AnchorRelRot { get; set; }

        private Rigidbody _rb;
        private QRAnchorManager _anchor;
        private bool _held;
        private Vector3 _lastHeldPos;
        private Vector3 _heldVel;

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            _anchor = QRAnchorManager.Instance;
            ApplyAuthorityPhysicsState();
            // Non-authority copies are positioned by Render() from the networked anchor-relative pose.
        }

        public override void FixedUpdateNetwork()
        {
            // Authority publishes its simulated (or hand-driven) world pose as anchor-relative truth.
            if (HasStateAuthority && _anchor != null)
            {
                SandboxMath.ToAnchorRelative(_anchor.AnchorPosition, _anchor.AnchorRotation,
                    transform.position, transform.rotation, out var p, out var r);
                AnchorRelPos = p;
                AnchorRelRot = r;
            }
        }

        public override void Render()
        {
            // Non-authority follows the networked pose (interpolation-free is fine at ball speeds;
            // upgrade to a NetworkTransform later if smoothing is needed).
            if (!HasStateAuthority && _anchor != null)
            {
                SandboxMath.FromAnchorRelative(_anchor.AnchorPosition, _anchor.AnchorRotation,
                    AnchorRelPos, AnchorRelRot, out var w, out var r);
                transform.SetPositionAndRotation(w, r);
            }
        }

        private void Update()
        {
            if (_held)
            {
                // Track hand velocity while held so we can throw on release.
                float dt = Time.deltaTime;
                if (dt > 0f)
                {
                    Vector3 v = (transform.position - _lastHeldPos) / dt;
                    _heldVel = Vector3.Lerp(_heldVel, v, 0.5f);
                }
                _lastHeldPos = transform.position;
                return;
            }

            // Not held: keep the rigidbody's kinematic state in sync with whoever currently owns the
            // ball. Done per-frame instead of via Fusion's IStateAuthorityChanged callback so authority
            // transfer (on grab) flips simulation on/off with no extra wiring.
            ApplyAuthorityPhysicsState();
        }

        /// <summary>Dynamic only when we own it and it's not in a hand; kinematic + follower otherwise.</summary>
        private void ApplyAuthorityPhysicsState()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            bool simulate = HasStateAuthority && !_held;
            _rb.isKinematic = !simulate;
            _rb.useGravity = simulate;
            if (simulate) _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        // --- Wired to MRTK ObjectManipulator events (see BallSandboxSetup manual step) ---

        public void OnGrabStart()
        {
            if (Object != null && Object.IsValid && !HasStateAuthority) Object.RequestStateAuthority();
            _held = true;
            _heldVel = Vector3.zero;
            _lastHeldPos = transform.position;
            if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; } // hand drives it
        }

        public void OnGrabEnd()
        {
            _held = false;
            ApplyAuthorityPhysicsState();
            if (_rb != null && !_rb.isKinematic)
                _rb.velocity = _heldVel * throwVelocityMultiplier; // the throw
        }
    }
}
