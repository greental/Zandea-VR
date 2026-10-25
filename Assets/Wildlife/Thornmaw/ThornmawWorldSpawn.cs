using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zandea.Wildlife
{
    /// <summary>Spawns a small number of roaming hunters on live desert terrain in Main.</summary>
    public sealed class ThornmawWorldSpawn : MonoBehaviour
    {
        readonly RaycastHit[] hits = new RaycastHit[32];
        public int maximumHunters = 2;
        public Vector2 spawnDistance = new Vector2(30f, 55f);
        public Vector2 intervalSeconds = new Vector2(95f, 160f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (SceneManager.GetActiveScene().name != "Main") return;
            new GameObject("Thornmaw wildlife spawn").AddComponent<ThornmawWorldSpawn>();
        }

        IEnumerator Start()
        {
            var settings = Resources.Load<ThornmawSpawnSettings>("Wildlife/ThornmawSpawnSettings");
            if (!settings || !settings.prefab)
            {
                Debug.LogWarning("Thornmaw spawn settings or prefab is missing.", this);
                Destroy(gameObject);
                yield break;
            }
            yield return new WaitForSeconds(8f);
            while (enabled)
            {
                int living = 0;
                foreach (var hunter in FindObjectsByType<ThornmawHunter>(FindObjectsSortMode.None))
                    if (hunter && !hunter.Dead) living++;
                if (living < maximumHunters)
                    TrySpawn(settings.prefab);
                yield return new WaitForSeconds(Random.Range(intervalSeconds.x, intervalSeconds.y));
            }
        }

        void TrySpawn(ThornmawHunter prefab)
        {
            var camera = Camera.main;
            if (!camera) return;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 candidate = camera.transform.position + forward * Random.Range(spawnDistance.x, spawnDistance.y) +
                    right * Random.Range(-18f, 18f);
                int count = Physics.RaycastNonAlloc(candidate + Vector3.up * 150f, Vector3.down, hits, 400f,
                    ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.PositiveInfinity;
                Vector3 ground = default;
                for (int i = 0; i < count; i++)
                    if (hits[i].collider is TerrainCollider && hits[i].normal.y > .8f && hits[i].distance < nearest)
                    {
                        nearest = hits[i].distance;
                        ground = hits[i].point;
                    }
                if (nearest == float.PositiveInfinity) continue;
                Instantiate(prefab, ground + Vector3.up * prefab.bodyHeight,
                    Quaternion.LookRotation(-forward, Vector3.up)).name = "Thornmaw wildlife";
                return;
            }
        }
    }
}
