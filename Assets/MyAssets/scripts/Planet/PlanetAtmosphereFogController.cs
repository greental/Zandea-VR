using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MyAssets.Planet
{
    /// <summary>
    /// First-pass URP-compatible atmosphere handoff mask using Unity fog/ambient settings.
    /// Restores previous RenderSettings when disabled.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Atmosphere Fog Controller")]
    public sealed class PlanetAtmosphereFogController : MonoBehaviour
    {
        [SerializeField] private PlanetTransitionState transitionState;
        [SerializeField] private ActivePlanetSelector activePlanetSelector;
        [SerializeField] private PlanetSystemRig owningRig;
        [SerializeField] private bool controlFog = true;
        [SerializeField] private bool controlAmbient = true;
        [Header("Far/orbit planet haze")]
        [Tooltip("Fog color used when viewing the planet from far away.")]
        [SerializeField] private Color spaceFogColor = new Color(0.01f, 0.012f, 0.018f, 1f);
        [Tooltip("Unity linear fog start distance while far/orbit haze is active. This is distance from the camera, not altitude.")]
        [SerializeField] private float spaceFogStart = 12000f;
        [Tooltip("Unity linear fog end distance while far/orbit haze is active. This is distance from the camera, not altitude.")]
        [SerializeField] private float spaceFogEnd = 30000f;
        [Tooltip("Altitude where far/orbit planet haze has fully faded out while approaching the planet.")]
        [SerializeField] private float spaceFogFadeOutAltitudeMeters = 12000f;
        [Tooltip("Altitude where far/orbit planet haze reaches full strength. Between Fade Out Altitude and Full Altitude it fades smoothly.")]
        [SerializeField] private float spaceFogFullAltitudeMeters = 30000f;

        [Header("Planet approach fog pulse")]
        [Tooltip("Ship altitude above the planet's surface where approach fog begins. Calculated from distance to planet center minus planet radius.")]
        [SerializeField, Min(0f), InspectorName("Approach Start Altitude")]
        [FormerlySerializedAs("approachFogStartIntensifyAltitudeMeters")] private float approachStartAltitudeMeters = 15000f;
        [Tooltip("Fog intensity at Approach Start. 0 uses the far-space fog profile; 1 uses the maximum approach density.")]
        [SerializeField, Range(0f, 1f), InspectorName("Approach Start Intensity")]
        [FormerlySerializedAs("approachFogStartWeight")] private float approachStartIntensity = 0.05f;
        [Tooltip("Ship altitude above the planet's surface where approach fog reaches its maximum intensity before easing out.")]
        [SerializeField, Min(0f), InspectorName("Approach Maximum Fog Altitude")]
        [FormerlySerializedAs("approachFogEndIntensifyStartDeclineAltitudeMeters")] private float approachMaximumFogAltitudeMeters = 9000f;
        [Tooltip("Fog intensity at Approach Maximum Fog. 1 is the strongest approach fog.")]
        [SerializeField, Range(0f, 1f), InspectorName("Approach Maximum Fog Intensity")]
        [FormerlySerializedAs("approachFogPeakWeight")] private float approachMaximumFogIntensity = 1f;
        [Tooltip("Ship altitude above the planet's surface where approach fog finishes easing out.")]
        [SerializeField, Min(0f), InspectorName("Approach End Altitude")]
        [FormerlySerializedAs("approachFogEndDeclineAltitudeMeters")] private float approachEndAltitudeMeters = 3000f;
        [Tooltip("Fog intensity at Approach End. This is the remaining approach fog at that altitude.")]
        [SerializeField, Range(0f, 1f), InspectorName("Approach End Intensity")]
        [FormerlySerializedAs("approachFogEndWeight")] private float approachEndIntensity = 0f;
        // Camera-distance bounds used at full approach intensity. Approach altitude
        // controls remain planet-surface distances in the inspector.
        private const float ApproachFogMaximumCameraEndDistanceMeters = 9000f;

        [Header("Surface fog")]
        [Tooltip("Fog color used near the surface.")]
        [SerializeField] private Color surfaceFogColor = new Color(0.48f, 0.58f, 0.68f, 1f);
        [Tooltip("Unity linear fog start distance for surface fog. This is distance from the camera, not altitude.")]
        [SerializeField] private float surfaceFogStart = 0f;
        [Tooltip("Unity linear fog end distance for surface fog. This is distance from the camera, not altitude.")]
        [SerializeField] private float surfaceFogEnd = 10000f;
        [Tooltip("Altitude where surface fog starts fading in. Defaults near surface-mode altitude.")]
        [SerializeField] private float surfaceFogFadeInAltitudeMeters = 3000f;
        [Tooltip("Altitude where surface fog is full strength.")]
        [SerializeField] private float surfaceFogFullAltitudeMeters = 250f;
        [SerializeField] private Color surfaceAmbient = new Color(0.42f, 0.45f, 0.5f, 1f);
        [SerializeField] private Color spaceAmbient = new Color(0.02f, 0.025f, 0.035f, 1f);

        [Header("Execution")]
        [Tooltip("Applies fog again from RenderPipelineManager.endFrameRendering so this component wins over scene/global fog writers that run after LateUpdate.")]
        [SerializeField] private bool applyAtEndOfFrame = true;

        [Header("Live values")]
        [SerializeField, Range(0f, 1f)] private float currentAtmosphereFactor;
        [SerializeField, InspectorName("Current Altitude Above Surface")] private float currentAltitudeMeters;
        [SerializeField, Range(0f, 1f)] private float farFogWeight;
        [SerializeField, Range(0f, 1f), InspectorName("Approach Curve Intensity")] private float approachFogWeight;
        [SerializeField, Range(0f, 1f), InspectorName("Current Approach Fog Strength")] private float effectiveApproachFogStrength;
        [SerializeField, Range(0f, 1f)] private float surfaceFogWeight;
        [SerializeField] private bool appliedFogEnabled;
        [SerializeField] private FogMode appliedFogMode;
        [SerializeField] private Color appliedFogColor;
        [SerializeField, InspectorName("Applied Fog Start Distance From Camera")] private float appliedFogStart;
        [SerializeField, InspectorName("Applied Fog End Distance From Camera")] private float appliedFogEnd;
        [SerializeField] private AmbientMode appliedAmbientMode;
        [SerializeField] private Color appliedAmbientLight;

        private bool saved;
        private bool oldFog;
        private FogMode oldFogMode;
        private Color oldFogColor;
        private float oldFogStart;
        private float oldFogEnd;
        private AmbientMode oldAmbientMode;
        private Color oldAmbientLight;

        public void Configure(PlanetTransitionState state)
        {
            Configure(state, activePlanetSelector, owningRig);
        }

        public void Configure(PlanetTransitionState state, ActivePlanetSelector selector, PlanetSystemRig rig)
        {
            transitionState = state;
            activePlanetSelector = selector;
            owningRig = rig;
        }

        public void ConfigurePlanetScale(float radiusMeters, float entryAltitudeMeters, float atmosphereHeightMeters, float lowerAtmosphereAltitudeMeters, float upperAtmosphereAltitudeMeters, bool configureApproachFogAltitudes = false)
        {
            radiusMeters = Mathf.Max(1f, radiusMeters);
            entryAltitudeMeters = Mathf.Max(0f, entryAltitudeMeters);
            atmosphereHeightMeters = Mathf.Max(0f, atmosphereHeightMeters);
            lowerAtmosphereAltitudeMeters = Mathf.Max(0f, lowerAtmosphereAltitudeMeters);
            upperAtmosphereAltitudeMeters = Mathf.Max(lowerAtmosphereAltitudeMeters + 1f, upperAtmosphereAltitudeMeters);

            spaceFogStart = radiusMeters * 0.2f;
            spaceFogEnd = radiusMeters * 0.5f;
            spaceFogFadeOutAltitudeMeters = entryAltitudeMeters;
            spaceFogFullAltitudeMeters = Mathf.Max(entryAltitudeMeters + 1f, radiusMeters * 0.5f);
            if (configureApproachFogAltitudes)
            {
                approachStartAltitudeMeters = entryAltitudeMeters;
                approachMaximumFogAltitudeMeters = entryAltitudeMeters * 0.6f;
                approachEndAltitudeMeters = entryAltitudeMeters / 5f;
            }
            surfaceFogEnd = Mathf.Max(1f, entryAltitudeMeters);
            surfaceFogFadeInAltitudeMeters = upperAtmosphereAltitudeMeters;
            surfaceFogFullAltitudeMeters = lowerAtmosphereAltitudeMeters;
        }

        private void OnEnable()
        {
            SaveSettings();
            RenderPipelineManager.endFrameRendering += OnEndFrameRendering;
        }

        private void OnValidate()
        {
            approachMaximumFogAltitudeMeters = Mathf.Min(approachMaximumFogAltitudeMeters,
                Mathf.Max(0f, approachStartAltitudeMeters - 1f));
            approachEndAltitudeMeters = Mathf.Min(approachEndAltitudeMeters,
                Mathf.Max(0f, approachMaximumFogAltitudeMeters - 1f));
        }

        private void OnDisable()
        {
            RenderPipelineManager.endFrameRendering -= OnEndFrameRendering;
            RestoreSettings();
        }

        private void LateUpdate()
        {
            ApplyAtmosphereSettings();
        }

        void OnEndFrameRendering(ScriptableRenderContext context, Camera[] cameras)
        {
            if (applyAtEndOfFrame)
                ApplyAtmosphereSettings();
        }

        void ApplyAtmosphereSettings()
        {
            if (transitionState == null)
                return;

            if (!IsActiveAtmosphereSource())
            {
                currentAtmosphereFactor = 1f;
                currentAltitudeMeters = 0f;
                farFogWeight = 0f;
                approachFogWeight = 0f;
                effectiveApproachFogStrength = 0f;
                surfaceFogWeight = 0f;
                RestoreSettings();
                return;
            }

            SaveSettings();

            float t = transitionState.AtmosphereFactor;
            currentAtmosphereFactor = t;
            currentAltitudeMeters = transitionState.AltitudeMeters;

            if (controlFog)
            {
                ResolveFogProfile(currentAltitudeMeters, out appliedFogEnabled, out appliedFogColor, out appliedFogStart, out appliedFogEnd);
                appliedFogMode = FogMode.Linear;

                RenderSettings.fog = appliedFogEnabled;
                RenderSettings.fogMode = appliedFogMode;
                RenderSettings.fogColor = appliedFogColor;
                RenderSettings.fogStartDistance = appliedFogStart;
                RenderSettings.fogEndDistance = appliedFogEnd;
            }

            if (controlAmbient)
            {
                appliedAmbientMode = AmbientMode.Flat;
                appliedAmbientLight = Color.Lerp(surfaceAmbient, spaceAmbient, t);

                RenderSettings.ambientMode = appliedAmbientMode;
                RenderSettings.ambientLight = appliedAmbientLight;
            }
        }

        bool IsActiveAtmosphereSource()
        {
            if (transitionState == null || transitionState.Planet == null || !transitionState.Planet.HasAtmosphere)
                return false;

            if (activePlanetSelector == null)
                return true;

            return owningRig != null
                ? activePlanetSelector.IsActiveRig(owningRig)
                : activePlanetSelector.ActivePlanet == transitionState.Planet;
        }


        void ResolveFogProfile(float altitudeMeters, out bool fogEnabled, out Color fogColor, out float fogStart, out float fogEnd)
        {
            float spaceFullAltitude = Mathf.Max(spaceFogFadeOutAltitudeMeters + 1f, spaceFogFullAltitudeMeters);
            farFogWeight = Smooth01(Mathf.InverseLerp(spaceFogFadeOutAltitudeMeters, spaceFullAltitude, altitudeMeters));

            float surfaceFadeAltitude = Mathf.Max(surfaceFogFadeInAltitudeMeters, surfaceFogFullAltitudeMeters) + 1f;
            float surfaceFullAltitude = Mathf.Min(surfaceFogFadeInAltitudeMeters, surfaceFogFullAltitudeMeters);
            surfaceFogWeight = 1f - Smooth01(Mathf.InverseLerp(surfaceFullAltitude, surfaceFadeAltitude, altitudeMeters));
            approachFogWeight = ResolveApproachFogWeight(altitudeMeters);
            float approachBlend = ResolveApproachFogBlend(altitudeMeters);
            effectiveApproachFogStrength = Mathf.Clamp01(approachFogWeight * approachBlend);

            float farFogEnd = Mathf.Lerp(Mathf.Max(spaceFogStart + 1f, spaceFogEnd * 2f), spaceFogEnd, farFogWeight);
            fogStart = Mathf.Lerp(spaceFogStart, 0f, effectiveApproachFogStrength);
            float approachEnd = Mathf.Max(fogStart + 1f, Mathf.Min(farFogEnd, ApproachFogMaximumCameraEndDistanceMeters));
            fogEnd = BlendFogEndByDensity(spaceFogStart, farFogEnd, fogStart, approachEnd, effectiveApproachFogStrength);
            fogColor = spaceFogColor;

            if (surfaceFogWeight > 0.001f)
            {
                float surfaceProfileEnd = Mathf.Lerp(
                    Mathf.Max(surfaceFogStart + 1f, this.surfaceFogEnd * 2f),
                    this.surfaceFogEnd,
                    surfaceFogWeight);
                float blendedFogStart = Mathf.Lerp(fogStart, surfaceFogStart, surfaceFogWeight);
                fogEnd = BlendFogEndByDensity(fogStart, fogEnd, blendedFogStart, surfaceProfileEnd, surfaceFogWeight);
                fogStart = blendedFogStart;
                fogColor = Color.Lerp(spaceFogColor, surfaceFogColor, surfaceFogWeight);
            }

            fogEnabled = farFogWeight > 0.001f || effectiveApproachFogStrength > 0.001f || surfaceFogWeight > 0.001f;
            if (!fogEnabled)
            {
                fogColor = Color.clear;
                fogStart = 0f;
                fogEnd = 0f;
            }
        }

        float ResolveApproachFogWeight(float altitudeMeters)
        {
            float start = Mathf.Max(approachStartAltitudeMeters, approachMaximumFogAltitudeMeters + 1f);
            float maximum = Mathf.Clamp(approachMaximumFogAltitudeMeters, 0f, start - 1f);
            float end = Mathf.Clamp(approachEndAltitudeMeters, 0f, maximum - 1f);

            if (altitudeMeters >= maximum)
            {
                float progress = 1f - Mathf.InverseLerp(maximum, start, altitudeMeters);
                return Mathf.Lerp(approachStartIntensity, approachMaximumFogIntensity, Smooth01(progress));
            }

            float decline = 1f - Mathf.InverseLerp(end, maximum, altitudeMeters);
            return Mathf.Lerp(approachMaximumFogIntensity, approachEndIntensity, Smooth01(decline));
        }

        float ResolveApproachFogBlend(float altitudeMeters)
        {
            float start = Mathf.Max(approachStartAltitudeMeters, approachMaximumFogAltitudeMeters + 1f);
            float maximum = Mathf.Clamp(approachMaximumFogAltitudeMeters, 0f, start - 1f);
            float end = Mathf.Clamp(approachEndAltitudeMeters, 0f, maximum - 1f);
            float startShoulder = Mathf.Max(1f, (start - maximum) * 0.1f);
            float endShoulder = Mathf.Max(1f, (maximum - end) * 0.1f);
            float enter = 1f - Smooth01(Mathf.InverseLerp(start, start + startShoulder, altitudeMeters));
            float leave = Smooth01(Mathf.InverseLerp(end - endShoulder, end, altitudeMeters));
            return Mathf.Clamp01(Mathf.Min(enter, leave));
        }

        static float BlendFogEndByDensity(float farStart, float farEnd, float approachStart, float approachEnd, float strength)
        {
            float farDensity = 1f / Mathf.Max(1f, farEnd - farStart);
            float approachDensity = 1f / Mathf.Max(1f, approachEnd - approachStart);
            float density = Mathf.Lerp(farDensity, approachDensity, Mathf.Clamp01(strength));
            return approachStart + 1f / Mathf.Max(0.000001f, density);
        }

        static float Smooth01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        void SaveSettings()
        {
            if (saved)
                return;

            saved = true;
            oldFog = RenderSettings.fog;
            oldFogMode = RenderSettings.fogMode;
            oldFogColor = RenderSettings.fogColor;
            oldFogStart = RenderSettings.fogStartDistance;
            oldFogEnd = RenderSettings.fogEndDistance;
            oldAmbientMode = RenderSettings.ambientMode;
            oldAmbientLight = RenderSettings.ambientLight;
        }

        void RestoreSettings()
        {
            if (!saved)
                return;

            RenderSettings.fog = oldFog;
            RenderSettings.fogMode = oldFogMode;
            RenderSettings.fogColor = oldFogColor;
            RenderSettings.fogStartDistance = oldFogStart;
            RenderSettings.fogEndDistance = oldFogEnd;
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbientLight;
            saved = false;
        }
    }
}
