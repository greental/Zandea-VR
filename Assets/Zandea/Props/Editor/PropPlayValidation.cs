using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using JunkerEquipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRKoombar.WeaponsStaging.Contracts;

namespace Zandea.Props.Editor
{
    [InitializeOnLoad]
    public static class PropPlayValidation
    {
        const string ScenePath = "Assets/Zandea/Props/ZandeaPropInteractions.unity";
        const string Request = "Temp/ZandeaProps.play-request";
        const string Report = "Temp/ZandeaProps.play-validation.txt";
        static string previousScene;
        static int phase, startFrame;
        static bool running;
        static string[] chestIds;
        static PropContainer chest;
        static string lidInitial;

        static PropPlayValidation()
        {
            running = SessionState.GetBool("ZandeaProps.running", false);
            previousScene = SessionState.GetString("ZandeaProps.previousScene", "");
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Mode;
        }
        [MenuItem("Tools/Zandea/Props/Run Representative Play Validation")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath))
                throw new InvalidOperationException("Save/import the prop test scene and exit Play Mode first.");
            previousScene = SceneManager.GetActiveScene().path;
            SessionState.SetString("ZandeaProps.previousScene", previousScene);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            running = true; phase = 0; startFrame = 0;
            SessionState.SetBool("ZandeaProps.running", true);
            File.WriteAllText(Report, "RUNNING\n");
            EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (File.Exists(Request) && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(Request);
                try { Begin(); } catch (Exception e) { File.WriteAllText(Report, "FAILED TO START: " + e); }
            }
            if (!running || !EditorApplication.isPlaying || Time.frameCount < 35) return;
            if (startFrame == 0) startFrame = Time.frameCount;
            try
            {
                if (phase == 0)
                {
                    var pot = Find("Test pot").GetComponent<BreakableProp>();
                    var stool = Find("Test stool").GetComponent<BreakableProp>();
                    var barrel = Find("Test barrel").GetComponent<BreakableProp>();
                    Require(pot && stool && barrel && !pot.IsBroken && !stool.IsBroken && !barrel.IsBroken,
                        "representative breakables start intact");
                    chest = Find("Test chest").GetComponent<PropContainer>();
                    Require(chest && chest.Contents.Count == 2, "chest creates two items once");
                    var chestStorage=chest.GetComponent<ChestStorageVolume>();
                    var chestInventory=chest.GetComponent<BackpackInventory>();
                    Require(chestStorage&&chestInventory&&chestInventory.alwaysOpenStorage&&chestInventory.storageVolume&&
                        chestInventory.storageVolume.validStorageIndicator,
                        "chest has backpack-style physical storage and green placement feedback");
                    var lid = chest.GetComponentInChildren<PropHingedLid>();
                    var hinge = lid ? lid.GetComponent<HingeJoint>() : null;
                    var lidGrab = lid ? lid.GetComponent<XRGrabInteractable>() : null;
                    Require(hinge && hinge.connectedBody == chest.GetComponent<Rigidbody>() &&
                        hinge.useLimits && lidGrab && lidGrab.movementType == XRBaseInteractable.MovementType.VelocityTracking &&
                        lid.GetComponent<HeldLootCollisionGuard>() && lid.GetComponent<BoxCollider>() &&
                        lidGrab.colliders.Contains(lid.GetComponent<BoxCollider>()),
                        "chest lid has an attached physical hinge, pickup shape and held-player filtering");
                    lidInitial = $"initial position={lid.transform.position} rotation={lid.transform.rotation.eulerAngles} " +
                        $"meshBounds={lid.GetComponent<MeshFilter>().sharedMesh.bounds} " +
                        $"lidBounds={lid.GetComponent<BoxCollider>().bounds} " +
                        $"rootBounds={string.Join(";", chest.GetComponents<BoxCollider>().Select(box => box.bounds.ToString()))}";
                    chestIds = chest.Contents.Select(i => i.InstanceId).ToArray();
                    phase = 1; startFrame = Time.frameCount;
                }
                else if (phase == 1 && Time.frameCount - startFrame >= 60)
                {
                    var lid = chest.GetComponentInChildren<PropHingedLid>();
                    var hinge = lid.GetComponent<HingeJoint>();
                    var lidCollider = lid.GetComponent<BoxCollider>();
                    var bottom = chest.GetComponents<BoxCollider>().Min(shape => shape.bounds.center.y);
                    float anchorError = Vector3.Distance(lid.transform.TransformPoint(hinge.anchor),
                        hinge.connectedBody.transform.TransformPoint(hinge.connectedAnchor));
                    Require(anchorError < .15f &&
                        lidCollider.bounds.center.y > bottom + .1f &&
                        hinge.angle >= hinge.limits.min - 5f && hinge.angle <= hinge.limits.max + 5f,
                        $"chest lid remains hinged above the box after physics settles: anchor={anchorError:F3} " +
                        $"lidY={lidCollider.bounds.center.y:F3} bottomY={bottom:F3} angle={hinge.angle:F1} " +
                        $"axis={hinge.axis} lidAnchor={hinge.anchor} baseAnchor={hinge.connectedAnchor} " +
                        $"lidScale={lid.transform.lossyScale} parentScale={lid.transform.parent.lossyScale} " +
                        $"lidLocal={lid.transform.localPosition} lidRot={lid.transform.localRotation.eulerAngles} {lidInitial}");
                    var manager = UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();
                    Require(manager, "representative scene has an XR interaction manager");
                    var handObject = new GameObject("Chest lid validation hand");
                    try
                    {
                        var hand = handObject.AddComponent<XRDirectInteractor>();
                        hand.interactionManager = manager;
                        hand.transform.position = lidCollider.bounds.center;
                        var lidGrab = lid.GetComponent<XRGrabInteractable>();
                        hand.StartManualInteraction((IXRSelectInteractable)lidGrab);
                        Require(lidGrab.isSelected && hinge.connectedBody == chest.GetComponent<Rigidbody>(),
                            "lid is selectable and remains attached while grabbed");
                        hand.EndManualInteraction();
                        Require(!lidGrab.isSelected && hinge.connectedBody == chest.GetComponent<Rigidbody>(),
                            "lid stays hinged after hand release");
                    }
                    finally { UnityEngine.Object.Destroy(handObject); }
                    Require(chest.Contents.Select(i => i.InstanceId).SequenceEqual(chestIds),
                        "closing chest keeps item identities");
                    chest.SetOpen(true);
                    Require(chest.GetComponentsInChildren<NpcWorldItem>().Length == 2,
                        "opened chest presents two reachable item objects");
                    Require(chest.GetComponentsInChildren<NpcWorldItem>().All(item =>
                        item.GetComponent<JunkerEquipment.VRInventoryItem>()?.CanStoreInBackpack == true),
                        "chest loot enters the existing backpack store/retrieve lifecycle");
                    Require(chest.Contents.Select(i => i.InstanceId).SequenceEqual(chestIds),
                        "reopening chest does not regenerate contents");
                    CheckChestBackpackLifecycle(manager);
                    Require(chest.Contents.Count == 1 && chest.Contents.All(i => chestIds.Contains(i.InstanceId)),
                        "taking backpack loot removes only the claimed chest item");
                    var barrel = Find("Test barrel").GetComponent<BreakableProp>();
                    var barrelContents = barrel.GetComponent<PropContainer>();
                    Require(barrelContents.Contents.Count == 1, "barrel has one existing item");
                    var originalId = barrelContents.Contents[0].InstanceId;
                    barrel.ApplyDamage(new DamagePacket { BaseDamage = 30, DamageType = DamageType.Kinetic,
                        HitPointWorld = barrel.transform.position, HitNormalWorld = Vector3.up });
                    Require(!barrel.IsBroken, "non-weapon contact cannot break the barrel");
                    barrel.ApplyDamage(new DamagePacket { BaseDamage = 30, DamageType = DamageType.Kinetic, WeaponId = "validation sword",
                        HitPointWorld = barrel.transform.position, HitNormalWorld = Vector3.up });
                    Require(barrel.IsBroken && barrelContents.Contents.Count == 0,
                        "melee damage breaks barrel and empties container once");
                    Require(UnityEngine.Object.FindObjectsByType<NpcWorldItem>(FindObjectsSortMode.None).Any(m =>
                        m.Item != null && m.Item.InstanceId == originalId), "barrel spills same item instance");
                    barrel.ApplyDamage(new DamagePacket { BaseDamage = 30, HitPointWorld = barrel.transform.position });
                    Require(barrelContents.Contents.Count == 0, "repeat damage cannot spill twice");
                    var pot = Find("Test pot").GetComponent<BreakableProp>();
                    pot.ApplyDamage(new DamagePacket { BaseDamage = 10, HitPointWorld = pot.transform.position });
                    Require(pot.IsBroken, "existing melee damage path breaks pot");
                    var stool = Find("Test stool").GetComponent<BreakableProp>();
                    stool.ApplyDamage(new DamagePacket { BaseDamage = 10, HitPointWorld = stool.transform.position });
                    Require(stool.IsBroken, "existing melee damage path breaks stool");
                    phase = 2; startFrame = Time.frameCount;
                }
                else if (phase == 2 && Time.frameCount - startFrame >= 2)
                {
                    // Destroy is deferred until the end of a Play frame. Check the
                    // active burst cap after the queue has retired its oldest debris.
                    var debris = UnityEngine.Object.FindObjectsByType<PropDebrisLifetime>(FindObjectsSortMode.None)
                        .Where(piece => piece.gameObject.scene.path == ScenePath).ToArray();
                    Require(debris.Length <= 3,
                        "active debris burst cap is respected; scene debris=" + debris.Length +
                        " [" + string.Join(", ", debris.Select(piece => piece.name)) + "]");
                    var rack = Find("Test rack").GetComponentInChildren<PropRackSocket>();
                    Require(rack && rack.CurrentItem != null && rack.CurrentItem.WorldObject,
                        "rack mounts one real world weapon with item identity");
                    var weapon = rack.CurrentItem;
                    rack.ItemTaken(weapon.WorldObject.GetComponent<PropItemPresentation>(), weapon);
                    Require(rack.CurrentItem == null && weapon.WorldObject, "taking weapon clears slot without destroying it");
                    var statue = Find("Test statue");
                    Require(!statue.GetComponent<Rigidbody>() && statue.GetComponent<Collider>(),
                        "statue remains static scenery");
                    File.WriteAllText(Report, "PASS: connected chest lid and pickup collider, chest loot store/retrieve with preserved item identity, chest open/close and claim, intentional barrel break and one-time spill, debris cap, rack removal, static statue.\nTracked-hand lid feel and Quest 2 performance require headset checks.\n");
                    running = false; SessionState.SetBool("ZandeaProps.running", false); EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Report, "FAIL: " + e + "\n");
                running = false; SessionState.SetBool("ZandeaProps.running", false); EditorApplication.isPlaying = false;
            }
        }
        static void CheckChestBackpackLifecycle(XRInteractionManager manager)
        {
            var worldItem = chest.GetComponentsInChildren<NpcWorldItem>().First();
            var inventoryItem = worldItem.GetComponent<VRInventoryItem>();
            var grab = worldItem.GetComponent<XRGrabInteractable>();
            var identity = worldItem.Item.InstanceId;
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab");
            var sourceBag = playerPrefab ? playerPrefab.GetComponentInChildren<BackpackInventory>(true) : null;
            Require(sourceBag && inventoryItem && grab, "real chest loot and backpack are available");
            var staging = new GameObject("Chest loot backpack validation");
            staging.SetActive(false);
            staging.transform.position = chest.transform.position + Vector3.right * 8f;
            var handObject = new GameObject("Chest loot validation hand");
            try
            {
                var bag = UnityEngine.Object.Instantiate(sourceBag, staging.transform);
                bag.transform.localPosition = Vector3.zero;
                bag.transform.localRotation = Quaternion.identity;
                bag.transform.localScale = sourceBag.transform.lossyScale;
                bag.equipment.rig = null;
                bag.backSlot = null;
                bag.GetComponent<XRGrabInteractable>().interactionManager = manager;
                staging.SetActive(true);
                bag.GetComponent<Rigidbody>().isKinematic = true;
                var hand = handObject.AddComponent<XRDirectInteractor>();
                hand.interactionManager = manager;
                hand.transform.position = worldItem.transform.position;
                hand.StartManualInteraction((IXRSelectInteractable)grab);
                Require(grab.isSelected, "chest loot can be selected as a world object");
                grab.trackPosition = grab.trackRotation = false;
                worldItem.transform.position = bag.storageVolume.transform.TransformPoint(bag.storageVolume.Box.center);
                Physics.SyncTransforms();
                hand.EndManualInteraction();
                Require(inventoryItem.CurrentContainer == bag && bag.Items.Contains(inventoryItem) &&
                    worldItem.Item.InstanceId == identity,
                    "chest loot stores in the existing backpack with the same item identity");
                hand.StartManualInteraction((IXRSelectInteractable)grab);
                Require(grab.isSelected && !inventoryItem.CurrentContainer && worldItem.Item.InstanceId == identity,
                    "chest loot retrieves as the same usable world object");
                worldItem.transform.position = bag.transform.position + Vector3.up * 2f;
                hand.EndManualInteraction();
            }
            finally
            {
                UnityEngine.Object.Destroy(handObject);
                UnityEngine.Object.Destroy(staging);
            }
        }
        static GameObject Find(string name)
        {
            var found = GameObject.Find(name);
            if (!found) throw new Exception("Missing " + name);
            return found;
        }
        static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
        static void Mode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || string.IsNullOrEmpty(previousScene)) return;
            var test = SceneManager.GetSceneByPath(ScenePath);
            if (test.isLoaded) EditorSceneManager.CloseScene(test, true);
            var previous = SceneManager.GetSceneByPath(previousScene);
            if (previous.isLoaded) SceneManager.SetActiveScene(previous);
            previousScene = null;
            SessionState.SetString("ZandeaProps.previousScene", "");
        }
    }
}
