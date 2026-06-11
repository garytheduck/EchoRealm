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
        // amount is the AI's numeric intensity (1 = "a bit", 2 = "twice", 10 = "ten times"). 0 = the
        // AI didn't supply one → fall back to the coarse magnitude bucket (back-compat with old ops).

        public static float ScaleFactor(string direction, string magnitude, float amount = 0f)
        {
            // An explicit multiplier ("twice as big", "ten times bigger") drives the factor directly,
            // clamped to the 3x ceiling ManipulableObject enforces. Soft qualifiers ("a bit"/"a lot",
            // amount ~1) keep the original feel via the magnitude buckets, so scale doesn't regress.
            float up = amount >= 2f
                ? Mathf.Clamp(amount, 2f, 3f)
                : (magnitude == "small" ? 1.15f : magnitude == "large" ? 1.8f : 1.4f);
            return direction == "smaller" ? 1f / up : up;
        }

        public static float MoveMeters(string magnitude, float amount = 0f)
        {
            // "a bit" (amount 1) ≈ 0.18 m, "ten times" (amount 10) ≈ 1.8 m, capped ≈ 2.5 m so a big
            // request travels ~10x a small one — the whole point of honoring intensity for moves.
            if (amount > 0f) return Mathf.Clamp(amount, 1f, 14f) * 0.18f;
            return magnitude == "small" ? 0.1f : magnitude == "large" ? 0.5f : 0.25f;
        }

        public static float YawDegrees(string direction, string magnitude, float amount = 0f)
        {
            // "a bit" ≈ 20°, "ten times" ≈ 180° (capped to a half-turn so it never over-spins).
            float d = amount > 0f
                ? Mathf.Clamp(amount, 1f, 9f) * 20f
                : (magnitude == "small" ? 15f : magnitude == "large" ? 90f : 45f);
            return direction == "left" ? -d : d;
        }

        /// <summary>
        /// Egocentric direction (relative to the speaker's head) → the prop's parent-local delta.
        /// </summary>
        public static Vector3 MoveDelta(Transform cam, Transform prop, string direction, string magnitude, float amount = 0f)
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
            return local.normalized * MoveMeters(magnitude, amount);
        }
    }
}
