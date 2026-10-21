using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    /// <summary>Makes root Transform edits visible on bone-bound fitting meshes in the editor.</summary>
    [ExecuteAlways]
    public sealed class SkinnedArmorFitAdjustment : MonoBehaviour
    {
        [SerializeField, HideInInspector] Vector3 baselinePosition;
        [SerializeField, HideInInspector] Quaternion baselineRotation = Quaternion.identity;
        [SerializeField, HideInInspector] Vector3 baselineScale = Vector3.one;
        [SerializeField, HideInInspector] bool initialized;
#if UNITY_EDITOR
        SkinnedMeshRenderer[] renderers;
        Mesh[] sources, previews;
        Vector3 lastPosition, lastScale;
        Quaternion lastRotation;

        public Matrix4x4 AdjustmentMatrix
        {
            get
            {
                var scale = new Vector3(SafeRatio(transform.localScale.x, baselineScale.x),
                    SafeRatio(transform.localScale.y, baselineScale.y), SafeRatio(transform.localScale.z, baselineScale.z));
                return Matrix4x4.TRS(transform.localPosition - baselinePosition,
                    transform.localRotation * Quaternion.Inverse(baselineRotation), scale);
            }
        }

        public void InitializeBaseline()
        {
            baselinePosition = transform.localPosition; baselineRotation = transform.localRotation;
            baselineScale = transform.localScale; initialized = true; BuildPreview(true);
        }

        void OnEnable()
        {
            if (!initialized) InitializeBaseline();
            else BuildPreview(true);
        }

        void Update()
        {
            if (Application.isPlaying) return;
            if (transform.localPosition != lastPosition || transform.localRotation != lastRotation || transform.localScale != lastScale)
                BuildPreview(false);
        }

        void OnDisable() => RestoreSources();
        void OnDestroy() => RestoreSources();

        void BuildPreview(bool rebuild)
        {
            if (Application.isPlaying) return;
            if (rebuild || renderers == null)
            {
                RestoreSources();
                renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                sources = new Mesh[renderers.Length]; previews = new Mesh[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    sources[i] = renderers[i].sharedMesh;
                    if (!sources[i]) continue;
                    previews[i] = Instantiate(sources[i]); previews[i].name = sources[i].name + " (fitting preview)";
                    previews[i].hideFlags = HideFlags.HideAndDontSave; renderers[i].sharedMesh = previews[i];
                }
            }
            var matrix = AdjustmentMatrix;
            for (int i = 0; i < renderers.Length; i++)
                if (sources[i] && previews[i]) ApplyMatrix(sources[i], previews[i], matrix);
            lastPosition = transform.localPosition; lastRotation = transform.localRotation; lastScale = transform.localScale;
        }

        void RestoreSources()
        {
            if (renderers != null && sources != null)
                for (int i = 0; i < renderers.Length && i < sources.Length; i++)
                    if (renderers[i] && sources[i]) renderers[i].sharedMesh = sources[i];
            if (previews != null) foreach (var mesh in previews) if (mesh) DestroyImmediate(mesh);
            renderers = null; sources = previews = null;
        }

        public Mesh[] SourceMeshes => sources;
        public void ResetTransformToBaseline()
        { transform.localPosition = baselinePosition; transform.localRotation = baselineRotation; transform.localScale = baselineScale; BuildPreview(false); }

        public static void ApplyMatrix(Mesh source, Mesh destination, Matrix4x4 matrix)
        {
            var vertices = source.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            destination.vertices = vertices;
            var normals = source.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                var normalMatrix = matrix.inverse.transpose;
                for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                destination.normals = normals;
            }
            destination.RecalculateBounds();
        }

        static float SafeRatio(float value, float baseline) => Mathf.Abs(baseline) < .00001f ? value : value / baseline;
#endif
    }
}
