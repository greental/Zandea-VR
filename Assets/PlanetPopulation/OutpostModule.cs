using UnityEngine;
using ScavengerNPC;

namespace MyAssets.PlanetTerrain
{
    public sealed class OutpostModule : MonoBehaviour
    {
        public Vector2 footprint = new Vector2(10, 10);
        [Min(0)] public float clearance = 3;
        [Min(0)] public float placementWeight = 1;
        public bool rotateFreely = true;
        public Vector3 localEntranceDirection = Vector3.forward;
    }

}
