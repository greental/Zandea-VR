using System.Collections;
using UnityEngine;
namespace Zandea.Wildlife
{
    // One local prototype spawn near the XR player; the future wildlife population system can replace this.
    public sealed class StrudWorldSpawnRunner : MonoBehaviour
    {
        readonly RaycastHit[] hits = new RaycastHit[32];
        IEnumerator Start()
        {
            var prefab = Resources.Load<GameObject>("Wildlife/Strud");
            if (!prefab) { Debug.LogWarning("Strud wildlife prefab is missing from Resources/Wildlife."); Destroy(gameObject); yield break; }
            float deadline = Time.time + 45;
            while (Time.time < deadline)
            {
                var camera = Camera.main;
                if (!camera) { yield return new WaitForSeconds(.5f); continue; }
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 center = camera.transform.position + forward * 24 + right * 8;
                Vector3 point = center + Vector3.up * 150;
                int count = Physics.RaycastNonAlloc(point, Vector3.down, hits, 400, ~0, QueryTriggerInteraction.Ignore);
                float distance = float.MaxValue; Vector3 ground = default;
                for (int i = 0; i < count; i++)
                    if (hits[i].collider is TerrainCollider && hits[i].distance < distance && hits[i].normal.y > .8f)
                    { distance = hits[i].distance; ground = hits[i].point; }
                if (distance < float.MaxValue)
                {
                    var creature = Instantiate(prefab, ground, Quaternion.LookRotation(forward));
                    creature.name = "Strud wildlife";
                    Destroy(gameObject); yield break;
                }
                yield return new WaitForSeconds(.5f);
            }
            Debug.LogWarning("Strud wildlife could not find a terrain collider near the player within 45 seconds.");
            Destroy(gameObject);
        }
    }
}
