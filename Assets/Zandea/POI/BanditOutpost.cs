using System;
using System.Collections.Generic;
using KenshiVR.MeleeCombat;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;

namespace Zandea.POI
{
    [DefaultExecutionOrder(-9000)]
    public sealed class BanditOutpost : MonoBehaviour, IFloatingOriginListener
    {
        public BanditOutpostDefinition definition;
        public PlanetTerrainTreadmill treadmill;
        public string poiId = "zandea.frontier.outpost.1";
        public int seed = 4301;
        public Vector2 stableSite;
        public float siteHeight;
        public bool siteSelected;
        public bool drawGizmos = true;
        public bool stream = true;
        public OutpostLayout Layout { get; private set; }
        public IReadOnlyList<StarvingBandit> Members => members;
        public bool IsLoaded => structures;
        readonly List<StarvingBandit> members = new List<StarvingBandit>();
        readonly List<BanditGroup> groups = new List<BanditGroup>();
        readonly HashSet<PhysicalMeleeWeapon> encounterWeapons = new HashSet<PhysicalMeleeWeapon>();
        readonly Dictionary<StarvingBandit, Vector3> shiftedAgents = new Dictionary<StarvingBandit, Vector3>();
        GameObject structures, encounter;
        NavMeshData navigation;
        NavMeshDataInstance navigationInstance;
        bool failed, shifted;
        float nextCheck;
        public Vector3 Center => treadmill.GetTileWorldPosition(new PlanetTileCoord(0, 0)) + new Vector3(stableSite.x, siteHeight, stableSite.y);
        public Quaternion Rotation => Quaternion.Euler(0, definition.gateQuarterTurns * 90, 0);
        public Vector3 GatePosition => ToWorld(Layout.gate);
        public Vector3 ApproachDirection => Rotation * Vector3.back;
        public Vector3 ToWorld(Vector2 local) => Center + Rotation * new Vector3(local.x, 0, local.y);
        public bool IsReserved(double stableX, double stableZ, float radius = 0)
        {
            EnsureLayout();
            var p = Quaternion.Inverse(Rotation) * new Vector3((float)(stableX - stableSite.x), 0, (float)(stableZ - stableSite.y));
            foreach (var r in Layout.reservations) if (r.Contains(new Vector2(p.x, p.z), radius)) return true;
            return false;
        }
        public void EnsureLayout() { if (Layout == null) Layout = OutpostLayout.Generate(definition, seed); }
        void Awake()
        {
            if (!treadmill) treadmill = FindFirstObjectByType<PlanetTerrainTreadmill>();
            try
            {
                EnsureLayout();
                if (!treadmill || !siteSelected) throw new InvalidOperationException("Select and validate an outpost site in the editor before playing.");
                treadmill.PrepareTerrainTile += Prepare;
                var preview = transform.Find("Layout Preview");
                if (preview) preview.gameObject.SetActive(false);
            }
            catch (Exception e) { Fail(e); }
        }
        void Prepare(PlanetTileCoord tile, TerrainData data) => OutpostTerrain.Stamp(treadmill, definition, stableSite, siteHeight, tile, data);
        void Update()
        {
            if (failed || !treadmill || !treadmill.Target || Time.time < nextCheck) return;
            nextCheck = Time.time + .5f;
            try
            {
                foreach (var npc in members) if (npc && npc.staff) encounterWeapons.Add(npc.staff);
                if (shifted && structures) { RebindNavigation(); shifted = false; }
                float distance = Vector2.Distance(new Vector2(treadmill.Target.position.x, treadmill.Target.position.z), new Vector2(Center.x, Center.z));
                if (!structures && (!stream || distance < definition.loadDistance) && GroundReady()) Load();
                else if (structures && stream && distance > definition.unloadDistance) Unload();
            }
            catch (Exception e) { Fail(e); }
        }
        bool GroundReady()
        {
            foreach (var p in new[] { Vector2.zero, definition.dimensions / 2, -definition.dimensions / 2,
                new Vector2(definition.dimensions.x / 2, -definition.dimensions.y / 2), new Vector2(-definition.dimensions.x / 2, definition.dimensions.y / 2) })
            {
                var world = ToWorld(p);
                bool found = false;
                foreach (var terrain in Terrain.activeTerrains)
                {
                    var o = terrain.transform.position; var s = terrain.terrainData.size;
                    if (world.x >= o.x && world.x <= o.x + s.x && world.z >= o.z && world.z <= o.z + s.z &&
                        Mathf.Abs(terrain.SampleHeight(world) + o.y - Center.y) < .2f) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }
        public GameObject InstantiateStructures(string name)
        {
            EnsureLayout();
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(Center, Rotation);
            foreach (var p in Layout.structures)
            {
                var instance = Instantiate(p.asset.prefab, root.transform);
                instance.name = p.kind + " - " + p.asset.prefab.name;
                instance.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                instance.transform.localScale = Vector3.Scale(p.asset.prefab.transform.localScale, p.scale);
                var offset = new Vector3(p.asset.center.x, p.asset.center.y - p.asset.size.y / 2, p.asset.center.z);
                instance.transform.localPosition = new Vector3(p.position.x, 0, p.position.y) -
                    instance.transform.localRotation * Vector3.Scale(offset, p.scale);
            }
            return root;
        }
        void Load()
        {
            structures = InstantiateStructures("Bandit Outpost Structures");
            Physics.SyncTransforms();
            BuildNavigation();
            // Validate the complete encounter before allowing any NPC Awake/Start.
            var points = new List<Vector3>();
            var filter = new NavMeshQueryFilter { agentTypeID = definition.bandit.GetComponent<NavMeshAgent>().agentTypeID, areaMask = NavMesh.AllAreas };
            foreach (var p in Layout.spawns)
            {
                if (!NavMesh.SamplePosition(ToWorld(p), out var hit, 1.2f, filter) || Mathf.Abs(hit.position.y - Center.y) > .4f)
                    throw new InvalidOperationException("Outpost spawn is not on walkable ground: " + p);
                points.Add(hit.position);
            }
            foreach (var p in Layout.patrol)
            {
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(points[0], ToWorld(p), filter, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Outpost patrol is unreachable: " + p);
            }
            if (!encounter)
            {
                encounter = new GameObject("Encounter State - " + poiId);
                encounter.transform.SetParent(transform, false);
                encounter.transform.position = Center;
                encounter.SetActive(false);
                for (int i = 0; i < 6; i++)
                {
                    var npc = Instantiate(definition.bandit, points[i], Rotation, encounter.transform);
                    npc.name = poiId + "/bandit/" + i;
                    members.Add(npc);
                    // Reuse existing sensing, engagement, melee and health behavior.
                    var groupObject = new GameObject("Guard role " + i);
                    groupObject.transform.SetParent(encounter.transform, false);
                    groupObject.transform.position = points[i];
                    var group = groupObject.AddComponent<BanditGroup>();
                    group.banditPrefab = definition.bandit;
                    group.authoredMembers = new[] { npc };
                    group.groupSize = 1;
                    group.buildLocalNavMesh = false;
                    group.patrolRadius = (i == 2 || i == 3) ? 9 : 2;
                    group.patrolWalkSpeed = definition.patrolWalkSpeed;
                    group.player = Camera.main ? Camera.main.transform : treadmill.Target;
                    var route = new List<Transform>();
                    foreach (var p in (i == 2 || i == 3) ? Layout.patrol : new List<Vector2> { Layout.spawns[i] })
                    {
                        var marker = new GameObject("Patrol waypoint").transform;
                        marker.SetParent(encounter.transform, false);
                        marker.position = ToWorld(p);
                        route.Add(marker);
                    }
                    group.patrolPoints = route.ToArray();
                    groups.Add(group);
                }
            }
            encounter.SetActive(true);
            foreach (var npc in members)
                if (npc && npc.agent && npc.agent.enabled && (!npc.health || (!npc.health.IsDead && !npc.health.IsUnconscious)))
                    npc.agent.Warp(npc.transform.position);
        }
        void BuildNavigation()
        {
            var sources = new List<NavMeshBuildSource>();
            var worldBounds = new Bounds(Center, new Vector3(definition.dimensions.magnitude + 40, 60, definition.dimensions.magnitude + 40));
            NavMeshBuilder.CollectSources(worldBounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            // Exclude unrelated encounters and moving bodies; preserve static terrain and structure colliders.
            sources.RemoveAll(s => s.component && (s.component.GetComponentInParent<StarvingBandit>() || s.component.GetComponentInParent<Rigidbody>()));
            var settings = NavMesh.GetSettingsByID(definition.bandit.GetComponent<NavMeshAgent>().agentTypeID);
            settings.overrideVoxelSize = true; settings.voxelSize = .16f;
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, worldBounds.size), Center, Quaternion.identity);
            if (!navigation) throw new InvalidOperationException("Local outpost NavMesh bake failed.");
            navigationInstance = NavMesh.AddNavMeshData(navigation, Center, Quaternion.identity);
        }
        void RebindNavigation()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) navigationInstance = NavMesh.AddNavMeshData(navigation, Center, Quaternion.identity);
            foreach (var pair in shiftedAgents)
            {
                var npc = pair.Key;
                if (!npc || !npc.agent || !npc.gameObject.activeInHierarchy) continue;
                npc.transform.position = pair.Value;
                npc.agent.enabled = true;
                if (!npc.agent.Warp(pair.Value)) throw new InvalidOperationException($"Failed to restore shifted bandit navigation. NPC={pair.Value} Center={Center} dataPosition={navigation.position} centerOnMesh={NavMesh.SamplePosition(Center, out var sample, 5, NavMesh.AllAreas)} sample={sample.position}");
            }
            shiftedAgents.Clear();
        }
        public void OnOriginShift(Vector3 shift)
        {
            shifted = true; nextCheck = 0;
            // Multiple shifts can occur before Update (for example initial recenter plus an explicit shift).
            foreach (var npc in new List<StarvingBandit>(shiftedAgents.Keys)) shiftedAgents[npc] -= shift;
            foreach (var npc in members)
            {
                if (!npc) continue;
                npc.groupDestination -= shift;
                if (!npc.agent || !npc.agent.enabled || !npc.gameObject.activeInHierarchy || npc.health && (npc.health.IsDead || npc.health.IsUnconscious)) continue;
                shiftedAgents[npc] = npc.transform.position;
                npc.agent.enabled = false;
            }
        }
        void Unload()
        {
            // Cache only six inactive NPCs, preserving complete injury/equipment/ragdoll state.
            foreach (var weapon in encounterWeapons)
                if (weapon && encounter && weapon.State == PhysicalMeleeWeapon.HoldState.World &&
                    Vector3.Distance(weapon.transform.position, Center) < definition.unloadDistance)
                    weapon.transform.SetParent(encounter.transform, true);
            if (encounter) encounter.SetActive(false);
            if (structures) Destroy(structures);
            structures = null;
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) Destroy(navigation);
            navigation = null;
        }
        void Fail(Exception e) { failed = true; Unload(); Debug.LogError("[Zandea Outpost] " + e, this); }
        void OnDestroy()
        {
            if (treadmill) treadmill.PrepareTerrainTile -= Prepare;
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) Destroy(navigation);
        }
        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !definition || !treadmill || !siteSelected) return;
            try { EnsureLayout(); } catch { return; }
            Gizmos.matrix = Matrix4x4.TRS(Center, Rotation, Vector3.one);
            foreach (var r in Layout.reservations)
            {
                Gizmos.color = r.kind == ReservationKind.FutureProp ? Color.magenta : r.kind == ReservationKind.Path || r.kind == ReservationKind.Approach ? Color.cyan : Color.yellow;
                Gizmos.matrix = Matrix4x4.TRS(ToWorld(r.center), Rotation * Quaternion.Euler(0, r.yaw, 0), Vector3.one);
                Gizmos.DrawWireCube(Vector3.up * .2f, new Vector3(r.size.x, .4f, r.size.y));
            }
            Gizmos.matrix = Matrix4x4.identity; Gizmos.color = Color.red;
            foreach (var p in Layout.spawns) Gizmos.DrawWireSphere(ToWorld(p) + Vector3.up, .6f);
        }
    }
}
