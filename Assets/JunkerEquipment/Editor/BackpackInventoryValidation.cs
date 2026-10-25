using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace JunkerEquipment.Editor
{
    [InitializeOnLoad]
    public static class BackpackInventoryValidation
    {
        const string Flag = "Junker.BackpackValidation";
        static BackpackInventoryValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Flag, false)) return;
                SessionState.SetBool(Flag, false);
                new GameObject("Backpack inventory validation").AddComponent<BackpackInventoryValidationRunner>();
            };
            EditorApplication.update += () =>
            {
                const string request = "Temp/BackpackInventory.validate.request";
                if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(request); Run();
            };
        }
        [MenuItem("Zandea/Junker Equipment/Validate physical backpack in Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }
    }

    public sealed class BackpackInventoryValidationRunner : MonoBehaviour
    {
        readonly StringBuilder log = new("Physical backpack / real XRI selection lifecycle\n");
        GameObject sandbox;
        XRInteractionManager manager;
        XRDirectInteractor hand;
        BackpackInventory bag;
        IEnumerator Start()
        {
            Application.runInBackground = true;
            var run = Checks();
            while (true)
            {
                object current;
                try { if (!run.MoveNext()) break; current = run.Current; }
                catch (Exception e) { log.AppendLine("FAIL " + e); Finish(); yield break; }
                yield return current;
            }
            log.AppendLine("PASS all synthetic checks. Physical headset reach, feel, haptics and cavity alignment still require VR testing.");
            Finish();
        }
        void Finish()
        {
            if (sandbox) Destroy(sandbox);
            File.WriteAllText("Temp/BackpackInventory.validation", log.ToString());
            EditorApplication.isPlaying = false;
        }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); log.AppendLine("PASS " + message); }
        GameObject Child(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(sandbox.transform, false); return go;
        }
        VRInventoryItem Item(string name, bool equipment)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.SetActive(false);
            go.transform.SetParent(sandbox.transform, false); go.transform.localScale = Vector3.one * .04f;
            var rb = go.AddComponent<Rigidbody>(); rb.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = go.AddComponent<XRGrabInteractable>(); grab.interactionManager = manager;
            grab.retainTransformParent = !equipment; grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            if (equipment) go.AddComponent<JunkerEquipmentItem>();
            var item = go.AddComponent<VRInventoryItem>(); go.SetActive(true); return item;
        }
        void Select(VRInventoryItem item)
        {
            hand.StartManualInteraction((IXRSelectInteractable)item.GetComponent<XRGrabInteractable>());
            var grab = item.GetComponent<XRGrabInteractable>(); grab.trackPosition = false; grab.trackRotation = false;
        }
        void Place(VRInventoryItem item, Vector3 offset)
        {
            item.transform.position = bag.storageVolume.transform.TransformPoint(bag.storageVolume.Box.center + offset);
            item.transform.rotation = bag.transform.rotation * Quaternion.Euler(23, 17, 48);
        }
        IEnumerator Checks()
        {
            sandbox = new GameObject("Isolated backpack checks"); sandbox.transform.position = new Vector3(5000, 1000, 5000);
            manager = Child("XRI manager").AddComponent<XRInteractionManager>();
            var handGo = Child("Test hand");
            var handShape = handGo.AddComponent<SphereCollider>(); handShape.isTrigger = true; handShape.radius = .06f;
            hand = handGo.AddComponent<XRDirectInteractor>(); hand.interactionManager = manager;
            hand.improveAccuracyWithSphereCollider = true;
            hand.physicsTriggerInteraction = QueryTriggerInteraction.Collide;
            hand.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            hand.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab");
            Check(prefab, "JunkerWeapon player prefab exists");
            var source = prefab.GetComponentInChildren<BackpackInventory>(true);
            Check(source && source.openVisual && source.storageVolume && source.storedItemsRoot, "configured backpack references on original prefab");
            foreach (var tool in prefab.GetComponentsInChildren<JunkerEquipmentItem>(true).Where(i => i.GetComponent<JunkerScrewdriver>() || i.GetComponent<JunkerBinoculars>()))
                Check(tool.GetComponent<VRInventoryItem>(), tool.name + " inventory compatible");
            var holder = Child("Inactive clone staging"); holder.SetActive(false);
            bag = Instantiate(source, holder.transform); bag.transform.localPosition = Vector3.zero; bag.transform.localRotation = Quaternion.identity;
            bag.transform.localScale = source.transform.lossyScale;
            bag.equipment.rig = null;
            bag.backSlot = null; // Never operate on the prefab asset's serialized slot during tests.
            bag.GetComponent<XRGrabInteractable>().interactionManager = manager;
            bag.GetComponent<XRGrabInteractable>().throwOnDetach = false;
            holder.SetActive(true);
            bag.GetComponent<Rigidbody>().isKinematic = true;
            var a = Item("Equipment test item", true); var b = Item("Generic test item", false);
            yield return null;
            // Reproduce the actual prefab's runtime equipment placement, including avatar scale
            // and the rotation imposed by Mount. A manual Select bypasses this reach regression.
            var rig = source.equipment.rig;
            var shoulder = Child("JunkerWeapon shoulder reach test").AddComponent<JunkerEquipmentSlot>();
            shoulder.radius = source.backSlot.radius;
            shoulder.approvedItemIds.Add("backpack");
            shoulder.transform.localPosition = rig.bodyOffset + Quaternion.Euler(rig.bodyEulerOffset) *
                Vector3.Scale(source.backSlot.transform.localPosition, rig.transform.lossyScale);
            shoulder.transform.localRotation = Quaternion.Euler(rig.bodyEulerOffset) * source.backSlot.transform.localRotation;
            bag.backSlot = shoulder;
            Check(shoulder.TryMount(bag.equipment), "mount actual JunkerWeapon backpack at authored runtime shoulder position");
            yield return null;
            Vector3 reachableHandle = bag.topHandle.position - sandbox.transform.position;
            log.AppendLine("INFO mounted JunkerWeapon handle relative to standing tracking origin: " + reachableHandle.ToString("F3"));
            Check(reachableHandle.x > -.5f && reachableHandle.x < -.02f && reachableHandle.y > 1.2f && reachableHandle.y < 1.8f &&
                reachableHandle.z > -.4f && reachableHandle.z < .1f, "mounted handle stays within left-shoulder reach instead of far behind torso");
            hand.transform.position = bag.topHandle.position + new Vector3(0, .04f, .04f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return null;
            var targets = new List<IXRInteractable>(); hand.GetValidTargets(targets);
            Check(targets.Contains(bag.GetComponent<XRGrabInteractable>()), "real direct-hand overlap detects the mounted trigger handle");
            hand.selectInput.QueueManualState(true, 1f);
            yield return null; yield return null;
            Check(bag.equipment.Grab.isSelected && !shoulder.Occupant, "grip input selects reachable backpack without forced manual selection");
            hand.selectInput.QueueManualState(false, 0f);
            yield return null; yield return null;
            Check(shoulder.Occupant == bag.equipment, "release at shoulder returns backpack to its slot");
            shoulder.ReleaseItem(bag.equipment); bag.backSlot = null;
            bag.GetComponent<Rigidbody>().isKinematic = true;
            bag.transform.SetPositionAndRotation(sandbox.transform.position, Quaternion.identity);
            hand.transform.position = sandbox.transform.position + Vector3.left * 3;
            Destroy(shoulder.gameObject);
            yield return null;
            Select(a); Place(a, new Vector3(-.04f, 0, 0));
            Vector3 p = bag.storedItemsRoot.InverseTransformPoint(a.transform.position);
            Quaternion r = Quaternion.Inverse(bag.storedItemsRoot.rotation) * a.transform.rotation;
            yield return null;
            Check(bag.storageVolume.validStorageIndicator.activeSelf, "valid anchor produces subtle placement indicator");
            hand.EndManualInteraction();
            Check(a.CurrentContainer == bag && bag.Items.Count == 1, "release stores synchronously before next physics frame");
            yield return null; yield return null;
            Check((a.transform.localPosition - p).sqrMagnitude < 1e-7f && Quaternion.Angle(a.transform.localRotation, r) < .01f, "exact relative placement survives XRI deferred detach");
            Check(a.GetComponent<Rigidbody>().isKinematic && !a.GetComponent<Rigidbody>().useGravity && a.GetComponent<Collider>().isTrigger, "stored physics and contact response disabled");
            Select(b); Place(b, new Vector3(.04f, 0, 0)); hand.EndManualInteraction();
            Check(b.CurrentContainer == bag && bag.Items.Count == 2, "second generic item stores without equipment-specific code");
            for (int i = 0; i < 12; i++)
            {
                bag.transform.position += new Vector3(.12f, -.03f, .05f);
                bag.transform.rotation = Quaternion.Euler(i * 29, i * 43, i * 17);
                yield return null;
                Check((a.transform.localPosition - p).sqrMagnitude < 1e-7f, "stored pose stable during shake " + i);
            }
            var slot = Child("Back slot").AddComponent<JunkerEquipmentSlot>();
            Check(slot.TryMount(bag.equipment), "existing backpack mount still works");
            yield return null;
            Check(!bag.openVisual.activeSelf && bag.closedVisual.activeSelf && a.GetComponent<XRGrabInteractable>().interactionLayers.value == 0 && !a.GetComponent<Collider>().enabled, "on-back contents cannot be grabbed or collide");
            Check(a.GetComponent<Renderer>().forceRenderingOff, "on-back contents hidden without changing renderer enabled state");
            hand.StartManualInteraction((IXRSelectInteractable)bag.GetComponent<XRGrabInteractable>());
            yield return null;
            Check(!bag.equipment.currentSlot && bag.openVisual.activeSelf && !bag.closedVisual.activeSelf, "existing grab detaches backpack and shows supplied open mesh");
            Check(a.GetComponent<Collider>().enabled && a.GetComponent<XRGrabInteractable>().interactionLayers.value != 0 && !a.GetComponent<Renderer>().forceRenderingOff, "contents visible and selectable after pull-forward");
            hand.EndManualInteraction(); bag.GetComponent<Rigidbody>().isKinematic = true;
            // Avoid a preexisting holster snap in this isolated test; bag has no equipment rig.
            yield return null;
            int instance = a.GetInstanceID(); Select(a);
            Check(!a.CurrentContainer && a.CurrentState == VRInventoryState.Held && a.GetInstanceID() == instance && bag.Items.Count == 1, "retrieve same instance and remove collection entry");
            a.transform.position = bag.transform.position + Vector3.up * 2;
            hand.EndManualInteraction(); yield return null;
            Check(!a.GetComponent<Rigidbody>().isKinematic && a.GetComponent<Rigidbody>().useGravity && !a.GetComponent<Collider>().isTrigger, "normal physics restored when retrieved equipment is dropped outside");
            for (int i = 0; i < 3; i++)
            {
                Select(a); Place(a, Vector3.zero); hand.EndManualInteraction(); yield return null;
                Check(bag.Items.Count == 2, "repeated storage has no duplicate entries " + i);
                Select(a); a.transform.position = bag.transform.position + Vector3.up * 2; hand.EndManualInteraction(); yield return null;
            }
            Select(a); Place(a, Vector3.zero); yield return null;
            Place(a, Vector3.right); hand.EndManualInteraction();
            Check(!a.CurrentContainer, "enter then leave before release does not store");
            Select(a); Place(a, new Vector3(bag.storageVolume.Box.size.x * .5f + .01f, 0, 0)); hand.EndManualInteraction();
            Check(!a.CurrentContainer, "collider edge overlap with anchor outside does not store");
            a.CanStoreInBackpack = false; Select(a); Place(a, Vector3.zero); hand.EndManualInteraction();
            Check(!a.CurrentContainer, "CanStoreInBackpack false rejected");
            a.CanStoreInBackpack = true; Select(a); Place(a, Vector3.zero);
            manager.CancelInteractableSelection((IXRSelectInteractable)a.GetComponent<XRGrabInteractable>());
            Check(!a.CurrentContainer, "canceled selection does not store");
            if (hand.isPerformingManualInteraction) hand.EndManualInteraction();
            Select(b); b.transform.position = bag.transform.position + Vector3.up * 2; hand.EndManualInteraction(); yield return null;
            Check(!b.GetComponent<Rigidbody>().isKinematic && b.GetComponent<Rigidbody>().useGravity && b.GetComponent<Rigidbody>().interpolation == RigidbodyInterpolation.Interpolate, "generic retrieved item restores original rigidbody settings");
            Check(!b.transform.IsChildOf(bag.transform) && b.GetComponent<XRGrabInteractable>().retainTransformParent, "generic drop stays detached and restores original XRI parent policy");
            Select(b); Place(b, Vector3.zero); hand.EndManualInteraction();
            Destroy(b.gameObject); yield return null; yield return null;
            Check(!bag.Items.Contains(b) && bag.Items.All(item => item), "destroyed stored item leaves no stale reference");
            Check(bag.Items.Count == 0, "canceled manual release cannot produce a later duplicate store");

            var bagGrab = bag.GetComponent<XRGrabInteractable>();
            Check(bag.topHandle && bagGrab.colliders.Count == 1 && bagGrab.colliders[0].transform == bag.topHandle, "only top handle grabs the bag");
            bag.transform.SetPositionAndRotation(sandbox.transform.position + Vector3.forward * 2, Quaternion.Euler(11, 67, 19));
            Quaternion pickupRotation = bag.transform.rotation;
            hand.transform.SetPositionAndRotation(bag.topHandle.position, Quaternion.Euler(60, -45, 22));
            hand.StartManualInteraction((IXRSelectInteractable)bagGrab);
            yield return null; yield return null;
            Check(Quaternion.Angle(bag.transform.rotation, pickupRotation) < .1f, "handle pickup preserves current bag rotation despite different controller orientation");
            hand.transform.position += new Vector3(.1f, .2f, .4f);
            yield return null; yield return null;
            Check(Quaternion.Angle(bag.transform.rotation, pickupRotation) < .1f, "translating held bag does not auto-face the player");
            Vector3 releasePosition = bag.transform.position;
            Quaternion releaseRotation = bag.transform.rotation;
            hand.EndManualInteraction();
            yield return new WaitForSeconds(.15f);
            Check(bag.GetComponent<Rigidbody>().isKinematic && !bag.GetComponent<Rigidbody>().useGravity &&
                Vector3.Distance(bag.transform.position, releasePosition) < .001f && Quaternion.Angle(bag.transform.rotation, releaseRotation) < .1f,
                "released bag floats at exact world position and rotation");
            hand.transform.position += Vector3.right;
            yield return null;
            Check(Vector3.Distance(bag.transform.position, releasePosition) < .001f, "floating bag does not follow the released hand");

            bag.backSlot = slot; slot.radius = .35f; bag.equipment.holsterRadius = .18f;
            hand.transform.position = bag.topHandle.position;
            hand.StartManualInteraction((IXRSelectInteractable)bagGrab);
            bagGrab.trackPosition = false; bagGrab.trackRotation = false;
            bag.transform.position = slot.transform.position + Vector3.right * .28f;
            hand.EndManualInteraction();
            yield return null;
            Check(slot.Occupant == bag.equipment && bag.State == BackpackState.OnBack, "release 28 cm from back slot mounts through real release path despite 18 cm item radius");
            hand.StartManualInteraction((IXRSelectInteractable)bagGrab);
            bag.transform.position = slot.transform.position + Vector3.forward * 1.2f;
            hand.EndManualInteraction(); yield return null;
            Check(!slot.Occupant && bag.IsUsable, "release in front floats instead of mounting");

            var cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/R4-PowerCell.prefab");
            Check(cellPrefab && cellPrefab.GetComponent<VRInventoryItem>(), "R4 battery prefab configured for inventory");
            var staging = Child("Battery staging"); staging.SetActive(false);
            var cellGo = Instantiate(cellPrefab, staging.transform);
            cellGo.GetComponent<XRGrabInteractable>().interactionManager = manager;
            var cell = cellGo.GetComponent<JunkerPowerCell>();
            var cellItem = cellGo.GetComponent<VRInventoryItem>();
            cell.remainingShots = 7;
            staging.SetActive(true); yield return null;
            Select(cellItem); Place(cellItem, Vector3.zero); hand.EndManualInteraction();
            Vector3 batteryPosition = cellItem.transform.localPosition;
            yield return new WaitForSeconds(.1f);
            Check(cellItem.CurrentContainer == bag && cellGo.GetComponent<Rigidbody>().isKinematic && !cellGo.GetComponent<Rigidbody>().useGravity,
                "R4 battery stays stored during its loose-physics recovery window");
            yield return new WaitForSeconds(.8f);
            Check(Vector3.Distance(cellItem.transform.localPosition, batteryPosition) < .001f && cell.remainingShots == 7,
                "stored battery remains stable and retains remaining charge");

            var pistol = Child("Battery reload test pistol"); pistol.SetActive(false);
            pistol.AddComponent<BoxCollider>();
            pistol.AddComponent<Rigidbody>();
            var pistolGrab = pistol.AddComponent<XRGrabInteractable>();
            pistolGrab.interactionManager = manager;
            var socket = pistol.AddComponent<JunkerPistolBatterySocket>();
            var anchor = new GameObject("Battery socket").transform; anchor.SetParent(pistol.transform, false); socket.socketAnchor = anchor;
            pistol.SetActive(true); pistol.GetComponent<Rigidbody>().isKinematic = true;
            anchor.position = cellItem.transform.position;
            Check(!socket.CanInsert(cell), "nearby pistol socket cannot steal stored battery");
            int batteryId = cell.GetInstanceID();
            Select(cellItem);
            anchor.position = bag.transform.position + Vector3.right;
            cellItem.transform.position = anchor.position;
            hand.EndManualInteraction(); yield return null; yield return null;
            Check(socket.InsertedCell == cell && cell.GetInstanceID() == batteryId && !cellItem.CurrentContainer && cell.remainingShots == 7,
                "retrieved same battery reloads pistol with its original charge");
            socket.OpenLatch(); Select(cellItem); Place(cellItem, Vector3.zero); hand.EndManualInteraction();
            yield return new WaitForSeconds(.1f);
            Check(!socket.InsertedCell && cellItem.CurrentContainer == bag && cellGo.GetComponent<Rigidbody>().isKinematic,
                "battery removed from pistol can be stored again without socket or physics conflict");
            Select(cellItem); cellItem.transform.position = bag.transform.position + Vector3.up * 2; hand.EndManualInteraction();
            yield return null;
            Check(!cellGo.GetComponent<Rigidbody>().isKinematic && cellGo.GetComponent<Rigidbody>().useGravity && !cellItem.CurrentContainer,
                "retrieved battery dropped outside resumes normal physics");
        }
    }
}
