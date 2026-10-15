using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Maintains full 3D sky orientation for frame swaps. Requires a skybox shader that consumes _SkyRotation quaternion.
    /// The current built-in cubemap skybox only supports Y rotation, so this controller logs a warning when the property is absent.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Koombar/Planet/Planet Sky Frame Controller")]
    public sealed class PlanetSkyFrameController : MonoBehaviour
    {
        private static readonly int SkyRotationId = Shader.PropertyToID("_SkyRotation");

        [SerializeField] private Material skyboxMaterial;
        [SerializeField] private Quaternion skyRotation = Quaternion.identity;
        [SerializeField] private bool warnIfUnsupported = true;

        public Quaternion SkyRotation => skyRotation;

        private void Reset()
        {
            skyboxMaterial = RenderSettings.skybox;
        }

        private void Awake()
        {
            if (skyboxMaterial == null)
                skyboxMaterial = RenderSettings.skybox;
            ApplyMaterialProperty();
        }

        public void ApplyFrameTransform(PlanetFrameTransform transform)
        {
            skyRotation = PlanetFrameTransform.Normalize(transform.rotation * skyRotation);
            ApplyMaterialProperty();
        }

        public void SetSkyboxMaterial(Material material)
        {
            skyboxMaterial = material;
            ApplyMaterialProperty();
        }

        void ApplyMaterialProperty()
        {
            if (skyboxMaterial == null)
                return;

            if (!skyboxMaterial.HasProperty(SkyRotationId))
            {
                if (warnIfUnsupported)
                {
                    Debug.LogWarning(
                        "[PlanetSkyFrameController] Current skybox material does not expose _SkyRotation. " +
                        "Built-in cubemap _Rotation is only Y-axis and cannot preserve arbitrary entry directions. " +
                        "Assign the Koombar full-rotation cubemap skybox for visual invariance.", this);
                    warnIfUnsupported = false;
                }
                return;
            }

            skyboxMaterial.SetVector(SkyRotationId, new Vector4(skyRotation.x, skyRotation.y, skyRotation.z, skyRotation.w));
        }
    }
}
