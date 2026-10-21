using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Short convex sections preserve the handle/head silhouette and stay below PhysX's hull limit.
public static class ArmedWeaponColliderMesh
{
    const string Folder = "Assets/MeleeCombat/GeneratedWeapons/ContactMeshes";
    public static Mesh[] Build(Mesh source, string key)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/MeleeCombat/GeneratedWeapons", "ContactMeshes");
        var vertices = source.vertices;
        var triangles = source.triangles;
        var bounds = source.bounds;
        int axis = bounds.size.x > bounds.size.y ? (bounds.size.x > bounds.size.z ? 0 : 2) : (bounds.size.y > bounds.size.z ? 1 : 2);
        var meshes = new List<Mesh>();
        const int sections = 10;
        for (int section = 0; section < sections; section++)
        {
            float low = Mathf.Lerp(bounds.min[axis], bounds.max[axis], section / (float)sections);
            float high = Mathf.Lerp(bounds.min[axis], bounds.max[axis], (section + 1f) / sections);
            var points = new List<Vector3>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var polygon = new List<Vector3> { vertices[triangles[i]], vertices[triangles[i+1]], vertices[triangles[i+2]] };
                if (polygon.All(p => p[axis] < low) || polygon.All(p => p[axis] > high)) continue;
                polygon = Clip(polygon, axis, low, true);
                polygon = Clip(polygon, axis, high, false);
                points.AddRange(polygon);
            }
            if (points.Count < 4) continue;
            var support = new List<Vector3>();
            for (int x = -3; x <= 3; x++)
            for (int y = -3; y <= 3; y++)
            for (int z = -3; z <= 3; z++)
            {
                var direction = new Vector3(x, y, z);
                if (direction == Vector3.zero) continue;
                float best = float.NegativeInfinity; Vector3 selected = default;
                foreach (var p in points) { float dot = Vector3.Dot(direction, p); if (dot > best) { best = dot; selected = p; } }
                if (!support.Contains(selected) && support.Count < 240) support.Add(selected);
            }
            if (support.Count < 4) continue;
            var path = Folder + "/" + key + "_" + section + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = !mesh;
            if (created) mesh = new Mesh { name = key + " contact " + section };
            mesh.Clear(); mesh.SetVertices(support);
            var faces = new List<int>();
            for (int i = 1; i < support.Count - 1; i++) { faces.Add(0); faces.Add(i); faces.Add(i + 1); }
            mesh.SetTriangles(faces, 0); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            meshes.Add(mesh);
        }
        return meshes.ToArray();
    }
    static List<Vector3> Clip(List<Vector3> polygon, int axis, float plane, bool above)
    {
        var result = new List<Vector3>();
        if (polygon.Count == 0) return result;
        var previous = polygon[polygon.Count - 1];
        bool previousInside = above ? previous[axis] >= plane : previous[axis] <= plane;
        foreach (var current in polygon)
        {
            bool inside = above ? current[axis] >= plane : current[axis] <= plane;
            if (inside != previousInside)
                result.Add(Vector3.Lerp(previous, current, (plane - previous[axis]) / (current[axis] - previous[axis])));
            if (inside) result.Add(current);
            previous = current; previousInside = inside;
        }
        return result;
    }
}
