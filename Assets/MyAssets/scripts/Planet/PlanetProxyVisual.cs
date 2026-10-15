using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Simple Task 2A planet proxy: a low-cost sphere plus optional transparent atmosphere shell.
    /// It is a visual handoff target only; it does not add high-detail physics.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Koombar/Planet/Planet Proxy Visual")]
    public sealed class PlanetProxyVisual : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private PlanetTransitionState transitionState;
        [SerializeField] private Transform proxyRoot;
        [SerializeField] private Material surfaceMaterial;
        [SerializeField] private Material atmosphereMaterial;
        [SerializeField] private Color generatedSurfaceColor = new Color(0.45f, 0.32f, 0.22f, 1f);
        [SerializeField] private Color generatedAtmosphereColor = new Color(0.25f, 0.55f, 1f, 0.22f);
        [SerializeField] private bool alwaysShowSurface = true;
        [SerializeField] private bool showAtmosphereShell = false;
        [SerializeField, Range(0f, 1f)] private float hideProxyBelowSurfaceFactor = 0.7f;
        [SerializeField, Range(0f, 1f)] private float hideSurfaceWhenOrbitalTerrainVisibleFactor = 0.25f;
        [SerializeField, Range(0f, 1f)] private float minimumSurfaceVisibility = 1f;
        [SerializeField] private bool alignToSurfaceVisualFrameInSurfaceMode = true;
        [SerializeField] private Transform surfaceVisualAnchor;
        [Tooltip("World-space distance to keep the planet proxy center below the surface/follow anchor while in Surface/planet mode. Use 0 to fall back to the planet radius. Increase this if the proxy is too close and clips through terrain.")]
        [SerializeField, Min(0f)] private float surfaceModeFollowDistanceMeters = 0f;
        [Tooltip("When enabled in Surface/planet mode, the proxy follows the anchor only on X/Z. Y is locked so altitude changes do not make the planet pop up/down.")]
        [SerializeField] private bool lockSurfaceModeY = true;
        [Tooltip("World Y treated as the flat surface height when Lock Surface Mode Y is enabled. Planet center is this value minus Surface Mode Follow Distance.")]
        [SerializeField] private float surfaceModeLockedSurfaceY = 0f;

        private MeshRenderer surfaceRenderer;
        private MeshRenderer atmosphereRenderer;
        private Color surfaceBaseColor;
        private Color atmosphereBaseColor;

        public void Configure(PlanetDefinition planetDefinition, PlanetTransitionState state)
        {
            planet = planetDefinition;
            transitionState = state;
            EnsureProxyObjects();
        }

        private void Reset()
        {
            planet = GetComponent<PlanetDefinition>();
            transitionState = GetComponent<PlanetTransitionState>();
        }

        private void Awake()
        {
            EnsureProxyObjects();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                EnsureProxyObjects();
        }

        private void LateUpdate()
        {
            if (planet == null)
                planet = GetComponent<PlanetDefinition>();
            if (transitionState == null)
                transitionState = GetComponent<PlanetTransitionState>();

            EnsureProxyObjects();
            if (planet == null || proxyRoot == null)
                return;

            proxyRoot.position = ResolveProxyPosition();
            proxyRoot.localRotation = Quaternion.identity;
            proxyRoot.localScale = Vector3.one;

            SetChildScale(surfaceRenderer, planet.RadiusMeters * 2f);
            SetChildScale(atmosphereRenderer, planet.AtmosphereRadiusMeters * 2f);

            float atmosphereFactor = transitionState != null ? transitionState.AtmosphereFactor : 1f;
            float spaceVisibility = transitionState != null
                ? Mathf.InverseLerp(hideProxyBelowSurfaceFactor, 1f, transitionState.AtmosphereFactor)
                : 1f;
            float orbitalHandoff = transitionState != null
                ? Mathf.InverseLerp(0f, hideSurfaceWhenOrbitalTerrainVisibleFactor, atmosphereFactor)
                : 1f;
            float surfaceVisibility = alwaysShowSurface ? 1f : Mathf.Max(minimumSurfaceVisibility, spaceVisibility * orbitalHandoff);

            SetRendererAlpha(surfaceRenderer, surfaceBaseColor, surfaceVisibility);
            SetRendererAlpha(atmosphereRenderer, atmosphereBaseColor, showAtmosphereShell && planet.HasAtmosphere ? spaceVisibility * 0.65f : 0f);
        }

        Vector3 ResolveProxyPosition()
        {
            if (alignToSurfaceVisualFrameInSurfaceMode && transitionState != null && transitionState.TravelMode == PlanetTravelMode.Surface)
            {
                Transform anchor = surfaceVisualAnchor != null ? surfaceVisualAnchor : transitionState.Target;
                if (anchor != null)
                {
                    float followDistance = ResolveSurfaceFollowDistance();
                    Vector3 position = anchor.position;
                    position.y = lockSurfaceModeY
                        ? transitionState.SurfaceOriginY + surfaceModeLockedSurfaceY - followDistance
                        : position.y - followDistance;
                    return position;
                }
            }

            return planet.Center;
        }

        float ResolveSurfaceFollowDistance()
        {
            float radius = planet != null ? planet.RadiusMeters : 1f;
            return surfaceModeFollowDistanceMeters > 0f ? surfaceModeFollowDistanceMeters : radius;
        }

        void EnsureProxyObjects()
        {
            if (proxyRoot == null)
            {
                Transform existing = transform.Find("PlanetProxyVisualRoot");
                proxyRoot = existing != null ? existing : new GameObject("PlanetProxyVisualRoot").transform;
                proxyRoot.SetParent(transform, false);
            }

            surfaceRenderer = EnsureSphere("PlanetProxySurface", surfaceRenderer, surfaceMaterial, generatedSurfaceColor);
            atmosphereRenderer = EnsureSphere("PlanetProxyAtmosphere", atmosphereRenderer, atmosphereMaterial, generatedAtmosphereColor);
            surfaceBaseColor = surfaceRenderer != null ? surfaceRenderer.sharedMaterial.color : generatedSurfaceColor;
            atmosphereBaseColor = atmosphereRenderer != null ? atmosphereRenderer.sharedMaterial.color : generatedAtmosphereColor;
        }

        MeshRenderer EnsureSphere(string childName, MeshRenderer renderer, Material assignedMaterial, Color generatedColor)
        {
            if (renderer != null)
                return renderer;

            Transform child = proxyRoot.Find(childName);
            GameObject go;
            if (child == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = childName;
                go.transform.SetParent(proxyRoot, false);
                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                    SafeDestroy(collider);
            }
            else
            {
                go = child.gameObject;
            }

            renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = assignedMaterial != null ? assignedMaterial : CreateGeneratedMaterial(childName + "Material", generatedColor);
            return renderer;
        }

        static Material CreateGeneratedMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            material.name = materialName;
            material.color = color;
            return material;
        }

        static void SetChildScale(MeshRenderer renderer, float diameter)
        {
            if (renderer != null)
                renderer.transform.localScale = Vector3.one * Mathf.Max(1f, diameter);
        }

        static void SetRendererAlpha(MeshRenderer renderer, Color baseColor, float alpha)
        {
            if (renderer == null)
                return;

            renderer.enabled = alpha > 0.01f;
            Material material = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
            if (material == null)
                return;

            Color color = baseColor;
            color.a *= Mathf.Clamp01(alpha);
            material.color = color;
        }

        static void SafeDestroy(Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
