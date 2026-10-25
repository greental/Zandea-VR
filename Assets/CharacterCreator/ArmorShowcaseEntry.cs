using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    /// <summary>Links an editable showcase instance to the asset whose fit it previews.</summary>
    public sealed class ArmorShowcaseEntry : MonoBehaviour
    {
        public AppearanceItemDefinition appearanceItem;
        public GameObject weaponPrefab;
        [TextArea] public string editingNote;
        [Tooltip("Used when capturing a new armor item that has no Appearance Item yet.")]
        public string newItemName;
        public AppearanceSlot newItemType = AppearanceSlot.Torso;
        [Tooltip("Exact character bone name, or $CharacterRoot. The fitted armor is saved relative to this transform.")]
        public string newItemAttachBoneName;
    }
}
