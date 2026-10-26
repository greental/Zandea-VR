using System;
using System.Collections.Generic;
using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.POI;

namespace Zandea.Props.Editor
{
    public static class PropSliceBuilder
    {
        const string Folder = "Assets/Zandea/Props";
        const string Models = Folder + "/Models/";
        const string Prefabs = "Assets/MyAssets/prefabs/buildings/props/";
        const string Materials = Folder + "/Materials/";
        const string Resource = Folder + "/Resources/ZandeaOutpostProps.asset";
        static readonly string[] Keys = { "pot", "stool", "chest", "barrel", "rack", "crate", "sack", "table", "statue" };

        [InitializeOnLoadMethod]
        static void WatchRequest() => EditorApplication.update += Tick;
        static void Tick()
        {
            const string request = "Temp/ZandeaProps.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying) return;
            File.Delete(request);
            try { Build(); File.WriteAllText("Temp/ZandeaProps.result", "OK"); }
            catch (Exception e) { File.WriteAllText("Temp/ZandeaProps.result", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Zandea/Props/Build Slice 8 Prefabs and Test Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Folder + "/Resources");
            var materials = new Dictionary<string, Material>();
            foreach (var key in Keys) materials[key] = Material(key);
            var potDebris = Debris("pot", materials["pot"], true);
            var stoolDebris = Debris("stool", materials["stool"], false);
            var barrelDebris = Debris("barrel", materials["barrel"], false);
            var bandage = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/medical/BandageRoll.prefab");
            var bandageItem = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>("Assets/MeleeCombat/Inventory/Bandage.asset");
            var foodItem = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>("Assets/MeleeCombat/Inventory/Dried Food.asset");
            var staffItem = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>("Assets/MeleeCombat/Inventory/Bo Staff.asset");
            var staff = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/Bo Staff.prefab");
            if (!bandage || !bandageItem || !staff || !staffItem) throw new Exception("Existing bandage/staff item or physical prefab missing.");
            var built = new Dictionary<string, GameObject>();
            foreach (var key in Keys)
                built[key] = BuildOne(key, materials[key], key == "pot" ? potDebris : key == "stool" ? stoolDebris :
                    key == "barrel" ? barrelDebris : null, bandageItem, foodItem, bandage, staffItem, staff);
            var config = AssetDatabase.LoadAssetAtPath<OutpostPropDefinition>(Resource);
            if (!config) { config = ScriptableObject.CreateInstance<OutpostPropDefinition>(); AssetDatabase.CreateAsset(config, Resource); }
            config.ceramicPot = built["pot"]; config.woodenStool = built["stool"];
            config.chest = built["chest"]; config.barrel = built["barrel"]; config.weaponRack = built["rack"];
            config.crate = built["crate"]; config.sack = built["sack"];
            config.table = built["table"]; config.statue = built["statue"];
            EditorUtility.SetDirty(config);
            CreateTestScene(built);
            Validate(built);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[Zandea Props] Built nine prop prefabs, three offline debris prefabs, and test scene.");
        }
        static Material Material(string key)
        {
            string path = Materials + key + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key };
                AssetDatabase.CreateAsset(mat, path);
            }
            var texturePath = Folder + "/Textures/" + key + "_base.jpg";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (!texture) throw new Exception("Missing texture " + texturePath);
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer && (importer.maxTextureSize > 1024 || !importer.mipmapEnabled))
            {
                importer.maxTextureSize = 1024; importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", key == "pot" ? .22f : key == "statue" ? .12f : .18f);
            var normalPath = Folder + "/Textures/" + key + "_normal.png";
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normal)
            {
                var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                if (normalImporter && normalImporter.textureType != TextureImporterType.NormalMap)
                { normalImporter.textureType = TextureImporterType.NormalMap; normalImporter.maxTextureSize = 1024; normalImporter.SaveAndReimport(); }
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                mat.EnableKeyword("_NORMALMAP");
            }
            var surfacePath = Folder + "/Textures/" + key + "_metallic_smooth.png";
            var surface = AssetDatabase.LoadAssetAtPath<Texture2D>(surfacePath);
            if (surface)
            {
                var surfaceImporter = AssetImporter.GetAtPath(surfacePath) as TextureImporter;
                if (surfaceImporter && surfaceImporter.sRGBTexture)
                { surfaceImporter.sRGBTexture = false; surfaceImporter.maxTextureSize = 1024; surfaceImporter.SaveAndReimport(); }
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(surfacePath));
                mat.SetFloat("_Metallic", 1);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
        static GameObject Model(string key, Transform parent, Material material)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + key + ".fbx");
            if (!source) throw new Exception("Missing model " + key);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = key == "statue" ? Quaternion.identity : Quaternion.Euler(-90, 0, 0);
            model.transform.localScale *= key == "pot" ? .05f : 1f;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++) shared[i] = material;
                renderer.sharedMaterials = shared;
            }
            return model;
        }
        static Bounds Bounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new Exception("No renderer: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static void AddBox(GameObject root, Bounds bounds)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center - root.transform.position;
            box.size = bounds.size;
        }
        static GameObject Debris(string key, Material material, bool ceramic)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + key + "_fractured.fbx");
            if (!source) throw new Exception("Missing offline fracture mesh for " + key);
            var root = new GameObject(key + " broken pieces");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                model.transform.localScale *= key == "pot" ? .05f : 1f;
                foreach (var mesh in model.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = mesh.GetComponent<MeshRenderer>();
                    if (renderer) renderer.sharedMaterials = new[] { material, material };
                    if (ceramic)
                    {
                        var collider = mesh.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh.sharedMesh; collider.convex = true;
                    }
                    else
                    {
                        var collider = mesh.gameObject.AddComponent<BoxCollider>();
                        collider.center = mesh.sharedMesh.bounds.center; collider.size = mesh.sharedMesh.bounds.size;
                    }
                    var body = mesh.gameObject.AddComponent<Rigidbody>();
                    body.mass = ceramic ? .07f : .3f;
                    body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                }
                root.AddComponent<PropDebrisLifetime>();
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + key + "_broken.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static GameObject BuildOne(string key, Material material, GameObject debris,
            NpcItemDefinition bandage, NpcItemDefinition food, GameObject bandagePrefab,
            NpcItemDefinition staffItem, GameObject staffPrefab)
        {
            var root = new GameObject("Zandea " + key);
            try
            {
                var model = Model(key, root.transform, material);
                var bounds = Bounds(model);
                float minimumHeight = key == "pot" ? .25f : key == "statue" ? 1f : key == "crate" ? .25f : .3f;
                float maximumHeight = key == "statue" ? 4f : key == "rack" ? 2.5f : 1.4f;
                if (bounds.size.y < minimumHeight || bounds.size.y > maximumHeight)
                    throw new Exception("Implausible Unity scale for " + key + ": " + bounds.size);
                if (key == "chest" || key == "crate")
                {
                    AddOpenBox(root, bounds);
                    var body = root.AddComponent<Rigidbody>();
                    body.mass = key == "chest" ? 25 : 9;
                    body.isKinematic = key == "chest";
                    if (key == "crate") AddGrab(root, body, false);
                    var container = root.AddComponent<PropContainer>();
                    container.initialContents = key == "chest" ? new[] { bandage, bandage } : new[] { bandage };
                    container.initialPhysicalPrefabs = key == "chest" ? new[] { bandagePrefab, bandagePrefab } : new[] { bandagePrefab };
                    var points = new List<Transform>();
                    for (int i = 0; i < 2; i++)
                    {
                        var point = new GameObject("Reachable contents " + i).transform;
                        point.SetParent(root.transform, false);
                        point.localPosition = new Vector3((i - .5f) * bounds.size.x * .33f,
                            bounds.min.y + bounds.size.y * .55f, 0);
                        points.Add(point);
                    }
                    container.presentationPoints = points.ToArray();
                    SetupLid(model, body, container);
                    root.AddComponent<ChestStorageVolume>();
                }
                else if (key == "rack")
                {
                    var socket = new GameObject("Bo Staff socket").AddComponent<PropRackSocket>();
                    socket.transform.SetParent(root.transform, false);
                    socket.transform.localPosition = new Vector3(0, bounds.min.y + bounds.size.y * .72f, 0);
                    socket.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    socket.startingItem = staffItem; socket.weaponPrefab = staffPrefab;
                }
                else
                {
                    AddBox(root, bounds);
                    if (key != "statue" && key != "table")
                    {
                        var body = root.AddComponent<Rigidbody>();
                        body.mass = key == "pot" ? 1.4f : key == "stool" ? 4f : key == "barrel" ? 13f : key == "crate" ? 9f : 2f;
                        AddGrab(root, body, key == "pot" || key == "sack");
                        if (debris)
                        {
                            var breakable = root.AddComponent<BreakableProp>();
                            breakable.fracturedPrefab = debris;
                            breakable.collisionEnergyThreshold = key == "pot" ? 8 : key == "stool" ? 28 : 80;
                            breakable.minimumImpactSpeed = key == "pot" ? 3 : key == "stool" ? 4 : 5;
                            breakable.requireWeaponDamage = key == "barrel";
                        }
                        if (key == "barrel")
                        {
                            var container = root.AddComponent<PropContainer>();
                            container.initialContents = new[] { bandage };
                            container.initialPhysicalPrefabs = new[] { bandagePrefab };
                        }
                    }
                }
                Debug.Log("[Zandea Props] " + key + " bounds=" + bounds.size);
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + key + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void AddGrab(GameObject root, Rigidbody body, bool backpack)
        {
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;
            var physical = root.AddComponent<PhysicalProp>();
            var inventory = root.AddComponent<JunkerEquipment.VRInventoryItem>();
            inventory.CanStoreInBackpack = backpack;
        }
        static void AddOpenBox(GameObject root, Bounds b)
        {
            float wall = Mathf.Min(.08f, b.size.x * .1f);
            var bottom = root.AddComponent<BoxCollider>();
            bottom.center = new Vector3(b.center.x, b.min.y + wall / 2, b.center.z);
            bottom.size = new Vector3(b.size.x, wall, b.size.z);
            for (int axis = 0; axis < 2; axis++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var shape = root.AddComponent<BoxCollider>();
                    shape.center = b.center + (axis == 0 ? Vector3.right * side * (b.extents.x - wall / 2) :
                        Vector3.forward * side * (b.extents.z - wall / 2));
                    shape.size = axis == 0 ? new Vector3(wall, b.size.y, b.size.z) : new Vector3(b.size.x, b.size.y, wall);
                }
        }
        static void SetupLid(GameObject model, Rigidbody baseBody, PropContainer container)
        {
            foreach (var mesh in model.GetComponentsInChildren<MeshFilter>())
            {
                if (!mesh.name.ToLowerInvariant().Contains("lid")) continue;
                var body = mesh.gameObject.AddComponent<Rigidbody>();
                body.mass = 2;
                var collider = mesh.gameObject.AddComponent<BoxCollider>();
                collider.center = mesh.sharedMesh.bounds.center; collider.size = mesh.sharedMesh.bounds.size;
                var hinge = mesh.gameObject.AddComponent<HingeJoint>();
                hinge.connectedBody = baseBody; hinge.axis = Vector3.right; hinge.useLimits = true;
                hinge.limits = new JointLimits { min = -4, max = 105, bounciness = 0 };
                var grab = mesh.gameObject.AddComponent<XRGrabInteractable>();
                grab.useDynamicAttach = true;
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.throwOnDetach = false;
                grab.retainTransformParent = true;
                mesh.gameObject.AddComponent<PropHingedLid>().container = container;
                return;
            }
            Debug.LogWarning("No separate lid mesh found for " + model.name);
        }
        static void CreateTestScene(Dictionary<string, GameObject> built)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Prop interaction floor"; floor.transform.localScale = new Vector3(2.5f, 1, 2.5f);
            SceneManager.MoveGameObjectToScene(floor, scene);
            string[] order = { "pot", "stool", "chest", "barrel", "rack", "crate", "sack", "table", "statue" };
            for (int i = 0; i < order.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(built[order[i]], scene);
                instance.transform.position = new Vector3((i % 3 - 1) * 3.2f, .02f, (i / 3 - 1) * 3.2f);
                instance.name = "Test " + order[i];
            }
            var initializer = new GameObject("Initialize test prop state");
            initializer.AddComponent<PropTestSceneInitializer>();
            SceneManager.MoveGameObjectToScene(initializer, scene);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/Player/VR player - JunkerWeapon.prefab");
            if (playerPrefab)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.transform.position = new Vector3(0, .1f, -8);
            }
            EditorSceneManager.SaveScene(scene, Folder + "/ZandeaPropInteractions.unity");
            EditorSceneManager.CloseScene(scene, true);
        }
        static void Validate(Dictionary<string, GameObject> built)
        {
            var lines = new List<string>();
            foreach (var key in Keys)
            {
                var prefab = built[key];
                if (!prefab || prefab.GetComponentsInChildren<MeshRenderer>(true).Length == 0)
                    throw new Exception("Missing visual prefab: " + key);
                if (key == "pot" || key == "stool" || key == "barrel")
                {
                    var breakable = prefab.GetComponent<BreakableProp>();
                    if (!breakable || !breakable.fracturedPrefab ||
                        breakable.fracturedPrefab.GetComponentsInChildren<Rigidbody>().Length < 5)
                        throw new Exception("Missing authored fragments: " + key);
                }
                if ((key == "chest" || key == "crate" || key == "barrel") && !prefab.GetComponent<PropContainer>())
                    throw new Exception("Missing container: " + key);
                if ((key == "chest" || key == "crate") && !prefab.GetComponentInChildren<PropHingedLid>())
                    throw new Exception("Missing separate hinged lid: " + key);
                if (key == "rack" && !prefab.GetComponentInChildren<PropRackSocket>())
                    throw new Exception("Missing weapon socket");
                if (key == "statue" && prefab.GetComponent<Rigidbody>())
                    throw new Exception("Statue must remain static");
                lines.Add(key + ": renderer, collision and role OK");
            }
            var d = AssetDatabase.LoadAssetAtPath<BanditOutpostDefinition>("Assets/Zandea/POI/BanditOutpost.asset");
            for (int seed = 4301; seed < 4311; seed++)
                if (OutpostLayout.Generate(d, seed).reservations.FindAll(r => r.kind == ReservationKind.FutureProp).Count < 10)
                    throw new Exception("Outpost prop sockets missing at seed " + seed);
            lines.Add("10 outpost seeds expose prop sockets");
            lines.Add("Physical grab, hinge, spill, rack, and headset behavior still require Play Mode / Quest 2 checks.");
            File.WriteAllLines("Temp/ZandeaProps.static-validation.txt", lines);
        }
    }
}
