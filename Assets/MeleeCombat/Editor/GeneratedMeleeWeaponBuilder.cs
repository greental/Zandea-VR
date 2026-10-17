using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat.Editor
{
    /// <summary>Converts imported generated GLBs to physics based XR and NPC melee prefabs.</summary>
    public static class GeneratedMeleeWeaponBuilder
    {
        const string Input = "Assets/MyAssets/mesh/weapons/Generated";
        const string Output = "Assets/MeleeCombat/GeneratedWeapons";

        [MenuItem("Tools/Melee Combat/Build missing generated weapons")]
        public static void BuildMissing() => Build(false);

        [MenuItem("Tools/Melee Combat/Rebuild generated weapons (discard prefab tuning)")]
        public static void RebuildAll() => Build(true);

        static void Build(bool overwrite)
        {
            Directory.CreateDirectory(Output);
            int built = 0, skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { Input }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;
                string destination = Output + "/" + Path.GetFileNameWithoutExtension(path) + ".prefab";
                if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(destination)) { skipped++; continue; }
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!source) { Debug.LogWarning("GLB has no imported GameObject: " + path); continue; }
                try { BuildOne(source, destination); built++; }
                catch (Exception error) { Debug.LogError("Could not build " + path + ": " + error); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Generated melee weapons: built {built}, kept {skipped} tuned prefabs. Output: {Output}");
        }

        static void BuildOne(GameObject source, string destination)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(destination));
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                visual.name = "Visual";
                foreach (var importedBody in visual.GetComponentsInChildren<Rigidbody>(true))
                    UnityEngine.Object.DestroyImmediate(importedBody);
                foreach (var importedShape in visual.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(importedShape);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("Model has no renderers");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                int axis = bounds.size.x > bounds.size.y
                    ? (bounds.size.x > bounds.size.z ? 0 : 2)
                    : (bounds.size.y > bounds.size.z ? 1 : 2);
                // Importers vary in axis and unit scale. Put the longest dimension on +Z.
                visual.transform.localRotation = axis == 0 ? Quaternion.Euler(0, -90, 0)
                    : axis == 1 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                bounds = BoundsOf(renderers);
                float rawLength = bounds.size.z;
                if (rawLength < .001f) throw new InvalidOperationException("Model has zero length");
                // 1 m default length is only a starting point; tune per weapon in the prefab.
                visual.transform.localScale = Vector3.one / rawLength;
                bounds = BoundsOf(renderers);
                visual.transform.position -= bounds.center;
                bounds = BoundsOf(renderers);

                float length = bounds.size.z;
                float radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.y) * .35f, .015f, .12f);
                var body = root.AddComponent<Rigidbody>();
                body.mass = 1.4f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                var shape = root.AddComponent<CapsuleCollider>();
                shape.direction = 2;
                shape.height = Mathf.Max(length, radius * 2.01f);
                shape.radius = radius;
                var grip = Marker(root.transform, "Grip", new Vector3(0, 0, -length * .32f));
                var rightGrip = Marker(root.transform, "Right grip", grip.localPosition);
                var leftGrip = Marker(root.transform, "Left grip", grip.localPosition);
                rightGrip.localRotation = grip.localRotation;
                leftGrip.localRotation = grip.localRotation;
                Marker(root.transform, "Positive striking tip", new Vector3(0, 0, length * .5f));
                Marker(root.transform, "Negative striking tip", new Vector3(0, 0, -length * .5f));
                var grab = root.AddComponent<XRGrabInteractable>();
                grab.attachTransform = grip;
                var weapon = root.AddComponent<PhysicalMeleeWeapon>();
                weapon.grip = grip;
                weapon.rightGrip = rightGrip;
                weapon.leftGrip = leftGrip;
                weapon.mass = body.mass;
                weapon.strikeType = PhysicalMeleeWeapon.StrikeType.Cutting;
                PrefabUtility.SaveAsPrefabAsset(root, destination);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Transform Marker(Transform parent, string name, Vector3 position)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = position;
            return marker;
        }
    }
}
