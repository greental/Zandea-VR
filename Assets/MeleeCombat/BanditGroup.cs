using System.Collections.Generic;
using JunkerBody;
using KenshiVR.CharacterCreator;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;

namespace KenshiVR.MeleeCombat
{
    /// <summary>Spawns a patrol around this object. The first living bandit leads.</summary>
    public sealed class BanditGroup : MonoBehaviour, MyAssets.Planet.IFloatingOriginListener
    {
        [Header("Spawn")]
        public StarvingBandit banditPrefab;
        [Tooltip("Optional complete bandit prefabs with different weapons. Each must have StarvingBandit, NavMeshAgent and the same character rig.")]
        public StarvingBandit[] weaponVariants;
        [Min(1)] public int groupSize = 4;
        [Tooltip("Scene bandits spawned from the Inspector. When set, these are used instead of runtime clones.")]
        public StarvingBandit[] authoredMembers;
        [Min(0.5f)] public float spawnRadius = 4f;
        [Min(0.1f)] public float navMeshSampleRadius = 3f;
        [Tooltip("Build navigation around this group from the active terrain tiles at runtime. Useful for the planet treadmill.")]
        public bool buildLocalNavMesh = true;
        [Min(10f)] public float localNavMeshRadius = 40f;
        [Tooltip("Optional appearance option sets. One is chosen independently for each bandit. The prefab's archetype is used when empty.")]
        public AppearanceArchetype[] appearanceOptions;
        [Header("Wearable options (empty uses the chosen archetype)")]
        public AppearanceItemDefinition[] headwearOptions;
        public AppearanceItemDefinition[] torsoOptions;
        public AppearanceItemDefinition[] legsOptions;
        public AppearanceItemDefinition[] outerArmorOptions;
        [Tooltip("Armed bandit prefabs to use for this group's weapon choices. The first option is the leader. Each prefab keeps its matching grip and attacks.")]
        public StarvingBandit[] weaponOptions;

        [Header("Patrol")]
        [Min(1f)] public float patrolRadius = 80f;
        [Min(0.1f)] public float patrolWalkSpeed = 1.5f;
        [Min(0.1f)] public float followDistance = 2f;
        [Min(0.1f)] public float waypointTolerance = 1.5f;
        public Vector2 pauseBetweenWaypoints = new Vector2(1f, 3f);
        [Tooltip("Optional validated settlement route. Empty retains roaming patrol behavior.")]
        public Transform[] patrolPoints;
        int patrolPointIndex;

        public void OnOriginShift(Vector3 shift)
        {
            home -= shift;
            waypoint -= shift;
        }


        [Header("Detection")]
        public Transform player;
        [Min(1f)] public float sightRange = 18f;
        [Range(1f, 360f)] public float fieldOfView = 140f;
        public LayerMask sightMask = ~0;
        [Min(0f)] public float forgetAfterSeconds = 8f;

        readonly List<StarvingBandit> members = new List<StarvingBandit>();
        StarvingBandit leader;
        Vector3 home, waypoint;
        float nextWaypoint, nextSense, lastSeen;
        bool engaged;
        NavMeshData localNavMesh;
        NavMeshDataInstance localNavMeshInstance;

        void Start()
        {
            bool useAuthored = authoredMembers != null && authoredMembers.Length > 0;
            if (!banditPrefab && !useAuthored) { Debug.LogError("Bandit Group needs a bandit prefab.", this); enabled = false; return; }
            if (!SnapToTerrain())
                Debug.LogWarning("Bandit Group is not above an active terrain tile. Move it over the terrain.", this);
            home = transform.position;
            if (buildLocalNavMesh) BuildLocalNavMesh();
            if (!useAuthored && !NavMesh.SamplePosition(home, out _, navMeshSampleRadius, NavMesh.AllAreas))
            { Debug.LogError("Bandit Group has no NavMesh near its position. Check terrain placement and navigation settings.", this); enabled = false; return; }
            if (useAuthored)
                foreach (var bandit in authoredMembers)
                {
                    if (!bandit) continue;
                    bandit.groupControlled = true;
                    bandit.combatGroup = this;
                    bandit.groupWalkSpeed = patrolWalkSpeed;
                    bandit.groupDestination = bandit.transform.position;
                    if (!bandit.staff)
                    {
                        var generated = bandit.GetComponent<NpcGeneratedWeapon>();
                        if (generated && !generated.EnsureStartingWeapon())
                            Debug.LogWarning("Bandit Group could not equip authored member " + bandit.name, bandit);
                    }
                    members.Add(bandit);
                }
            else
            {
                int headwearOffset = headwearOptions != null && headwearOptions.Length > 0
                    ? Random.Range(0, headwearOptions.Length) : 0;
                for (int i = 0; i < Mathf.Max(1, groupSize); i++)
                {
                    Vector2 offset = i == 0 ? Vector2.zero : Random.insideUnitCircle * spawnRadius;
                    Vector3 proposed = home + new Vector3(offset.x, 0f, offset.y);
                    if (!NavMesh.SamplePosition(proposed, out var hit, navMeshSampleRadius, NavMesh.AllAreas))
                    { Debug.LogWarning("Bandit Group could not find NavMesh for a spawn.", this); continue; }
                    var selectedPrefab = PickBanditPrefab(i);
                    var bandit = Instantiate(selectedPrefab, hit.position, transform.rotation);
                    bandit.name = i == 0 ? "Bandit Leader" : "Bandit Follower " + i;
                    bandit.groupControlled = true;
                    bandit.combatGroup = this;
                    bandit.groupWalkSpeed = patrolWalkSpeed;
                    bandit.groupDestination = hit.position;
                    var appearance = bandit.GetComponent<NPCCharacterAppearance>();
                    if (appearance)
                    {
                        var option = PickAppearance();
                        if (option) appearance.archetype = option;
                        appearance.Generate(Random.Range(1, int.MaxValue));
                        var cosmetics = appearance.cosmetics;
                        if (cosmetics)
                        {
                            cosmetics.headwear = headwearOptions != null && headwearOptions.Length > 0
                                ? headwearOptions[(headwearOffset + i) % headwearOptions.Length] : cosmetics.headwear;
                            cosmetics.torso = PickItem(torsoOptions, cosmetics.torso);
                            cosmetics.legs = PickItem(legsOptions, cosmetics.legs);
                            cosmetics.outerArmor = PickItem(outerArmorOptions, cosmetics.outerArmor);
                            cosmetics.Refresh();
                        }
                    }
                    members.Add(bandit);
                }
            }
            if (members.Count == 0) { enabled = false; return; }
            leader = members[0];
            waypoint = leader.transform.position;
            nextWaypoint = Time.time;
        }

        StarvingBandit PickBanditPrefab(int memberIndex)
        {
            var options = weaponOptions != null && weaponOptions.Length > 0 ? weaponOptions : weaponVariants;
            if (options == null || options.Length == 0) return banditPrefab;
            int start = memberIndex % options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var option = options[(start + i) % options.Length];
                var generated = option ? option.GetComponent<NpcGeneratedWeapon>() : null;
                if (option && option.GetComponent<NavMeshAgent>() &&
                    (option.staff || generated && generated.startEquipped && generated.weaponPrefab && generated.gripTarget))
                    return option;
            }
            return banditPrefab;
        }

        static AppearanceItemDefinition PickItem(AppearanceItemDefinition[] options, AppearanceItemDefinition fallback)
        {
            if (options == null || options.Length == 0) return fallback;
            return options[Random.Range(0, options.Length)]; // A null entry intentionally leaves this slot bare.
        }

        bool SnapToTerrain()
        {
            foreach (var terrain in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (!terrain.enabled || !terrain.terrainData) continue;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (transform.position.x < origin.x || transform.position.x > origin.x + size.x ||
                    transform.position.z < origin.z || transform.position.z > origin.z + size.z) continue;
                Vector3 position = transform.position;
                position.y = terrain.SampleHeight(position) + origin.y;
                transform.position = position;
                return true;
            }
            return false;
        }

        void BuildLocalNavMesh()
        {
            float radius = Mathf.Max(localNavMeshRadius, patrolRadius + spawnRadius + 10f);
            var sources = new List<NavMeshBuildSource>();
            foreach (var terrain in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (!terrain.enabled || !terrain.terrainData) continue;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (origin.x > home.x + radius || origin.x + size.x < home.x - radius ||
                    origin.z > home.z + radius || origin.z + size.z < home.z - radius) continue;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain,
                    sourceObject = terrain.terrainData, transform = terrain.transform.localToWorldMatrix, area = 0 });
            }
            if (sources.Count == 0) return;
            var sourceBandit = banditPrefab ? banditPrefab : authoredMembers != null && authoredMembers.Length > 0 ? authoredMembers[0] : null;
            var prefabAgent = sourceBandit ? sourceBandit.GetComponent<NavMeshAgent>() : null;
            int agentType = prefabAgent ? prefabAgent.agentTypeID : NavMesh.GetSettingsByIndex(0).agentTypeID;
            var bounds = new Bounds(home, new Vector3(radius * 2f, 80f, radius * 2f));
            localNavMesh = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(agentType), sources,
                bounds, Vector3.zero, Quaternion.identity);
            if (localNavMesh) localNavMeshInstance = NavMesh.AddNavMeshData(localNavMesh);
        }

        void OnDestroy()
        {
            if (localNavMeshInstance.valid) localNavMeshInstance.Remove();
            if (localNavMesh) Destroy(localNavMesh);
        }

        AppearanceArchetype PickAppearance()
        {
            if (appearanceOptions == null || appearanceOptions.Length == 0) return null;
            int start = Random.Range(0, appearanceOptions.Length);
            for (int i = 0; i < appearanceOptions.Length; i++)
            {
                var option = appearanceOptions[(start + i) % appearanceOptions.Length];
                if (option) return option;
            }
            return null;
        }

        void Update()
        {
            members.RemoveAll(member => !member);
            if (members.Count == 0) { enabled = false; return; }
            if (!Alive(leader))
            {
                leader = members.Find(Alive);
                if (!leader) { enabled = false; return; }
                waypoint = leader.transform.position;
                nextWaypoint = Time.time;
            }
            if (!player || !player.gameObject.activeInHierarchy) ResolvePlayer();
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + .2f;
                if (player && AnyMemberSeesPlayer()) { engaged = true; lastSeen = Time.time; }
                else if (engaged && Time.time - lastSeen > forgetAfterSeconds) engaged = false;
            }
            if (engaged)
            {
                foreach (var member in members)
                    if (Alive(member)) { member.target = player; member.groupEngaged = true; }
                return;
            }
            if (Time.time >= nextWaypoint && Vector3.Distance(leader.transform.position, waypoint) <= waypointTolerance)
            {
                PickWaypoint();
                nextWaypoint = Time.time + Random.Range(Mathf.Min(pauseBetweenWaypoints.x, pauseBetweenWaypoints.y),
                    Mathf.Max(pauseBetweenWaypoints.x, pauseBetweenWaypoints.y));
            }
            leader.groupEngaged = false;
            leader.groupDestination = waypoint;
            int slot = 0;
            foreach (var member in members)
            {
                if (!Alive(member) || member == leader) continue;
                member.groupEngaged = false;
                slot++;
                float angle = slot * 2.39996f;
                Vector3 desired = leader.transform.position - leader.transform.forward * followDistance +
                    (leader.transform.right * Mathf.Sin(angle) + leader.transform.forward * Mathf.Cos(angle)) * followDistance;
                member.groupDestination = NavMesh.SamplePosition(desired, out var hit, navMeshSampleRadius, NavMesh.AllAreas)
                    ? hit.position : leader.transform.position;
            }
        }

        void PickWaypoint()
        {
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                for (int i = 0; i < patrolPoints.Length; i++)
                {
                    var point = patrolPoints[patrolPointIndex++ % patrolPoints.Length];
                    if (!point || !leader.agent || !leader.agent.isOnNavMesh) continue;
                    var route = new NavMeshPath();
                    if (leader.agent.CalculatePath(point.position, route) && route.status == NavMeshPathStatus.PathComplete)
                    { waypoint = point.position; return; }
                }
                waypoint = leader.transform.position;
                return;
            }
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * patrolRadius;
                Vector3 desired = home + new Vector3(offset.x, 0f, offset.y);
                if (!NavMesh.SamplePosition(desired, out var hit, navMeshSampleRadius, NavMesh.AllAreas)) continue;
                var path = new NavMeshPath();
                if (!leader.agent || !leader.agent.isOnNavMesh ||
                    !leader.agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                waypoint = hit.position;
                return;
            }
            waypoint = leader.transform.position;
        }

        bool AnyMemberSeesPlayer()
        {
            foreach (var member in members)
            {
                if (!Alive(member)) continue;
                Vector3 eye = member.transform.position + Vector3.up * 1.5f;
                Vector3 direction = player.position - eye;
                if (direction.magnitude > sightRange || Vector3.Angle(member.transform.forward, direction) > fieldOfView * .5f) continue;
                var hits = Physics.RaycastAll(eye, direction.normalized, direction.magnitude, sightMask, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                bool blocked = false;
                foreach (var hit in hits)
                {
                    if (hit.transform.IsChildOf(player.root)) break;
                    if (members.Exists(other => other && hit.transform.IsChildOf(other.transform))) continue;
                    var heldWeapon = hit.collider.GetComponentInParent<PhysicalMeleeWeapon>();
                    if (heldWeapon && heldWeapon.State == PhysicalMeleeWeapon.HoldState.HeldByNpc &&
                        members.Exists(other => other && heldWeapon.ownerRoot == other.transform)) continue;
                    blocked = true;
                    break;
                }
                if (blocked) continue;
                return true;
            }
            return false;
        }

        void ResolvePlayer()
        {
            var body = FindFirstObjectByType<VRBodyController>();
            if (body && body.hmd && body.hmd.gameObject.activeInHierarchy) player = body.hmd;
            else if (Camera.main) player = Camera.main.transform;
        }

        static bool Alive(StarvingBandit member) => member && (!member.health || !member.health.IsDead);
    }
}
