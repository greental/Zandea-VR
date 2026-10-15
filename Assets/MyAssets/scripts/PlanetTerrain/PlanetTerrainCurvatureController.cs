using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    /// <summary>
    /// Runtime-only visual curvature controller for Unity Terrain treadmill tiles.
    ///
    /// The shader deformation is intentionally visual-only: TerrainData, TerrainCollider,
    /// terrain transforms, and treadmill recycling remain flat and unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlanetTerrainCurvatureController : MonoBehaviour
    {
        private static readonly int PlanetCurvatureRadiusId = Shader.PropertyToID("_PlanetCurvatureRadius");
        private static readonly int CurvatureAnchorXZId = Shader.PropertyToID("_CurvatureAnchorXZ");
        private static readonly int CurvatureStrengthId = Shader.PropertyToID("_CurvatureStrength");
        private static readonly int CurvatureFalloffInnerRadiusId = Shader.PropertyToID("_CurvatureFalloffInnerRadius");
        private static readonly int CurvatureFalloffOuterRadiusId = Shader.PropertyToID("_CurvatureFalloffOuterRadius");
        private static readonly int CurvatureFalloffStrengthId = Shader.PropertyToID("_CurvatureFalloffStrength");

        [Header("Curvature")]
        [SerializeField] private bool curvatureEnabled = true;
        [SerializeField] private float planetRadiusMeters = 30000f;
        [SerializeField, Range(0f, 1f)] private float curvatureStrength = 1f;
        [SerializeField] private Transform anchor;

        [Header("Circular Visible Footprint")]
        [Tooltip("Terrain is normally curved inside this world-space radius from the curvature anchor.")]
        [SerializeField, Min(0f)] private float fullDetailRadiusMeters = 1900f;
        [Tooltip("Terrain is progressively sunk between the full-detail radius and this world-space radius.")]
        [SerializeField, Min(0f)] private float falloffOuterRadiusMeters = 2350f;
        [Tooltip("Additional visual-only downward sink applied at and beyond the outer falloff radius.")]
        [SerializeField, Min(0f)] private float falloffStrengthMeters = 3000f;

        [Header("Terrain Material")]
        [SerializeField] private bool applyMaterialToChildTerrains = true;
        [SerializeField] private Material curvedTerrainMaterial;
        [SerializeField, Min(0.1f)] private float materialRefreshIntervalSeconds = 1f;

        [Header("Debug")]
        [SerializeField] private bool showDebugOverlay;

        private Terrain[] cachedTerrains;
        private float nextMaterialRefreshTime;

        public bool CurvatureEnabled
        {
            get => curvatureEnabled;
            set => curvatureEnabled = value;
        }

        public float PlanetRadiusMeters
        {
            get => planetRadiusMeters;
            set => planetRadiusMeters = Mathf.Max(1f, value);
        }

        public float CurvatureStrength
        {
            get => curvatureStrength;
            set => curvatureStrength = Mathf.Clamp01(value);
        }

        public Transform Anchor
        {
            get => anchor;
            set => anchor = value;
        }

        public float FullDetailRadiusMeters
        {
            get => fullDetailRadiusMeters;
            set => fullDetailRadiusMeters = Mathf.Max(0f, value);
        }

        public float FalloffOuterRadiusMeters
        {
            get => falloffOuterRadiusMeters;
            set => falloffOuterRadiusMeters = Mathf.Max(0f, value);
        }

        public float FalloffStrengthMeters
        {
            get => falloffStrengthMeters;
            set => falloffStrengthMeters = Mathf.Max(0f, value);
        }

        private void OnEnable()
        {
            RefreshTerrainCache();
            ApplyGlobals();
            ApplyMaterialToTerrains(force: true);
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(CurvatureStrengthId, 0f);
            Shader.SetGlobalFloat(CurvatureFalloffStrengthId, 0f);
        }

        private void LateUpdate()
        {
            ApplyGlobals();

            if (applyMaterialToChildTerrains && Time.unscaledTime >= nextMaterialRefreshTime)
            {
                nextMaterialRefreshTime = Time.unscaledTime + materialRefreshIntervalSeconds;
                RefreshTerrainCache();
                ApplyMaterialToTerrains(force: false);
            }
        }

        [ContextMenu("Refresh Child Terrains")]
        private void RefreshTerrainCache()
        {
            cachedTerrains = GetComponentsInChildren<Terrain>(includeInactive: true);
        }

        [ContextMenu("Apply Curved Material To Child Terrains")]
        private void ApplyMaterialToTerrainsFromContextMenu()
        {
            RefreshTerrainCache();
            ApplyMaterialToTerrains(force: true);
        }

        private void ApplyGlobals()
        {
            Vector3 anchorPosition = anchor != null ? anchor.position : transform.position;
            float safeRadius = Mathf.Max(1f, planetRadiusMeters);
            float effectiveStrength = curvatureEnabled ? Mathf.Clamp01(curvatureStrength) : 0f;
            float safeInnerRadius = Mathf.Max(0f, fullDetailRadiusMeters);
            float safeOuterRadius = Mathf.Max(safeInnerRadius + 1f, falloffOuterRadiusMeters);
            float effectiveFalloffStrength = curvatureEnabled ? Mathf.Max(0f, falloffStrengthMeters) : 0f;

            Shader.SetGlobalFloat(PlanetCurvatureRadiusId, safeRadius);
            Shader.SetGlobalVector(CurvatureAnchorXZId, new Vector4(anchorPosition.x, anchorPosition.z, 0f, 0f));
            Shader.SetGlobalFloat(CurvatureStrengthId, effectiveStrength);
            Shader.SetGlobalFloat(CurvatureFalloffInnerRadiusId, safeInnerRadius);
            Shader.SetGlobalFloat(CurvatureFalloffOuterRadiusId, safeOuterRadius);
            Shader.SetGlobalFloat(CurvatureFalloffStrengthId, effectiveFalloffStrength);
        }

        private void ApplyMaterialToTerrains(bool force)
        {
            if (!applyMaterialToChildTerrains || curvedTerrainMaterial == null || cachedTerrains == null)
            {
                return;
            }

            for (int i = 0; i < cachedTerrains.Length; i++)
            {
                Terrain terrain = cachedTerrains[i];
                if (terrain == null)
                {
                    continue;
                }

                if (force || terrain.materialTemplate != curvedTerrainMaterial)
                {
                    terrain.materialTemplate = curvedTerrainMaterial;
                }
            }
        }

        private void OnGUI()
        {
            if (!showDebugOverlay)
            {
                return;
            }

            Vector3 anchorPosition = anchor != null ? anchor.position : transform.position;
            float safeRadius = Mathf.Max(1f, planetRadiusMeters);
            float strength = curvatureEnabled ? Mathf.Clamp01(curvatureStrength) : 0f;
            float safeInnerRadius = Mathf.Max(0f, fullDetailRadiusMeters);
            float safeOuterRadius = Mathf.Max(safeInnerRadius + 1f, falloffOuterRadiusMeters);
            float effectiveFalloffStrength = curvatureEnabled ? Mathf.Max(0f, falloffStrengthMeters) : 0f;

            GUILayout.BeginArea(new Rect(12f, 12f, 380f, 190f), GUI.skin.box);
            GUILayout.Label("Terrain Curvature POC");
            GUILayout.Label($"Enabled: {curvatureEnabled}  Strength: {strength:0.00}");
            GUILayout.Label($"Radius: {safeRadius:0} m");
            GUILayout.Label($"Circular footprint: {safeInnerRadius:0} m -> {safeOuterRadius:0} m, sink {effectiveFalloffStrength:0} m");
            GUILayout.Label($"Anchor XZ: {anchorPosition.x:0.0}, {anchorPosition.z:0.0}");
            GUILayout.Label($"Drop @ 1km: {ComputeDrop(1000f, safeRadius, strength):0.0} m");
            GUILayout.Label($"Drop @ 2km: {ComputeDrop(2000f, safeRadius, strength):0.0} m");
            GUILayout.Label($"Drop @ 4km: {ComputeDrop(4000f, safeRadius, strength):0.0} m");
            GUILayout.EndArea();
        }

        private static float ComputeDrop(float distanceMeters, float radiusMeters, float strength)
        {
            return distanceMeters * distanceMeters / (2f * Mathf.Max(1f, radiusMeters)) * Mathf.Clamp01(strength);
        }
    }
}