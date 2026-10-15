using System;
using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat
{
    public sealed class ClothingLootMeshes : MonoBehaviour
    {
        public Mesh[] owned;
        void OnDestroy()
        {
            if (owned == null) return;
            foreach (var mesh in owned)
                if (mesh)
                {
                    if (Application.isPlaying) Destroy(mesh);
                    else DestroyImmediate(mesh);
                }
        }
    }

    public static class ClothingLootFactory
    {
        public sealed class Snapshot : IDisposable
        {
            internal readonly List<Mesh> meshes = new();
            internal readonly List<Material[]> materials = new();
            internal Bounds bounds;
            internal bool hasBounds;
            public bool Valid => meshes.Count > 0;
            public Bounds Bounds => bounds;
            public void Dispose()
            {
                foreach (var mesh in meshes)
                    if (mesh)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                        else UnityEngine.Object.DestroyImmediate(mesh);
                    }
                meshes.Clear(); materials.Clear();
            }
        }

        public static Snapshot Capture(AppearanceSpawnedItem worn, NpcItemDefinition item)
        {
            return Capture(worn ? worn.transform : null, item);
        }
        public static Snapshot Capture(Transform worn, NpcItemDefinition item)
        {
            var snapshot = new Snapshot();
            if (!worn || !item) return snapshot;
            Matrix4x4 worldToRoot = worn.worldToLocalMatrix;
            foreach (var renderer in worn.GetComponentsInChildren<Renderer>(true))
            {
                Mesh source = null;
                bool baked = false;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
                {
                    source = new Mesh();
                    skin.BakeMesh(source);
                    baked = true;
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter)) source = filter.sharedMesh;
                if (!source) continue;
                Mesh mesh = baked ? source : UnityEngine.Object.Instantiate(source);
                mesh.name = item.itemId + "_loot_" + snapshot.meshes.Count;
                if (mesh.isReadable)
                {
                    var vertices = mesh.vertices;
                    var normals = mesh.normals;
                    Matrix4x4 toRoot = worldToRoot * renderer.localToWorldMatrix;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                    if (normals != null && normals.Length == vertices.Length)
                        for (int i = 0; i < normals.Length; i++) normals[i] = toRoot.MultiplyVector(normals[i]).normalized;
                    Flatten(vertices, normals, item);
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    mesh.RecalculateNormals();
                    if (mesh.uv != null && mesh.uv.Length == vertices.Length) mesh.RecalculateTangents();
                }
                else Debug.LogWarning("Clothing mesh is not readable; loot keeps its original shape: " + renderer.name, worn);
                snapshot.meshes.Add(mesh);
                snapshot.materials.Add(renderer.sharedMaterials);
                if (!snapshot.hasBounds) { snapshot.bounds = mesh.bounds; snapshot.hasBounds = true; }
                else snapshot.bounds.Encapsulate(mesh.bounds);
            }
            return snapshot;
        }

        static void Flatten(Vector3[] vertices, Vector3[] normals, NpcItemDefinition item)
        {
            if (item.flattenAxis == GarmentFlattenAxis.None || vertices.Length == 0) return;
            int axis = (int)item.flattenAxis - 1;
            float center = 0;
            foreach (var vertex in vertices) center += vertex[axis];
            center /= vertices.Length;
            float half = item.minimumThickness * .5f;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float offset = v[axis] - center;
                float side = offset;
                if (Mathf.Abs(side) < half && normals != null && normals.Length == vertices.Length) side = normals[i][axis];
                if (side == 0) side = i % 2 == 0 ? 1 : -1;
                v[axis] = center + Mathf.Sign(side) *
                    Mathf.Max(Mathf.Abs(offset) * item.flattenFactor, half);
                vertices[i] = v;
            }
        }

        public static GameObject Create(NpcItemDefinition item, Snapshot snapshot, Vector3 position, Quaternion rotation, Vector3 scale = default)
        {
            if (!item || snapshot == null || !snapshot.Valid) return null;
            var root = new GameObject(item.displayName + " loot");
            root.SetActive(false);
            root.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(item.lootEuler));
            root.transform.localScale = scale == default ? Vector3.one : scale;
            for (int i = 0; i < snapshot.meshes.Count; i++)
            {
                var visual = new GameObject("Cloth " + i);
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = snapshot.meshes[i];
                visual.AddComponent<MeshRenderer>().sharedMaterials = snapshot.materials[i];
            }
            var bounds = snapshot.bounds;
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = new Vector3(Mathf.Max(.04f, bounds.size.x), Mathf.Max(.04f, bounds.size.y), Mathf.Max(.025f, bounds.size.z));
            var body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(.12f, item.weight);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var grip = new GameObject("Grip").transform;
            grip.SetParent(root.transform, false);
            grip.localPosition = GripPoint(bounds, item);
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.attachTransform = grip;
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = false;
            grab.matchAttachRotation = true;
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            var owned = root.AddComponent<ClothingLootMeshes>();
            owned.owned = snapshot.meshes.ToArray();
            snapshot.meshes.Clear(); snapshot.materials.Clear();
            root.SetActive(true);
            return root;
        }
        public static Vector3 GripPoint(Bounds bounds, NpcItemDefinition item) =>
            bounds.center + Vector3.up * bounds.extents.y * .75f + Vector3.forward * bounds.extents.z + item.gripOffset;
    }
}
