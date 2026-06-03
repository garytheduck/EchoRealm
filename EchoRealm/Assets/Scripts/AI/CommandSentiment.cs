using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.AI
{
    public enum CommandTone { Nurture, Chaos, Neutral }

    /// <summary>Classifies a world command as nurturing, chaotic, or neutral. Pure logic.</summary>
    public static class CommandSentiment
    {
        private static readonly HashSet<string> NurtureSet = new HashSet<string>
        { "grow_tree", "grow_flowers", "spawn_butterflies", "spawn_fireflies", "day", "glow_objects", "open_path", "rain" };

        private static readonly HashSet<string> ChaosSet = new HashSet<string>
        { "fire", "earthquake", "lightning", "wind", "fog", "night", "close_path", "shrink_scene" };

        public static CommandTone Classify(string command)
        {
            if (string.IsNullOrEmpty(command)) return CommandTone.Neutral;
            string c = command.Trim().ToLowerInvariant();
            if (NurtureSet.Contains(c)) return CommandTone.Nurture;
            if (ChaosSet.Contains(c)) return CommandTone.Chaos;
            return CommandTone.Neutral; // stop_*, character anims, etc.
        }
    }

    /// <summary>Drop-on-any-GameObject self-test (right-click component ▸ Run CommandSentiment Self-Test).</summary>
    public class CommandSentimentSelfTest : MonoBehaviour
    {
        [ContextMenu("Run CommandSentiment Self-Test")]
        public void Run()
        {
            void Check(string cmd, CommandTone want)
            {
                var got = CommandSentiment.Classify(cmd);
                Debug.Log($"[SentimentTest] '{cmd}' → {got} ({(got == want ? "PASS" : "FAIL expected " + want)})");
            }
            Check("grow_tree", CommandTone.Nurture);
            Check("fire", CommandTone.Chaos);
            Check("  Earthquake ", CommandTone.Chaos);
            Check("stop_rain", CommandTone.Neutral);
            Check("astronaut_jump", CommandTone.Neutral);
        }
    }
}
