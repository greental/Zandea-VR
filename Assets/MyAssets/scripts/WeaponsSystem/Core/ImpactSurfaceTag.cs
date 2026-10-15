using UnityEngine;

namespace VRKoombar.WeaponsStaging.Core
{
    public enum ImpactSurfaceType
    {
        Default,
        Metal,
        Sand,
        Rock,
        Concrete,
        Shield,
        Organic
    }

    /// <summary>Attach to hit objects/terrain roots to classify impact surface response.</summary>
    [DisallowMultipleComponent]
    public sealed class ImpactSurfaceTag : MonoBehaviour
    {
        [SerializeField] private ImpactSurfaceType surfaceType = ImpactSurfaceType.Default;
        public ImpactSurfaceType SurfaceType => surfaceType;
    }
}
