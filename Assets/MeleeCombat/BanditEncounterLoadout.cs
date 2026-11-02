using KenshiVR.CharacterCreator;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    [CreateAssetMenu(menuName = "Zandea/Bandit Encounter Loadout")]
    public sealed class BanditEncounterLoadout : ScriptableObject
    {
        public GameObject[] weaponPrefabs;
        public AppearanceItemDefinition[] followerHeadwear;
        public AppearanceItemDefinition[] leaderHeadwear;
        public AppearanceItemDefinition[] torso;
        public AppearanceItemDefinition[] legs;
    }
}
