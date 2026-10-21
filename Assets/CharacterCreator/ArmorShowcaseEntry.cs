using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    /// <summary>Links an editable showcase instance to the asset whose fit it previews.</summary>
    public sealed class ArmorShowcaseEntry : MonoBehaviour
    {
        public AppearanceItemDefinition appearanceItem;
        public GameObject weaponPrefab;
        [TextArea] public string editingNote;
    }
}
