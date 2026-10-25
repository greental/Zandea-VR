using System;
using System.IO;
using System.Linq;
using System.Reflection;
using JunkerBody;
using JunkerEquipment;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    [InitializeOnLoad]
    public static class ClothingWearPlayValidation
    {
        const string Request = "Temp/ClothingWear.play-request";
        const string Result = "Temp/ClothingWear.play-result";
        const string Player = "Assets/CharacterCreator/Player/VR player - Man Creator.prefab";
        const string Shirt = "Assets/MeleeCombat/Inventory/Rag Shirt.asset";
        const string Pants = "Assets/MeleeCombat/Inventory/Rag Pants.asset";
        const string Running = "ClothingWearPlayValidation.Running";
        static GameObject original, actor, ownerObject, shirtObject, pantsObject, otherShirtObject;
        static int frame = -1;

        static ClothingWearPlayValidation() => EditorApplication.update += Tick;

        [MenuItem("Zandea/Character Creator/Validate clothing storage and wear in Play Mode")]
        public static void Run() { File.WriteAllText(Request, "run"); Tick(); }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
            if (!SessionState.GetBool(Running, false))
            {
                if (!File.Exists(Request)) return;
                File.Delete(Request);
                SessionState.SetBool(Running, true);
                frame = -1;
                if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
            }
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (frame < 0)
                {
                    var source = UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Single(body => body.gameObject.scene.path == "Assets/Main.unity");
                    original = source.transform.root.gameObject;
                    original.SetActive(false);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player);
                    Require(prefab, "Man player prefab missing");
                    actor = UnityEngine.Object.Instantiate(prefab, original.transform.position, original.transform.rotation);
                    frame = Time.frameCount;
                    return;
                }
                if (Time.frameCount < frame + 15) return;
                Validate();
                File.WriteAllText(Result, "PASS same shirt and pants instances: flat world loot -> rolled backpack contents -> flat retrieval -> fitted creator-body clothing -> removable world loot. Item GUIDs and slot identity preserved.");
                Finish();
            }
            catch (Exception error)
            {
                File.WriteAllText(Result, "FAIL " + error);
                Debug.LogException(error);
                Finish();
            }
        }

        static void Validate()
        {
            var player = actor.GetComponentInChildren<PlayerWearableClothing>(true);
            var bag = actor.GetComponentInChildren<BackpackInventory>(true);
            Require(player && bag && bag.storageVolume && bag.equipment.currentSlot,
                "player wardrobe or mounted backpack unavailable");
            bag.equipment.currentSlot.ReleaseItem(bag.equipment);
            bag.equipment.Body.isKinematic = true;
            Require(bag.IsUsable, "backpack did not open");
            ownerObject = new GameObject("Validation clothing owner");
            var owner = ownerObject.AddComponent<NpcInventory>();
            shirtObject = Spawn(owner, Shirt);
            pantsObject = Spawn(owner, Pants);
            otherShirtObject = Spawn(owner, Shirt);
            var shirt = shirtObject.GetComponent<WearableLoot>();
            var pants = pantsObject.GetComponent<WearableLoot>();
            Require(shirt && pants && shirt.CanWear && pants.CanWear,
                "world clothing did not receive wearable identity");
            var shirtId = shirt.WorldItem.Item.InstanceId;
            var pantsId = pants.WorldItem.Item.InstanceId;
            Store(bag, shirt);
            Store(bag, pants);
            Require(bag.Items.Count == 2 && shirt.InventoryItem.CurrentContainer == bag &&
                pants.InventoryItem.CurrentContainer == bag, "two garments not stored in shared backpack");
            var roll = shirt.transform.Find("Rolled garment grip");
            Require(roll, "stored shirt has no roll visual");
            bool flatsHidden = shirt.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.transform != roll)
                .All(renderer => renderer.forceRenderingOff);
            Require(roll.GetComponent<Renderer>().enabled && roll.GetComponent<Collider>().enabled &&
                !shirt.GetComponent<BoxCollider>().enabled && flatsHidden,
                $"stored shirt is not a grabbable compact roll: bag={bag.State}, rollVisible={roll.GetComponent<Renderer>().enabled}, rollCollider={roll.GetComponent<Collider>().enabled}, flatCollider={shirt.GetComponent<BoxCollider>().enabled}, flatsHidden={flatsHidden}");
            Retrieve(shirt);
            Retrieve(pants);
            Require(bag.Items.Count == 0 && !shirt.InventoryItem.CurrentContainer &&
                shirt.GetComponent<BoxCollider>().enabled && !roll.GetComponent<Renderer>().enabled &&
                shirt.WorldItem.Item.InstanceId == shirtId && pants.WorldItem.Item.InstanceId == pantsId,
                "retrieval did not restore the same flat garment instances");
            shirt.transform.position = player.Anchor(shirt.Appearance.slot).position;
            pants.transform.position = player.Anchor(pants.Appearance.slot).position;
            bool shirtWears = player.TryWear(shirt);
            bool pantsWears = player.TryWear(pants);
            Require(shirtWears && pantsWears && shirt.IsWorn && pants.IsWorn,
                $"shirt and pants did not occupy their own player clothing slots: shirt={shirtWears}, pants={pantsWears}, shirtCan={shirt.CanWear}, pantsCan={pants.CanWear}, shirtHeld={shirt.InventoryItem.IsHeld}, pantsHeld={pants.InventoryItem.IsHeld}, shirtContainer={shirt.InventoryItem.CurrentContainer}, pantsContainer={pants.InventoryItem.CurrentContainer}, torso={player.cosmetics.torso}, legs={player.cosmetics.legs}");
            var cosmetics = player.cosmetics;
            Require(cosmetics.torso == shirt.Appearance && cosmetics.legs == pants.Appearance &&
                actor.GetComponentsInChildren<AppearanceSpawnedItem>(true).Any(item =>
                    item.slot == AppearanceSlot.Torso && item.definition == shirt.Appearance) &&
                actor.GetComponentsInChildren<AppearanceSpawnedItem>(true).Any(item =>
                    item.slot == AppearanceSlot.Legs && item.definition == pants.Appearance) &&
                shirt.WorldItem.Item.InstanceId == shirtId && pants.WorldItem.Item.InstanceId == pantsId,
                "worn appearance or loot identity was lost");
            var otherShirt = otherShirtObject.GetComponent<WearableLoot>();
            otherShirt.transform.position = player.Anchor(AppearanceSlot.Torso).position;
            Require(!player.TryWear(otherShirt) && !otherShirt.IsWorn && cosmetics.torso == shirt.Appearance,
                "second shirt replaced an occupied clothing slot");
            Require(player.Remove(shirt) && player.Remove(pants) && !shirt.IsWorn && !pants.IsWorn &&
                shirt.GetComponent<BoxCollider>().enabled && cosmetics.torso == null && cosmetics.legs == null,
                "removing garments did not restore wearable world objects and empty slots");
        }

        static GameObject Spawn(NpcInventory owner, string path)
        {
            var definition = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(path);
            Require(definition && definition.worldPrefab && definition.equippedAppearance,
                "missing clothing definition " + path);
            var item = new NpcItemInstance(definition);
            Require(owner.Add(item), "cannot create clothing item " + path);
            var physical = UnityEngine.Object.Instantiate(definition.worldPrefab);
            Require(owner.Drop(item, physical.transform.position, physical.transform.rotation, physical),
                "cannot drop clothing item " + path);
            return physical;
        }

        static void Store(BackpackInventory bag, WearableLoot loot)
        {
            loot.transform.position = bag.storageVolume.transform.TransformPoint(bag.storageVolume.Box.center);
            Physics.SyncTransforms();
            var method = typeof(BackpackInventory).GetMethod("TryStoreReleased", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null && (bool)method.Invoke(bag, new object[] { loot.InventoryItem, bag.storageVolume }),
                "garment release did not enter backpack");
        }

        static void Retrieve(WearableLoot loot)
        {
            var method = typeof(VRInventoryItem).GetMethod("ReleaseFromContainer", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, "backpack retrieval method missing");
            method.Invoke(loot.InventoryItem, new object[] { false });
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        static void Finish()
        {
            if (shirtObject) UnityEngine.Object.Destroy(shirtObject);
            if (pantsObject) UnityEngine.Object.Destroy(pantsObject);
            if (otherShirtObject) UnityEngine.Object.Destroy(otherShirtObject);
            if (ownerObject) UnityEngine.Object.Destroy(ownerObject);
            if (actor) UnityEngine.Object.Destroy(actor);
            if (original) original.SetActive(true);
            shirtObject = pantsObject = otherShirtObject = ownerObject = actor = original = null;
            SessionState.SetBool(Running, false);
            EditorApplication.isPlaying = false;
        }
    }
}
