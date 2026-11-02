using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Zandea.Wildlife;

namespace KenshiVR.MeleeCombat
{
    // Reuses the project's authored bandit group settings to create occasional roaming encounters.
    public sealed class RandomBanditEncounterDirector : MonoBehaviour
    {
        public Vector2 intervalSeconds = new(90f, 180f);
        public Vector2 distanceAhead = new(38f, 60f);
        public int maximumLiveEncounters = 1;
        [Min(0f)] public float settlementOutsideWallMargin = WildlifeSettlementExclusion.DefaultOutsideWallMargin;
        readonly System.Collections.Generic.List<GameObject> encounters = new();
        IEnumerator Start()
        {
            yield return new WaitForSeconds(20f);
            while (enabled)
            {
                encounters.RemoveAll(item => !item || !item.GetComponent<BanditGroup>() ||
                    !item.GetComponent<BanditGroup>().enabled);
                bool spawned = encounters.Count >= maximumLiveEncounters || TrySpawn();
                if (spawned && encounters.Count > 0)
                {
                    yield return null; // Let the new group's Start validate its NavMesh.
                    encounters.RemoveAll(item => !item || !item.GetComponent<BanditGroup>() ||
                        !item.GetComponent<BanditGroup>().enabled);
                    spawned = encounters.Count > 0;
                }
                yield return new WaitForSeconds(spawned ?
                    Random.Range(intervalSeconds.x, intervalSeconds.y) : 5f);
            }
        }
        bool TrySpawn()
        {
            var camera = Camera.main;
            var template = FindFirstObjectByType<BanditGroup>();
            var loadout = Resources.Load<BanditEncounterLoadout>("Encounters/BanditEncounterLoadout");
            var variants = new System.Collections.Generic.List<StarvingBandit>();
            if (loadout && loadout.weaponPrefabs != null)
                foreach (var prefab in loadout.weaponPrefabs)
                    if (prefab && prefab.TryGetComponent<StarvingBandit>(out var bandit)) variants.Add(bandit);
            var banditPrefab = variants.Count > 0 ? variants[0] : template ? template.banditPrefab : null;
            if (!camera || !banditPrefab) return false;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            Vector3 proposed = camera.transform.position + forward * Random.Range(distanceAhead.x, distanceAhead.y) +
                Vector3.Cross(Vector3.up, forward) * Random.Range(-14f, 14f);
            Vector3 spawnPoint;
            if (NavMesh.SamplePosition(proposed, out var hit, 30f, NavMesh.AllAreas))
                spawnPoint = hit.position;
            else
            {
                // The planet terrain does not have navigation until BanditGroup
                // builds its local mesh. Place the group on an active terrain tile.
                Terrain terrain = null;
                foreach (var candidate in Terrain.activeTerrains)
                {
                    if (!candidate || !candidate.enabled || !candidate.terrainData) continue;
                    var terrainCollider = candidate.GetComponent<TerrainCollider>();
                    if (!terrainCollider || !terrainCollider.enabled) continue;
                    var origin = candidate.transform.position;
                    var size = candidate.terrainData.size;
                    if (proposed.x < origin.x || proposed.x > origin.x + size.x ||
                        proposed.z < origin.z || proposed.z > origin.z + size.z) continue;
                    terrain = candidate;
                    break;
                }
                if (!terrain) return false;
                spawnPoint = proposed;
                spawnPoint.y = terrain.SampleHeight(proposed) + terrain.transform.position.y;
            }
            // BanditGroup scatters members up to four metres from its root.
            // Check the final NavMesh/terrain position, not just the proposal.
            if (WildlifeSettlementExclusion.IsExcluded(spawnPoint,
                settlementOutsideWallMargin + 4f)) return false;
            var root = new GameObject("Random bandit encounter");
            root.transform.SetPositionAndRotation(spawnPoint, Quaternion.LookRotation(-forward));
            var group = root.AddComponent<BanditGroup>();
            group.banditPrefab = banditPrefab;
            if (variants.Count > 0) group.weaponOptions = variants.ToArray();
            if (loadout)
            {
                group.headwearOptions = loadout.followerHeadwear;
                group.leaderHeadwearOptions = loadout.leaderHeadwear;
                group.torsoOptions = loadout.torso;
                group.legsOptions = loadout.legs;
            }
            if (template)
            {
                group.weaponVariants = template.weaponVariants;
                if (variants.Count == 0) group.weaponOptions = template.weaponOptions;
                group.appearanceOptions = template.appearanceOptions;
                if (!loadout)
                {
                    group.headwearOptions = template.headwearOptions;
                    group.leaderHeadwearOptions = template.leaderHeadwearOptions;
                    group.torsoOptions = template.torsoOptions;
                    group.legsOptions = template.legsOptions;
                }
                group.outerArmorOptions = template.outerArmorOptions;
            }
            group.groupSize = Random.Range(3, 6);
            group.buildLocalNavMesh = true;
            group.patrolRadius = template ? template.patrolRadius : 80f;
            encounters.Add(root);
            return true;
        }
    }

    static class RandomBanditEncounterInstall
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (SceneManager.GetActiveScene().name != "Main" || Object.FindFirstObjectByType<RandomBanditEncounterDirector>()) return;
            new GameObject("Random encounter director").AddComponent<RandomBanditEncounterDirector>();
        }
    }
}
