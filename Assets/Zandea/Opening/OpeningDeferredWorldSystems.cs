using UnityEngine;
using Zandea.POI;
using KenshiVR.MeleeCombat;

namespace Zandea.Opening
{
    /// <summary>Keeps later settlement streaming out of the authored opening scene.</summary>
    public sealed class OpeningDeferredWorldSystems : MonoBehaviour
    {
        public GameObject[] activateAfterOpening;
        public BanditOutpost kareth;
        public GameObject[] karethActors;
        public bool[] karethActorsWereActive;
        public BanditGroup[] karethGroups;
        public bool[] karethGroupsWereEnabled;

        // Wire this to a later chapter's explicit handoff, when its terrain frame is ready.
        public void ActivateLaterWorld()
        {
            if (activateAfterOpening != null)
                foreach (var item in activateAfterOpening) if (item) item.SetActive(true);
            if (karethActors != null && karethActorsWereActive != null)
                for (int i = 0; i < karethActors.Length && i < karethActorsWereActive.Length; i++)
                    if (karethActors[i]) karethActors[i].SetActive(karethActorsWereActive[i]);
            if (karethGroups != null && karethGroupsWereEnabled != null)
                for (int i = 0; i < karethGroups.Length && i < karethGroupsWereEnabled.Length; i++)
                    if (karethGroups[i]) karethGroups[i].enabled = karethGroupsWereEnabled[i];
            if (kareth) kareth.BeginStreamingFromAuthoredPreview();
        }
    }
}
