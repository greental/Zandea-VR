using System.Linq;
using System.IO;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace JunkerEquipment.Editor
{
    public static class JunkerPistolKitSetup
    {
        const string ModelPath = "Assets/MyAssets/mesh/weapons/JKR/JKR-88.glb";
        const string PrefabPath = "Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab";
        const string Shot1 = "Assets/MyAssets/Audio/Weapons/pko/mrfriends-pistol-shot-233473.mp3";
        const string Shot2 = "Assets/MyAssets/Audio/Weapons/pko/freesound_community-9mm-pistol-shoot-short-reverb-7152.mp3";
        const string CellModelPath = "Assets/MyAssets/mesh/weapons/JKR/R4-PowerCell.glb";
        const string CellPrefabPath = "Assets/MyAssets/prefabs/weapons/R4-PowerCell.prefab";
        const string ClickPath = "Assets/MyAssets/Audio/switch1.mp3";
        const string LaserMaterialPath = "Assets/laserShotMat.mat";

        [MenuItem("Zandea/Junker Equipment/Configure JKR-88 laser shot")]
        public static void ConfigureJkrLaserShot()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(LaserMaterialPath);
            if (!material) { Debug.LogError("Razara laser material is missing: " + LaserMaterialPath); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) { Debug.LogError("JKR-88 prefab is missing: " + PrefabPath); return; }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var kit = root.GetComponent<JunkerPistolKit>();
                if (!kit) throw new System.InvalidOperationException("JKR-88 has no pistol kit.");
                kit.addLaserShot = true;
                kit.laserShotMaterial = material;
                kit.laserShotSpeed = 45f;
                kit.laserShotLength = .28f;
                kit.laserShotWidth = .009f;
                kit.InstallPistolComponents();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("JKR-88 laser bolt configured; Razara prefab and firing system were not changed.", prefab);
        }

        [MenuItem("Zandea/Junker Equipment/Create JKR-88 pistol and scene pickup")]
        public static void CreateJkr88()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!modelAsset) { Debug.LogError("JKR-88 model was not imported: " + ModelPath); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab)
            {
                var root = new GameObject("JKR-88");
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
                    visual.name = "Visual";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) throw new System.InvalidOperationException("JKR-88 import has no renderer.");
                    var bounds = BoundsOf(renderers);
                    float length = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    visual.transform.localScale *= .32f / Mathf.Max(.001f, length);
                    bounds = BoundsOf(renderers);
                    // The JKR-88 model is one mesh: rear grip at +X, barrel at -X.
                    // Yawing the visual 90 degrees gives the pistol a +Z firing axis.
                    visual.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y + .02f, -bounds.min.z - .04f);
                    bounds = BoundsOf(renderers);

                    var muzzle = new GameObject("Muzzle").transform;
                    muzzle.SetParent(root.transform, false);
                    muzzle.localPosition = new Vector3(bounds.center.x, bounds.center.y + .015f, bounds.max.z + .008f);
                    var grip = new GameObject("Grip").transform;
                    grip.SetParent(root.transform, false);

                    var kit = root.AddComponent<JunkerPistolKit>();
                    kit.itemId = "JKR-88";
                    kit.visualRoot = visual.transform;
                    kit.gripPose = grip;
                    kit.muzzle = muzzle;
                    kit.addReflexDot = false; // This one-piece model has iron sights, no reflex window.
                    kit.shotClips = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>(Shot1), AssetDatabase.LoadAssetAtPath<AudioClip>(Shot2) }
                        .Where(c => c).ToArray();
                    kit.InstallPistolComponents();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    if (!prefab) throw new System.InvalidOperationException("Could not save " + PrefabPath);
                }
                finally { Object.DestroyImmediate(root); }
            }

            var cellPrefab = EnsurePowerCellPrefab();
            if (cellPrefab) { UpgradeJkrBattery(cellPrefab); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }

            var rig = Object.FindFirstObjectByType<JunkerEquipmentRig>();
            if (!rig) { Debug.Log("Created JKR-88 pistol prefab. Add it to a scene with JunkerEquipmentRig to use a holster.", prefab); return; }
            var slot = rig.slots.FirstOrDefault(s => s && s.legacyAcceptedItemId == "PKO");
            if (slot && (!slot.approvedItemIds.Contains("PKO") || !slot.approvedItemIds.Contains("JKR-88")))
            {
                Undo.RecordObject(slot, "Allow JKR-88 in pistol holster");
                if (!slot.approvedItemIds.Contains("PKO")) slot.approvedItemIds.Add("PKO");
                if (!slot.approvedItemIds.Contains("JKR-88")) slot.approvedItemIds.Add("JKR-88");
                EditorUtility.SetDirty(slot);
            }
            var existing = Object.FindObjectsByType<JunkerPistolKit>(FindObjectsSortMode.None)
                .FirstOrDefault(k => k.itemId == "JKR-88");
            if (!existing)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(instance, "Place JKR-88 pistol");
                var bench = GameObject.Find("Repair bench");
                if (bench) instance.transform.SetPositionAndRotation(
                    bench.transform.TransformPoint(new Vector3(.28f, 1.04f, -.13f)), bench.transform.rotation);
                else instance.transform.position = rig.body.xrOrigin.TransformPoint(new Vector3(.45f, 1.05f, .8f));
                var kit = instance.GetComponent<JunkerPistolKit>();
                var item = instance.GetComponent<JunkerEquipmentItem>();
                kit.equipmentRig = rig;
                item.rig = rig;
                EditorUtility.SetDirty(kit);
                EditorUtility.SetDirty(item);
                PrefabUtility.RecordPrefabInstancePropertyModifications(kit);
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
            var spare = GameObject.Find("R4-PowerCell spare");
            if (!spare && cellPrefab)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(cellPrefab, rig.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(instance, "Place spare R4 power cell");
                instance.name = "R4-PowerCell spare";
                var bench = GameObject.Find("Repair bench");
                if (bench) instance.transform.SetPositionAndRotation(
                    bench.transform.TransformPoint(new Vector3(.43f, 1.04f, -.13f)), bench.transform.rotation);
                else instance.transform.position = rig.body.xrOrigin.TransformPoint(new Vector3(.58f, 1.05f, .8f));
            }
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
            EditorSceneManager.SaveScene(rig.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("JKR-88 pistol ready. It uses PKO firing/equipment behavior and shares the right-hip slot when PKO is removed.", prefab);
        }

        static GameObject EnsurePowerCellPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
            if (existing) return existing;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CellModelPath);
            if (!model) { Debug.LogError("R4 power-cell model was not imported: " + CellModelPath); return null; }
            var root = new GameObject("R4-PowerCell");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                visual.name = "Visual";
                var bounds = BoundsOf(visual.GetComponentsInChildren<Renderer>(true));
                visual.transform.localScale *= .115f / Mathf.Max(.001f, bounds.size.y);
                bounds = BoundsOf(visual.GetComponentsInChildren<Renderer>(true));
                visual.transform.localPosition -= bounds.center;
                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(.062f, .115f, .06f);
                var body = root.AddComponent<Rigidbody>();
                body.mass = .3f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var grab = root.AddComponent<XRGrabInteractable>();
                grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Instantaneous;
                grab.useDynamicAttach = true;
                grab.throwOnDetach = true;
                grab.retainTransformParent = false;
                var cell = root.AddComponent<JunkerPowerCell>();
                cell.capacity = cell.remainingShots = 24;
                return PrefabUtility.SaveAsPrefabAsset(root, CellPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void UpgradeJkrBattery(GameObject cellPrefab)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var kit = root.GetComponent<JunkerPistolKit>();
                if (kit.usesPowerCell && kit.startingCell) return;
                var anchor = root.transform.Find("Power-cell socket");
                if (!anchor)
                {
                    anchor = new GameObject("Power-cell socket").transform;
                    anchor.SetParent(root.transform, false);
                    // The cell hangs below the rear grip, with the top contacts inside the receiver.
                    anchor.localPosition = new Vector3(0f, .025f, -.035f);
                }
                var cell = (GameObject)PrefabUtility.InstantiatePrefab(cellPrefab, anchor);
                cell.name = "R4-PowerCell loaded";
                cell.transform.localPosition = Vector3.zero;
                cell.transform.localRotation = Quaternion.identity;
                var body = cell.GetComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                cell.GetComponent<Collider>().isTrigger = true;
                kit.usesPowerCell = true;
                kit.powerCellSocket = anchor;
                kit.startingCell = cell.GetComponent<JunkerPowerCell>();
                kit.latchClick = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickPath);
                kit.cellInsertionRadius = .18f;
                kit.InstallPistolComponents();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Zandea/Junker Equipment/Inspect JKR-88 pistol")]
        public static void InspectJkr88()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) { Debug.LogError("JKR-88 prefab has not been created."); return; }
            var kit = prefab.GetComponent<JunkerPistolKit>();
            var item = prefab.GetComponent<JunkerEquipmentItem>();
            var pistol = prefab.GetComponent<JunkerPistol>();
            var grab = prefab.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            var bounds = BoundsOf(prefab.GetComponentsInChildren<Renderer>(true).Where(r => r.transform.IsChildOf(kit.visualRoot)).ToArray());
            var slot = Object.FindObjectsByType<JunkerEquipmentSlot>(FindObjectsSortMode.None).FirstOrDefault(s => s.legacyAcceptedItemId == "PKO");
            var report = $"Model bounds={bounds}\nGrip={kit.gripPose.localPosition}, muzzle={kit.muzzle.localPosition}, muzzle forward={kit.muzzle.forward}\n" +
                $"Item={item.itemId}, rig={(item.rig ? item.rig.name : "runtime binding")}, grab={grab}, body={prefab.GetComponent<Rigidbody>()}, collider={prefab.GetComponent<Collider>()}\n" +
                $"Fire={pistol.fireRate}/s range={pistol.range} damage={pistol.damage}, shots={pistol.shotClips?.Length}, right hip accepts={slot && slot.Accepts(item)}\n";
            File.WriteAllText("Temp/JKR-88-inspection.txt", report);
            var copy = Object.Instantiate(prefab);
            try { JunkerEquipmentValidation.Render(copy, "Temp/JKR-88-preview.png", new Vector3(.7f,.55f,-1f)); }
            finally { if(copy) Object.DestroyImmediate(copy); }
            Debug.Log(report, prefab);
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }

    [InitializeOnLoad]
    public static class Jkr88PlayValidation
    {
        const string Flag = "JunkerEquipment.Jkr88Validation";
        const string PreviousScene = "JunkerEquipment.Jkr88PreviousScene";
        static Jkr88PlayValidation() { EditorApplication.playModeStateChanged += Changed; }

        [MenuItem("Zandea/Junker Equipment/Test JKR-88 pistol")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/JunkerBody.unity")
            {
                if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().isDirty || string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                    throw new System.InvalidOperationException("Save the current single scene before temporarily opening JunkerBody for the JKR-88 test.");
                SessionState.SetString(PreviousScene, SceneManager.GetActiveScene().path);
                EditorSceneManager.OpenScene("Assets/Scenes/JunkerBody.unity", OpenSceneMode.Single);
            }
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                new GameObject("JKR-88 temporary validation").AddComponent<Jkr88PlayValidationRunner>();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                var previous = SessionState.GetString(PreviousScene, "");
                if (!string.IsNullOrEmpty(previous))
                {
                    SessionState.EraseString(PreviousScene);
                    EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                }
            }
        }
    }

    public sealed class Jkr88PlayValidationRunner : MonoBehaviour
    {
        readonly System.Text.StringBuilder report = new System.Text.StringBuilder("JKR-88 Play Mode validation\n");
        IEnumerator Start()
        {
            Application.runInBackground = true;
            var run = Run();
            while (true)
            {
                object current;
                try { if (!run.MoveNext()) break; current = run.Current; }
                catch (System.Exception e) { report.AppendLine("FAIL " + e); Finish(); yield break; }
                yield return current;
            }
            Finish();
        }
        void Check(bool condition, string message)
        {
            if (!condition) throw new System.Exception(message);
            report.AppendLine("PASS " + message);
        }
        IEnumerator Run()
        {
            yield return null;
            var items = FindObjectsByType<JunkerEquipmentItem>(FindObjectsSortMode.None);
            var jkr = items.FirstOrDefault(i => i.itemId == "JKR-88");
            GameObject temporaryJkr = null;
            if (!jkr)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab");
                Check(prefab, "current JKR-88 prefab is available");
                temporaryJkr = Instantiate(prefab);
                temporaryJkr.transform.position = new Vector3(0f, 5000f, 0f);
                Physics.SyncTransforms();
                jkr = temporaryJkr.GetComponent<JunkerEquipmentItem>();
            }
            var pko = items.FirstOrDefault(i => i.itemId == "PKO");
            var slot = FindObjectsByType<JunkerEquipmentSlot>(FindObjectsSortMode.None).FirstOrDefault(s => s.legacyAcceptedItemId == "PKO");
            var gun = jkr.GetComponent<JunkerPistol>();
            var kit = jkr.GetComponent<JunkerPistolKit>();
            Check(jkr.rig, "JKR-88 binds to the equipment rig");
            if (slot && pko) Check(slot.Accepts(jkr) && slot.Accepts(pko), "JKR-88 and PKO share the pistol equipment slot");
            Check(gun && kit && jkr.Grab && jkr.Body && jkr.GetComponent<Collider>() && gun.muzzle && gun.shotAudio && gun.shotClips.Length > 0, "complete pistol components and audio");
            Check(gun.showLaserShot && gun.laserShotMaterial, "JKR-88 laser bolt uses Razara's red laser material");
            Check(!jkr.GetComponent<JunkerReflexSight>() && !kit.movingSlide, "one-piece iron-sight model uses whole-mesh recoil");
            var socket = gun.batterySocket;
            Check(socket && socket.InsertedCell && !socket.IsOpen && socket.InsertedCell.remainingShots == socket.shotsPerCell && socket.latchClick, "loaded and latched R4 cell with click audio");
            Check(socket.InsertedCell.transform.parent == socket.socketAnchor && socket.InsertedCell.transform.localPosition.sqrMagnitude < .000001f,
                "loaded cell is attached at the socket, not floating beside the pistol");
            var spare = FindObjectsByType<JunkerPowerCell>(FindObjectsSortMode.None).Single(c => !c.Socket);
            Check(spare.GetComponent<XRGrabInteractable>() && spare.GetComponent<Rigidbody>() && spare.GetComponent<Collider>(), "spare power cell is physically grabbable");

            // Freeze the world pickup while synthetic direct hands are prepared.
            jkr.Body.isKinematic = true;
            jkr.Body.useGravity = false;
            var handObject = new GameObject("JKR-88 test hand");
            handObject.SetActive(false);
            handObject.transform.position = jkr.GetComponent<Collider>().bounds.center;
            var sphere = handObject.AddComponent<SphereCollider>(); sphere.radius = .04f; sphere.isTrigger = true;
            var handBody = handObject.AddComponent<Rigidbody>(); handBody.isKinematic = true; handBody.useGravity = false;
            var hand = handObject.AddComponent<XRDirectInteractor>();
            hand.improveAccuracyWithSphereCollider = false;
            hand.physicsTriggerInteraction = QueryTriggerInteraction.Ignore;
            handObject.SetActive(true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            var targets = new System.Collections.Generic.List<IXRInteractable>(); // XRI interface lives in Interactables.
            hand.GetValidTargets(targets);
            Check(targets.Contains(jkr.Grab), "direct hand detects JKR-88 collider");
            hand.StartManualInteraction((IXRSelectInteractable)jkr.Grab);
            yield return null;
            Check(jkr.state == EquipmentState.Held && jkr.Grab.isSelected, "JKR-88 can be held through equipment item");

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "JKR-88 temporary target";
            target.layer = 30;
            target.transform.position = gun.muzzle.position + gun.muzzle.forward * 2f;
            target.transform.localScale = new Vector3(.5f, .5f, .1f);
            var health = target.AddComponent<JunkerTarget>();
            var targetBody = target.AddComponent<Rigidbody>(); targetBody.useGravity = false;
            gun.hitMask = 1 << 30;
            Physics.SyncTransforms();
            Check(gun.TryFire() && !gun.LastShotHit && health.Hits == 0, "trigger launches bolt without instant damage");
            var bolt = FindFirstObjectByType<JunkerLaserShot>();
            Check(bolt && Vector3.Distance(bolt.StartPoint, gun.muzzle.position) < .02f, "visible laser starts at muzzle");
            Check(gun.flash && gun.flash.activeSelf, "muzzle flash and recoil activate on shot");
            float impactDeadline = Time.time + 1f;
            while (health.Hits == 0 && Time.time < impactDeadline) yield return null;
            Check(gun.LastShotHit && health.Hits == 1 && health.health < 100f && bolt && bolt.HasImpacted,
                "projectile applies damage only on arrival");
            yield return new WaitForFixedUpdate();
            Check(targetBody.linearVelocity.sqrMagnitude > .001f, "laser impact moves physical target");
            Check(FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r => r.name == "Laser scorch"),
                "laser impact leaves a scorch mark");
            var original = socket.InsertedCell;
            Vector3 originalScale = original.transform.lossyScale;
            Vector3 spareScale = spare.transform.lossyScale;
            Check(original.remainingShots == socket.shotsPerCell - 1, "shot consumes one unit of battery charge");
            socket.OpenLatch();
            socket.OpenLatch();
            Check(socket.IsOpen && !socket.CanFire() && original.GetComponent<XRGrabInteractable>().enabled,
                "A opens the latch once and repeated presses cannot relock it");
            Check(original.transform.localPosition.sqrMagnitude < .000001f,
                "opening the latch does not float the cell away from the socket");
            var cellHandObject = new GameObject("R4 temporary test hand");
            cellHandObject.SetActive(false);
            cellHandObject.transform.position = original.GetComponent<Collider>().bounds.center;
            var cellHandCollider = cellHandObject.AddComponent<SphereCollider>(); cellHandCollider.radius = .025f; cellHandCollider.isTrigger = true;
            var cellHandBody = cellHandObject.AddComponent<Rigidbody>(); cellHandBody.isKinematic = true; cellHandBody.useGravity = false;
            var cellHand = cellHandObject.AddComponent<XRDirectInteractor>();
            cellHand.improveAccuracyWithSphereCollider = false;
            cellHand.physicsTriggerInteraction = QueryTriggerInteraction.Ignore;
            cellHandObject.SetActive(true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            var cellTargets = new System.Collections.Generic.List<IXRInteractable>();
            cellHand.GetValidTargets(cellTargets);
            Check(cellTargets.Contains(original.GetComponent<XRGrabInteractable>()), "second direct hand detects unlocked loaded cell");
            cellHand.StartManualInteraction((IXRSelectInteractable)original.GetComponent<XRGrabInteractable>());
            yield return null;
            Check(original.GetComponent<XRGrabInteractable>().isSelected, "loaded cell can be gripped directly");
            Check(!socket.InsertedCell && !original.Socket && original.GetComponent<Rigidbody>().useGravity, "unlocked cell can be pulled free and thrown");
            Check(Vector3.Distance(original.transform.lossyScale, originalScale) < .001f, "removing loaded cell keeps its size");
            cellHand.EndManualInteraction();
            yield return null;
            yield return new WaitForFixedUpdate();
            Check(!socket.InsertedCell, "removed cell does not snap straight back when released beside the socket");
            Check(!original.GetComponent<Rigidbody>().isKinematic && original.GetComponent<Rigidbody>().useGravity,
                "removed cell regains dynamic gravity after XR release");
            Check(!gun.TryFire(), "empty socket cannot fire");
            original.GetComponent<Rigidbody>().isKinematic = true;
            original.GetComponent<Rigidbody>().useGravity = false;
            cellHandObject.transform.position = original.GetComponent<Collider>().bounds.center;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            cellHand.StartManualInteraction((IXRSelectInteractable)original.GetComponent<XRGrabInteractable>());
            yield return null;
            Check(original.GetComponent<XRGrabInteractable>().isSelected, "same removed cell can be grabbed again");
            cellHandObject.transform.position = socket.socketAnchor.position + socket.socketAnchor.right * .2f;
            for (int i = 0; i < 12; i++) yield return null;
            cellHandObject.transform.position = socket.socketAnchor.position;
            for (int i = 0; i < 15; i++) yield return null;
            cellHand.EndManualInteraction();
            yield return null; yield return null;
            Check(socket.InsertedCell == original && !socket.IsOpen && original.remainingShots == socket.shotsPerCell - 1,
                "same battery reattaches without swapping and retains charge");
            socket.OpenLatch();
            cellHandObject.transform.position = original.GetComponent<Collider>().bounds.center;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            cellHand.StartManualInteraction((IXRSelectInteractable)original.GetComponent<XRGrabInteractable>());
            yield return null;
            cellHandObject.transform.position = socket.socketAnchor.position + socket.socketAnchor.right * .3f;
            for (int i = 0; i < 15; i++) yield return null;
            cellHand.EndManualInteraction();
            yield return null;
            Check(!socket.InsertedCell, "removing the same cell again leaves the socket free for a spare");
            socket.shotsPerCell = 7; // A designer's Inspector value must configure each fresh inserted cell.
            spare.GetComponent<Rigidbody>().isKinematic = true;
            spare.GetComponent<Rigidbody>().useGravity = false;
            cellHandObject.transform.position = spare.GetComponent<Collider>().bounds.center;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            cellHand.StartManualInteraction((IXRSelectInteractable)spare.GetComponent<XRGrabInteractable>());
            yield return null;
            Check(spare.GetComponent<XRGrabInteractable>().isSelected, "spare cell can be gripped");
            cellHandObject.transform.position = socket.socketAnchor.position;
            for (int i = 0; i < 15; i++) yield return null;
            cellHand.EndManualInteraction();
            yield return null; yield return null;
            Check(socket.InsertedCell == spare && spare.Socket == socket, "released spare cell snaps into open socket");
            Check(Vector3.Distance(spare.transform.lossyScale, spareScale) < .001f, "inserting spare cell under scaled pistol keeps its size");
            Check(!socket.IsOpen && !spare.GetComponent<XRGrabInteractable>().enabled && socket.CanFire(),
                "new cell auto-locks, cannot be selected, and arms pistol");
            Check(spare.capacity == 7 && spare.remainingShots == 7, "Inspector shots-per-cell setting configures a fresh cell");
            Check(spare.transform.parent == socket.socketAnchor && spare.transform.localPosition.sqrMagnitude < .000001f,
                "reattached cell rests exactly at the socket");
            Check(Quaternion.Angle(spare.transform.localRotation, Quaternion.Euler(socket.mountedEuler)) < 1f,
                "blue meter uses the socket's left-facing orientation");
            for (int i = 0; i < 7; i++) Check(socket.TryConsumeShot(), "configured shot " + (i + 1) + " fires");
            Check(!socket.TryConsumeShot() && !socket.CanFire(), "cell stops firing at configured shot count");
            socket.ToggleLatch();
            socket.Remove(spare);
            Check(socket.Insert(spare) && !socket.IsOpen && spare.remainingShots == 0 && !socket.CanFire(),
                "reinserting a spent cell does not replenish it");
            Check(Vector3.Distance(spare.transform.lossyScale, spareScale) < .001f, "repeated cell insertion does not enlarge it");
            hand.EndManualInteraction();
            yield return null;
            Check(jkr.state == EquipmentState.World && jkr.Body.useGravity, "release restores world item physics");

            if (slot && pko)
            {
                slot.ReleaseItem(pko);
                pko.Body.isKinematic = true; pko.Body.useGravity = false;
                Check(slot.TryMount(jkr) && slot.Occupant == jkr && jkr.state == EquipmentState.Holstered, "JKR-88 fits PKO's right-hip holster");
                slot.ReleaseItem(jkr);
                Check(slot.TryMount(pko) && slot.Occupant == pko, "PKO can retake the same holster");
            }
            Destroy(target); Destroy(handObject); Destroy(cellHandObject);
            if (temporaryJkr) Destroy(temporaryJkr);
        }
        void Finish()
        {
            File.WriteAllText("Temp/JKR-88-playvalidation.txt", report.ToString());
            EditorApplication.isPlaying = false;
        }
    }

    [InitializeOnLoad]
    public static class JkrLaserPlayValidation
    {
        const string Flag = "JunkerEquipment.JkrLaserValidation";
        static JkrLaserPlayValidation() { EditorApplication.playModeStateChanged += Changed; }

        [MenuItem("Zandea/Junker Equipment/Test JKR-88 laser shot")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new System.InvalidOperationException("Save open scenes before running the JKR laser Play Mode test.");
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Flag, false)) return;
            SessionState.SetBool(Flag, false);
            new GameObject("JKR laser temporary validation").AddComponent<JkrLaserPlayValidationRunner>();
        }
    }

    public sealed class JkrLaserPlayValidationRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            Application.runInBackground = true;
            yield return null;
            string result = null;
            GameObject gunObject = null, target = null, shooter = null;
            JunkerPistol gun = null;
            JunkerTarget health = null;
            JunkerLaserShot shot = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab");
                if (!prefab) throw new System.Exception("Current JKR-88 prefab is missing.");
                gunObject = Instantiate(prefab);
                gunObject.transform.SetPositionAndRotation(new Vector3(0f, 5000f, 0f), Quaternion.identity);
                gun = gunObject.GetComponent<JunkerPistol>();
                if (!gun || !gun.showLaserShot || !gun.laserShotMaterial || !gun.muzzle)
                    throw new System.Exception("JKR laser visual is not configured.");
                var body = gunObject.GetComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false;
                shooter = new GameObject("JKR laser test shooter");
                target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.layer = 30;
                target.transform.position = gun.muzzle.position + gun.muzzle.forward * 2f;
                target.transform.localScale = new Vector3(.5f, .5f, .1f);
                health = target.AddComponent<JunkerTarget>();
                gun.hitMask = 1 << 30;
                Physics.SyncTransforms();
                if (!gun.TryFireNpc(shooter.transform, target.transform.position) || gun.LastShotHit || health.Hits != 0)
                    throw new System.Exception("JKR shot damaged before projectile arrival.");
                shot = FindFirstObjectByType<JunkerLaserShot>();
                if (!shot || Vector3.Distance(shot.StartPoint, gun.muzzle.position) > .02f)
                    throw new System.Exception("Projectile did not start at the muzzle.");
                if (!shot.GetComponent<LineRenderer>() || shot.GetComponent<LineRenderer>().sharedMaterial != gun.laserShotMaterial)
                    throw new System.Exception("Visual bolt is missing Razara's laser material.");
            }
            catch (System.Exception error) { result = "FAIL " + error; }
            if (result == null)
            {
                float deadline = Time.time + 1f;
                while (health.Hits == 0 && Time.time < deadline) yield return null;
                result = gun.LastShotHit && health.Hits == 1 && shot && shot.HasImpacted &&
                    Vector3.Distance(shot.EndPoint, target.GetComponent<Collider>().ClosestPoint(gun.muzzle.position)) <= .1f
                    ? "PASS JKR laser bolt travels from muzzle and applies damage once on impact."
                    : "FAIL Projectile did not apply one hit at arrival.";
            }
            File.WriteAllText("Temp/JKR-88-laservalidation.txt", result);
            if (gunObject) Destroy(gunObject);
            if (target) Destroy(target);
            if (shooter) Destroy(shooter);
            EditorApplication.isPlaying = false;
        }
    }
}
