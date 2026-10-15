using UnityEngine;

namespace JunkerEquipment
{
    public sealed class JunkerReflexSight : MonoBehaviour
    {
        public Transform muzzle;
        public Transform sightWindow;
        public Transform dot;
        public Camera viewer;
        [Min(.005f)] public float apertureRadius = .035f;
        [Min(1f)] public float zeroDistance = 25f;
        [Min(.1f)] public float maximumEyeDistance = 1.5f;
        [Min(.0005f)] public float dotDiameter = .0045f;

        void Awake()
        {
            if (!viewer) viewer = Camera.main;
            SetVisible(false);
        }

        void LateUpdate()
        {
            if (!viewer) viewer = Camera.main;
            if (!viewer || !muzzle || !sightWindow || !dot) { SetVisible(false); return; }

            Vector3 eye = viewer.transform.position;
            // Use the muzzle axis as the optic normal. This remains valid even if an
            // imported helper transform has a different local-forward convention.
            Vector3 normal = muzzle.forward;
            Vector3 toWindow = sightWindow.position - eye;
            float eyeDistance = toWindow.magnitude;
            if (eyeDistance > maximumEyeDistance || Vector3.Dot(toWindow, normal) <= 0f)
            { SetVisible(false); return; }

            // Project the muzzle's zeroed aim point back onto the optic glass from
            // the viewer's eye. The result has real reflex-sight parallax.
            Vector3 aimPoint = muzzle.position + muzzle.forward * zeroDistance;
            Vector3 eyeRay = (aimPoint - eye).normalized;
            float denominator = Vector3.Dot(eyeRay, normal);
            if (Mathf.Abs(denominator) < .0001f) { SetVisible(false); return; }
            float distanceToPlane = Vector3.Dot(sightWindow.position - eye, normal) / denominator;
            if (distanceToPlane <= 0f) { SetVisible(false); return; }

            Vector3 point = eye + eyeRay * distanceToPlane;
            Vector3 radial = point - sightWindow.position;
            radial -= normal * Vector3.Dot(radial, normal);
            if (radial.sqrMagnitude > apertureRadius * apertureRadius)
            { SetVisible(false); return; }

            dot.position = point - normal * .0008f;
            dot.localScale = Vector3.one * dotDiameter;
            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (dot && dot.gameObject.activeSelf != visible) dot.gameObject.SetActive(visible);
        }

        void OnDrawGizmosSelected()
        {
            if (!sightWindow) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(sightWindow.position, apertureRadius);
        }
    }
}
