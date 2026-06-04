// CS0414: speakingRate/voicePitch/preferMaleVoice are read only in WINDOWS_UWP (device) builds,
// so they appear "unused" when compiling for the Editor. Suppress that Editor-only warning.
#pragma warning disable CS0414
using UnityEngine;
#if WINDOWS_UWP
using System;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
#endif

namespace EchoRealm.Characters
{
    /// <summary>
    /// Speaks the Oracle's lines aloud on HoloLens 2 using the built-in Windows TTS
    /// (Windows.Media.SpeechSynthesis — free, offline, no API key). Voices BOTH scripted
    /// and AI-generated lines (whatever OracleController.Speak/SpeakDramatic is given).
    ///
    /// Setup: add this to the Oracle GameObject (an AudioSource is auto-added). OracleController
    /// auto-finds it. In the Unity Editor there's no UWP TTS, so it just logs — test on device.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class OracleVoice : MonoBehaviour
    {
        [Header("Voice (tune for clarity)")]
        [Tooltip("Speaking rate. 1 = normal; LOWER = slower & clearer. 0.8 reads as a calm oracle.")]
        [Range(0.5f, 2f)] [SerializeField] private float speakingRate = 0.8f;
        [Tooltip("Voice pitch. 1 = normal; lower = deeper. ~0.95 gives a grave, oracular tone.")]
        [Range(0.5f, 2f)] [SerializeField] private float voicePitch = 0.95f;
        [Tooltip("Prefer a male voice for the priest Oracle (if one is installed on the device).")]
        [SerializeField] private bool preferMaleVoice = true;
        [Tooltip("0 = always crystal-clear (2D); 1 = fully positional (from the priest). Clarity-first default is low.")]
        [Range(0f, 1f)] [SerializeField] private float spatialBlend = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        private AudioSource _audio;

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = spatialBlend;
#if WINDOWS_UWP
            InitSynth();
#endif
        }

        /// <summary>Speak a line aloud (interrupts any current line so words never overlap).</summary>
        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
#if WINDOWS_UWP
            _ = SpeakAsync(text);
#else
            if (logEvents) Debug.Log($"[OracleVoice] (Editor — no UWP TTS) would speak: \"{text}\"");
#endif
        }

#if WINDOWS_UWP
        private SpeechSynthesizer _synth;

        private void InitSynth()
        {
            try
            {
                _synth = new SpeechSynthesizer();
                ApplyOptions();

                var chosen = PickVoice();
                if (chosen != null) _synth.Voice = chosen;

                Debug.Log($"[OracleVoice] Using voice: {_synth.Voice?.DisplayName} ({_synth.Voice?.Language}). " +
                          $"Installed voices: {AllVoiceNames()}");
            }
            catch (Exception ex) { Debug.LogError($"[OracleVoice] TTS init failed: {ex.Message}"); }
        }

        private VoiceInformation PickVoice()
        {
            var voices = SpeechSynthesizer.AllVoices;

            // 1) English + male (the grave priest-oracle vibe)
            foreach (var v in voices)
                if (IsEnglish(v) && v.Gender == VoiceGender.Male) return v;

            // 2) any English voice (don't let a foreign-accent voice read English)
            foreach (var v in voices)
                if (IsEnglish(v)) return v;

            // 3) any male, only if asked
            if (preferMaleVoice)
                foreach (var v in voices)
                    if (v.Gender == VoiceGender.Male) return v;

            return null; // keep the system default
        }

        private static bool IsEnglish(VoiceInformation v)
            => v.Language != null && v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        private static string AllVoiceNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var v in SpeechSynthesizer.AllVoices) names.Add($"{v.DisplayName} [{v.Language}]");
            return names.Count > 0 ? string.Join(", ", names) : "(none)";
        }

        private void ApplyOptions()
        {
            _synth.Options.SpeakingRate = speakingRate; // <1 = slower, clearer
            _synth.Options.AudioPitch   = voicePitch;   // <1 = deeper
        }

        private async Task SpeakAsync(string text)
        {
            if (_synth == null) return;
            try
            {
                ApplyOptions(); // pick up Inspector tweaks each time
                SpeechSynthesisStream stream = await _synth.SynthesizeTextToStreamAsync(text);
                var bytes = new byte[stream.Size];
                using (var dr = new DataReader(stream))
                {
                    await dr.LoadAsync((uint)stream.Size);
                    dr.ReadBytes(bytes);
                }
                UnityEngine.WSA.Application.InvokeOnAppThread(() => PlayWav(bytes, text), false);
            }
            catch (Exception ex) { Debug.LogError($"[OracleVoice] TTS failed: {ex.Message}"); }
        }

        private void PlayWav(byte[] wav, string text)
        {
            var clip = WavToClip(wav, "OracleLine");
            if (clip == null) { Debug.LogWarning("[OracleVoice] Could not decode TTS WAV."); return; }
            _audio.Stop();
            _audio.spatialBlend = spatialBlend;
            _audio.clip = clip;
            _audio.Play();
            if (logEvents) Debug.Log($"[OracleVoice] Speaking: \"{text}\"");
        }

        // Decodes a 16-bit PCM WAV (what SpeechSynthesizer returns) into an AudioClip.
        private static AudioClip WavToClip(byte[] wav, string name)
        {
            if (wav == null || wav.Length < 44) return null;
            int channels   = BitConverter.ToInt16(wav, 22);
            int sampleRate  = BitConverter.ToInt32(wav, 24);
            int bits        = BitConverter.ToInt16(wav, 34);

            // Find the "data" chunk (skip any chunks between fmt and data).
            int idx = 12, dataOffset = -1, dataSize = 0;
            while (idx + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, idx, 4);
                int size = BitConverter.ToInt32(wav, idx + 4);
                if (id == "data") { dataOffset = idx + 8; dataSize = size; break; }
                idx += 8 + size + (size & 1);
            }
            if (dataOffset < 0 || bits != 16 || channels < 1) return null;
            dataSize = Mathf.Min(dataSize, wav.Length - dataOffset);

            int total = dataSize / 2;
            var samples = new float[total];
            for (int i = 0; i < total; i++)
                samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;

            var clip = AudioClip.Create(name, total / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
#endif
    }
}
