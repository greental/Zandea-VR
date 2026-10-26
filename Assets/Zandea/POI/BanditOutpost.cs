using System;
using System.Collections.Generic;
using JunkerBody;
using KenshiVR.MeleeCombat;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;
using Zandea.Props;

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
        Transform authoredLayout;
        Quaternion authoredRotation;
        bool hasAuthoredProps;
        readonly List<PhysicalProp> authoredProps = new();
        OutpostPropPopulation propPopulation;
        NavMeshData navigation;
        NavMeshDataInstance navigationInstance;
        bool failed, shifted, surfaceAligned;
        Transform surfaceShipTarget;
        VRBodyController walkingPlayer;
        float nextCheck;
        public Vector3 Center => treadmill.GetTileWorldPosition(new PlanetTileCoord(0, 0)) + new Vector3(stableSite.x, siteHeight, stableSite.y);
        public Quaternion Rotation => authoredLayout ? authoredRotation : Quaternion.Euler(0, definition.gateQuarterTurns * 90, 0);
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
        public void InvalidateLayout() { Layout = null; }
        void Awake()
        {
            var generatedPreview = transform.Find("Generated Layout Preview");
            if (generatedPreview)
            {
                generatedPreview.gameObject.SetActive(false);
                Destroy(generatedPreview.gameObject);
            }
            if (!treadmill) treadmill = FindFirstObjectByType<PlanetTerrainTreadmill>();
            try
            {
                EnsureLayout();
                if (!treadmill || !siteSelected) throw new InvalidOperationException("Select and validate an outpost site in the editor before playing.");
                // Older Main scenes saved this component disabled, so revealing the
                // terrain root after landing left the POI with no streaming updates.
                if (!treadmill.enabled) treadmill.enabled = true;
                propPopulation = GetComponent<OutpostPropPopulation>();
                if (!propPopulation) propPopulation = gameObject.AddComponent<OutpostPropPopulation>();
                treadmill.PrepareTerrainTile += Prepare;
                treadmill.CenterTileChanged += OnCenterTileChanged;
                var preview = transform.Find("Layout Preview");
                if (preview)
                {
                    authoredLayout = preview;
                    authoredRotation = preview.rotation;
                    AdoptAuthoredSite(preview.position);
                    CollectSceneAuthoringProps(preview);
                    authoredProps.AddRange(preview.GetComponentsInChildren<PhysicalProp>());
                    foreach (var prop in authoredProps)
                    {
                        var grab = prop.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                        if (grab) grab.retainTransformParent = false;
                    }
                    preview.gameObject.SetActive(false);
                }
            }
            catch (Exception e) { Fail(e); }
        }
        void Prepare(PlanetTileCoord tile, TerrainData data)
        {
            // The Main layout was fitted by hand to the authored terrain's slope.
            // A procedural plateau buries its downhill walls and leaves roof pieces floating.
            // Pueblo's authored layout was arranged on the level review terrain.
            // It still needs its deterministic stamp when streamed into Main.
            if (!authoredLayout || poiId.StartsWith("zandea.pueblo.", StringComparison.Ordinal))
                OutpostTerrain.Stamp(treadmill, definition, stableSite, siteHeight, tile, data, Rotation);
        }
        void AdoptAuthoredSite(Vector3 position)
        {
            // Scene terrain can be translated far from the runtime tile frame.
            // Resolve the authored tile by its data, then retain the local offset.
            var tiles = treadmill.AuthoredTileSet;
            if (!tiles || tiles.authoredTiles == null) return;
            foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
            {
                if (!terrain.terrainData) continue;
                var local = position - terrain.transform.position;
                var size = terrain.terrainData.size;
                if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z) continue;
                // Wrapped TerrainData can occur at several logical coordinates.
                // Prefer the scene tile identity over the shared asset identity.
                const string prefix = "PT_AuthoredTile_x";
                var coordinates = terrain.name.StartsWith(prefix)
                    ? terrain.name.Substring(prefix.Length).Split(new[] { "_z" }, StringSplitOptions.None) : Array.Empty<string>();
                if (coordinates.Length == 2 && int.TryParse(coordinates[0], out int x) && int.TryParse(coordinates[1], out int z))
                {
                    stableSite = new Vector2(x * treadmill.TileSizeMeters + local.x, z * treadmill.TileSizeMeters + local.z);
                    siteHeight = local.y;
                    return;
                }
                foreach (var tile in tiles.authoredTiles)
                    if (tile != null && tile.terrainData == terrain.terrainData)
                    {
                        stableSite = new Vector2(tile.coord.x * treadmill.TileSizeMeters + local.x,
                            tile.coord.z * treadmill.TileSizeMeters + local.z);
                        siteHeight = local.y;
                        return;
                    }
            }
            Debug.LogWarning("Authored outpost has no matching scene terrain tile; retaining its saved stable site.", this);
        }

        void CollectSceneAuthoringProps(Transform preview)
        {
            hasAuthoredProps = preview.GetComponentInChildren<PhysicalProp>(true) || preview.GetComponentInChildren<BreakableProp>(true);
            if (gameObject.scene.path != "Assets/Main.unity") return;
            // Bring the existing hand-arranged props along with the buildings.
            foreach (var sceneRoot in gameObject.scene.GetRootGameObjects())
            {
                if (sceneRoot == gameObject) continue;
                string name = sceneRoot.name;
                if (!(name.StartsWith("Adobe") || name.StartsWith("GateSide") ||
                      sceneRoot.GetComponent<BanditGroup>() ||
                      name.StartsWith("pot") || name.StartsWith("sack") || name.StartsWith("barrel") ||
                      name.StartsWith("crate") || name.StartsWith("chest") || name.StartsWith("stool") ||
                      name.StartsWith("table") || name.StartsWith("rack") || name.StartsWith("statue") ||
                      sceneRoot.GetComponent<PhysicalProp>() || sceneRoot.GetComponent<BreakableProp>())) continue;
                var delta = sceneRoot.transform.position - preview.position;
                if (new Vector2(delta.x, delta.z).magnitude < 80 && Mathf.Abs(delta.y) < 20)
                {
                    sceneRoot.transform.SetParent(preview, true);
                    if (sceneRoot.GetComponent<PhysicalProp>() || sceneRoot.GetComponent<BreakableProp>())
                        hasAuthoredProps = true;
                }
            }
        }
        void OnCenterTileChanged(PlanetTileCoord previous, PlanetTileCoord current, PlanetTileCoord delta)
        {
            // The treadmill can recycle the ground beneath this outpost in its Update.
            // Retire its physics objects in the same frame, before the next physics step.
            if (structures && stream && !SiteTileReady()) Unload();
        }
        void Update()
        {
            AlignStandaloneSurfaceToPlayer();
            FollowWalkingPlayer();
            if (failed || !treadmill || Time.time < nextCheck) return;
            nextCheck = Time.time + .5f;
            try
            {
                foreach (var npc in members) if (npc && npc.staff) encounterWeapons.Add(npc.staff);
                if (shifted && structures) { RebindNavigation(); shifted = false; }
                // The authored settlement belongs to its terrain tile. A visible tile
                // must never leave the POI absent just because the observer is far away.
                bool tileReady = SiteTileReady();
                if (!structures && tileReady) Load();
                else if (structures && stream && !tileReady) Unload();
            }
            catch (Exception e) { Fail(e); }
        }
        void AlignStandaloneSurfaceToPlayer()
        {
            if (surfaceAligned || !treadmill || gameObject.scene.path != "Assets/Main.unity") return;
            if (FindFirstObjectByType<PlanetTerrainTransitionController>(FindObjectsInactive.Include))
            { surfaceAligned = true; return; } // Koombar's landing controller owns that frame.
            if (!walkingPlayer) walkingPlayer = FindFirstObjectByType<VRBodyController>();
            if (!walkingPlayer || !walkingPlayer.xrOrigin) return;
            var playerPosition = walkingPlayer.xrOrigin.position;
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (!terrain || !terrain.enabled || !terrain.transform.IsChildOf(treadmill.transform) || !terrain.terrainData) continue;
                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (playerPosition.x < origin.x || playerPosition.x > origin.x + size.x ||
                    playerPosition.z < origin.z || playerPosition.z > origin.z + size.z) continue;
                surfaceAligned = true;
                float ground = terrain.SampleHeight(playerPosition) + origin.y;
                float correction = playerPosition.y - ground;
                if (Mathf.Abs(correction) > 30f)
                {
                    treadmill.SetTileVerticalOffset(treadmill.TileVerticalOffsetMeters + correction);
                    Debug.Log($"[BanditOutpost] Aligned standalone terrain and POI to player start by {correction:F1} m.", this);
                }
                return;
            }
        }
        bool SiteTileReady() => treadmill && TerrainAt(Center);
        void FollowWalkingPlayer()
        {
            if (!treadmill || !treadmill.gameObject.activeInHierarchy || !treadmill.Target) return;
            if (!walkingPlayer) walkingPlayer = FindFirstObjectByType<VRBodyController>();
            if (!walkingPlayer || !walkingPlayer.xrOrigin) return;
            var playerTarget = walkingPlayer.xrOrigin;
            if (treadmill.Target != playerTarget) surfaceShipTarget = treadmill.Target;
            if (!surfaceShipTarget) return;
            var separation = Vector2.Distance(
                new Vector2(playerTarget.position.x, playerTarget.position.z),
                new Vector2(surfaceShipTarget.position.x, surfaceShipTarget.position.z));
            var desired = separation > 10f ? playerTarget : surfaceShipTarget;
            if (treadmill.Target != desired) treadmill.ConfigureTarget(desired);
        }
        bool GroundReady()
        {
            if (authoredLayout)
            {
                // Authored foundations follow a slope, so require live terrain beneath
                // the full footprint without comparing every sample to one flat height.
                foreach (Transform child in authoredLayout)
                {
                    if (!TerrainAt(child.position)) return false;
                }
                return true;
            }
            foreach (var p in new[] { Vector2.zero, definition.dimensions / 2, -definition.dimensions / 2,
                new Vector2(definition.dimensions.x / 2, -definition.dimensions.y / 2), new Vector2(-definition.dimensions.x / 2, definition.dimensions.y / 2) })
            {
                var world = ToWorld(p);
                bool found = false;
                foreach (var terrain in Terrain.activeTerrains)
                {
                    if (!terrain || !terrain.enabled) continue;
                    var terrainCollider = terrain.GetComponent<TerrainCollider>();
                    if (!terrainCollider || !terrainCollider.enabled || terrainCollider.terrainData != terrain.terrainData) continue;
                    var o = terrain.transform.position; var s = terrain.terrainData.size;
                    if (world.x >= o.x && world.x <= o.x + s.x && world.z >= o.z && world.z <= o.z + s.z &&
                        Mathf.Abs(terrain.SampleHeight(world) + o.y - Center.y) < .2f) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }
        bool TerrainAt(Vector3 world)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (!terrain || !terrain.enabled) continue;
                var collider = terrain.GetComponent<TerrainCollider>();
                if (!collider || !collider.enabled || collider.terrainData != terrain.terrainData) continue;
                var o = terrain.transform.position;
                var s = terrain.terrainData.size;
                if (world.x >= o.x && world.x <= o.x + s.x && world.z >= o.z && world.z <= o.z + s.z) return true;
            }
            return false;
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
            structures = authoredLayout ? authoredLayout.gameObject : InstantiateStructures("Bandit Outpost Structures");
            structures.transform.SetPositionAndRotation(Center, Rotation);
            RepairLodAlignment(structures);
            foreach (var lid in structures.GetComponentsInChildren<Zandea.Props.PropHingedLid>(true)) lid.gameObject.SetActive(true);
            foreach (var stair in structures.GetComponentsInChildren<Transform>(true))
                if (stair.name.StartsWith("AdobeStairs") && !stair.GetComponent<OutpostStairRamp>())
                    stair.gameObject.AddComponent<OutpostStairRamp>();
            // The outpost bakes terrain and stair colliders together. A group's
            // separate terrain-only NavMesh would overlap the lower stair foot
            // without connecting to the ramp, stranding agents on that island.
            foreach (var group in structures.GetComponentsInChildren<BanditGroup>(true))
                group.buildLocalNavMesh = false;
            structures.SetActive(true);
            foreach (var prop in authoredProps)
                if (prop && !(prop.GetComponent<BreakableProp>()?.IsBroken ?? false))
                {
                    prop.SetDormant(false);
                    foreach (var lid in prop.GetComponentsInChildren<Zandea.Props.PropHingedLid>(true)) lid.gameObject.SetActive(true);
                }
            if (!hasAuthoredProps) propPopulation.Load(this);
            Physics.SyncTransforms();
            BuildNavigation();
            foreach (var stair in structures.GetComponentsInChildren<OutpostStairRamp>(true))
                stair.ConnectNavigation();
            if (definition.residentBandits == 0) return;
            if (authoredLayout && authoredLayout.GetComponentsInChildren<StarvingBandit>(true).Length > 0)
            {
                members.Clear();
                members.AddRange(authoredLayout.GetComponentsInChildren<StarvingBandit>(true));
                return;
            }
            // Validate the complete encounter before allowing any NPC Awake/Start.
            var points = new List<Vector3>();
            var filter = new NavMeshQueryFilter { agentTypeID = definition.bandit.GetComponent<NavMeshAgent>().agentTypeID, areaMask = NavMesh.AllAreas };
            foreach (var p in Layout.spawns)
            {
                if (!NavMesh.SamplePosition(ToWorld(p), out var hit, authoredLayout ? 8f : 1.2f, filter) || Mathf.Abs(hit.position.y - Center.y) > .4f)
                    throw new InvalidOperationException("Outpost spawn is not on walkable ground: " + p);
                points.Add(hit.position);
            }
            var patrol = new List<Vector3>();
            foreach (var p in Layout.patrol)
            {
                Vector3 destination = ToWorld(p);
                if (authoredLayout && NavMesh.SamplePosition(destination, out var walkable, 8f, filter)) destination = walkable.position;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(points[0], destination, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    if (authoredLayout) continue;
                    throw new InvalidOperationException("Outpost patrol is unreachable: " + p);
                }
                patrol.Add(destination);
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
                    foreach (var p in (i == 2 || i == 3) && patrol.Count > 0 ? patrol : new List<Vector3> { points[i] })
                    {
                        var marker = new GameObject("Patrol waypoint").transform;
                        marker.SetParent(encounter.transform, false);
                        marker.position = p;
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
        static void RepairLodAlignment(GameObject root)
        {
            foreach(var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var levels=group.GetLODs();if(levels.Length<2||levels[0].renderers.Length==0)continue;
                Bounds reference=levels[0].renderers[0].bounds;
                for(int i=1;i<levels.Length;i++)foreach(var renderer in levels[i].renderers)
                {
                    if(!renderer)continue;
                    Vector3 delta=reference.center-renderer.bounds.center;
                    if(delta.sqrMagnitude>.0025f)renderer.transform.position+=delta;
                }
                group.RecalculateBounds();group.ForceLOD(-1);
            }
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
            if (propPopulation) propPopulation.Unload();
            foreach (var prop in authoredProps)
            {
                if (!prop || prop.IsHeld) continue;
                var delta = prop.transform.position - Center;
                if (new Vector2(delta.x, delta.z).magnitude < definition.unloadDistance) prop.SetDormant(true);
            }
            // Cache only six inactive NPCs, preserving complete injury/equipment/ragdoll state.
            foreach (var weapon in encounterWeapons)
                if (weapon && encounter && weapon.State == PhysicalMeleeWeapon.HoldState.World &&
                    Vector3.Distance(weapon.transform.position, Center) < definition.unloadDistance)
                    weapon.transform.SetParent(encounter.transform, true);
            if (encounter) encounter.SetActive(false);
            if (structures)
            {
                if (authoredLayout && structures == authoredLayout.gameObject) structures.SetActive(false);
                else Destroy(structures);
            }
            structures = null;
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) Destroy(navigation);
            navigation = null;
        }
        void Fail(Exception e) { failed = true; Unload(); Debug.LogError("[Zandea Outpost] " + e, this); }
        void OnDestroy()
        {
            if (treadmill)
            {
                treadmill.PrepareTerrainTile -= Prepare;
                treadmill.CenterTileChanged -= OnCenterTileChanged;
            }
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
