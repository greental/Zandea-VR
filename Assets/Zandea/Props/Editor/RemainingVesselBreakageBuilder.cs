using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zandea.Props.Editor
{
    public static class RemainingVesselBreakageBuilder
    {
        const string Folder = "Assets/Zandea/Props/Remaining/";
        const string Prefabs = "Assets/MyAssets/prefabs/buildings/props/";
        const string Request = "Tools/Props/work/remaining-import/fracture-build.request";
        const string Result = "Tools/Props/work/remaining-import/fracture-build.result";

        [Serializable] sealed class Manifest { public Vessel[] items; }
        [Serializable] sealed class Vessel { public string id; public string source; public string meshContains; }

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

        [MenuItem("Zandea/Props/Build All Vessel Breakage")]
        public static void Build()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "fracture_manifest.json"));
            if (manifest == null || manifest.items == null || manifest.items.Length == 0)
                throw new InvalidDataException("Empty vessel fracture manifest");
            foreach (var vessel in manifest.items) BuildOne(vessel.id);
            UpgradeMarketPotGroup();
            CreateRuntimeDefinition();
            int authored = UpgradeAuthoredSettlement();
            AssetDatabase.SaveAssets();
            Debug.Log("[Zandea Props] Built offline breakage for " + manifest.items.Length +
                      " vessel prefabs and " + authored + " directly authored Pueblo vessels.");
        }

        public static bool AllFracturesPresent()
        {
            var path = Folder + "fracture_manifest.json";
            if (!File.Exists(path)) return false;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.items == null || manifest.items.Length == 0) return false;
            foreach (var vessel in manifest.items)
                if (!File.Exists(Folder + "Fractures/Models/" + vessel.id + "_fractured.fbx")) return false;
            return true;
        }

        static void BuildOne(string id)
        {
            var intactPath = Prefabs + id + ".prefab";
            var fracturePath = Folder + "Fractures/Models/" + id + "_fractured.fbx";
            var intactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(intactPath);
            var fractureModel = AssetDatabase.LoadAssetAtPath<GameObject>(fracturePath);
            if (!intactPrefab || !fractureModel) throw new FileNotFoundException("Intact or fractured vessel missing: " + id);

            var intact = PrefabUtility.LoadPrefabContents(intactPath);
            try
            {
                var originalScale = intact.transform.localScale;
                intact.transform.localScale = Vector3.one;
                var intactBounds = Bounds(intact);
                intact.transform.localScale = originalScale;
                var intactRenderer = intact.GetComponentInChildren<MeshRenderer>(true);
                if (!intactRenderer || intactRenderer.sharedMaterials.Length == 0)
                    throw new InvalidDataException("No vessel material: " + id);

                var brokenRoot = new GameObject(id + " broken pieces");
                try
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(fractureModel);
                    model.transform.SetParent(brokenRoot.transform, false);
                    var meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
                    if (meshFilters.Length < 5) throw new InvalidDataException("Too few offline shards: " + id);
                    var intactLongest = Mathf.Max(intactBounds.size.x, intactBounds.size.y, intactBounds.size.z);
                    var sourceScale = model.transform.localScale;
                    var prefabRotation = intact.GetComponentInChildren<MeshFilter>(true).transform.localRotation;
                    var rotations = id.StartsWith("Pueblo_", StringComparison.Ordinal)
                        ? new[] { Quaternion.identity, prefabRotation, Quaternion.Euler(90, 0, 0), Quaternion.Euler(-90, 0, 0) }
                        : new[] { Quaternion.identity };
                    float bestError = float.PositiveInfinity;
                    Quaternion bestRotation = Quaternion.identity;
                    foreach (var rotation in rotations)
                    {
                        model.transform.localRotation = rotation;
                        var candidate = Bounds(model);
                        var longest = Mathf.Max(candidate.size.x, candidate.size.y, candidate.size.z);
                        if (longest < .001f) continue;
                        float factor = intactLongest / longest;
                        float error = 0;
                        for (int axis = 0; axis < 3; axis++)
                            error += Mathf.Abs(candidate.size[axis] * factor - intactBounds.size[axis]) /
                                     Mathf.Max(.03f, intactBounds.size[axis]);
                        if (error < bestError) { bestError = error; bestRotation = rotation; }
                    }
                    model.transform.localRotation = bestRotation;
                    var brokenBounds = Bounds(model);
                    var brokenLongest = Mathf.Max(brokenBounds.size.x, brokenBounds.size.y, brokenBounds.size.z);
                    if (brokenLongest < .001f) throw new InvalidDataException("Zero fracture bounds: " + id);
                    model.transform.localScale = sourceScale * (intactLongest / brokenLongest);
                    brokenBounds = Bounds(model);
                    model.transform.localPosition += intactBounds.center - brokenBounds.center;
                    brokenBounds = Bounds(model);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        var expected = intactBounds.size[axis];
                        var actual = brokenBounds.size[axis];
                        if (expected > .03f && Mathf.Abs(actual - expected) > expected * .35f)
                            throw new InvalidDataException("Fracture axis does not match intact vessel " + id +
                                                           " axis " + axis + ": " + expected + " vs " + actual);
                    }
                    foreach (var filter in meshFilters)
                    {
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (!renderer) throw new InvalidDataException("Shard renderer missing: " + id);
                        var slots = renderer.sharedMaterials;
                        for (int i = 0; i < slots.Length; i++)
                            slots[i] = intactRenderer.sharedMaterials[Mathf.Min(i, intactRenderer.sharedMaterials.Length - 1)];
                        renderer.sharedMaterials = slots;
                        var collider = filter.gameObject.AddComponent<BoxCollider>();
                        collider.center = filter.sharedMesh.bounds.center;
                        collider.size = filter.sharedMesh.bounds.size;
                        var body = filter.gameObject.AddComponent<Rigidbody>();
                        body.mass = Mathf.Clamp(intactLongest * .12f, .04f, .2f);
                    }
                    brokenRoot.AddComponent<PropDebrisLifetime>();
                    var brokenPrefab = PrefabUtility.SaveAsPrefabAsset(brokenRoot, Prefabs + id + "_broken.prefab");
                    if (!brokenPrefab) throw new IOException("Could not save broken vessel " + id);
                    var breakable = intact.GetComponent<BreakableProp>();
                    if (!breakable) breakable = intact.AddComponent<BreakableProp>();
                    breakable.fracturedPrefab = brokenPrefab;
                    var metal = id.Contains("brass");
                    breakable.minimumImpactSpeed = metal ? 4.5f : 2.5f;
                    breakable.collisionEnergyThreshold = metal ? 18f : 7f;
                    breakable.meleeDamageThreshold = metal ? 8f : 4f;
                    breakable.requireWeaponDamage = false;
                    if (!intact.GetComponent<Rigidbody>()) throw new InvalidDataException("No Rigidbody: " + id);
                    PrefabUtility.SaveAsPrefabAsset(intact, intactPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(brokenRoot); }
            }
            finally { PrefabUtility.UnloadPrefabContents(intact); }
        }

        static Bounds Bounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidDataException("No renderers: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static int UpgradeAuthoredSettlement()
        {
            const string authoredPath = "Assets/MyAssets/prefabs/buildings/PuebloBonito/PuebloAuthoredSettlement.prefab";
            if (!File.Exists(authoredPath)) return 0;
            var root = PrefabUtility.LoadPrefabContents(authoredPath);
            try
            {
                int count = 0;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh || filter.GetComponentInParent<BreakableProp>()) continue;
                    var source = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    string id = null;
                    foreach (var name in new[] { "earth_pot", "round_jar", "tall_jar", "terracotta_pot" })
                        if (source == "Assets/Zandea/Pueblo/Models/" + name + ".fbx") id = "Pueblo_" + name;
                    if (id == null) continue;
                    var broken = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + id + "_broken.prefab");
                    if (!broken) throw new FileNotFoundException("Authored vessel debris missing: " + id);
                    var inheritedBody = filter.GetComponentInParent<Rigidbody>();
                    var target = inheritedBody ? inheritedBody.gameObject : filter.gameObject;
                    if (!target.GetComponentInChildren<Collider>())
                    {
                        var collider = filter.gameObject.AddComponent<BoxCollider>();
                        collider.center = filter.sharedMesh.bounds.center;
                        collider.size = filter.sharedMesh.bounds.size;
                    }
                    var body = target.GetComponent<Rigidbody>();
                    if (!body)
                    {
                        body = target.AddComponent<Rigidbody>();
                        body.isKinematic = true;
                        body.useGravity = false;
                    }
                    var breakable = target.AddComponent<BreakableProp>();
                    breakable.fracturedPrefab = broken;
                    breakable.minimumImpactSpeed = 2.5f;
                    breakable.collisionEnergyThreshold = 7f;
                    breakable.meleeDamageThreshold = 4f;
                    count++;
                }
                if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, authoredPath);
                return count;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void UpgradeMarketPotGroup()
        {
            const string path = Prefabs + "Zandea_market_pots.prefab";
            if (!File.Exists(path)) return;
            var source = PrefabUtility.LoadPrefabContents(path);
            var ids = new[] { "pot01_a", "pot01_b", "pot01_c", "pot02_a", "pot02_b", "pot02_c", "pot02_d" };
            var needles = new[] { "Pot_01_010", "Pot_01_011", "Pot_01_012", "Pot_02_022",
                                  "Pot_02_024", "Pot_02_025", "Pot_02_026" };
            var positions = new Vector3[ids.Length];
            try
            {
                if (source.GetComponentsInChildren<BreakableProp>(true).Length == ids.Length) return;
                var filters = source.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < ids.Length; i++)
                {
                    MeshFilter match = null;
                    foreach (var filter in filters)
                        if (filter.name.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (match) throw new InvalidDataException("Ambiguous market pot " + needles[i]);
                            match = filter;
                        }
                    if (!match) throw new InvalidDataException("Missing market pot " + needles[i]);
                    var bounds = match.GetComponent<Renderer>().bounds;
                    positions[i] = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(source); }
            var group = new GameObject("Zandea market pots");
            try
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    var part = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Zandea_market_pots_" + ids[i] + ".prefab");
                    if (!part || !part.GetComponent<BreakableProp>())
                        throw new InvalidDataException("Market pot is not breakable: " + ids[i]);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(part);
                    instance.transform.SetParent(group.transform, false);
                    instance.transform.localPosition = positions[i];
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(group, path);
                var catalog = AssetDatabase.LoadAssetAtPath<RemainingPropCatalog>(Folder + "RemainingPropCatalog.asset");
                if (catalog)
                {
                    foreach (var entry in catalog.entries)
                        if (entry.id == "market_pots") entry.prefab = prefab;
                    EditorUtility.SetDirty(catalog);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(group); }
        }

        static void CreateRuntimeDefinition()
        {
            const string path = "Assets/Zandea/Props/Resources/ZandeaVesselBreakage.asset";
            var definition = AssetDatabase.LoadAssetAtPath<VesselBreakageDefinition>(path);
            if (!definition)
            {
                definition = ScriptableObject.CreateInstance<VesselBreakageDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            var entries = new List<VesselBreakageDefinition.Entry>();
            foreach (var name in new[] { "earth_pot", "round_jar", "tall_jar", "terracotta_pot" })
            {
                var debris = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Pueblo_" + name + "_broken.prefab");
                if (!debris) throw new FileNotFoundException("Pueblo debris missing: " + name);
                int found = 0;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Zandea/Pueblo/Models/" + name + ".fbx"))
                    if (asset is Mesh mesh)
                    {
                        entries.Add(new VesselBreakageDefinition.Entry { sourceMesh = mesh, fracturedPrefab = debris });
                        found++;
                    }
                if (found == 0) throw new InvalidDataException("Pueblo mesh missing: " + name);
            }
            definition.authoredPuebloMeshes = entries.ToArray();
            EditorUtility.SetDirty(definition);
        }
    }
}
