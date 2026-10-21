using UnityEngine;

namespace Zandea.FriendlyNPC
{
    public sealed class BillboardLabel : MonoBehaviour
    {
        Transform viewer;
        void LateUpdate()
        {
            if (!viewer && Camera.main) viewer = Camera.main.transform;
            if (!viewer) return;
            var direction = viewer.position - transform.position;
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }
}
