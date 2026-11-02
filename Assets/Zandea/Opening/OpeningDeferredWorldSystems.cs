using UnityEngine;

namespace Zandea.Opening
{
    /// <summary>Keeps later settlement streaming out of the authored opening scene.</summary>
    public sealed class OpeningDeferredWorldSystems : MonoBehaviour
    {
        public GameObject[] activateAfterOpening;

        // Wire this to a later chapter's explicit handoff, when its terrain frame is ready.
        public void ActivateLaterWorld()
        {
            if (activateAfterOpening == null) return;
            foreach (var item in activateAfterOpening) if (item) item.SetActive(true);
        }
    }
}
