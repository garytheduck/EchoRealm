using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Tags a scene prop as voice-manipulable ("Claude, make this bigger"). Holds the stable
    /// networked id + friendly kind, records its ORIGINAL local transform at startup (used both
    /// as the clamp reference and as the target of the manual "reset this" command), and applies
    /// clamped scale/move/rotate ops. Added automatically by ManipulableRegistry.
    /// </summary>
    public class ManipulableObject : MonoBehaviour
    {
        /// <summary>Stable id shared across devices (hierarchy path under SceneRoot).</summary>
        public string Id;

        /// <summary>Friendly type for the AI prompt: cloud/bush/rock/tree/flower/mushroom/object.</summary>
        public string Kind;

        private Vector3 _origScale, _origPos;
        private Quaternion _origRot;
        private bool _captured;

        private void Awake()
        {
            _origScale = transform.localScale;
            _origPos = transform.localPosition;
            _origRot = transform.localRotation;
            _captured = true;
        }

        /// <summary>Short description for the AI ("a bush, current size 1.0x of original").</summary>
        public string Context()
        {
            float ratio = transform.localScale.x / Mathf.Max(_origScale.x, 1e-4f);
            return $"a {Kind} (current size {ratio:F2}x of original)";
        }

        // ---- Clamped relative ops (applied identically on every device) ----

        public void ApplyScale(float factor)
        {
            Vector3 s = transform.localScale * factor;
            float ratio = Mathf.Clamp(s.x / Mathf.Max(_origScale.x, 1e-4f), 0.3f, 3f);
            transform.localScale = _origScale * ratio;
        }

        public void ApplyMove(Vector3 parentLocalDelta)
        {
            Vector3 p = transform.localPosition + parentLocalDelta;
            // Keep within 1 m of where it started so nothing flies away.
            transform.localPosition = _origPos + Vector3.ClampMagnitude(p - _origPos, 1f);
        }

        public void ApplyYaw(float degrees)
            => transform.localRotation = Quaternion.Euler(0f, degrees, 0f) * transform.localRotation;

        /// <summary>Manual "reset this" — restore the original transform.</summary>
        public void ResetTransform()
        {
            if (!_captured) return;
            transform.localScale = _origScale;
            transform.localPosition = _origPos;
            transform.localRotation = _origRot;
        }

        // ---- Absolute get/set for networking + late-join (idempotent) ----

        public void SetLocal(Vector3 scale, Vector3 pos, Quaternion rot)
        {
            transform.localScale = scale;
            transform.localPosition = pos;
            transform.localRotation = rot;
        }

        public void GetLocal(out Vector3 scale, out Vector3 pos, out Quaternion rot)
        {
            scale = transform.localScale;
            pos = transform.localPosition;
            rot = transform.localRotation;
        }
    }
}
