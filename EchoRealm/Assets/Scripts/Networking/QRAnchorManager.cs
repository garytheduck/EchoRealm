// CS0414: fields used only in UWP builds appear "unused" in Editor
#pragma warning disable 0414

using UnityEngine;

#if WINDOWS_UWP
using Microsoft.MixedReality.OpenXR;
using System;
#endif

namespace EchoRealm.Networking
{
    /// <summary>
    /// Detects a physical QR code via HoloLens 2 camera and uses its pose
    /// to establish a shared spatial origin (SceneRoot) for all devices.
    ///
    /// Flow:
    /// 1. Attach to a GameObject (e.g., "QRAnchorManager").
    /// 2. Set sceneRoot to the parent transform that holds all holographic content.
    /// 3. On Start, begins watching for QR codes.
    /// 4. When the target QR code is detected, aligns sceneRoot to QR pose.
    /// 5. Fires OnAnchorEstablished so FusionNetworkManager can start the session.
    ///
    /// NOTE: QR tracking is built into Mixed Reality OpenXR Plugin 1.11.2+.
    ///       No separate Microsoft.MixedReality.QR package needed.
    /// </summary>
    public class QRAnchorManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The root transform that holds all scene content. Will be repositioned to match QR pose.")]
        [SerializeField] private Transform sceneRoot;

        [Header("QR Settings")]
        [Tooltip("Expected data content of the QR code (e.g., 'EchoRealm-Anchor'). Leave empty to use the first QR detected.")]
        [SerializeField] private string expectedQRData = "EchoRealm-Anchor";

        [Tooltip("Minimum physical size of QR code in meters to accept (filters noise).")]
        [SerializeField] private float minQRSizeMeters = 0.05f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True after the anchor has been established from a QR code.</summary>
        public bool IsAnchored { get; private set; }

        /// <summary>Fired when the QR anchor is successfully established.</summary>
        public event System.Action OnAnchorEstablished;

#if WINDOWS_UWP
        private ARMarkerManager markerManager;
#endif

        private void Start()
        {
            if (sceneRoot == null)
            {
                Debug.LogError("[QRAnchor] SceneRoot not assigned! Assign the root transform in Inspector.");
                return;
            }

#if WINDOWS_UWP
            StartQRTracking();
#else
            // In Unity Editor (play mode), simulate anchor at origin
            Log("Running in Editor — simulating QR anchor at world origin.");
            IsAnchored = true;
            OnAnchorEstablished?.Invoke();
#endif
        }

#if WINDOWS_UWP
        private void StartQRTracking()
        {
            Log("Starting QR Code tracking via OpenXR ARMarkerManager...");

            // ARMarkerManager is part of Mixed Reality OpenXR Plugin 1.11.2+
            // It handles QR code detection through the HoloLens camera
            markerManager = gameObject.AddComponent<ARMarkerManager>();
            markerManager.markersChanged += OnMarkersChanged;

            Log("QR tracking active. Waiting for QR code scan...");
        }

        private void OnMarkersChanged(ARMarkersChangedEventArgs args)
        {
            // Process newly added markers
            foreach (var marker in args.added)
            {
                ProcessMarker(marker);
            }

            // Also check updated markers (improved pose accuracy)
            foreach (var marker in args.updated)
            {
                if (!IsAnchored)
                {
                    ProcessMarker(marker);
                }
            }
        }

        private void ProcessMarker(ARMarker marker)
        {
            if (IsAnchored) return;

            // Check if this is a QR code marker
            if (marker.markerType != ARMarkerType.QRCode) return;

            string decodedString = marker.GetDecodedString();
            float sizeMeters = marker.size.x; // QR physical width in meters

            Log($"QR detected: data='{decodedString}', size={sizeMeters:F3}m, " +
                $"pos={marker.transform.position}, rot={marker.transform.rotation.eulerAngles}");

            // Filter by expected content (if specified)
            if (!string.IsNullOrEmpty(expectedQRData) && decodedString != expectedQRData)
            {
                Log($"QR data mismatch. Expected: '{expectedQRData}', got: '{decodedString}'. Skipping.");
                return;
            }

            // Filter by minimum size
            if (sizeMeters < minQRSizeMeters)
            {
                Log($"QR too small ({sizeMeters:F3}m < {minQRSizeMeters:F3}m). Skipping.");
                return;
            }

            // Align sceneRoot to QR code pose
            AlignSceneToQR(marker.transform.position, marker.transform.rotation);
        }
#endif

        /// <summary>
        /// Positions sceneRoot so that the QR code's position becomes the world origin
        /// for all holographic content. Both HoloLens devices scanning the same QR code
        /// will place sceneRoot at the same physical location.
        /// </summary>
        private void AlignSceneToQR(Vector3 qrPosition, Quaternion qrRotation)
        {
            Log($"Aligning SceneRoot to QR pose: pos={qrPosition}, rot={qrRotation.eulerAngles}");

            sceneRoot.position = qrPosition;
            sceneRoot.rotation = qrRotation;

            IsAnchored = true;
            OnAnchorEstablished?.Invoke();

            Log("QR Anchor ESTABLISHED. Scene is now spatially aligned.");
        }

        /// <summary>
        /// Manually set the anchor (useful for testing or alternative anchor methods).
        /// </summary>
        public void SetAnchorManually(Vector3 position, Quaternion rotation)
        {
            if (sceneRoot == null) return;
            AlignSceneToQR(position, rotation);
        }

        private void OnDestroy()
        {
#if WINDOWS_UWP
            if (markerManager != null)
            {
                markerManager.markersChanged -= OnMarkersChanged;
            }
#endif
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[QRAnchor] {message}");
        }
    }
}
