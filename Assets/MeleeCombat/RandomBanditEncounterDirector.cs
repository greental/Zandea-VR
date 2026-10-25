using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat
{
    // Reuses the project's authored bandit group settings to create occasional roaming encounters.
    public sealed class RandomBanditEncounterDirector : MonoBehaviour
    {
        public Vector2 intervalSeconds = new(90f, 180f);
        public Vector2 distanceAhead = new(38f, 60f);
        public int maximumLiveEncounters = 1;
        readonly System.Collections.Generic.List<GameObject> encounters = new();
        IEnumerator Start()
        {
            yield return new WaitForSeconds(20f);
            while (enabled)
            {
                encounters.RemoveAll(item => !item);
                if (encounters.Count < maximumLiveEncounters) TrySpawn();
                yield return new WaitForSeconds(Random.Range(intervalSeconds.x, intervalSeconds.y));
            }
        }
        void TrySpawn()
        {
            var camera = Camera.main;
            var template = FindFirstObjectByType<BanditGroup>();
            var fallbackObject = Resources.Load<GameObject>("Encounters/CombinedStarvingBandit");
            var banditPrefab = template && template.banditPrefab ? template.banditPrefab :
                fallbackObject ? fallbackObject.GetComponent<StarvingBandit>() : null;
            if (!camera || !banditPrefab) return;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            Vector3 proposed = camera.transform.position + forward * Random.Range(distanceAhead.x, distanceAhead.y) +
                Vector3.Cross(Vector3.up, forward) * Random.Range(-14f, 14f);
            Vector3 spawnPoint;
            if (NavMesh.SamplePosition(proposed, out var hit, 30f, NavMesh.AllAreas)) spawnPoint = hit.position;
            else if (Physics.Raycast(proposed + Vector3.up * 250f, Vector3.down, out var ground, 600f, ~0, QueryTriggerInteraction.Ignore)) spawnPoint = ground.point;
            else return;
            var root = new GameObject("Random bandit encounter");
            root.transform.SetPositionAndRotation(spawnPoint, Quaternion.LookRotation(-forward));
            var group = root.AddComponent<BanditGroup>();
            group.banditPrefab = banditPrefab;
            if (template)
            {
                group.weaponVariants = template.weaponVariants;
                group.weaponOptions = template.weaponOptions;
                group.appearanceOptions = template.appearanceOptions;
                group.headwearOptions = template.headwearOptions; group.torsoOptions = template.torsoOptions;
                group.legsOptions = template.legsOptions; group.outerArmorOptions = template.outerArmorOptions;
            }
            group.groupSize = Random.Range(3, 6);
            group.buildLocalNavMesh = !template || template.buildLocalNavMesh;
            group.patrolRadius = template ? template.patrolRadius : 80f;
            encounters.Add(root);
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
