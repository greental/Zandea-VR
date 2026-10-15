using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Authoritative persistent representation of the currently displayed space/world frame.
    /// Rotations change during discrete Space <-> Surface swaps. Floating-origin translations
    /// also update the displayed origin without advancing the logical surface frame.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Koombar/Planet/Planet Universe Frame State")]
    public sealed class PlanetUniverseFrameState : MonoBehaviour, IFloatingOriginListener
    {
        [SerializeField] private Vector3 currentOrigin = Vector3.zero;
        [SerializeField] private Quaternion currentRotation = Quaternion.identity;
        [SerializeField] private int discreteSwapCount;

        public Vector3 CurrentOrigin => currentOrigin;
        public Quaternion CurrentRotation => currentRotation;
        public int DiscreteSwapCount => discreteSwapCount;

        public PlanetFrameTransform CurrentCanonicalToDisplayed => new PlanetFrameTransform(Vector3.zero, currentOrigin, currentRotation);

        public void OnOriginShift(Vector3 shift) => currentOrigin -= shift;

        public void ResetFrame()
        {
            currentOrigin = Vector3.zero;
            currentRotation = Quaternion.identity;
            discreteSwapCount = 0;
        }

        public void ApplyDiscreteFrameTransform(PlanetFrameTransform transform)
        {
            // New displayed frame = transform applied to previous displayed frame.
            currentOrigin = transform.TransformPoint(currentOrigin);
            currentRotation = PlanetFrameTransform.Normalize(transform.rotation * currentRotation);
            discreteSwapCount++;
        }
    }
}
