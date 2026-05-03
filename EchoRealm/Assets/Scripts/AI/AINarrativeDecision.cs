using System;
using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// Structured response from the AI when asked to choose a scene variant
    /// at an act transition (e.g. "which Act 3 should play based on player behavior?").
    ///
    /// Returned by NarrativeDecisionEngine.RequestDecisionAsync() and
    /// consumed by FilmDirector → ActManager to shape the next act.
    /// </summary>
    [Serializable]
    public class AINarrativeDecision
    {
        /// <summary>
        /// Key matching one of the configured SceneVariant.key values.
        /// E.g. "cooperative", "chaotic", "mysterious".
        /// </summary>
        public string chosen_variant;

        /// <summary>
        /// What the Oracle character says aloud at the moment of transition.
        /// Should be 1-2 evocative sentences.
        /// </summary>
        public string oracle_narration;

        /// <summary>
        /// Mood for the next act: joyful/scared/curious/mysterious/calm/excited/sad.
        /// </summary>
        public string mood;

        /// <summary>
        /// AI's reasoning for picking this variant.
        /// Not shown to players — used for session logging and dissertation analysis.
        /// </summary>
        public string narrative_reason;
    }
}
