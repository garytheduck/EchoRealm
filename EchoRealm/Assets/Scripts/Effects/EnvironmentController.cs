using UnityEngine;

namespace EchoRealm.Effects
{
    /// <summary>
    /// Manages nature/creature particle effects: butterflies, fireflies, fire, lightning.
    /// Creates particle systems programmatically if not assigned in Inspector.
    ///
    /// Attach to an "EnvironmentEffects" GameObject under SceneRoot.
    /// </summary>
    public class EnvironmentController : MonoBehaviour
    {
        [Header("Effect References (auto-created if null)")]
        [SerializeField] private ParticleSystem fireSystem;
        [SerializeField] private ParticleSystem butterfliesSystem;
        [SerializeField] private ParticleSystem firefliesSystem;

        [Header("Fire Settings")]
        [SerializeField] private Color fireColorStart = new Color(1f, 0.6f, 0f, 0.8f);
        [SerializeField] private Color fireColorEnd = new Color(1f, 0f, 0f, 0f);

        [Header("Firefly Settings")]
        [SerializeField] private Color fireflyColor = new Color(0.8f, 1f, 0.3f, 0.9f);
        [SerializeField] private int fireflyCount = 50;

        [Header("Butterfly Settings")]
        [SerializeField] private int butterflyCount = 20;

        [Header("Fire Light")]
        [SerializeField] private Light fireLight;

        public ParticleSystem Fire => fireSystem;
        public ParticleSystem Butterflies => butterfliesSystem;
        public ParticleSystem Fireflies => firefliesSystem;

        private void Awake()
        {
            if (fireSystem == null) fireSystem = CreateFireSystem();
            if (butterfliesSystem == null) butterfliesSystem = CreateButterfliesSystem();
            if (firefliesSystem == null) firefliesSystem = CreateFirefliesSystem();

            StopAll();
        }

        public void StopAll()
        {
            SetEffect(fireSystem, false);
            SetEffect(butterfliesSystem, false);
            SetEffect(firefliesSystem, false);
            if (fireLight != null) fireLight.enabled = false;
        }

        public void SetEffect(ParticleSystem ps, bool active)
        {
            if (ps == null) return;
            if (active) { ps.gameObject.SetActive(true); ps.Play(); }
            else { ps.Stop(); ps.gameObject.SetActive(false); }
        }

        public void SetFire(bool active)
        {
            SetEffect(fireSystem, active);
            if (fireLight != null) fireLight.enabled = active;
        }

        // ------------------------------------------------------------------
        // Fire: upward flames with glow
        // ------------------------------------------------------------------
        private ParticleSystem CreateFireSystem()
        {
            var go = new GameObject("FireEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0, 0, 0.5f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = 200;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = fireColorStart;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.5f; // Rise upward
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = 80f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.2f;

            // Color over lifetime: orange → red → transparent
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new(new Color(1f, 0.9f, 0.3f), 0f),
                    new(new Color(1f, 0.4f, 0f), 0.5f),
                    new(new Color(0.8f, 0.1f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                    new(0.9f, 0f), new(0.7f, 0.5f), new(0f, 1f)
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // Size decreases over lifetime
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateAdditiveMaterial(fireColorStart);

            // Add fire light
            if (fireLight == null)
            {
                var lightGo = new GameObject("FireLight");
                lightGo.transform.SetParent(go.transform);
                lightGo.transform.localPosition = new Vector3(0, 0.3f, 0);
                fireLight = lightGo.AddComponent<Light>();
                fireLight.type = LightType.Point;
                fireLight.color = new Color(1f, 0.6f, 0.2f);
                fireLight.intensity = 2f;
                fireLight.range = 3f;
                fireLight.enabled = false;
            }

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Butterflies: colorful particles with erratic movement
        // ------------------------------------------------------------------
        private ParticleSystem CreateButterfliesSystem()
        {
            var go = new GameObject("ButterfliesEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0, 0.8f, 0);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = butterflyCount;
            main.startLifetime = 10f;
            main.startSpeed = 0.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            main.loop = true;

            // Random colors for butterflies
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.4f, 0.7f), // Pink
                new Color(0.3f, 0.6f, 1f)   // Blue
            );

            var emission = ps.emission;
            emission.rateOverTime = butterflyCount / 5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.5f;

            // Erratic butterfly-like movement
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateAdditiveMaterial(Color.white);

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Fireflies: small glowing dots with gentle movement (best at night)
        // ------------------------------------------------------------------
        private ParticleSystem CreateFirefliesSystem()
        {
            var go = new GameObject("FirefliesEffect");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0, 0.5f, 0);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = fireflyCount;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = 0.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
            main.startColor = fireflyColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;

            var emission = ps.emission;
            emission.rateOverTime = fireflyCount / 4f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2f;

            // Gentle floating
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.2f;
            noise.frequency = 0.3f;

            // Pulsing glow (alpha oscillation)
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new(fireflyColor, 0f), new(fireflyColor, 1f) },
                new GradientAlphaKey[] {
                    new(0f, 0f), new(1f, 0.15f), new(0.3f, 0.4f),
                    new(1f, 0.6f), new(0.3f, 0.85f), new(0f, 1f)
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateAdditiveMaterial(fireflyColor);

            go.SetActive(false);
            return ps;
        }

        // ------------------------------------------------------------------
        // Utility
        // ------------------------------------------------------------------
        private Material CreateAdditiveMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Particles/Standard Unlit"));
            mat.SetColor("_Color", color);
            mat.SetFloat("_Mode", 1);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }
    }
}
