using System.Collections;
using UnityEngine;

namespace EchoRealm.Effects
{
    /// <summary>
    /// Shared "time is rewinding" feel: a procedural tape-rewind whoosh + a brief time-ripple
    /// (converging particle ring around the scene and a dip of the main light). Subscribes to
    /// FilmSync.OnRewindApplied, which fires on EVERY device the moment a rewind has applied, so
    /// both headsets hear/see it in sync.
    ///
    /// Everything is built at runtime (the project ships no audio assets — the whoosh is synthesized
    /// with AudioClip.Create, the same recipe OracleVoice uses for TTS WAVs). Pure observer:
    /// auto-attached by RewindMenu; remove it and rewind behaves exactly as before.
    /// </summary>
    public class RewindFX : MonoBehaviour
    {
        [Header("Sound")]
        [Tooltip("Volume of the procedural rewind whoosh (0 disables the sound).")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.55f;
        [Tooltip("Length of the whoosh, seconds.")]
        [SerializeField] private float soundSeconds = 0.9f;

        [Header("Visual")]
        [Tooltip("Particles converging toward the scene during the ripple (0 disables them).")]
        [SerializeField] private int particleCount = 140;
        [Tooltip("How long the light dips + the ring converges, seconds.")]
        [SerializeField] private float visualSeconds = 0.8f;
        [Tooltip("Main light is dimmed to this fraction at the dip's deepest point.")]
        [SerializeField, Range(0f, 1f)] private float lightDip = 0.35f;
        [SerializeField] private Color ringColor = new Color(0.55f, 0.85f, 1f, 0.9f); // pale time-blue

        private AudioSource _audio;
        private AudioClip _whoosh;
        private ParticleSystem _ring;
        private Light _mainLight;
        private Coroutine _ripple;
        private float _preDipIntensity;   // light intensity before the dip started
        private bool _dipActive;          // a dip is in progress (restore before restarting)

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;   // UI-style cue, not positional
            _whoosh = BuildWhooshClip(soundSeconds);

            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { _mainLight = l; break; }

            BuildRing();
        }

        private void OnEnable()  => Networking.FilmSync.OnRewindApplied += HandleRewind;
        private void OnDisable() => Networking.FilmSync.OnRewindApplied -= HandleRewind;

        private void HandleRewind(float t)
        {
            if (_audio != null && _whoosh != null && volume > 0f)
            {
                _audio.Stop();
                _audio.clip = _whoosh;
                _audio.volume = volume;
                _audio.Play();
            }

            if (_ripple != null)
            {
                StopCoroutine(_ripple);
                // The interrupted ripple may have left the light mid-dip — restore the TRUE base
                // first, or back-to-back rewinds would capture the dimmed value and dim forever.
                if (_dipActive && _mainLight != null) _mainLight.intensity = _preDipIntensity;
                _dipActive = false;
            }
            _ripple = StartCoroutine(TimeRipple());
        }

        // ------------------------------------------------------------------
        // Visual: converging ring + light dip
        // ------------------------------------------------------------------

        private IEnumerator TimeRipple()
        {
            // Center the ring on the scene (falls back to 1.2 m in front of the head).
            Transform sceneRoot = Networking.QRAnchorManager.Instance != null
                ? Networking.QRAnchorManager.Instance.SceneRoot : null;
            Vector3 center;
            float radius;
            if (sceneRoot != null && sceneRoot.gameObject.activeInHierarchy)
            {
                center = sceneRoot.position;
                radius = Mathf.Clamp(1.6f * Mathf.Max(sceneRoot.localScale.x, 0.05f), 0.3f, 1.8f);
            }
            else
            {
                var cam = Camera.main;
                if (cam == null) yield break;
                center = cam.transform.position + cam.transform.forward * 1.2f;
                radius = 0.6f;
            }

            if (_ring != null && particleCount > 0)
            {
                _ring.transform.position = center;
                var shape = _ring.shape;
                shape.radius = radius;
                _ring.Emit(particleCount);
            }

            // Brief dip of the main light — the world "blinks" as time slips back.
            if (_mainLight != null)
            {
                _preDipIntensity = _mainLight.intensity;
                _dipActive = true;
                float half = visualSeconds * 0.4f;
                for (float t = 0f; t < visualSeconds; t += Time.unscaledDeltaTime)
                {
                    if (_mainLight == null) { _dipActive = false; yield break; }
                    float k = t < half ? t / half : 1f - (t - half) / (visualSeconds - half);
                    _mainLight.intensity = Mathf.Lerp(_preDipIntensity, _preDipIntensity * lightDip, k);
                    yield return null;
                }
                if (_mainLight != null) _mainLight.intensity = _preDipIntensity;
                _dipActive = false;
            }
            _ripple = null;
        }

        // Ring of particles born on a sphere shell and pulled INWARD (negative start speed) — they
        // converge on the scene like time being sucked backward. Built once, emitted per rewind.
        private void BuildRing()
        {
            if (particleCount <= 0) return;
            var go = new GameObject("RewindRipple");
            go.transform.SetParent(transform, false);
            _ring = go.AddComponent<ParticleSystem>();

            var main = _ring.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.6f;
            main.startSpeed = -2.2f;            // negative = inward, toward the shape's center
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = ringColor;
            main.maxParticles = 512;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _ring.emission;
            emission.rateOverTime = 0f;         // burst-only via Emit()

            var shape = _ring.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.8f;

            // Same shader family WeatherController relies on (already in Always Included Shaders).
            var renderer = _ring.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = ringColor;
                renderer.material = mat;
            }
        }

        // ------------------------------------------------------------------
        // Sound: procedural tape-rewind whoosh (rising sweep + wobble), no assets needed
        // ------------------------------------------------------------------

        private static AudioClip BuildWhooshClip(float seconds)
        {
            const int rate = 22050;
            int n = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var samples = new float[n];
            float phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t01 = i / (float)n;
                // Rising sweep 220 → 1300 Hz with an 18 Hz warble — the classic "tape spinning back".
                float freq = Mathf.Lerp(220f, 1300f, t01 * t01)
                             * (1f + 0.22f * Mathf.Sin(2f * Mathf.PI * 18f * t01 * seconds));
                phase += 2f * Mathf.PI * freq / rate;
                // Smooth in/out envelope so it doesn't click.
                float env = Mathf.Sin(Mathf.PI * t01);
                samples[i] = 0.5f * env * Mathf.Sin(phase)
                           + 0.08f * env * (Random.value * 2f - 1f);   // a breath of noise
            }

            var clip = AudioClip.Create("RewindWhoosh", n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
