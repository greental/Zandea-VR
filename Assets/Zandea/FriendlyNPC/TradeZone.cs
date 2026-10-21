using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TradeZone : MonoBehaviour
    {
        BoxCollider box;
        void Awake() => box = GetComponent<BoxCollider>();
        public bool Contains(Vector3 point)
        {
            if (!box || !box.enabled) return false;
            var local = box.transform.InverseTransformPoint(point) - box.center;
            var half = box.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}
