using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// Debug tool for testing voice commands in Unity Editor.
    /// Provides a text field to type commands that get processed
    /// through the same AI pipeline as real voice input.
    ///
    /// Only active in Unity Editor (stripped from UWP builds).
    /// </summary>
    public class DebugVoiceInput : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("Debug Input")]
        [SerializeField] private string debugCommand = "make it rain and let Dobby dance";

        [Header("Quick Test Commands")]
        [SerializeField] private bool sendOnStart = false;

        private VoiceCommandProcessor voiceProcessor;

        private void Start()
        {
            voiceProcessor = FindObjectOfType<VoiceCommandProcessor>();

            if (sendOnStart && !string.IsNullOrEmpty(debugCommand))
            {
                // Delay slightly to let systems initialize
                Invoke(nameof(SendDebugCommand), 2f);
            }
        }

        [ContextMenu("Send Debug Command")]
        public void SendDebugCommand()
        {
            if (voiceProcessor == null)
            {
                voiceProcessor = FindObjectOfType<VoiceCommandProcessor>();
                if (voiceProcessor == null)
                {
                    Debug.LogError("[DebugVoice] VoiceCommandProcessor not found in scene!");
                    return;
                }
            }

            Debug.Log($"[DebugVoice] Sending: '{debugCommand}'");
            voiceProcessor.ProcessDebugInput(debugCommand);
        }

        // Quick test shortcuts via context menu
        [ContextMenu("Test: Make it rain")]
        private void TestRain() { debugCommand = "make it rain"; SendDebugCommand(); }

        [ContextMenu("Test: Night time")]
        private void TestNight() { debugCommand = "make it night with stars"; SendDebugCommand(); }

        [ContextMenu("Test: Dobby dance")]
        private void TestDobbyDance() { debugCommand = "Dobby, dance for me!"; SendDebugCommand(); }

        [ContextMenu("Test: Forest")]
        private void TestForest() { debugCommand = "I want to see a beautiful forest"; SendDebugCommand(); }

        [ContextMenu("Test: Fire + consequence")]
        private void TestFire() { debugCommand = "set everything on fire!"; SendDebugCommand(); }

        [ContextMenu("Test: Complex command")]
        private void TestComplex() { debugCommand = "make it rain, grow flowers, and release butterflies"; SendDebugCommand(); }
#endif
    }
}
