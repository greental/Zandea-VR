using UnityEngine;
using Unity.XR.CoreUtils;

namespace Zandea.FriendlyNPC
{
    public sealed class BillboardLabel : MonoBehaviour
    {
        Transform viewer;
        float nextLookup;
        void LateUpdate()
        {
            if (!viewer && Time.time >= nextLookup)
            {
                nextLookup = Time.time + 2f;
                var origin = FindFirstObjectByType<XROrigin>();
                viewer = origin && origin.Camera ? origin.Camera.transform :
                    Camera.main ? Camera.main.transform : null;
            }
            if (!viewer) return;
            var direction = viewer.position - transform.position;
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }
}
