using System.Collections.Generic;

namespace EchoRealm.Film
{
    /// <summary>Classifies world commands that are one-shot (no lasting scene state).
    /// These are skipped when seeking/rewinding so we reconstruct resting state, not a
    /// re-fired earthquake. Mirrors the momentary cases in CommandExecutor.ExecuteCommand.</summary>
    public static class TransientCommands
    {
        private static readonly HashSet<string> _transient = new HashSet<string>
        {
            "earthquake", "lightning",
            "dobby_dance", "dobby_wave", "dobby_scared", "dobby_celebrate",
            "astronaut_jump", "astronaut_wave", "astronaut_look_around",
        };

        public static bool IsTransient(string command)
            => command != null && _transient.Contains(command.Trim().ToLowerInvariant());
    }
}
