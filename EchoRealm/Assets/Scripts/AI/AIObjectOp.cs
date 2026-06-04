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

        /// <summary>small | medium | large</summary>
        public string magnitude;
    }
}
