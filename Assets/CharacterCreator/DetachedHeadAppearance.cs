using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    public sealed class DetachedHeadAppearance : MonoBehaviour
    {
        public GameObject hiddenHair;
        public void RevealHair(){if(hiddenHair)hiddenHair.SetActive(true);}
    }
}
