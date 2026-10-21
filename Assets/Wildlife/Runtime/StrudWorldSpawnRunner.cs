using System.Collections;
using UnityEngine;
namespace Zandea.Wildlife
{
    // Maintains a small, varied local wildlife population ahead and to either side of the player.
    public sealed class StrudWorldSpawnRunner : MonoBehaviour
    {
        readonly RaycastHit[] hits = new RaycastHit[32];
        public Vector2 intervalSeconds = new(75f, 140f);
        public Vector2 distance = new(22f, 48f);
        public int maximumAnimals = 4;
        IEnumerator Start()
        {
            var prefabs = Resources.LoadAll<GameObject>("Wildlife");
            if (prefabs.Length == 0) { Debug.LogWarning("Wildlife prefabs are missing from Resources/Wildlife."); Destroy(gameObject); yield break; }
            while (enabled)
            {
                var camera = Camera.main;
                if (!camera) { yield return new WaitForSeconds(.5f); continue; }
                if (FindObjectsByType<WildlifeBrain>(FindObjectsSortMode.None).Length >= maximumAnimals)
                { yield return new WaitForSeconds(10f); continue; }
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 center = camera.transform.position + forward * Random.Range(distance.x, distance.y) + right * Random.Range(-18f, 18f);
                Vector3 point = center + Vector3.up * 150;
                int count = Physics.RaycastNonAlloc(point, Vector3.down, hits, 400, ~0, QueryTriggerInteraction.Ignore);
                float hitDistance = float.MaxValue; Vector3 ground = default;
                for (int i = 0; i < count; i++)
                    if (hits[i].collider is TerrainCollider && hits[i].distance < hitDistance && hits[i].normal.y > .8f)
                    { hitDistance = hits[i].distance; ground = hits[i].point; }
                if (hitDistance < float.MaxValue)
                {
                    var prefab = prefabs[Random.Range(0, prefabs.Length)];
                    var creature = Instantiate(prefab, ground, Quaternion.LookRotation(forward));
                    creature.name = prefab.name + " wildlife";
                }
                yield return new WaitForSeconds(Random.Range(intervalSeconds.x, intervalSeconds.y));
            }
        }
    }
}
