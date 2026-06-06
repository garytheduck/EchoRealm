using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>What the replay engine drives. Implemented for real by UnityReplayTarget
    /// (Assembly-CSharp) and by fakes in tests. opType matches EchoRealm.Interaction.ObjOpType
    /// (Scale=0, Move=1, Rotate=2, Reset=3); scalar = factor for Scale / degrees for Rotate.</summary>
    public interface IReplayTarget
    {
        void ResetToBaseline();
        void ApplyWorldCommand(string command);
        void ApplyObjectOp(string id, int opType, float scalar, Vector3 delta);
        void ApplyActState(int act, string variant);
        void SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot);
    }
}
