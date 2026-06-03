using UnityEngine;

namespace EchoRealm.Effects
{
    /// <summary>
    /// Manages weather-related particle effects: rain, fog, wind, dust.
    /// Creates particle systems programmatically if not assigned in Inspector.
    ///
    /// Attach to a "WeatherEffects" GameObject under SceneRoot.
    /// CommandExecutor calls these methods via the effect references.
    /// </summary>
    public class WeatherController : MonoBehaviour
    {
        [Header("Effect References (auto-created if null)")]
        [SerializeField] private ParticleSystem rainSystem;
        [SerializeField] private ParticleSystem fogSystem;
        [SerializeField] private ParticleSystem windSystem;
        [SerializeField] private ParticleSystem dustSystem;

        [Tooltip("Material for all weather particles. Assign a lightweight particle material " +
                 "(e.g. Mobile/Particles/Additive). Because it's referenced here, its shader ships " +
                 "in the build — no Shader.Find, no stripping/magenta/crash on HoloLens, fast build.")]
        [SerializeField] private Material particleMaterial;

        [Header("Rain Settings")]
        [SerializeField] private int rainParticleCount = 500;
        [SerializeField] private Color rainColor = new Color(0.7f, 0.8f, 1f, 0.5f);

        [Header("Fog Settings")]
        [SerializeField] private int fogParticleCount = 100;
        [SerializeField] private Color fogColor = new Color(0.8f, 0.8f, 0.9f, 0.15f);

        [Header("Wind Settings")]
        [SerializeField] private int windParticleCount = 200;
        [SerializeField] private Color windLeafColor = new Color(0.3f, 0.6f, 0.2f, 0.8f);

        public ParticleSystem Rain => rainSystem;
        public ParticleSystem Fog => fogSystem;
        public ParticleSystem Wind => windSystem;
        public ParticleSystem Dust => dustSystem;

        private void Awake()
        {
            if (rainSystem == null) rainSystem = CreateRainSystem();
            if (fogSystem == null) fogSystem = CreateFogSystem();
            if (windSystem == null) windSystem = CreateWindSystem();
            if (dustSystem == null) dustSystem = CreateDustSystem();

            // Start all disabled
            StopAll();
        }

        public void StopAll()
        {
            SetEffect(rainSystem, false);
            SetEffect(fogSystem, false);
            SetEffect(windSystem, false);
            SetEffect(dustSystem, false);
        }

        public void SetEffect(ParticleSystem ps, bool active)
        {
            if (ps == null) return;
            if (active) { ps.gameObject.SetActive(true); ps.Play(); }
            else { ps.Stop(); ps.gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------
        // Rain: vertical falling drops from above
        // ------------------------------------------------------------------
        private ParticleSystem CreateRainSystem()
        {
            var go = new GameObject("RainEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0, 2f, 0);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = rainParticleCount;
            main.startLifetime = 2f;
            main.startSpeed = 5f;
            main.startSize = 0.02f;
            main.startColor = rainColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = rainParticleCount / 2f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3f, 0.1f, 3f);

            // Stretch particles to look like raindrops
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3f;
            renderer.material = CreateParticleMaterial(rainColor);

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Fog: slow-moving large transparent particles at ground level
        // ------------------------------------------------------------------
        private ParticleSystem CreateFogSystem()
        {
            var go = new GameObject("FogEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0, 0.2f, 0);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = fogParticleCount;
            main.startLifetime = 8f;
            main.startSpeed = 0.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startColor = fogColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = fogParticleCount / 4f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(4f, 0.3f, 4f);

            // Fade in/out
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new(Color.white, 0f), new(Color.white, 1f) },
                new GradientAlphaKey[] { new(0f, 0f), new(1f, 0.3f), new(1f, 0.7f), new(0f, 1f) }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateParticleMaterial(fogColor);

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Wind: horizontal leaf-like particles
        // ------------------------------------------------------------------
        private ParticleSystem CreateWindSystem()
        {
            var go = new GameObject("WindEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(-2f, 1f, 0);
            go.transform.localRotation = Quaternion.Euler(0, 0, -15f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = windParticleCount;
            main.startLifetime = 3f;
            main.startSpeed = 2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = windLeafColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.3f;
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = windParticleCount / 3f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.5f, 2f, 3f);

            // Turbulence via noise
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 1f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateParticleMaterial(windLeafColor);

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Dust: small particles kicked up (earthquake effect)
        // ------------------------------------------------------------------
        private ParticleSystem CreateDustSystem()
        {
            var go = new GameObject("DustEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = 300;
            main.startLifetime = 3f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new Color(0.6f, 0.5f, 0.4f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.1f; // Float upward slightly
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = 100f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3f, 0.1f, 3f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1f;
            noise.frequency = 2f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateParticleMaterial(new Color(0.6f, 0.5f, 0.4f, 0.5f));

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Utility
        // ------------------------------------------------------------------
        private Material CreateParticleMaterial(Color color)
        {
            // Prefer an Inspector-assigned material — its shader is referenced by the scene and so
            // ships in the build (no stripping), and it builds far faster than adding the heavy
            // Standard particle shaders to Always Included Shaders.
            if (particleMaterial != null)
            {
                var inst = new Material(particleMaterial);
                if (inst.HasProperty("_Color")) inst.SetColor("_Color", color);
                if (inst.HasProperty("_TintColor")) inst.SetColor("_TintColor", color);
                return inst;
            }

            // Fallback: a built-in shader. Works in the Editor; may be stripped on device — return
            // null rather than crashing if nothing is found (the caller assigns null safely, so
            // Awake() finishes and StopAll() runs — no rogue particles playing from the start).
            Shader shader = Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[Weather] No particle shader available (stripped on device). " +
                                 "Assign 'Particle Material' on WeatherController to fix rendering.");
                return null;
            }
            var mat = new Material(shader);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }
    }
}
