using UnityEngine;
using ScavengerNPC;

namespace MyAssets.PlanetTerrain
{
    [CreateAssetMenu(menuName = "Koombar/Planet/Outpost Faction Theme")]
    public sealed class OutpostFactionTheme : ScriptableObject
    {
        public GameObject[] smallBuildings;
        public GameObject[] mediumBuildings;
        public GameObject[] largeBuildings;
        public GameObject[] landingPads;
        public GameObject[] aaTurrets;
        public GameObject[] props;
        public GameObject[] loot;
        public ScavengerEnemy npcPrefab;
        public GameObject pyxol2LeanPrefab;
        public GameObject junkHoverBikePrefab;
        public GameObject canopyPrefab;
        public GameObject antennaPrefab;
        public GameObject wallPrefab;
        public GameObject gatePrefab;
        public GameObject pipePrefab;
    }

}
