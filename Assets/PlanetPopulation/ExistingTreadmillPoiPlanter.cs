using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MyAssets.PlanetTerrain
{
    [DefaultExecutionOrder(-10000)]
    [RequireComponent(typeof(PlanetTerrainTreadmill))]
    public sealed class ExistingTreadmillPoiPlanter : MonoBehaviour
    {
        [Serializable]
        public sealed class Site
        {
            public enum SiteSize { Small, Medium, Large }
            public PlanetTileCoord tile;
            public Vector2 localPosition;
            public float normalizedHeight;
            public int seed;
            public SiteSize size;
        }

        public struct Placement
        {
            public GameObject prefab;
            public Vector2 offset;
            public float yaw;
            public bool vehicle;

            public Placement(GameObject prefab, float x, float z, float yaw = 0f, bool vehicle = false)
            {
                this.prefab = prefab;
                offset = new Vector2(x, z);
                this.yaw = yaw;
                this.vehicle = vehicle;
            }
        }

        public int seed = 42;
        [Range(0f, 1f)] public float tileChance = .3f;
        [Min(8f)] public float siteRadius = 45f;
        [Range(1f, 45f)] public float maximumSiteSlope = 14f;
        [Range(0f, 15f)] public float maximumFlattenHeightDifference = 3f;
        public bool flattenWhenNeeded = true;
        [Min(20f)] public float activationDistance = 600f;
        [Min(30f)] public float deactivationDistance = 800f;
        public OutpostFactionTheme faction;
        [Header("Optional modular building shells")]
        public bool generateModularBuildings;
        public ModularBuildingCatalog modularBuildingCatalog;
        public List<Site> sites = new List<Site>();
        [HideInInspector] public bool terrainPrepared;

        public static List<Placement> CreateLayout(Site site, OutpostFactionTheme faction)
        {
            var result = new List<Placement>();
            if (!faction) return result;
            GameObject Small(int index) => faction.smallBuildings != null && faction.smallBuildings.Length > 0
                ? faction.smallBuildings[index % faction.smallBuildings.Length] : null;
            GameObject Medium(int index) => faction.mediumBuildings != null && faction.mediumBuildings.Length > 0
                ? faction.mediumBuildings[index % faction.mediumBuildings.Length] : Small(index);
            GameObject Prop(int index) => faction.props != null && faction.props.Length > 0
                ? faction.props[index % faction.props.Length] : null;
            if (site.size == Site.SiteSize.Large)
            {
                GameObject Large(int index) => faction.largeBuildings != null && faction.largeBuildings.Length > 0
                    ? faction.largeBuildings[index % faction.largeBuildings.Length] : Medium(index);
                result.Add(new Placement(Large(0), -16f, 8f, 90f));
                result.Add(new Placement(Large(1), 17f, 10f, -90f));
                result.Add(new Placement(Medium(0), -18f, -18f, 135f));
                result.Add(new Placement(Small(0), 18f, -18f, -135f));
                result.Add(new Placement(faction.canopyPrefab, 0f, -7f));
                result.Add(new Placement(faction.antennaPrefab, -31f, 19f));
                result.Add(new Placement(faction.gatePrefab, 0f, -37f));
                result.Add(new Placement(faction.wallPrefab, -27f, 32f));
                result.Add(new Placement(faction.wallPrefab, 27f, 32f));
                result.Add(new Placement(faction.pipePrefab, -10f, -7f, 90f));
                result.Add(new Placement(Prop(0), -8f, -11f));
                result.Add(new Placement(Prop(1), 9f, -11f));
                result.Add(new Placement(faction.pyxol2LeanPrefab, 31f, -27f, 225f, true));
                result.Add(new Placement(faction.junkHoverBikePrefab, -30f, -27f, 135f, true));
                return result;
            }
            bool medium = site.size == Site.SiteSize.Medium;

            // Buildings establish the compact settlement footprint.
            result.Add(new Placement(medium ? Medium(0) : Small(0), -9f, 0f, 90f));
            result.Add(new Placement(Small(1), medium ? 12f : 9f, -6f, -90f));
            if (medium) result.Add(new Placement(Small(2), 11f, 11f, -110f));

            // Clutter touches walls and entrances instead of filling the central path.
            result.Add(new Placement(faction.canopyPrefab, medium ? 3f : 0f, 9f, 0f));
            result.Add(new Placement(faction.antennaPrefab, medium ? -15f : -14f, 8f));
            result.Add(new Placement(Prop(0), -14f, -7f, 20f));
            result.Add(new Placement(Prop(1), -12f, -8f, -12f));
            result.Add(new Placement(Prop(2), medium ? 17f : 14f, -9f, 35f));
            if (medium)
            {
                result.Add(new Placement(Prop(3), 17f, 13f));
                result.Add(new Placement(Prop(4), -3f, 14f));
            }
            result.Add(new Placement(faction.pipePrefab, medium ? 1f : -2f, -1f, 90f));
            result.Add(new Placement(faction.pipePrefab, medium ? 5f : 2f, -1f, 90f));
            if (medium)
            {
                result.Add(new Placement(faction.pipePrefab, 10f, 2f, 0f));
                result.Add(new Placement(faction.pipePrefab, 10f, 6f, 0f));
            }

            // A broken perimeter leaves the entrance and sight lines open.
            result.Add(new Placement(faction.wallPrefab, -19f, 21f));
            result.Add(new Placement(faction.wallPrefab, -11f, 21f));
            result.Add(new Placement(faction.wallPrefab, medium ? 5f : 12f, 21f));
            if (medium)
            {
                result.Add(new Placement(faction.wallPrefab, 13f, 21f));
                result.Add(new Placement(faction.wallPrefab, -24f, 12f, 90f));
                result.Add(new Placement(faction.gatePrefab, 2f, -23f));
            }

            // Parked vehicles are added after the built footprint.
            if (medium) result.Add(new Placement(faction.pyxol2LeanPrefab, 25f, -17f, 225f, true));
            result.Add(new Placement(faction.junkHoverBikePrefab, medium ? -20f : 20f, -17f, 135f, true));
            return result;
        }

        PlanetTerrainTreadmill treadmill;
        readonly Dictionary<long, GameObject> active = new Dictionary<long, GameObject>();
        readonly Dictionary<long, NavMeshDataInstance> navMeshes = new Dictionary<long, NavMeshDataInstance>();
        readonly Dictionary<long, Vector3> navMeshCenters = new Dictionary<long, Vector3>();
        readonly HashSet<long> wanted = new HashSet<long>();
        readonly List<long> removal = new List<long>();
        float nextUpdate;

        void Awake()
        {
            treadmill = GetComponent<PlanetTerrainTreadmill>();
            var preview = transform.Find("Scavenger POIs (Scene Preview)");
            if (preview) preview.gameObject.SetActive(false);
        }

        void OnDrawGizmos()
        {
            var source = GetComponent<PlanetTerrainTreadmill>();
            if (!source || source.AuthoredTileSet == null || sites == null) return;
            Gizmos.color = new Color(1f, .75f, .1f, .9f);
            foreach (var site in sites)
            {
                if (site == null) continue;
                Vector3 center = source.GetTileWorldPosition(site.tile) +
                    new Vector3(site.localPosition.x,
                        site.normalizedHeight * source.AuthoredTileSet.generationSettings.maxHeightMeters,
                        site.localPosition.y);
                Gizmos.DrawWireSphere(center, siteRadius);
                Gizmos.DrawSphere(center, 4f);
            }
        }

        void OnDestroy()
        {
            foreach (var mesh in navMeshes.Values) mesh.Remove();
            navMeshes.Clear();
        }

        void Update()
        {
            if (Time.time < nextUpdate || !treadmill || !treadmill.Target || !treadmill.gameObject.activeInHierarchy) return;
            nextUpdate = Time.time + 1f;
            wanted.Clear();
            var tileSet = treadmill.AuthoredTileSet;
            if (tileSet == null) return;
            var center = treadmill.CenterTile;
            int ring = Mathf.CeilToInt(activationDistance / treadmill.TileSizeMeters) + 2;
            for (int z = center.z - ring; z <= center.z + ring; z++)
                for (int x = center.x - ring; x <= center.x + ring; x++)
                {
                    var logical = new PlanetTileCoord(x, z);
                    var authored = tileSet.WrapCoord(logical);
                    for (int i = 0; i < sites.Count; i++)
                    {
                        var site = sites[i];
                        if (!site.tile.Equals(authored)) continue;
                        Vector3 world = treadmill.GetTileWorldPosition(logical) +
                            new Vector3(site.localPosition.x, site.normalizedHeight * tileSet.generationSettings.maxHeightMeters, site.localPosition.y);
                        if (Vector2.Distance(new Vector2(world.x, world.z), new Vector2(treadmill.Target.position.x, treadmill.Target.position.z)) > activationDistance) continue;
                        if (!TryGround(world, out float groundY)) continue;
                        world.y = groundY;
                        long key = ((long)x << 32) ^ (uint)z ^ ((long)i << 48);
                        wanted.Add(key);
                        if (active.TryGetValue(key, out var existing) && existing && navMeshCenters.TryGetValue(key, out var bakedCenter) &&
                            (world - bakedCenter).sqrMagnitude > 4f)
                        {
                            if (navMeshes.TryGetValue(key, out var oldMesh)) { oldMesh.Remove(); navMeshes.Remove(key); }
                            navMeshCenters.Remove(key);
                            Destroy(existing);
                            active.Remove(key);
                        }
                        if (!active.ContainsKey(key)) active.Add(key, BuildSite(site, world, key));
                    }
                }
            removal.Clear();
            foreach (var pair in active)
                if (!wanted.Contains(pair.Key) && (!pair.Value ||
                    Vector2.Distance(new Vector2(pair.Value.transform.position.x, pair.Value.transform.position.z),
                        new Vector2(treadmill.Target.position.x, treadmill.Target.position.z)) > deactivationDistance))
                    removal.Add(pair.Key);
            foreach (long key in removal)
            {
                if (navMeshes.TryGetValue(key, out var mesh)) { mesh.Remove(); navMeshes.Remove(key); }
                navMeshCenters.Remove(key);
                if (active[key]) Destroy(active[key]);
                active.Remove(key);
            }
        }

        GameObject BuildSite(Site site, Vector3 center, long key)
        {
            var preview = transform.Find("Scavenger POIs (Scene Preview)");
            int siteIndex = sites.IndexOf(site);
            bool authored = preview && siteIndex >= 0 && siteIndex < preview.childCount;
            if (authored && generateModularBuildings)
                Debug.LogWarning($"POI {site.seed} uses an authored scene-preview layout; modular replacement currently applies only to procedural building slots.", this);
            GameObject root = authored
                ? Instantiate(preview.GetChild(siteIndex).gameObject, center, Quaternion.identity, transform)
                : new GameObject("Scavenger POI " + site.seed);
            if (!authored)
            {
                root.transform.SetParent(transform, true);
                root.transform.position = center;
                foreach (var placement in CreateLayout(site, faction))
                {
                    if (!placement.prefab) continue;
                    Vector3 position = center + new Vector3(placement.offset.x, 0, placement.offset.y);
                    position.y = Ground(position);
                    bool buildingSlot = faction && ((Array.IndexOf(faction.smallBuildings ?? Array.Empty<GameObject>(), placement.prefab) >= 0) ||
                        (Array.IndexOf(faction.mediumBuildings ?? Array.Empty<GameObject>(), placement.prefab) >= 0) ||
                        (Array.IndexOf(faction.largeBuildings ?? Array.Empty<GameObject>(), placement.prefab) >= 0));
                    if (buildingSlot && generateModularBuildings && modularBuildingCatalog)
                    {
                        var plot = new ModularBuildingGenerator.Plot { center = position, maximumWidth = 16, maximumDepth = 16,
                            roadPosition = center, seed = unchecked(site.seed ^ Mathf.RoundToInt(placement.offset.x * 37 + placement.offset.y * 101)) };
                        if (ModularBuildingGenerator.TryPlan(plot, modularBuildingCatalog,
                            p => TryGround(p, out float h) ? (float?)h : null, out var blueprint, out string warning))
                        {
                            if (ModularBuildingGenerator.Instantiate(blueprint, modularBuildingCatalog, plot.seed, root.transform)) continue;
                            warning = "Roof module axes, scale metadata, or selected prefab are invalid.";
                        }
                        Debug.LogWarning($"Modular building at POI {site.seed}: {warning} Existing prefab retained.", this);
                    }
                    var instance = Instantiate(placement.prefab, position,
                        Quaternion.Euler(0, placement.yaw, 0), root.transform);
                    if (placement.vehicle) LiftVehicleToGround(instance, position.y);
                }
            }
            root.name = $"Scavenger POI {site.seed} ({site.size})";
            if (!faction || !faction.npcPrefab || NavMesh.GetSettingsCount() == 0) return root;
            var sources = new List<NavMeshBuildSource>();
            foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
            {
                if (!terrain.enabled || !terrain.terrainData) continue;
                Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                if (origin.x > center.x + siteRadius || origin.x + size.x < center.x - siteRadius ||
                    origin.z > center.z + siteRadius || origin.z + size.z < center.z - siteRadius) continue;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain,
                    sourceObject = terrain.terrainData, transform = terrain.transform.localToWorldMatrix, area = 0 });
            }
            if (sources.Count == 0) return root;
            int agentType = faction.npcPrefab.TryGetComponent<NavMeshAgent>(out var agent)
                ? agent.agentTypeID : NavMesh.GetSettingsByIndex(0).agentTypeID;
            var navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(agentType), sources,
                new Bounds(center, new Vector3(siteRadius * 3f, 80f, siteRadius * 3f)), Vector3.zero, Quaternion.identity);
            if (!navData) return root;
            navMeshes[key] = NavMesh.AddNavMeshData(navData);
            navMeshCenters[key] = center;
            var patrol = new Transform[4];
            for (int i = 0; i < patrol.Length; i++)
            {
                float angle = i * Mathf.PI * .5f;
                Vector3 position = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 9f;
                position.y = Ground(position);
                var marker = new GameObject("Patrol " + i).transform;
                marker.SetParent(root.transform); marker.position = position; patrol[i] = marker;
            }
            var authoredNpcs = authored ? root.GetComponentsInChildren<ScavengerNPC.ScavengerEnemy>(true) : Array.Empty<ScavengerNPC.ScavengerEnemy>();
            if (authoredNpcs.Length > 0)
            {
                for (int i = 0; i < authoredNpcs.Length; i++)
                {
                    var npc = authoredNpcs[i];
                    if (NavMesh.SamplePosition(npc.transform.position + Vector3.up * .5f, out var hit, 3f, NavMesh.AllAreas))
                    {
                        var npcAgent = npc.GetComponent<NavMeshAgent>();
                        if (npcAgent && npcAgent.enabled) npcAgent.Warp(hit.position);
                        else npc.transform.position = hit.position;
                    }
                    ConfigureNpc(npc, patrol, i, root.transform);
                }
            }
            else for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * .5f + .4f;
                Vector3 position = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 7f;
                position.y = Ground(position);
                if (!NavMesh.SamplePosition(position + Vector3.up * .5f, out var hit, 2f, NavMesh.AllAreas) ||
                    Mathf.Abs(hit.position.y - position.y) > 1.5f) continue;
                var npc = Instantiate(faction.npcPrefab, hit.position, Quaternion.identity, root.transform);
                ConfigureNpc(npc, patrol, i, root.transform);
            }
            return root;
        }

        static void ConfigureNpc(ScavengerNPC.ScavengerEnemy npc, Transform[] patrol, int index, Transform parent)
        {
            npc.patrolPoints = patrol;
            npc.idleAction = index % 4 == 0 ? ScavengerNPC.ScavengerEnemy.Action.Patrol :
                index % 4 == 1 ? ScavengerNPC.ScavengerEnemy.Action.Squatting :
                index % 4 == 2 ? ScavengerNPC.ScavengerEnemy.Action.Standing : ScavengerNPC.ScavengerEnemy.Action.Sitting;
            if (index % 4 == 3 && !npc.seat)
            {
                var seat = new GameObject("Seat").transform;
                seat.SetParent(parent); seat.position = npc.transform.position;
                npc.seat = seat;
            }
        }

        bool TryGround(Vector3 position, out float groundY)
        {
            foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
            {
                if (!terrain.enabled || !terrain.gameObject.activeInHierarchy || !terrain.terrainData) continue;
                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (position.x >= origin.x && position.x <= origin.x + size.x &&
                    position.z >= origin.z && position.z <= origin.z + size.z)
                {
                    groundY = terrain.SampleHeight(position) + origin.y;
                    return true;
                }
            }
            groundY = position.y;
            return false;
        }

        float Ground(Vector3 position) => TryGround(position, out float height) ? height : position.y;

        public static void LiftVehicleToGround(GameObject vehicle, float groundY)
        {
            if (!vehicle) return;
            var renderers = vehicle.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            float bottom = float.PositiveInfinity;
            foreach (var renderer in renderers)
                if (renderer.enabled && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    bottom = Mathf.Min(bottom, renderer.bounds.min.y);
            if (!float.IsPositiveInfinity(bottom))
                vehicle.transform.position += Vector3.up * (groundY - bottom + .25f);
        }

        static uint Hash(int value, int salt)
        {
            uint x = (uint)value ^ ((uint)salt * 0x9e3779b9u);
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu;
            return x ^ (x >> 16);
        }
    }
}
