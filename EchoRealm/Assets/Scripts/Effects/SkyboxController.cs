using System.Collections;
using UnityEngine;

namespace EchoRealm.Effects
{
    /// <summary>
    /// Manages day/night skybox transitions with smooth blending.
    /// Creates procedural skybox materials if none are assigned.
    ///
    /// For HoloLens 2: skybox is visible through the transparent display,
    /// so we primarily control the ambient light color and intensity.
    /// The skybox mainly affects lighting mood rather than a full sky dome.
    ///
    /// Attach to a manager GameObject. Referenced by CommandExecutor for day/night commands.
    /// </summary>
    public class SkyboxController : MonoBehaviour
    {
        [Header("Skybox Materials (auto-created if null)")]
        [SerializeField] private Material daySkybox;
        [SerializeField] private Material nightSkybox;

        [Header("Lighting")]
        [SerializeField] private Light mainDirectionalLight;

        [Header("Day Settings")]
        [SerializeField] private Color dayLightColor = new Color(1f, 0.96f, 0.9f);
        [SerializeField] private float dayLightIntensity = 1f;
        [SerializeField] private Color dayAmbientColor = new Color(0.5f, 0.55f, 0.6f);
        [SerializeField] private Color dayFogColor = new Color(0.7f, 0.8f, 0.9f);

        [Header("Night Settings")]
        [SerializeField] private Color nightLightColor = new Color(0.2f, 0.25f, 0.4f);
        [SerializeField] private float nightLightIntensity = 0.15f;
        [SerializeField] private Color nightAmbientColor = new Color(0.05f, 0.05f, 0.15f);
        [SerializeField] private Color nightFogColor = new Color(0.02f, 0.02f, 0.06f);

        [Header("Transition")]
        [SerializeField] private float transitionDuration = 3f;

        /// <summary>True if it's currently night time.</summary>
        public bool IsNight { get; private set; }

        /// <summary>True if a transition is in progress.</summary>
        public bool IsTransitioning { get; private set; }

        public Material DaySkybox => daySkybox;
        public Material NightSkybox => nightSkybox;

        public static SkyboxController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (mainDirectionalLight == null)
                mainDirectionalLight = FindDirectionalLight();

            if (daySkybox == null) daySkybox = CreateDaySkybox();
            if (nightSkybox == null) nightSkybox = CreateNightSkybox();

            // Start in day mode
            ApplyDayImmediate();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Transition to night time with smooth blending.</summary>
        public void TransitionToNight()
        {
            if (IsNight && !IsTransitioning) return;
            StopAllCoroutines();
            StartCoroutine(TransitionCoroutine(toNight: true));
        }

        /// <summary>Transition to day time with smooth blending.</summary>
        public void TransitionToDay()
        {
            if (!IsNight && !IsTransitioning) return;
            StopAllCoroutines();
            StartCoroutine(TransitionCoroutine(toNight: false));
        }

        /// <summary>Immediately set to day (no transition).</summary>
        public void ApplyDayImmediate()
        {
            IsNight = false;
            RenderSettings.skybox = daySkybox;
            RenderSettings.ambientLight = dayAmbientColor;
            RenderSettings.fogColor = dayFogColor;
            if (mainDirectionalLight != null)
            {
                mainDirectionalLight.color = dayLightColor;
                mainDirectionalLight.intensity = dayLightIntensity;
            }
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Immediately set to night (no transition).</summary>
        public void ApplyNightImmediate()
        {
            IsNight = true;
            RenderSettings.skybox = nightSkybox;
            RenderSettings.ambientLight = nightAmbientColor;
            RenderSettings.fogColor = nightFogColor;
            if (mainDirectionalLight != null)
            {
                mainDirectionalLight.color = nightLightColor;
                mainDirectionalLight.intensity = nightLightIntensity;
            }
            DynamicGI.UpdateEnvironment();
        }

        // ------------------------------------------------------------------
        // Smooth Transition
        // ------------------------------------------------------------------

        private IEnumerator TransitionCoroutine(bool toNight)
        {
            IsTransitioning = true;

            Color fromLightColor = mainDirectionalLight != null ? mainDirectionalLight.color : dayLightColor;
            float fromLightIntensity = mainDirectionalLight != null ? mainDirectionalLight.intensity : dayLightIntensity;
            Color fromAmbient = RenderSettings.ambientLight;
            Color fromFog = RenderSettings.fogColor;

            Color toLightColor = toNight ? nightLightColor : dayLightColor;
            float toLightIntensity = toNight ? nightLightIntensity : dayLightIntensity;
            Color toAmbient = toNight ? nightAmbientColor : dayAmbientColor;
            Color toFog = toNight ? nightFogColor : dayFogColor;

            // Set target skybox immediately (blending skybox materials requires custom shader)
            RenderSettings.skybox = toNight ? nightSkybox : daySkybox;

            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                float t = elapsed / transitionDuration;
                // Smooth step for nicer easing
                t = t * t * (3f - 2f * t);

                if (mainDirectionalLight != null)
                {
                    mainDirectionalLight.color = Color.Lerp(fromLightColor, toLightColor, t);
                    mainDirectionalLight.intensity = Mathf.Lerp(fromLightIntensity, toLightIntensity, t);
                }

                RenderSettings.ambientLight = Color.Lerp(fromAmbient, toAmbient, t);
                RenderSettings.fogColor = Color.Lerp(fromFog, toFog, t);

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Ensure final values are exact
            if (toNight) ApplyNightImmediate(); else ApplyDayImmediate();

            IsNight = toNight;
            IsTransitioning = false;

            Debug.Log($"[Skybox] Transition complete: {(toNight ? "NIGHT" : "DAY")}");
        }

        // ------------------------------------------------------------------
        // Procedural Skybox Creation
        // ------------------------------------------------------------------

        private Material CreateDaySkybox()
        {
            // Procedural skybox with gradient: light blue top, white horizon
            var mat = new Material(Shader.Find("Skybox/Procedural"));
            mat.SetFloat("_SunSize", 0.04f);
            mat.SetFloat("_SunSizeConvergence", 5f);
            mat.SetFloat("_AtmosphereThickness", 1f);
            mat.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.8f));
            mat.SetColor("_GroundColor", new Color(0.4f, 0.35f, 0.3f));
            mat.SetFloat("_Exposure", 1.3f);
            return mat;
        }

        private Material CreateNightSkybox()
        {
            // Dark procedural skybox
            var mat = new Material(Shader.Find("Skybox/Procedural"));
            mat.SetFloat("_SunSize", 0.01f);
            mat.SetFloat("_SunSizeConvergence", 1f);
            mat.SetFloat("_AtmosphereThickness", 0.2f);
            mat.SetColor("_SkyTint", new Color(0.02f, 0.02f, 0.08f));
            mat.SetColor("_GroundColor", new Color(0.01f, 0.01f, 0.03f));
            mat.SetFloat("_Exposure", 0.1f);
            return mat;
        }

        private Light FindDirectionalLight()
        {
            foreach (var light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional)
                    return light;
            }
            return null;
        }
    }
}
