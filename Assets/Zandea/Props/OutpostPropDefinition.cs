using UnityEngine;

namespace Zandea.Props
{
    [CreateAssetMenu(menuName = "Zandea/Outpost Props")]
    public sealed class OutpostPropDefinition : ScriptableObject
    {
        public GameObject ceramicPot, woodenStool, chest, barrel, weaponRack;
        public GameObject crate, sack, table, statue;
        public GameObject wickerBasket;
        [Tooltip("Optional deterministic pot mix. Empty uses ceramicPot only.")]
        public GameObject[] ceramicPotVariants;
        [Tooltip("Optional grounded props for the settlement storage area.")]
        public GameObject[] extraStorageProps;
        [Range(0, 3)] public int basketsPerCanopy;
        [Range(0, 4)] public int storageBaskets;
        [Range(0, 4)] public int extraPotsPerCanopy = 1;
        [Min(.5f)] public float potScale = 2f;
        [Min(.5f)] public float sackScale = 1.5f;
        [Min(.5f)] public float crateScale = 1.5f;
        public bool drawPlacementGizmos = true;
    }
}
