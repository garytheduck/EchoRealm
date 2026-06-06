using UnityEngine;
using EchoRealm.AI; // VoiceCommandProcessor

namespace EchoRealm.Sandbox
{
    /// <summary>Registers a first-chance speech interceptor so ball phrases are handled locally and
    /// never reach the narrative AI / ActionCollector. This is the entire voice-isolation boundary.</summary>
    public class BallVoiceHook : MonoBehaviour
    {
        [SerializeField] private BallSpawner spawner;

        private void OnEnable()
        {
            if (spawner == null) spawner = FindObjectOfType<BallSpawner>();
            VoiceCommandProcessor.SpeechInterceptor = TryHandle;
        }

        private void OnDisable()
        {
            if (VoiceCommandProcessor.SpeechInterceptor == (System.Func<string, bool>)TryHandle)
                VoiceCommandProcessor.SpeechInterceptor = null;
        }

        /// <summary>Returns true (consumed) for ball phrases; false lets the utterance flow on normally.</summary>
        public bool TryHandle(string text)
        {
            switch (BallPhrases.Match(text))
            {
                case BallPhrases.Intent.Spawn:  spawner?.SpawnBall();  return true;
                case BallPhrases.Intent.Remove: spawner?.ClearBalls(); return true;
                default: return false;
            }
        }
    }
}
