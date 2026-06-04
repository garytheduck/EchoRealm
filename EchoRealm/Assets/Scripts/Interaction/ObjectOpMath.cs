using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>Networked op kinds for a single manipulated object.</summary>
    public enum ObjOpType { Scale = 0, Move = 1, Rotate = 2, Reset = 3 }

    /// <summary>
    /// Pure helpers used by the SPEAKING device to turn a parsed AIObjectOp into concrete numbers.
    /// Magnitude → factor/distance/degrees, and egocentric direction → the prop's PARENT-local
    /// delta. Parent-local is frame-consistent and co-located across devices (same hierarchy +
    /// QR-aligned SceneRoot ancestor), so the same delta applied on every headset looks identical.
    /// </summary>
    public static class ObjectOpMath
    {
        public static float ScaleFactor(string direction, string magnitude)
        {
            float up = magnitude == "small" ? 1.15f : magnitude == "large" ? 1.8f : 1.4f;
            return direction == "smaller" ? 1f / up : up;
        }

        public static float MoveMeters(string magnitude)
            => magnitude == "small" ? 0.1f : magnitude == "large" ? 0.5f : 0.25f;

        public static float YawDegrees(string direction, string magnitude)
        {
            float d = magnitude == "small" ? 15f : magnitude == "large" ? 90f : 45f;
            return direction == "left" ? -d : d;
        }

        /// <summary>
        /// Egocentric direction (relative to the speaker's head) → the prop's parent-local delta.
        /// </summary>
        public static Vector3 MoveDelta(Transform cam, Transform prop, string direction, string magnitude)
        {
            if (cam == null || prop == null) return Vector3.zero;
            Vector3 world =
                direction == "right"   ?  cam.right :
                direction == "left"    ? -cam.right :
                direction == "up"      ?  cam.up :
                direction == "down"    ? -cam.up :
                direction == "closer"  ? -cam.forward :
                direction == "farther" ?  cam.forward : Vector3.zero;
            if (world == Vector3.zero) return Vector3.zero;

            Vector3 local = prop.parent != null ? prop.parent.InverseTransformDirection(world) : world;
            if (local.sqrMagnitude < 1e-8f) return Vector3.zero;
            return local.normalized * MoveMeters(magnitude);
        }
    }
}
