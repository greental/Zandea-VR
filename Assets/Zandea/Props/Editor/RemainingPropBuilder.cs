using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.Props.Editor
{
    public static class RemainingPropBuilder
    {
        const string Source = "Assets/Zandea/Props/Remaining/";
        const string Prefabs = "Assets/MyAssets/prefabs/buildings/props/";
        const string Request = "Tools/Props/work/remaining-import/build.request";
        const string Result = "Tools/Props/work/remaining-import/build.result";

        [Serializable] sealed class Manifest { public Item[] items; }
        [Serializable] sealed class Item
        {
            public string key;
            public string kind;
            public string category;
            public float sizeMeters;
            public Part[] parts;
        }
        [Serializable] sealed class Part { public string id; public string meshContains; public string kind; }

        [InitializeOnLoadMethod]
        static void WatchRequest() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try
            {
                Build();
                File.WriteAllText(Result, "OK");
            }
            catch (Exception error)
            {
                File.WriteAllText(Result, error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Zandea/Props/Build Remaining Downloaded Prefabs")]
        public static void Build()
        {
            var manifestPath = Source + "manifest.json";
            if (!File.Exists(manifestPath)) throw new FileNotFoundException(manifestPath);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.items == null || manifest.items.Length == 0)
                throw new InvalidDataException("Empty remaining prop manifest");
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Source + "Materials");
            var entries = new List<RemainingPropCatalog.Entry>();
            foreach (var item in manifest.items)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "Models/" + item.key + ".fbx");
                if (!model) throw new FileNotFoundException("Missing prop FBX: " + item.key);
                var root = new GameObject("Zandea " + item.key.Replace('_', ' '));
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    visual.name = "Visual";
                    visual.transform.SetParent(root.transform, false);
                    visual.transform.localPosition = Vector3.zero;
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) throw new InvalidDataException("No renderer: " + item.key);
                    foreach (var renderer in renderers)
                    {
                        var slots = renderer.sharedMaterials;
                        for (var i = 0; i < slots.Length; i++)
                        {
                            var index = MaterialIndex(slots[i] ? slots[i].name : "", i);
                            slots[i] = GetMaterial(item.key, index);
                        }
                        renderer.sharedMaterials = slots;
                    }
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    if (bounds.size.magnitude < .001f) throw new InvalidDataException("Zero bounds: " + item.key);
                    var longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    if (longest < item.sizeMeters * .5f || longest > item.sizeMeters * 2f)
                    {
                        visual.transform.localScale *= item.sizeMeters / longest;
                        bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    }
                    if (Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) > item.sizeMeters * 1.1f)
                        throw new InvalidDataException("Implausible bounds: " + item.key + " " + bounds.size);
                    visual.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                    if (item.kind == "physical")
                    {
                        var box = root.AddComponent<BoxCollider>();
                        box.size = bounds.size;
                        box.center = new Vector3(0, bounds.extents.y, 0);
                        var body = root.AddComponent<Rigidbody>();
                        body.mass = Mathf.Clamp(bounds.size.magnitude * 1.5f, .4f, 15f);
                        body.interpolation = RigidbodyInterpolation.Interpolate;
                        var grab = root.AddComponent<XRGrabInteractable>();
                        grab.useDynamicAttach = true;
                        grab.throwOnDetach = true;
                        root.AddComponent<PhysicalProp>();
                    }
                    else if (item.kind == "static")
                    {
                        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (!filter.sharedMesh) continue;
                            var collider = filter.gameObject.AddComponent<MeshCollider>();
                            collider.sharedMesh = filter.sharedMesh;
                        }
                    }
                    else throw new InvalidDataException("Unknown prop kind " + item.kind);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "Zandea_" + item.key + ".prefab");
                    if (!prefab) throw new IOException("Could not save prefab " + item.key);
                    entries.Add(new RemainingPropCatalog.Entry { id = item.key, category = item.category, prefab = prefab });
                    if (item.parts != null) BuildParts(item, visual, entries);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var catalogPath = Source + "RemainingPropCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<RemainingPropCatalog>(catalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<RemainingPropCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.entries = entries.ToArray();
            EditorUtility.SetDirty(catalog);
            var outpost = AssetDatabase.LoadAssetAtPath<OutpostPropDefinition>(
                "Assets/Zandea/Props/Resources/ZandeaOutpostProps.asset");
            if (outpost)
            {
                outpost.additionalProps = catalog;
                EditorUtility.SetDirty(outpost);
            }
            AssetDatabase.SaveAssets();
            if (RemainingVesselBreakageBuilder.AllFracturesPresent())
                RemainingVesselBreakageBuilder.Build();
            Debug.Log("[Zandea Props] Built " + entries.Count + " remaining prop prefabs and catalog.");
        }

        static void BuildParts(Item item, GameObject visual, List<RemainingPropCatalog.Entry> entries)
        {
            var filters = visual.GetComponentsInChildren<MeshFilter>(true);
            foreach (var part in item.parts)
            {
                MeshFilter source = null;
                foreach (var filter in filters)
                    if (filter.name.IndexOf(part.meshContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (source) throw new InvalidDataException("Ambiguous kit part " + item.key + ":" + part.id);
                        source = filter;
                    }
                if (!source || !source.sharedMesh) throw new InvalidDataException("Missing kit part " + item.key + ":" + part.id);
                var sourceRenderer = source.GetComponent<MeshRenderer>();
                if (!sourceRenderer) throw new InvalidDataException("Missing renderer for " + item.key + ":" + part.id);
                var id = item.key + "_" + part.id;
                var root = new GameObject("Zandea " + id.Replace('_', ' '));
                try
                {
                    var meshObject = new GameObject("Visual");
                    meshObject.transform.SetParent(root.transform, false);
                    meshObject.transform.position = source.transform.position;
                    meshObject.transform.rotation = source.transform.rotation;
                    meshObject.transform.localScale = source.transform.lossyScale;
                    meshObject.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                    var renderer = meshObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                    var bounds = renderer.bounds;
                    if (bounds.size.magnitude < .001f) throw new InvalidDataException("Zero bounds for " + id);
                    meshObject.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                    if (part.kind == "physical")
                    {
                        var box = root.AddComponent<BoxCollider>();
                        box.size = bounds.size;
                        box.center = new Vector3(0, bounds.extents.y, 0);
                        var body = root.AddComponent<Rigidbody>();
                        body.mass = Mathf.Clamp(bounds.size.magnitude * 1.5f, .4f, 15f);
                        body.interpolation = RigidbodyInterpolation.Interpolate;
                        var grab = root.AddComponent<XRGrabInteractable>();
                        grab.useDynamicAttach = true;
                        grab.throwOnDetach = true;
                        root.AddComponent<PhysicalProp>();
                    }
                    else if (part.kind == "static")
                        meshObject.AddComponent<MeshCollider>().sharedMesh = source.sharedMesh;
                    else throw new InvalidDataException("Unknown kit part kind " + part.kind);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "Zandea_" + id + ".prefab");
                    if (!prefab) throw new IOException("Could not save kit part " + id);
                    entries.Add(new RemainingPropCatalog.Entry { id = id, category = item.category, prefab = prefab });
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        static int MaterialIndex(string name, int fallback)
        {
            var marker = name.LastIndexOf("_mat_", StringComparison.Ordinal);
            if (marker >= 0 && int.TryParse(name.Substring(marker + 5, 2), out var parsed)) return parsed;
            return fallback;
        }

        static Material GetMaterial(string key, int index)
        {
            var texturePath = Source + "Textures/" + key + "_" + index.ToString("00") + "_base.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (!texture) throw new FileNotFoundException("Missing prop albedo: " + texturePath);
            var materialPath = Source + "Materials/" + key + "_" + index.ToString("00") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key + "_" + index.ToString("00") };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .18f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
