using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat
{
    // Reuses the project's authored bandit group settings to create occasional roaming encounters.
    public sealed class RandomBanditEncounterDirector : MonoBehaviour
    {
        public Vector2 intervalSeconds = new(150f, 260f);
        public Vector2 distanceAhead = new(38f, 60f);
        public int maximumLiveEncounters = 1;
        int spawnedEncounters;
        IEnumerator Start()
        {
            yield return new WaitForSeconds(20f);
            while (enabled)
            {
                if (spawnedEncounters < maximumLiveEncounters) TrySpawn();
                yield return new WaitForSeconds(Random.Range(intervalSeconds.x, intervalSeconds.y));
            }
        }
        void TrySpawn()
        {
            var camera = Camera.main;
            var template = FindFirstObjectByType<BanditGroup>();
            if (!camera || !template || !template.banditPrefab) return;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            Vector3 proposed = camera.transform.position + forward * Random.Range(distanceAhead.x, distanceAhead.y) +
                Vector3.Cross(Vector3.up, forward) * Random.Range(-14f, 14f);
            if (!NavMesh.SamplePosition(proposed, out var hit, 15f, NavMesh.AllAreas)) return;
            var root = new GameObject("Random bandit encounter");
            root.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(-forward));
            var group = root.AddComponent<BanditGroup>();
            group.banditPrefab = template.banditPrefab;
            group.weaponVariants = template.weaponVariants;
            group.weaponOptions = template.weaponOptions;
            group.appearanceOptions = template.appearanceOptions;
            group.headwearOptions = template.headwearOptions; group.torsoOptions = template.torsoOptions;
            group.legsOptions = template.legsOptions; group.outerArmorOptions = template.outerArmorOptions;
            group.groupSize = Random.Range(3, 6);
            group.buildLocalNavMesh = template.buildLocalNavMesh;
            group.patrolRadius = template.patrolRadius;
            spawnedEncounters++;
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
