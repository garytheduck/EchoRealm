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

        [Tooltip("OFF (default): individual props can't be hand-grabbed — grabbing a prop moves the WHOLE " +
                 "scene, and props are manipulated only via 'Claude' voice + eye-tracking. ON: restores " +
                 "per-prop hand-grab alongside the whole-scene grab.")]
        [SerializeField] private bool allowIndividualPropGrab = false;

        public static SceneManipulationReporter Instance { get; private set; }

        /// <summary>True while this device is driving the world transform — by hand grab, or by a
        /// programmatic driver (PalmHold) via <see cref="ExternalDrive"/>. FilmSync reads this to
        /// decide whether to STREAM the scene transform to peers instead of following it.</summary>
        public bool IsManipulating => _grabbing || ExternalDrive;

        /// <summary>Additive hook: set true by code that moves the whole scene programmatically
        /// (e.g. PalmHold's follow-the-palm) so the transform is streamed to every headset exactly
        /// like during a hand grab. Default false — behavior is identical to before when unused.</summary>
        public bool ExternalDrive { get; set; }

        private bool _grabbing;

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

            // Scene-level manipulation only (default): turn OFF each individual prop's hand-grab so the
            // hands can only ever move the WHOLE scene. Props stay registered + gaze-resolvable for the
            // "Claude, …" voice path. Reversible — set allowIndividualPropGrab = true to bring it back.
            if (!allowIndividualPropGrab) DisableIndividualPropGrabs();

            var mgr = _om.interactionManager;

            // 1) World grab keeps colliders that are NEITHER protected NOR owned by a prop with an
            //    ENABLED ObjectManipulator. With per-prop grab off (default), props fall through to the
            //    whole-scene grab, so grabbing a prop moves the whole scene.
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

            Debug.Log($"[SceneGrab] World grab uses {keep.Count} colliders; individual prop grab " +
                      $"{(allowIndividualPropGrab ? "ENABLED" : "DISABLED")}; excluded {protectedObjects.Length} protected object(s).");
        }

        // Scene-level manipulation only: disable every prop's own ObjectManipulator so a hand-grab on
        // a prop drives the WHOLE-scene grab instead. Skips the world-grab manipulator and protected
        // objects. The component stays attached (just disabled), so ManipulableRegistry still discovers
        // the prop and the "Claude" voice + gaze path is unaffected.
        private void DisableIndividualPropGrabs()
        {
            int n = 0;
            foreach (var om in GetComponentsInChildren<ObjectManipulator>(true))
            {
                if (om == _om || IsProtected(om.transform) || !om.enabled) continue;
                om.enabled = false;
                n++;
            }
            if (n > 0) Debug.Log($"[SceneGrab] Disabled {n} individual prop grab(s) — hands move the whole scene only.");
        }

        private bool IsProtected(Transform t)
        {
            foreach (var p in protectedObjects)
                if (p != null && (t == p || t.IsChildOf(p))) return true;
            return false;
        }

        // True if this collider belongs to a descendant prop with its OWN *enabled* ObjectManipulator,
        // so it should grab individually rather than move the whole world. With individual prop grab
        // disabled (the default), those manipulators are off and their colliders fall into the world
        // grab. The walk stops at this transform (SceneRoot), so SceneRoot's own grab collider is kept.
        private bool HasOwnManipulator(Transform t)
        {
            for (Transform x = t; x != null && x != transform; x = x.parent)
            {
                var om = x.GetComponent<ObjectManipulator>();
                if (om != null && om.enabled) return true;
            }
            return false;
        }

        private void OnFirstSelect(SelectEnterEventArgs _) => _grabbing = true;
        private void OnLastDeselect(SelectExitEventArgs _) => _grabbing = false;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
