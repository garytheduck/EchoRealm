using System;

namespace EchoRealm.AI
{
    /// <summary>
    /// One parsed object-manipulation, produced by the AI from a spoken "Claude, …" request
    /// for the object the user is looking at. Consumed by VoiceCommandProcessor → ObjectOpMath
    /// → FilmSync. Tier A of gaze-directed object manipulation.
    /// </summary>
    [Serializable]
    public class AIObjectOp
    {
        /// <summary>scale | move | rotate | reset</summary>
        public string action;

        /// <summary>bigger | smaller | left | right | up | down | closer | farther | none</summary>
        public string direction;

        /// <summary>small | medium | large — coarse fallback used only when <see cref="amount"/> is 0.</summary>
        public string magnitude;

        /// <summary>
        /// Numeric intensity the speaker implied: 1 = "a bit", 2 = "twice", 10 = "ten times", etc.
        /// 0 = unspecified → fall back to the <see cref="magnitude"/> bucket. This is what lets
        /// "move it ten times to the right" travel far more than "move it a bit" — the same way
        /// "ten times bigger" out-scales "a bit bigger". JsonUtility leaves it 0 when the AI omits it.
        /// </summary>
        public float amount;
    }
}
