using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EchoRealm.AI
{
    // ------------------------------------------------------------------
    // Data types (defined here, used across AI + Film namespaces)
    // ------------------------------------------------------------------

    /// <summary>
    /// One possible narrative path for an act.
    /// Configure these in the NarrativeDecisionEngine Inspector.
    /// </summary>
    [Serializable]
    public class SceneVariant
    {
        [Tooltip("Short identifier used in code and in the AI prompt. E.g.: cooperative")]
        public string key = "default";

        [Tooltip("Readable name shown in logs and Session Logger.")]
        public string displayName = "Default Path";

        [Tooltip("Description sent to the AI so it can evaluate whether this variant fits the players' behavior.")]
        [TextArea(2, 4)]
        public string aiDescription = "The default act variant.";

        [Tooltip("Oracle line spoken at the transition if AI is unavailable.")]
        [TextArea(2, 3)]
        public string fallbackOracleLine = "The path ahead awaits.";

        [Tooltip("Fallback mood for the next act if AI is unavailable.")]
        public string fallbackMood = "mysterious";
    }

    /// <summary>
    /// Groups all available variants for one act transition.
    /// </summary>
    [Serializable]
    public class ActVariantSet
    {
        [Tooltip("This set applies when transitioning FROM this act number. '2' = end of Act 2.")]
        public int fromAct = 2;

        public SceneVariant[] variants;
    }

    // ------------------------------------------------------------------
    // NarrativeDecisionEngine
    // ------------------------------------------------------------------

    /// <summary>
    /// At each act transition, collects the PlayerBehaviorProfile summary and sends
    /// it to the AI with a list of available scene variants. The AI returns which
    /// variant best matches how the players have been interacting.
    ///
    /// FilmDirector calls RequestDecisionAsync() before starting each act.
    /// The returned AINarrativeDecision drives both ActManager content and Oracle dialogue.
    ///
    /// MULTIPLAYER NOTE:
    ///   Only the MASTER HoloLens should call RequestDecisionAsync().
    ///   After receiving the decision, FilmDirector must broadcast the chosen_variant
    ///   key to all peers via FusionNetworkManager so every device runs the same variant.
    ///   The sync hook is at FilmDirector.OnActCompleted() — add an RPC call there.
    /// </summary>
    public class NarrativeDecisionEngine : MonoBehaviour
    {
        [Header("Scene Variants")]
        [Tooltip("Define one ActVariantSet per act transition. Defaults are built at runtime if left empty.")]
        [SerializeField] private ActVariantSet[] variantSets;

        [Header("Debug")]
        [SerializeField] private bool logDecisions = true;

        public static NarrativeDecisionEngine Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (variantSets == null || variantSets.Length == 0)
                variantSets = BuildDefaultVariantSets();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Ask the AI to choose a scene variant for the transition from
        /// <paramref name="fromAct"/> to the next act.
        /// Always returns a valid decision (falls back gracefully if AI fails).
        /// </summary>
        public async Task<AINarrativeDecision> RequestDecisionAsync(int fromAct, int toAct)
        {
            var variantSet = GetVariantSet(fromAct);
            if (variantSet == null || variantSet.variants == null || variantSet.variants.Length == 0)
            {
                Log($"No variants configured for Act {fromAct}→{toAct}. Using empty default.", warning: true);
                return BuildFallback(null);
            }

            string behaviorSummary = ActionCollector.Instance != null
                ? ActionCollector.Instance.GetBehaviorSummary()
                : "No behavior data (ActionCollector not found).";

            string gazeSummary = Interaction.EyeTrackingManager.Instance != null
                ? Interaction.EyeTrackingManager.Instance.GetGazeSummary()
                : "nothing specific";

            // How forcefully the audience intervened — lets the AI scale the drama of its choice
            // (a calm audience leans default/triumphant; a stormy one earns consequences).
            string disruptionSummary = DisruptionMeter.Instance != null
                ? $" Audience disruption level: {DisruptionMeter.Instance.LevelDescription}."
                : "";

            var manager = AIManager.Instance;
            if (manager == null || !manager.IsReachable)
            {
                Log($"AI unavailable. Falling back to first variant for Act {fromAct}→{toAct}.", warning: true);
                return BuildFallback(variantSet.variants[0]);
            }

            // Build AI prompt
            var variantLines = new System.Text.StringBuilder();
            foreach (var v in variantSet.variants)
                variantLines.AppendLine($"- \"{v.key}\": {v.aiDescription}");

            string prompt =
                "You are the AI director of EchoRealm, a mixed reality film on HoloLens 2. " +
                $"Act {fromAct} has just ended and Act {toAct} is about to begin. " +
                $"Here is how the players behaved during Act {fromAct}: {behaviorSummary} " +
                $"What the players watched most: {gazeSummary}.{disruptionSummary} " +
                $"Available Act {toAct} variants:\n{variantLines}" +
                "Choose the variant that best matches the players' behavior and creates the most engaging narrative arc. " +
                "Respond ONLY with valid JSON — no markdown, no explanation — containing exactly these fields: " +
                "\"chosen_variant\" (string: exactly one of the variant keys listed above), " +
                "\"oracle_narration\" (string: 1-2 evocative sentences the Oracle says at this transition), " +
                "\"mood\" (string: one of joyful/scared/curious/mysterious/calm/excited/sad), " +
                "\"narrative_reason\" (string: one sentence explaining your choice).";

            Log($"Requesting Act {fromAct}→{toAct} decision. Behavior: {behaviorSummary}");

            string responseText = await manager.SendPromptAsync(prompt);

            if (string.IsNullOrEmpty(responseText))
            {
                Log("AI returned empty response. Using fallback.", warning: true);
                return BuildFallback(variantSet.variants[0]);
            }

            responseText = StripCodeFences(responseText);

            try
            {
                var decision = JsonUtility.FromJson<AINarrativeDecision>(responseText);

                // Validate the key is one we recognise
                if (!IsValidVariantKey(variantSet, decision.chosen_variant))
                {
                    Log($"AI returned unknown variant key '{decision.chosen_variant}'. Clamping to first.", warning: true);
                    decision.chosen_variant = variantSet.variants[0].key;
                }

                Log($"Decision: variant='{decision.chosen_variant}' mood='{decision.mood}' reason='{decision.narrative_reason}'");
                return decision;
            }
            catch (Exception ex)
            {
                Log($"JSON parse failed: {ex.Message}. Using fallback.", warning: true);
                return BuildFallback(variantSet.variants[0]);
            }
        }

        /// <summary>
        /// Returns the SceneVariant object for a given act transition and variant key.
        /// Useful for ActManager to look up obstacle GameObjects etc.
        /// </summary>
        public SceneVariant GetVariantConfig(int fromAct, string key)
        {
            var set = GetVariantSet(fromAct);
            if (set == null) return null;
            foreach (var v in set.variants)
                if (v.key == key) return v;
            return set.variants.Length > 0 ? set.variants[0] : null;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private ActVariantSet GetVariantSet(int fromAct)
        {
            foreach (var set in variantSets)
                if (set.fromAct == fromAct) return set;
            return null;
        }

        private static bool IsValidVariantKey(ActVariantSet set, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var v in set.variants)
                if (v.key == key) return true;
            return false;
        }

        private static AINarrativeDecision BuildFallback(SceneVariant variant)
        {
            if (variant == null)
                return new AINarrativeDecision
                {
                    chosen_variant  = "default",
                    oracle_narration = "The path ahead awaits.",
                    mood            = "mysterious",
                    narrative_reason = "Fallback — no variant data available."
                };

            return new AINarrativeDecision
            {
                chosen_variant  = variant.key,
                oracle_narration = variant.fallbackOracleLine,
                mood            = variant.fallbackMood,
                narrative_reason = "Fallback — AI was unavailable; first configured variant used."
            };
        }

        private static string StripCodeFences(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(nl + 1);
                if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
                text = text.Trim();
            }
            return text;
        }

        private void Log(string msg, bool warning = false)
        {
            if (!logDecisions) return;
            if (warning) Debug.LogWarning($"[NarrativeDecision] {msg}");
            else         Debug.Log($"[NarrativeDecision] {msg}");
        }

        // ------------------------------------------------------------------
        // Default variant sets (used when Inspector is left empty)
        // ------------------------------------------------------------------

        private static ActVariantSet[] BuildDefaultVariantSets() => new[]
        {
            // Act 2 → Act 3: the trial mirrors the world the players shaped (nurture vs chaos).
            new ActVariantSet
            {
                fromAct = 2,
                variants = new[]
                {
                    new SceneVariant
                    {
                        key         = "verdant",
                        displayName = "The Verdant Trial",
                        aiDescription =
                            "The players mostly NURTURED the grove (grew trees/flowers, butterflies, daylight). " +
                            "The heart is wrapped in blooming overgrowth the two must part together. Choose for a nurturing world tone.",
                        fallbackOracleLine = "The grove has bloomed for you. Now part its embrace — together.",
                        fallbackMood       = "calm"
                    },
                    new SceneVariant
                    {
                        key         = "scorched",
                        displayName = "The Scorched Trial",
                        aiDescription =
                            "The players mostly unleashed CHAOS (fire, storms, earthquakes, night). " +
                            "The heart is ringed by flame and storm the two must calm together. Choose for a chaotic world tone.",
                        fallbackOracleLine = "You stirred the grove's fury. Now quiet it — together.",
                        fallbackMood       = "scared"
                    },
                    new SceneVariant
                    {
                        key         = "twilight",
                        displayName = "The Twilight Trial",
                        aiDescription =
                            "The players were BALANCED or watchful. Hidden glyphs on the stones reveal only " +
                            "when both focus on the heart together. Choose for a balanced/observant world tone.",
                        fallbackOracleLine = "Between bloom and fire lies a hidden path. Look — together.",
                        fallbackMood       = "curious"
                    }
                }
            },

            // Act 3 → Act 4: the ending OUTCOME — not just its tone — reflects how the players
            // shaped the world. ActManager.RunAct4 branches on the chosen key: triumphant = the
            // traveler goes home; bittersweet = the world burns and the portal collapses;
            // mysterious = the portal stays sealed and the traveler remains as guardian.
            new ActVariantSet
            {
                fromAct = 3,
                variants = new[]
                {
                    new SceneVariant
                    {
                        key         = "triumphant",
                        displayName = "The Passage",
                        aiDescription =
                            "Choose for a NURTURING, harmonious session that solved the trial well. " +
                            "OUTCOME: the grove rewards the players — daylight returns, the portal opens " +
                            "fully and the traveler goes home. Warm, proud, celebratory.",
                        fallbackOracleLine = "You shaped this world with care, and it opens the way home.",
                        fallbackMood       = "joyful"
                    },
                    new SceneVariant
                    {
                        key         = "bittersweet",
                        displayName = "The Reckoning",
                        aiDescription =
                            "Choose for a CHAOTIC, destructive session (fire, storms, earthquakes, night). " +
                            "OUTCOME: the grove retaliates — the world catches fire, the path slams shut " +
                            "and the portal collapses; the traveler CANNOT go home and remains in the grove.",
                        fallbackOracleLine = "You raised storms, and storms have answers. The door will not hold.",
                        fallbackMood       = "scared"
                    },
                    new SceneVariant
                    {
                        key         = "mysterious",
                        displayName = "The Sealed Door",
                        aiDescription =
                            "Choose for a WATCHFUL, passive or balanced session. OUTCOME: mist falls, the " +
                            "portal never wakes and stays sealed; the traveler remains as the grove's " +
                            "guardian. Ambiguous and wondrous.",
                        fallbackOracleLine = "Between bloom and fire, the door keeps its own counsel. Watch.",
                        fallbackMood       = "mysterious"
                    }
                }
            }
        };
    }
}
