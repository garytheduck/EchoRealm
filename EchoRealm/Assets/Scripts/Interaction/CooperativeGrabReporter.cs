using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Put on the Heart Stone. Wire the MRTK interactable's Select-Entered (or OnClicked) event
    /// to ReportGrab(). When a player grabs the stone, this reports it — tagged with that player's
    /// index — to the master's CooperationDetector via FilmSync. When BOTH players grab the stone
    /// within the cooperation window, a cooperation event registers; reaching the Act-3 goal opens
    /// the Origin Echo. (Reporting flows to the master, which drives the act transition for everyone.)
    /// </summary>
    public class CooperativeGrabReporter : MonoBehaviour
    {
        [Tooltip("Shared id both headsets report (must match on both devices). Defaults to this object's name.")]
        [SerializeField] private string objectId = "HeartStone";
        [SerializeField] private bool logEvents = true;

        private void Reset() { objectId = gameObject.name; }

        /// <summary>Wire this to the interactable's Select-Entered / OnClicked UnityEvent.</summary>
        public void ReportGrab()
        {
            string id = string.IsNullOrEmpty(objectId) ? gameObject.name : objectId;

            int playerIndex = 0;
            var nm = Networking.FusionNetworkManager.Instance;
            if (nm != null && !nm.IsMaster) playerIndex = 1;

            var sync = Networking.FilmSync.Instance;
            if (sync != null) sync.SubmitInteraction(playerIndex, id, InteractionType.Grab);
            else CooperationDetector.Instance?.ReportInteraction(playerIndex, id, InteractionType.Grab);

            if (logEvents) Debug.Log($"[CoopGrab] Player {playerIndex + 1} grabbed '{id}'.");
        }
    }
}
