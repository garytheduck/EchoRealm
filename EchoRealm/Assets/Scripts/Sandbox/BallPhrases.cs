namespace EchoRealm.Sandbox
{
    /// <summary>Pure utterance → intent matcher for the ball sandbox. Forgiving substring match
    /// (the HoloLens recognizer is noisy), mirroring the pocket/unpocket handling in
    /// VoiceCommandProcessor. No AI, no allocation beyond ToLowerInvariant.</summary>
    public static class BallPhrases
    {
        public enum Intent { None, Spawn, Remove }

        private static readonly string[] SpawnPhrases =
            { "drop a ball", "drop the ball", "spawn a ball", "spawn ball", "new ball", "give me a ball" };
        private static readonly string[] RemovePhrases =
            { "remove the ball", "remove ball", "remove balls", "delete the ball", "delete ball", "clear balls", "clear the balls" };

        public static Intent Match(string text)
        {
            if (string.IsNullOrEmpty(text)) return Intent.None;
            string t = text.ToLowerInvariant();
            foreach (var p in RemovePhrases) if (t.Contains(p)) return Intent.Remove;
            foreach (var p in SpawnPhrases) if (t.Contains(p)) return Intent.Spawn;
            return Intent.None;
        }
    }
}
