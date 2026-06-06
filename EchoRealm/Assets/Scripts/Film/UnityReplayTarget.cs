using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Interaction;

namespace EchoRealm.Film
{
    /// <summary>Applies replayed timeline events to the live scene via the existing systems.
    /// Used by both live rewind and offline playback. Object ops are re-applied relatively
    /// (exactly as during play), so ordered replay from baseline reproduces the exact state.</summary>
    public class UnityReplayTarget : IReplayTarget
    {
        public void ResetToBaseline()
        {
            // Props back to their captured originals.
            var reg = ManipulableRegistry.Instance;
            if (reg != null)
                foreach (var mo in reg.All)
                    if (mo != null) mo.ResetTransform();

            // World back to defaults.
            CommandExecutor.Instance?.ResetWorldToDefaults();

            // Acts: clear obstacles/portal (act 0 = pre-start visual state).
            ActManager.Instance?.ApplyActState(0, null);
        }

        public void ApplyWorldCommand(string command)
            => CommandExecutor.Instance?.ExecuteCommand(command);

        public void ApplyObjectOp(string id, int opType, float scalar, Vector3 delta)
        {
            var mo = ManipulableRegistry.Instance?.FindById(id);
            if (mo == null) return;
            switch ((ObjOpType)opType)
            {
                case ObjOpType.Scale:  mo.ApplyScale(scalar); break;
                case ObjOpType.Move:   mo.ApplyMove(delta);   break;
                case ObjOpType.Rotate: mo.ApplyYaw(scalar);   break;
                case ObjOpType.Reset:  mo.ResetTransform();   break;
            }
        }

        public void ApplyActState(int act, string variant)
            => ActManager.Instance?.ApplyActState(act, variant);

        public void SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot)
        {
            var mo = ManipulableRegistry.Instance?.FindById(id);
            if (mo != null) mo.SetLocal(scale, pos, rot);
        }
    }
}
