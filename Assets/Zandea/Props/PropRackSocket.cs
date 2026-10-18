using System.Collections;
using KenshiVR.MeleeCombat;
using JunkerEquipment;
using UnityEngine;

namespace Zandea.Props
{
    public sealed class PropRackSocket : MonoBehaviour
    {
        public NpcItemDefinition startingItem;
        public GameObject weaponPrefab;
        [Min(.1f)] public float returnRadius = .4f;
        public NpcItemInstance CurrentItem { get; private set; }
        string stableId;
        bool initialized;
        void Start()
        {
            if (!initialized) Initialize(OutpostPropPopulation.ScenePropId(gameObject));
        }
        public void Initialize(string id)
        {
            if (initialized) return;
            initialized = true;
            stableId = id;
            if (!startingItem || !weaponPrefab) return;
            CurrentItem = new NpcItemInstance(startingItem, id + "/weapon");
            Mount(Instantiate(weaponPrefab, transform.position, transform.rotation), CurrentItem);
        }
        void Mount(GameObject world, NpcItemInstance item)
        {
            world.transform.SetPositionAndRotation(transform.position, transform.rotation);
            world.transform.SetParent(transform, true);
            var body = world.GetComponent<Rigidbody>();
            if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; body.useGravity = false; }
            var marker = world.GetComponent<NpcWorldItem>();
            if (!marker) marker = world.AddComponent<NpcWorldItem>();
            marker.Bind(item);
            // The existing physical weapon and equipment components remain on the same instance.
            var presentation = world.GetComponent<PropItemPresentation>();
            if (!presentation) presentation = world.AddComponent<PropItemPresentation>();
            presentation.Bind(null, this, item);
        }
        public void ItemTaken(PropItemPresentation presentation, NpcItemInstance item)
        {
            if (CurrentItem != item) return;
            CurrentItem = null;
            presentation.transform.SetParent(null, true);
        }
        public IEnumerator TryReturnAfterRelease(PropItemPresentation presentation, NpcItemInstance item)
        {
            yield return null; // Wait for the weapon's existing drop/backpack callbacks.
            if (!presentation || item == null || !presentation.gameObject.activeInHierarchy) yield break;
            var weapon = presentation.GetComponent<PhysicalMeleeWeapon>();
            if (weapon && weapon.State != PhysicalMeleeWeapon.HoldState.World) yield break;
            var inventory = presentation.GetComponent<VRInventoryItem>();
            if (inventory && inventory.CurrentContainer) yield break;
            if (CurrentItem == null && Vector3.Distance(presentation.transform.position, transform.position) <= returnRadius)
            {
                CurrentItem = item;
                Mount(presentation.gameObject, item);
            }
            else
            {
                // The rack pins its starting weapon. A dropped weapon must become
                // dynamic again when it stays in the world away from the socket.
                var body = presentation.GetComponent<Rigidbody>();
                if (body) { body.isKinematic = false; body.useGravity = true; }
            }
        }
    }
}
