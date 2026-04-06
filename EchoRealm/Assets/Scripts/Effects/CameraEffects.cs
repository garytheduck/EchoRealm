using System.Collections;
using UnityEngine;

namespace EchoRealm.Effects
{
    /// <summary>
    /// Camera-based effects: screen shake (earthquake), lightning flash.
    /// Works with HoloLens 2 by shaking the SceneRoot instead of the camera
    /// (camera is head-tracked and shouldn't be moved directly).
    /// </summary>
    public class CameraEffects : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("SceneRoot transform — shaken instead of camera for MR compatibility.")]
        [SerializeField] private Transform sceneRoot;

        [Tooltip("Main directional light — used for lightning flash.")]
        [SerializeField] private Light mainLight;

        [Header("Earthquake Settings")]
        [SerializeField] private float shakeDuration = 2f;
        [SerializeField] private float shakeMagnitude = 0.03f;

        [Header("Lightning Settings")]
        [SerializeField] private float flashIntensity = 5f;

        /// <summary>True if a shake is currently active.</summary>
        public bool IsShaking { get; private set; }

        public static CameraEffects Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Trigger earthquake screen shake.</summary>
        public void Earthquake()
        {
            if (!IsShaking)
                StartCoroutine(ShakeCoroutine());
        }

        /// <summary>Trigger lightning flash on the main light.</summary>
        public void LightningFlash()
        {
            StartCoroutine(LightningCoroutine());
        }

        // ------------------------------------------------------------------
        // Coroutines
        // ------------------------------------------------------------------

        private IEnumerator ShakeCoroutine()
        {
            if (sceneRoot == null) yield break;

            IsShaking = true;
            Vector3 originalPos = sceneRoot.localPosition;
            float elapsed = 0f;

            while (elapsed < shakeDuration)
            {
                // Decreasing intensity over time
                float intensity = shakeMagnitude * (1f - elapsed / shakeDuration);
                float x = Random.Range(-1f, 1f) * intensity;
                float y = Random.Range(-1f, 1f) * intensity;
                float z = Random.Range(-1f, 1f) * intensity;

                sceneRoot.localPosition = originalPos + new Vector3(x, y, z);
                elapsed += Time.deltaTime;
                yield return null;
            }

            sceneRoot.localPosition = originalPos;
            IsShaking = false;
        }

        private IEnumerator LightningCoroutine()
        {
            if (mainLight == null) yield break;

            float originalIntensity = mainLight.intensity;
            Color originalColor = mainLight.color;

            // Flash 1
            mainLight.color = Color.white;
            mainLight.intensity = flashIntensity;
            yield return new WaitForSeconds(0.08f);

            // Dark
            mainLight.intensity = originalIntensity * 0.3f;
            yield return new WaitForSeconds(0.05f);

            // Flash 2 (slightly dimmer)
            mainLight.intensity = flashIntensity * 0.7f;
            yield return new WaitForSeconds(0.12f);

            // Fade back
            float fadeTime = 0.5f;
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                float t = elapsed / fadeTime;
                mainLight.intensity = Mathf.Lerp(flashIntensity * 0.4f, originalIntensity, t);
                mainLight.color = Color.Lerp(Color.white, originalColor, t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            mainLight.intensity = originalIntensity;
            mainLight.color = originalColor;
        }
    }
}
