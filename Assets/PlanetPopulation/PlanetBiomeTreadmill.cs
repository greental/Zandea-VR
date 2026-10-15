using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    /// <summary>
    /// One entry point for an authored biome treadmill. The existing treadmill,
    /// curvature, biome sampler, and wrap components retain their tested runtime
    /// implementations; this component owns their wiring and keeps POI spawning
    /// separate from terrain generation.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-250)]
    [RequireComponent(typeof(PlanetTerrainTreadmill), typeof(PlanetTerrainCurvatureController), typeof(PlanetPopulationPoc))]
    public sealed class PlanetBiomeTreadmill : MonoBehaviour
    {
        public PlanetTerrainTreadmill Treadmill => GetComponent<PlanetTerrainTreadmill>();
        public PlanetTerrainCurvatureController Curvature => GetComponent<PlanetTerrainCurvatureController>();
        public PlanetPopulationPoc Biomes => GetComponent<PlanetPopulationPoc>();
        public PlanetBiomeWrapRuntime Wrap => GetComponent<PlanetBiomeWrapRuntime>();
        public PlanetTerrainTileSet AuthoredTileSet => Treadmill ? Treadmill.AuthoredTileSet : null;

        public void ConfigureTarget(Transform target)
        {
            Treadmill.ConfigureTarget(target);
            Curvature.Anchor = target;
        }

        public void SampleBiomeWeights(PlanetSamplePoint point, float[] weights) =>
            Biomes.SampleBiomeWeights(point, weights);

        void Reset()
        {
            EnsureWiring();
#if UNITY_EDITOR
            var curve = Curvature;
            if (curve)
            {
                var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/MyAssets/shaders/Terrain/CurvedTerrain/M_CurvedTerrainLit.mat");
                var serialized = new UnityEditor.SerializedObject(curve);
                var property = serialized.FindProperty("curvedTerrainMaterial");
                if (property != null && property.objectReferenceValue == null)
                {
                    property.objectReferenceValue = material;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
#endif
        }

        void Awake() => EnsureWiring();

        void OnValidate()
        {
            var biomes = Biomes;
            if (biomes) biomes.populatePlanet = false;
            var curve = Curvature;
            var treadmill = Treadmill;
            if (curve && treadmill && !curve.Anchor) curve.Anchor = treadmill.Target;
        }

        void EnsureWiring()
        {
            if (!GetComponent<PlanetBiomeWrapRuntime>())
                gameObject.AddComponent<PlanetBiomeWrapRuntime>();
            var biomes = Biomes;
            if (biomes) biomes.populatePlanet = false;
            var curve = Curvature;
            var treadmill = Treadmill;
            if (curve && treadmill && !curve.Anchor) curve.Anchor = treadmill.Target;
        }
    }
}
