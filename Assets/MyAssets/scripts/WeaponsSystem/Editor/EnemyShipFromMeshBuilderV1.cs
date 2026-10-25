#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Core;

/// <summary>Builds a playable enemy ship prefab from an imported model, using Atlas V2 as the combat template.</summary>
public static class EnemyShipFromMeshBuilderV1
{
    const string TemplatePath = "Assets/MyAssets/prefabs/Enemies/enemyShips/AtlasEnemyCombatV2.prefab";
    const string YellowJacketModelPath = "Assets/MyAssets/mesh/Ships/YellowJacket/YellowJacket.glb";
    const string YellowJacketOutputPath = "Assets/MyAssets/prefabs/Enemies/enemyShips/YellowJacketCombatV2.prefab";
    const string PocScenePath = "Assets/Scenes/TerrainTools/SpaceOrbitTerrain_EnemyCombatV2_POC.unity";

    [InitializeOnLoadMethod]
    static void BuildMissingYellowJacketOnReload()
    {
        EditorSceneManager.sceneSaved -= OnPocSceneSaved;
        EditorSceneManager.sceneSaved += OnPocSceneSaved;
        EditorApplication.delayCall += () =>
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(YellowJacketOutputPath) == null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(YellowJacketModelPath) != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) != null)
                BuildYellowJacket();
            // Scene-based upgrades run through the explicit menu or scene-saved hook.
            // Opening a preview of a gameplay scene on every domain reload executes
            // its Awake methods during Play Mode and creates unrelated runtime systems.
        };
    }

    static void OnPocSceneSaved(Scene scene)
    {
        if (scene.path == PocScenePath)
            EditorApplication.delayCall += UpgradeYellowJacketFromSavedScene;
    }

    [MenuItem("Zandea/Ships/Build YellowJacket Combat V2")]
    public static void BuildYellowJacket()
    {
        Build(YellowJacketModelPath, YellowJacketOutputPath, 20f);
        UpgradeYellowJacketFromSavedScene();
    }

    [MenuItem("Zandea/Ships/Use Saved YellowJacket Turrets")]
    public static void UpgradeYellowJacketFromSavedScene()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(YellowJacketOutputPath);
        if (prefab == null) return;
        // A preview scene reads the user's saved mounts without changing the open scene.
        Scene preview = EditorSceneManager.OpenPreviewScene(PocScenePath);
        try
        {
            Transform source = null;
            foreach (GameObject sceneRoot in preview.GetRootGameObjects())
                if (sceneRoot.name == "YellowJacket" && sceneRoot.GetComponentsInChildren<WeaponController>(true).Length >= 2)
                    source = sceneRoot.transform;
            if (source == null) return;

            GameObject contents = PrefabUtility.LoadPrefabContents(YellowJacketOutputPath);
            try
            {
                if (contents.transform.Find("turret_left") == null)
                {
                    Transform visual = contents.transform.Find("Visual_YellowJacket");
                    if (visual == null) throw new InvalidOperationException("YellowJacket visual is missing.");
                    foreach (string name in new[] { "LaserLeft", "LaserRight" })
                    {
                        Transform oldMount = contents.transform.Find(name);
                        if (oldMount != null) UnityEngine.Object.DestroyImmediate(oldMount.gameObject);
                    }
                    foreach (string name in new[] { "turret_left", "turret_right" })
                    {
                        Transform sourceMount = source.Find(name);
                        if (sourceMount == null) throw new InvalidOperationException("Saved " + name + " is missing.");
                        GameObject mount = UnityEngine.Object.Instantiate(sourceMount.gameObject, visual);
                        mount.name = name;
                        mount.transform.localPosition = sourceMount.localPosition;
                        mount.transform.localRotation = sourceMount.localRotation;
                        mount.transform.localScale = sourceMount.localScale;
                        WeaponController weapon = mount.GetComponent<WeaponController>();
                        SerializedObject settings = new SerializedObject(weapon);
                        settings.FindProperty("headAimRaycastIgnoreRoot").objectReferenceValue = contents.transform;
                        settings.FindProperty("headAimUpReference").objectReferenceValue = contents.transform;
                        settings.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, YellowJacketOutputPath);
                    Debug.Log("YellowJacket combat prefab now uses the two saved RazorCannons turrets.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            Scene openScene = SceneManager.GetSceneByPath(PocScenePath);
            if (!openScene.IsValid() || !openScene.isLoaded || openScene.isDirty) return;
            Transform oldShip = null;
            foreach (GameObject sceneRoot in openScene.GetRootGameObjects())
                if (sceneRoot.name == "YellowJacket" && sceneRoot.GetComponent<NPCShipCombatV2>() == null)
                    oldShip = sceneRoot.transform;
            if (oldShip == null) return;
            GameObject replacement = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(YellowJacketOutputPath), openScene) as GameObject;
            replacement.transform.SetPositionAndRotation(oldShip.position, oldShip.rotation);
            replacement.name = "YellowJacketCombatV2";
            EnemyTargetHudV2 hud = UnityEngine.Object.FindFirstObjectByType<EnemyTargetHudV2>();
            if (hud != null)
            {
                SerializedObject hudData = new SerializedObject(hud);
                hudData.FindProperty("enemy").objectReferenceValue = replacement.GetComponent<NPCShipCombatV2>();
                hudData.ApplyModifiedPropertiesWithoutUndo();
            }
            UnityEngine.Object.DestroyImmediate(oldShip.gameObject);
            EditorSceneManager.SaveScene(openScene);
            Debug.Log("Replaced saved YellowJacket mesh with its combat prefab in the POC scene.");
        }
        catch (Exception ex) { Debug.LogError("YellowJacket turret upgrade failed: " + ex); }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    [MenuItem("Zandea/Ships/Build Enemy Ship From Selected Mesh")]
    public static void BuildSelected()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogError("Select an imported model GameObject asset in the Project window first.");
            return;
        }
        string name = Path.GetFileNameWithoutExtension(path);
        string output = "Assets/MyAssets/prefabs/Enemies/enemyShips/" + name + "CombatV2.prefab";
        Build(path, output, 20f);
    }

    [MenuItem("Zandea/Ships/Build Enemy Ship From Selected Mesh", true)]
    static bool CanBuildSelected() => Selection.activeObject != null &&
        AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(Selection.activeObject)) != null;

    public static bool Build(string modelPath, string outputPath, float desiredLengthMeters)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        GameObject templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        if (modelAsset == null || templateAsset == null)
        {
            Debug.LogError("Ship build needs an imported model GameObject and AtlasEnemyCombatV2 template. Model: " + modelPath);
            return false;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(outputPath) != null)
        {
            Debug.LogWarning("Ship prefab already exists; refusing to overwrite: " + outputPath);
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(TemplatePath);
        try
        {
            WeaponController[] sourceLasers = root.GetComponentsInChildren<WeaponController>(true);
            WeaponController laser = null;
            for (int i = 0; i < sourceLasers.Length; i++)
                if (sourceLasers[i].WeaponDamageType == DamageType.Laser) { laser = sourceLasers[i]; break; }
            if (laser == null || laser.GetComponent<WeaponHardpoint>() == null)
                throw new InvalidOperationException("Atlas V2 template needs a laser WeaponController and WeaponHardpoint.");

            root.name = Path.GetFileNameWithoutExtension(outputPath);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            GameObject[] oldChildren = new GameObject[root.transform.childCount];
            for (int i = 0; i < oldChildren.Length; i++) oldChildren[i] = root.transform.GetChild(i).gameObject;

            GameObject model = PrefabUtility.InstantiatePrefab(modelAsset, root.transform) as GameObject;
            if (model == null) throw new InvalidOperationException("Could not instantiate imported model: " + modelPath);
            model.name = "Visual_" + Path.GetFileNameWithoutExtension(modelPath);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            Bounds raw = RendererBounds(model);
            float horizontalLength = Mathf.Max(raw.size.x, raw.size.z);
            if (horizontalLength < .001f) throw new InvalidOperationException("Imported model has no visible renderer bounds.");
            model.transform.localScale = Vector3.one * Mathf.Max(1f, desiredLengthMeters) / horizontalLength;
            Bounds bounds = RendererBounds(model);
            model.transform.localPosition = -bounds.center;
            bounds = RendererBounds(model);

            BoxCollider collider = root.GetComponent<BoxCollider>();
            if (collider == null) collider = root.AddComponent<BoxCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.size = new Vector3(bounds.size.x * 1.12f, Mathf.Max(3f, bounds.size.y * 1.4f), bounds.size.z * 1.12f);
            Rigidbody body = root.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            float halfWidth = bounds.extents.x;
            float muzzleZ = root.transform.InverseTransformPoint(bounds.center + root.transform.forward * bounds.extents.z * .78f).z;
            float muzzleY = root.transform.InverseTransformPoint(bounds.center).y;
            CreateLaser(root.transform, laser, "LaserLeft", -halfWidth * .58f, muzzleY, muzzleZ);
            CreateLaser(root.transform, laser, "LaserRight", halfWidth * .58f, muzzleY, muzzleZ);

            for (int i = 0; i < oldChildren.Length; i++) UnityEngine.Object.DestroyImmediate(oldChildren[i]);

            // The pilot discovers child weapons at runtime. Clear references to the old mesh hierarchy.
            NPCShipCombatV2 pilot = root.GetComponent<NPCShipCombatV2>();
            if (pilot == null || root.GetComponent<FlightController>() == null ||
                root.GetComponent<HOTASController>() == null || root.GetComponent<CombatDamageable>() == null ||
                root.GetComponent<ShieldSystem>() == null)
                throw new InvalidOperationException("Combat template is missing a required ship component.");
            SerializedObject pilotData = new SerializedObject(pilot);
            pilotData.FindProperty("target").objectReferenceValue = null;
            pilotData.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            Debug.Log("Built enemy ship prefab: " + outputPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Ship build failed: " + ex.Message);
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void CreateLaser(Transform ship, WeaponController source, string name, float x, float y, float z)
    {
        GameObject mount = new GameObject(name);
        mount.transform.SetParent(ship, false);
        mount.transform.localPosition = new Vector3(x, y, z);
        GameObject bore = new GameObject("Muzzle");
        bore.transform.SetParent(mount.transform, false);
        bore.transform.localPosition = Vector3.forward * .3f;
        WeaponHardpoint hardpoint = mount.AddComponent<WeaponHardpoint>();
        EditorUtility.CopySerialized(source.GetComponent<WeaponHardpoint>(), hardpoint);
        WeaponController weapon = mount.AddComponent<WeaponController>();
        EditorUtility.CopySerialized(source, weapon);
        SerializedObject settings = new SerializedObject(weapon);
        settings.FindProperty("hardpoint").objectReferenceValue = hardpoint;
        settings.FindProperty("muzzle").objectReferenceValue = bore.transform;
        settings.FindProperty("headAimRaycastIgnoreRoot").objectReferenceValue = ship;
        settings.FindProperty("headAimUpReference").objectReferenceValue = ship;
        settings.FindProperty("aimFromHead").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
#endif
