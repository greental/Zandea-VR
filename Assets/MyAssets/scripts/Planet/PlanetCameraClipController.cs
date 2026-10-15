using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Keeps active view cameras' far clips large enough to see the full scaffold planet.
    /// This is a pragmatic prototype helper until the planet gets a scaled distant impostor/render pass.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Camera Clip Controller")]
    public sealed class PlanetCameraClipController : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private Transform viewer;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool autoFindMainCamera = true;
        [SerializeField] private bool driveAllEnabledCameras = true;
        [SerializeField, Min(0f)] private float extraClipPaddingMeters = 10000f;
        [SerializeField, Min(1f)] private float minimumFarClipMeters = 200000f;

        public void Configure(PlanetDefinition planetDefinition, Transform viewerTransform)
        {
            planet = planetDefinition;
            viewer = viewerTransform;
        }

        private void Reset()
        {
            planet = FindFirstObjectByType<PlanetDefinition>();
            viewer = FindFirstObjectByType<Rigidbody>()?.transform;
            targetCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (planet == null)
                planet = FindFirstObjectByType<PlanetDefinition>();
            if (viewer == null)
                viewer = FindFirstObjectByType<Rigidbody>()?.transform;
            if (targetCamera == null && autoFindMainCamera)
                targetCamera = Camera.main;
            if (planet == null)
                return;

            if (driveAllEnabledCameras)
            {
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < cameras.Length; i++)
                    ApplyFarClip(cameras[i]);
                return;
            }

            ApplyFarClip(targetCamera);
        }

        private void OnPreCull()
        {
            if (!driveAllEnabledCameras)
                ApplyFarClip(targetCamera);
        }

        void ApplyFarClip(Camera cameraToDrive)
        {
            if (planet == null || cameraToDrive == null || !cameraToDrive.enabled)
                return;

            Vector3 viewPosition = viewer != null ? viewer.position : cameraToDrive.transform.position;
            float distanceToCenter = Vector3.Distance(viewPosition, planet.Center);
            float requiredFarClip = distanceToCenter + planet.AtmosphereRadiusMeters + extraClipPaddingMeters;
            cameraToDrive.farClipPlane = Mathf.Max(cameraToDrive.farClipPlane, minimumFarClipMeters, requiredFarClip);
        }
    }
}