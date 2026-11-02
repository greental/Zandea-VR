using JunkerBody;
using MyAssets.Planet;
using UnityEngine;
using Zandea.POI;

namespace Zandea.Pueblo
{
    // An explicit Main test start in the treadmill's surface frame. The ship's
    // Space-to-Surface transition can move terrain relative to scene coordinates.
    public sealed class PuebloMissionTestStart : MonoBehaviour
    {
        public bool enableForTesting = true;
        public Vector3 localStart = new(0f, 1.7f, -55f);
        public float repositionIfFartherThan = 100f;
        bool applied;
        BanditOutpost poi;

        void Update() => ApplyIfReady();

        public bool ApplyIfReady()
        {
            if (applied || !enableForTesting || gameObject.scene.path != "Assets/Main.unity") return applied;
            if (!poi) poi = GetComponent<BanditOutpost>();
            if (!poi || !poi.treadmill || !poi.treadmill.gameObject.activeInHierarchy) return false;
            var transition = FindFirstObjectByType<PlanetTerrainTransitionController>();
            if (transition && transition.TravelMode != PlanetTravelMode.Surface) return false;
            var player = FindFirstObjectByType<VRBodyController>();
            if (!player || !player.xrOrigin) return false;
            Vector3 destination = poi.Center + poi.Rotation * localStart;
            var current = player.xrOrigin.position;
            if (Vector2.Distance(new Vector2(current.x, current.z),
                new Vector2(destination.x, destination.z)) > repositionIfFartherThan)
                player.xrOrigin.position = destination;
            applied = true;
            return true;
        }
    }
}
