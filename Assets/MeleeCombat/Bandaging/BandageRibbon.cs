using UnityEngine;

namespace KenshiVR.Bandaging
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BandageRibbon : MonoBehaviour
    {
        WoundWrapZone zone;
        Mesh mesh;
        float width, thickness, startAngle, center;
        float visualRadiusScale = 1f;
        int samplesPerTurn, totalTurns;
        Vector3[] vertices;
        Vector2[] uvs;
        int[] triangles;
        static Material fallback;

        public void Initialize(WoundWrapZone target, Material material, float clothWidth, float clothThickness,
            int samples, int turns, float angle, float axialCenter, float radiusScale = 1f)
        {
            zone = target; width = clothWidth; thickness = clothThickness;
            visualRadiusScale = Mathf.Clamp(radiusScale, .5f, 1.1f);
            samplesPerTurn = Mathf.Max(8, samples); totalTurns = Mathf.Max(1, turns);
            startAngle = angle; center = axialCenter;
            int rows = samplesPerTurn * totalTurns + 2;
            vertices = new Vector3[rows * 2];
            uvs = new Vector2[vertices.Length];
            triangles = new int[(rows - 1) * 6];
            mesh = new Mesh { name = "Procedural bandage", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterial = material ? material : DefaultMaterial();
        }
        public void UpdateRibbon(float signedAngle, Vector3 freeTip, bool finished = false)
        {
            if (!zone || !mesh) return;
            float turns = Mathf.Abs(signedAngle) / 360f;
            int steps = Mathf.Min(samplesPerTurn * totalTurns, Mathf.CeilToInt(turns * samplesPerTurn));
            int direction = signedAngle < 0f ? -1 : 1;
            int rows = Mathf.Max(2, steps + 1 + (finished ? 0 : 1));
            float cap = Mathf.Max(0f, zone.length * .5f - width * .5f);
            float pitch = Mathf.Min(width * .82f, cap * 2f / Mathf.Max(1, totalTurns));
            for (int i = 0; i < rows; i++)
            {
                bool free = !finished && i == rows - 1;
                float fraction = steps == 0 ? 0f : Mathf.Min(1f, i / (float)steps);
                float a = (startAngle + signedAngle * fraction) * Mathf.Deg2Rad;
                float axial = Mathf.Clamp(center + (turns * fraction - totalTurns * .5f) * pitch, -cap, cap);
                float r = zone.RadiusAt(axial) * visualRadiusScale + zone.surfaceOffset + thickness;
                Vector3 first, second;
                if (free)
                {
                    var local = zone.bone.InverseTransformPoint(freeTip);
                    Vector3 widthAxis = zone.bone.InverseTransformVector(zone.bone.rotation * zone.localFrame * Vector3.up);
                    first = local - widthAxis * width * .5f;
                    second = local + widthAxis * width * .5f;
                }
                else
                {
                    first = zone.bone.InverseTransformPoint(zone.ToWorld(new Vector3(Mathf.Cos(a) * r, axial - width * .5f, Mathf.Sin(a) * r)));
                    second = zone.bone.InverseTransformPoint(zone.ToWorld(new Vector3(Mathf.Cos(a) * r, axial + width * .5f, Mathf.Sin(a) * r)));
                }
                vertices[i * 2] = first; vertices[i * 2 + 1] = second;
                uvs[i * 2] = new Vector2(0f, turns * fraction);
                uvs[i * 2 + 1] = new Vector2(1f, turns * fraction);
            }
            int t = 0;
            for (int i = 0; i < rows - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
            mesh.Clear();
            mesh.SetVertices(vertices, 0, rows * 2);
            mesh.SetUVs(0, uvs, 0, rows * 2);
            mesh.SetTriangles(triangles, 0, t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
        public void ExpireAfter(float seconds)
        {
            if (seconds > 0f) Destroy(gameObject, seconds);
        }
        static Material DefaultMaterial()
        {
            if (fallback) return fallback;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            fallback = new Material(shader) { name = "Bandage cloth" };
            fallback.SetColor("_BaseColor", new Color(.82f, .78f, .65f));
            fallback.doubleSidedGI = true;
            fallback.SetFloat("_Cull", 0f);
            return fallback;
        }
        void OnDestroy() { if (mesh) Destroy(mesh); }
    }
}
