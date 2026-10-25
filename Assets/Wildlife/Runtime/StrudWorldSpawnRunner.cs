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
        public int maximumAnimals = 10;
        public Vector2Int groupSize = new(3, 5);
        public Vector2 sizeRange = new(.58f, 1.35f);
        IEnumerator Start()
        {
            var prefab = Resources.Load<GameObject>("Wildlife/Strud 1");
            if (!prefab) prefab = Resources.Load<GameObject>("Wildlife/Strud");
            if (!prefab) { Debug.LogWarning("Strud 1 is missing from Resources/Wildlife."); Destroy(gameObject); yield break; }
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
                    int remaining = maximumAnimals - FindObjectsByType<WildlifeBrain>(FindObjectsSortMode.None).Length;
                    int amount = Mathf.Min(remaining, Random.Range(groupSize.x, groupSize.y + 1));
                    for (int i = 0; i < amount; i++)
                    {
                        Vector3 offset = right * Random.Range(-3.5f, 3.5f) + forward * Random.Range(-2.5f, 2.5f);
                        var creature = Instantiate(prefab, ground + offset, Quaternion.LookRotation(forward));
                        float size = i == 0 ? Random.Range(.58f, .76f) : i == amount - 1 ? Random.Range(1.15f, sizeRange.y) : Random.Range(.82f, 1.08f);
                        creature.transform.localScale = Vector3.one * size;
                        creature.name = "Strud 1 wildlife " + (i + 1);
                    }
                }
                yield return new WaitForSeconds(Random.Range(intervalSeconds.x, intervalSeconds.y));
            }
        }
    }
}
