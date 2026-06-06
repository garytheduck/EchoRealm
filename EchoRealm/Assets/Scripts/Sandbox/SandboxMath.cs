using UnityEngine;

namespace EchoRealm.Sandbox
{
    /// <summary>Pure pose math for the ball sandbox. The ball lives in world space but is synced
    /// relative to the QR anchor so it stays co-located on the real floor across headsets — the
    /// same anchor-relative scheme FilmSync uses for SceneRoot.</summary>
    public static class SandboxMath
    {
        public static void ToAnchorRelative(Vector3 anchorPos, Quaternion anchorRot,
            Vector3 worldPos, Quaternion worldRot, out Vector3 relPos, out Quaternion relRot)
        {
            Quaternion inv = Quaternion.Inverse(anchorRot);
            relPos = inv * (worldPos - anchorPos);
            relRot = inv * worldRot;
        }

        public static void FromAnchorRelative(Vector3 anchorPos, Quaternion anchorRot,
            Vector3 relPos, Quaternion relRot, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = anchorPos + anchorRot * relPos;
            worldRot = anchorRot * relRot;
        }

        /// <summary>A spawn point in front of a head pose, flattened to horizontal and lowered to
        /// roughly chest height so the ball is immediately reachable.</summary>
        public static Vector3 SpawnPositionInFront(Vector3 headPos, Quaternion headRot, float forward, float down)
        {
            Vector3 fwd = headRot * Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
            return headPos + fwd * forward + Vector3.down * down;
        }
    }
}
