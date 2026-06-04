using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Reports when THIS device is actively grabbing the whole world (the SceneRoot's
    /// ObjectManipulator). FilmSync reads this: the grabbing device drives the shared world
    /// transform and streams it to the master; every other device follows the networked value.
    ///
    /// Attach to the SAME GameObject as the SceneRoot ObjectManipulator (i.e. SceneRoot).
    /// </summary>
    [RequireComponent(typeof(ObjectManipulator))]
    public class SceneManipulationReporter : MonoBehaviour
    {
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

        private void OnFirstSelect(SelectEnterEventArgs _) => IsManipulating = true;
        private void OnLastDeselect(SelectExitEventArgs _) => IsManipulating = false;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
