using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Lives on SceneRoot (the GameObject with the whole-world ObjectManipulator). Two jobs:
    ///
    /// 1) Reports when THIS device is grabbing the world, so FilmSync knows to DRIVE the shared
    ///    world transform (and stream it to the master) instead of following it.
    ///
    /// 2) Resolves the collider conflict: an ObjectManipulator on SceneRoot otherwise claims EVERY
    ///    child collider, so grabbing the HeartStone / characters would grab the whole world instead.
    ///    We keep "grab anywhere = move the world" but EXCLUDE the Protected Objects (HeartStone,
    ///    characters, portal, trial/story props) so their own interactables (e.g. the cooperative
    ///    HeartStone grab) respond instead.
    ///
    /// Setup: add to SceneRoot; drag the protected objects (HeartStone, Oracle, Astronaut, portal,
    /// trial objects) into Protected Objects.
    /// </summary>
    [RequireComponent(typeof(ObjectManipulator))]
    public class SceneManipulationReporter : MonoBehaviour
    {
        [Tooltip("Objects (and their children) that must NOT be grabbable as 'the world': the HeartStone, " +
                 "the Oracle/Astronaut, the portal, trial objects, etc. Their colliders are removed from the " +
                 "world grab so their own interactables (cooperative grab, etc.) respond instead.")]
        [SerializeField] private Transform[] protectedObjects;

        public static SceneManipulationReporter Instance { get; private set; }

        /// <summary>True while at least one hand is grabbing the world on this device.</summary>
        public bool IsManipulating { get; private set; }

        private ObjectManipulator _om;

        private void Awake()
        {
            Instance = this;
            _om = GetComponent<ObjectManipulator>();
        }

        private void OnEnable()
        {
            if (_om == null) return;
            _om.firstSelectEntered.AddListener(OnFirstSelect);
            _om.lastSelectExited.AddListener(OnLastDeselect);
        }

        private void OnDisable()
        {
            if (_om == null) return;
            _om.firstSelectEntered.RemoveListener(OnFirstSelect);
            _om.lastSelectExited.RemoveListener(OnLastDeselect);
        }

        // Runs after every interactable has registered. Rebuild the world grab's colliders to exclude
        // the protected subtrees, then hand those colliders back to the protected objects' own interactables.
        private void Start()
        {
            if (_om == null || protectedObjects == null || protectedObjects.Length == 0) return;

            var mgr = _om.interactionManager;

            // 1) World grab keeps colliders that are NEITHER protected NOR part of a prop that has its
            //    OWN ObjectManipulator (those grab individually). Everything else grabs the whole world.
            var keep = new List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (!IsProtected(c.transform) && !HasOwnManipulator(c.transform)) keep.Add(c);

            if (mgr != null) mgr.UnregisterInteractable(_om as IXRInteractable);
            _om.colliders.Clear();
            _om.colliders.AddRange(keep);
            if (mgr != null) mgr.RegisterInteractable(_om as IXRInteractable);

            // 2) Each protected object's own interactable re-claims its collider (e.g. the HeartStone's
            //    StatefulInteractable that fires the cooperative grab).
            foreach (var p in protectedObjects)
            {
                if (p == null) continue;
                foreach (var pi in p.GetComponentsInChildren<XRBaseInteractable>(true))
                {
                    var m = pi.interactionManager;
                    if (m == null) continue;
                    m.UnregisterInteractable(pi as IXRInteractable);
                    m.RegisterInteractable(pi as IXRInteractable);
                }
            }

            Debug.Log($"[SceneGrab] World grab uses {keep.Count} colliders; excluded {protectedObjects.Length} protected object(s).");
        }

        private bool IsProtected(Transform t)
        {
            foreach (var p in protectedObjects)
                if (p != null && (t == p || t.IsChildOf(p))) return true;
            return false;
        }

        // True if this collider belongs to a descendant prop with its OWN ObjectManipulator, so it
        // should grab individually rather than move the whole world. The walk stops at this transform
        // (SceneRoot), so SceneRoot's own grab collider is still kept for the world grab.
        private bool HasOwnManipulator(Transform t)
        {
            for (Transform x = t; x != null && x != transform; x = x.parent)
                if (x.GetComponent<ObjectManipulator>() != null) return true;
            return false;
        }

        private void OnFirstSelect(SelectEnterEventArgs _) => IsManipulating = true;
        private void OnLastDeselect(SelectExitEventArgs _) => IsManipulating = false;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
