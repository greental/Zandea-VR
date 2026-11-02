using System.Collections;
using System.Collections.Generic;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEngine;
using UnityEngine.AI;

namespace Zandea.Opening
{
    /// <summary>
    /// Places the authored opening at the active terrain surface and builds a small
    /// opening-only NavMesh after the terrain's startup vertical adjustment.
    /// Runs once at scene start; normal player locomotion is never corrected afterward.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class OpeningSurfaceAnchor : MonoBehaviour
    {
        public VRBodyController player;
        public BanditGroup group;
        public StarvingBandit[] slavers;
        public Transform escortDestination;
        [Min(0f)] public float playerGroundClearance = .08f;
        [Min(.1f)] public float navSnapRadius = 4f;
        [Min(.1f)] public float navReadyTimeout = 5f;
        [Min(.1f)] public float groundReadyTimeout = 10f;
        bool groupWasEnabled;
        bool[] slaverWasEnabled;
        NavMeshData navigation;
        NavMeshDataInstance navigationInstance;

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
            if (!player || !player.xrOrigin || !group || slavers == null || slavers.Length != 3)
            { Debug.LogError("Opening surface anchor references are incomplete.", this); yield break; }
            float deadline = Time.time + groundReadyTimeout;
            float ground;
            while (!TryGround(player.xrOrigin.position, out ground))
            {
                if (Time.time >= deadline)
                { Debug.LogError("Opening player has no active terrain beneath its XZ start.", this); yield break; }
                yield return null;
            }

            PlaceAtGround(ground);
            // Main's terrain treadmill and outposts can adjust their vertical frame
            // during the first Update. Let that settle before building navigation.
            yield return null;
            if (TryGround(player.xrOrigin.position, out ground)) PlaceAtGround(ground);
            if (!BuildNavigation())
            { Debug.LogError("Opening local terrain NavMesh could not be built.", this); yield break; }

            if (slaverWasEnabled != null)
                for (int i = 0; i < slavers.Length; i++)
                    if (slavers[i]) slavers[i].enabled = slaverWasEnabled[i];
            if (groupWasEnabled) group.enabled = true;
            yield return BindAgentsAfterNavBuild();
        }

        void PlaceAtGround(float ground)
        {
            float delta = ground + playerGroundClearance - player.xrOrigin.position.y;
            if (Mathf.Abs(delta) > .01f)
            {
                player.transform.root.position += Vector3.up * delta;
                group.transform.position += Vector3.up * delta;
                // The escort destination is usually a child of the group root.
                if (escortDestination && !escortDestination.IsChildOf(group.transform))
                    escortDestination.position += Vector3.up * delta;
            }
            foreach (var slaver in slavers)
            {
                if (!slaver) continue;
                Vector3 position = slaver.transform.position + Vector3.up * delta;
                if (TryGround(position, out float localGround)) position.y = localGround;
                slaver.transform.position = position;
            }
            if (escortDestination && TryGround(escortDestination.position, out float destinationGround))
            {
                var position = escortDestination.position;
                position.y = destinationGround;
                escortDestination.position = position;
            }
        }

        bool BuildNavigation()
        {
            var first = slavers[0] ? slavers[0].agent : null;
            if (!first) return false;
            const float radius = 32f;
            Vector3 center = group.transform.position;
            var bounds = new Bounds(center, new Vector3(radius * 2f, 60f, radius * 2f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            sources.RemoveAll(source => source.component &&
                (source.component.GetComponentInParent<StarvingBandit>() ||
                 source.component.GetComponentInParent<Rigidbody>()));
            if (sources.Count == 0) { Debug.LogError("Opening navigation has no static terrain/collider sources.", this); return false; }
            var settings = NavMesh.GetSettingsByID(first.agentTypeID);
            settings.overrideVoxelSize = true;
            settings.voxelSize = .16f;
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, bounds.size), center, Quaternion.identity);
            if (!navigation) return false;
            navigationInstance = NavMesh.AddNavMeshData(navigation, center, Quaternion.identity);
            if (!navigationInstance.valid) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = first.agentTypeID, areaMask = NavMesh.AllAreas };
            bool walkable = NavMesh.SamplePosition(center, out _, navSnapRadius, filter);
            Debug.Log($"Opening navigation: sources={sources.Count}, center={center}, walkable={walkable}", this);
            return walkable;
        }

        IEnumerator BindAgentsAfterNavBuild()
        {
            // Give agents one frame after adding the opening NavMesh to register.
            yield return null;
            float deadline = Time.time + navReadyTimeout;
            while (Time.time < deadline)
            {
                bool allBound = true;
                foreach (var slaver in slavers)
                {
                    if (!slaver || !slaver.agent) { allBound = false; continue; }
                    var agent = slaver.agent;
                    if (agent.isOnNavMesh) continue;
                    var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
                    if (!NavMesh.SamplePosition(slaver.transform.position, out var hit, navSnapRadius, filter) ||
                        Vector2.Distance(new Vector2(hit.position.x, hit.position.z),
                            new Vector2(slaver.transform.position.x, slaver.transform.position.z)) > navSnapRadius)
                    { allBound = false; continue; }
                    if (!agent.Warp(hit.position)) allBound = false;
                }
                if (allBound) yield break;
                yield return null;
            }
            Debug.LogError("Opening slavers did not bind to a NavMesh after terrain alignment.", this);
        }

        static bool TryGround(Vector3 position, out float ground)
        {
            ground = 0f;
            bool found = false;
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (!terrain || !terrain.enabled || !terrain.terrainData) continue;
                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (position.x < origin.x || position.x > origin.x + size.x ||
                    position.z < origin.z || position.z > origin.z + size.z) continue;
                float candidate = terrain.SampleHeight(position) + origin.y;
                if (!found || candidate > ground) { ground = candidate; found = true; }
            }
            return found;
        }

        void OnDestroy()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) Destroy(navigation);
        }
    }
}
