using System.Collections.Generic;
using UnityEngine;
using MyAssets.PlanetTerrain;

namespace MyAssets.Planet
{
    /// <summary>
    /// Deprecated visual-only low-resolution cube-sphere terrain proxy.
    /// The current planet POC deliberately uses no orbital terrain layer; this component keeps any old scene
    /// instances non-rendering unless explicitly re-enabled for future debugging work.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Orbital Terrain Mesh")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlanetOrbitalTerrainMesh : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private PlanetTerrainTreadmill terrainSource;
        [SerializeField] private PlanetTransitionState transitionState;
        [SerializeField] private bool orbitalTerrainEnabled = false;
        [SerializeField, Range(4, 96)] private int resolutionPerFace = 32;
        [SerializeField, Min(0f)] private float visualHeightScale = 1f;
        [SerializeField] private bool rebuildOnStart = true;
        [SerializeField] private bool driveVisibilityFromTransition = true;
        [SerializeField, Range(0f, 1f)] private float visibleAtAtmosphereFactor = 0.25f;
        [SerializeField] private Material material;

        [Header("Generated vertex colors")]
        [SerializeField] private Color lowlandColor = new Color(0.30f, 0.28f, 0.18f, 1f);
        [SerializeField] private Color midlandColor = new Color(0.45f, 0.34f, 0.22f, 1f);
        [SerializeField] private Color rockColor = new Color(0.34f, 0.32f, 0.30f, 1f);
        [SerializeField] private Color peakColor = new Color(0.72f, 0.68f, 0.58f, 1f);

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh generatedMesh;

        private const bool ProjectAllowsOrbitalTerrain = false;

        public void Configure(PlanetDefinition planetDefinition, PlanetTerrainTreadmill treadmill, PlanetTransitionState state)
        {
            planet = planetDefinition;
            terrainSource = treadmill;
            transitionState = state;
        }

        private void Reset()
        {
            planet = FindFirstObjectByType<PlanetDefinition>();
            terrainSource = FindFirstObjectByType<PlanetTerrainTreadmill>();
            transitionState = FindFirstObjectByType<PlanetTransitionState>();
        }

        private void Awake()
        {
            ResolveComponents();
            ApplyEnabledState();
        }

        private void Start()
        {
            if (IsOrbitalTerrainAllowed && rebuildOnStart)
                RebuildMesh();
        }

        private void LateUpdate()
        {
            if (planet == null)
                planet = FindFirstObjectByType<PlanetDefinition>();
            if (transitionState == null)
                transitionState = FindFirstObjectByType<PlanetTransitionState>();

            if (planet != null)
                transform.position = planet.Center;

            if (!IsOrbitalTerrainAllowed)
            {
                if (meshRenderer != null)
                    meshRenderer.enabled = false;
                return;
            }

            if (driveVisibilityFromTransition && meshRenderer != null && transitionState != null)
                meshRenderer.enabled = transitionState.AtmosphereFactor >= visibleAtAtmosphereFactor && generatedMesh != null;
        }

        [ContextMenu("Rebuild Orbital Terrain Mesh")]
        public void RebuildMesh()
        {
            ResolveComponents();
            if (!IsOrbitalTerrainAllowed)
            {
                ApplyEnabledState();
                return;
            }

            if (planet == null)
                planet = FindFirstObjectByType<PlanetDefinition>();
            if (terrainSource == null)
                terrainSource = FindFirstObjectByType<PlanetTerrainTreadmill>();
            if (planet == null || terrainSource == null)
            {
                Debug.LogWarning("[PlanetOrbitalTerrainMesh] Requires a PlanetDefinition and PlanetTerrainTreadmill to build.", this);
                return;
            }

            generatedMesh = BuildCubeSphereMesh();
            generatedMesh.name = "PlanetOrbitalTerrainMesh";
            meshFilter.sharedMesh = generatedMesh;
            if (material != null)
                meshRenderer.sharedMaterial = material;
            else if (meshRenderer.sharedMaterial == null)
                meshRenderer.sharedMaterial = CreateDefaultMaterial();
        }

        void ResolveComponents()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();
        }

        void ApplyEnabledState()
        {
            if (meshRenderer != null)
                meshRenderer.enabled = IsOrbitalTerrainAllowed && generatedMesh != null;
        }

        bool IsOrbitalTerrainAllowed => ProjectAllowsOrbitalTerrain && orbitalTerrainEnabled;

        Mesh BuildCubeSphereMesh()
        {
            int res = Mathf.Clamp(resolutionPerFace, 4, 96);
            var vertices = new List<Vector3>(6 * (res + 1) * (res + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var colors = new List<Color>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(6 * res * res * 6);

            for (int face = 0; face < 6; face++)
            {
                int baseIndex = vertices.Count;
                for (int y = 0; y <= res; y++)
                {
                    float v = y / (float)res;
                    for (int x = 0; x <= res; x++)
                    {
                        float u = x / (float)res;
                        Vector2 faceUv = new Vector2(u * 2f - 1f, v * 2f - 1f);
                        Vector3 dir = FaceUvToDirection((PlanetCubeFace)face, faceUv);
                        float height01 = SampleHeight01(faceUv, (PlanetCubeFace)face);
                        float worldHeight = height01 * terrainSource.GenerationSettings.maxHeightMeters * visualHeightScale;
                        vertices.Add(dir * (planet.RadiusMeters + worldHeight));
                        normals.Add(dir);
                        colors.Add(EvaluateColor(height01, EstimateSlope01(faceUv, (PlanetCubeFace)face)));
                        uvs.Add(new Vector2(u, v));
                    }
                }

                int row = res + 1;
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        int i0 = baseIndex + y * row + x;
                        int i1 = i0 + 1;
                        int i2 = i0 + row;
                        int i3 = i2 + 1;
                        AddOutwardTriangle(triangles, vertices, i0, i2, i1);
                        AddOutwardTriangle(triangles, vertices, i1, i2, i3);
                    }
                }
            }

            var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddOutwardTriangle(List<int> triangles, List<Vector3> vertices, int a, int b, int c)
        {
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            Vector3 center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            if (Vector3.Dot(normal, center) < 0f)
            {
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                return;
            }

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        float SampleHeight01(Vector2 faceUv, PlanetCubeFace face)
        {
            PlanetSamplePoint sample = FaceUvToSamplePoint(faceUv, face);
            return terrainSource.HeightProvider.SampleHeight01(sample);
        }

        float EstimateSlope01(Vector2 faceUv, PlanetCubeFace face)
        {
            float step = 2f / Mathf.Max(4f, resolutionPerFace);
            float hL = SampleHeight01(faceUv + Vector2.left * step, face);
            float hR = SampleHeight01(faceUv + Vector2.right * step, face);
            float hD = SampleHeight01(faceUv + Vector2.down * step, face);
            float hU = SampleHeight01(faceUv + Vector2.up * step, face);
            return Mathf.Clamp01((Mathf.Abs(hR - hL) + Mathf.Abs(hU - hD)) * 3.5f);
        }

        PlanetSamplePoint FaceUvToSamplePoint(Vector2 faceUv, PlanetCubeFace face)
        {
            float faceMeters = Mathf.Max(terrainSource.GenerationSettings.tileSizeMeters, planet.RadiusMeters * 2f);
            float offset = ((int)face + 1) * faceMeters * 1.37f;
            return new PlanetSamplePoint(faceUv.x * faceMeters * 0.5f + offset, faceUv.y * faceMeters * 0.5f - offset);
        }

        Color EvaluateColor(float height01, float slope01)
        {
            Color heightColor = height01 < 0.45f
                ? Color.Lerp(lowlandColor, midlandColor, Mathf.InverseLerp(0.12f, 0.45f, height01))
                : Color.Lerp(midlandColor, peakColor, Mathf.InverseLerp(0.45f, 0.95f, height01));

            return Color.Lerp(heightColor, rockColor, Mathf.Clamp01(slope01));
        }

        static Vector3 FaceUvToDirection(PlanetCubeFace face, Vector2 uv)
        {
            Vector3 p;
            switch (face)
            {
                case PlanetCubeFace.PositiveX: p = new Vector3(1f, uv.y, -uv.x); break;
                case PlanetCubeFace.NegativeX: p = new Vector3(-1f, uv.y, uv.x); break;
                case PlanetCubeFace.PositiveY: p = new Vector3(uv.x, 1f, -uv.y); break;
                case PlanetCubeFace.NegativeY: p = new Vector3(uv.x, -1f, uv.y); break;
                case PlanetCubeFace.PositiveZ: p = new Vector3(uv.x, uv.y, 1f); break;
                default: p = new Vector3(-uv.x, uv.y, -1f); break;
            }

            return p.normalized;
        }

        static Material CreateDefaultMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Hidden/InternalErrorShader");

            return shader != null ? new Material(shader) { name = "PlanetOrbitalTerrainMaterial" } : null;
        }
    }
}
