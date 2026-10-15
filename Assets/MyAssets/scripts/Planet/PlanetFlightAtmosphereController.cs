using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Optional bridge from the central altitude transition state to the existing FlightController atmosphere flag.
    /// This preserves the current wind/buffeting system while avoiding separate trigger-only atmosphere thresholds.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Flight Atmosphere Controller")]
    public sealed class PlanetFlightAtmosphereController : MonoBehaviour
    {
        [SerializeField] private PlanetTransitionState transitionState;
        [SerializeField] private FlightController flightController;
        [SerializeField] private ActivePlanetSelector activePlanetSelector;
        [SerializeField] private PlanetSystemRig owningRig;
        [SerializeField, Range(0f, 1f)] private float inAtmosphereWhenFactorBelow = 0.95f;

        public void Configure(PlanetTransitionState state, FlightController controller)
        {
            Configure(state, controller, activePlanetSelector, owningRig);
        }

        public void Configure(PlanetTransitionState state, FlightController controller, ActivePlanetSelector selector, PlanetSystemRig rig)
        {
            transitionState = state;
            flightController = controller;
            activePlanetSelector = selector;
            owningRig = rig;
        }

        private void Reset()
        {
            transitionState = FindFirstObjectByType<PlanetTransitionState>();
            flightController = FindFirstObjectByType<FlightController>();
        }

        private void Update()
        {
            if (flightController == null)
                flightController = FindFirstObjectByType<FlightController>();
            if (transitionState == null || flightController == null)
                return;

            if (!IsActiveAtmosphereSource())
            {
                if (activePlanetSelector == null || !activePlanetSelector.HasActivePlanet || IsActiveRigOrNoSelector())
                    flightController.inAtmosphere = false;
                return;
            }

            flightController.inAtmosphere = transitionState.AtmosphereFactor <= inAtmosphereWhenFactorBelow;
        }

        bool IsActiveAtmosphereSource()
        {
            if (transitionState == null || transitionState.Planet == null || !transitionState.Planet.HasAtmosphere)
                return false;

            return IsActiveRigOrNoSelector();
        }

        bool IsActiveRigOrNoSelector()
        {
            if (activePlanetSelector == null)
                return true;

            return owningRig != null
                ? activePlanetSelector.IsActiveRig(owningRig)
                : activePlanetSelector.ActivePlanet == transitionState.Planet;
        }
    }
}