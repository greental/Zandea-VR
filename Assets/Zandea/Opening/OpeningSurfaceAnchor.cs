using System.Collections;
using System.Collections.Generic;
using KenshiVR.MeleeCombat;
using UnityEngine;
using UnityEngine.AI;

namespace Zandea.Opening
{
    /// <summary>Builds navigation around saved scene positions without moving the player or slavers.</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class OpeningSurfaceAnchor : MonoBehaviour
    {
        public BanditGroup group;
        public StarvingBandit[] slavers;
        [Min(8f)] public float navigationRadius = 32f;
        [Min(.1f)] public float groundTolerance = .75f;
        [Min(.1f)] public float readyTimeout = 10f;

        NavMeshData navigation;
        NavMeshDataInstance navigationInstance;
        bool groupWasEnabled;
        bool[] slaverWasEnabled;

        void Awake()
        {
            if (!group) return;
            groupWasEnabled = group.enabled;
            group.enabled = false;
            group.buildLocalNavMesh = false;
            if (slavers == null) return;
            slaverWasEnabled = new bool[slavers.Length];
            for (int i = 0; i < slavers.Length; i++)
                if (slavers[i]) { slaverWasEnabled[i] = slavers[i].enabled; slavers[i].enabled = false; }
        }

        IEnumerator Start()
        {
            if (!group || slavers == null || slavers.Length == 0)
            { Debug.LogError("Opening navigation references are incomplete.", this); yield break; }

            // The planet treadmill may finish its first tile update after Start.
            yield return null;
            float deadline = Time.time + readyTimeout;
            while (Time.time < deadline && !BuildNavigation()) yield return null;
            if (!navigationInstance.valid)
            { Debug.LogError("No NavMesh at the saved opening position. Fix scene placement in the Inspector.", this); yield break; }

            if (slaverWasEnabled != null)
                for (int i = 0; i < slavers.Length; i++)
                    if (slavers[i]) slavers[i].enabled = slaverWasEnabled[i];
            if (groupWasEnabled) group.enabled = true;
            yield return null;
            foreach (var slaver in slavers)
                if (slaver && (!slaver.agent || !slaver.agent.isOnNavMesh))
                    Debug.LogError($"{slaver.name} is off NavMesh at its saved position {slaver.transform.position}.", slaver);
        }

        bool BuildNavigation()
        {
            var first = slavers[0] ? slavers[0].agent : null;
            if (!first) return false;
            Vector3 center = Vector3.zero;
            int count = 0;
            foreach (var slaver in slavers)
                if (slaver) { center += slaver.transform.position; count++; }
            if (count == 0) return false;
            center /= count;
            var bounds = new Bounds(center, new Vector3(navigationRadius * 2f, 80f, navigationRadius * 2f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            sources.RemoveAll(source => source.component &&
                (source.component.GetComponentInParent<StarvingBandit>() ||
                 source.component.GetComponentInParent<Rigidbody>()));
            if (sources.Count == 0) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = first.agentTypeID, areaMask = NavMesh.AllAreas };
            foreach (var slaver in slavers)
            {
                if (!slaver) continue;
                var position = slaver.transform.position;
                bool hasGround = false;
                foreach (var terrain in Terrain.activeTerrains)
                {
                    if (!terrain || !terrain.enabled || !terrain.terrainData) continue;
                    var origin = terrain.transform.position;
                    var size = terrain.terrainData.size;
                    if (position.x < origin.x || position.x > origin.x + size.x ||
                        position.z < origin.z || position.z > origin.z + size.z) continue;
                    hasGround = true;
                    if (Mathf.Abs(terrain.SampleHeight(position) + origin.y - position.y) > groundTolerance)
                        return false;
                    break;
                }
                if (!hasGround) return false;
            }
            var settings = NavMesh.GetSettingsByID(first.agentTypeID);
            settings.overrideVoxelSize = true;
            settings.voxelSize = .16f;
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, bounds.size), center, Quaternion.identity);
            if (!navigation) return false;
            navigationInstance = NavMesh.AddNavMeshData(navigation);
            if (!navigationInstance.valid) return false;
            if (!NavMesh.SamplePosition(center, out _, groundTolerance, filter))
            {
                navigationInstance.Remove(); Destroy(navigation); navigation = null;
                return false;
            }
            return true;
        }

        void OnDestroy()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) Destroy(navigation);
        }
    }
}
