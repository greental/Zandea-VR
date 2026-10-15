using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerEquipment;

namespace ScavengerNPC
{
    // A simple NPC-only belt: real R4 cells are visible during combat and become loot on death.
    public sealed class ScavengerAmmoBelt : MonoBehaviour
    {
        public Transform waist;
        public JunkerPowerCell cellPrefab;
        [Range(0, 8)] public int startingCells = 3;
        public Vector3[] slotOffsets =
        {
            new Vector3(-.22f, -.12f, .13f),
            new Vector3(0f, -.12f, .17f),
            new Vector3(.22f, -.12f, .13f)
        };
        public Vector3 cellEuler = new Vector3(90, 0, 0);
        JunkerPowerCell[] cells;
        public int Remaining
        {
            get
            {
                int count = 0;
                if (cells != null) foreach (var cell in cells) if (cell && cell.remainingShots > 0) count++;
                return count;
            }
        }
        void Start()
        {
            if (!waist)
            {
                var pose = GetComponent<ScavengerPose>();
                if (pose) waist = pose.hips;
            }
            if (!waist || !cellPrefab || startingCells <= 0) return;
            cells = new JunkerPowerCell[startingCells];
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = Instantiate(cellPrefab);
                cell.name = "Scavenger spare R4 " + (i + 1);
                cell.transform.SetPositionAndRotation(waist.TransformPoint(Slot(i)), waist.rotation * Quaternion.Euler(cellEuler));
                cell.transform.SetParent(waist, true);
                cell.remainingShots = cell.capacity;
                var body = cell.GetComponent<Rigidbody>();
                if (body) { body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None; }
                foreach (var shape in cell.GetComponentsInChildren<Collider>(true)) if (shape) shape.isTrigger = true;
                var grab = cell.GetComponent<XRGrabInteractable>();
                if (grab) grab.enabled = false;
                cells[i] = cell;
            }
        }
        Vector3 Slot(int index)
        {
            if (slotOffsets != null && index < slotOffsets.Length) return slotOffsets[index];
            return new Vector3((index - (startingCells - 1) * .5f) * .2f, -.12f, .15f);
        }
        public JunkerPowerCell TakeChargedCell()
        {
            if (cells == null) return null;
            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i] || cells[i].remainingShots <= 0) continue;
                var cell = cells[i];
                cells[i] = null;
                return cell;
            }
            return null;
        }
        public void DropRemaining()
        {
            if (cells == null) return;
            // Cells are mounted against the hips. If their colliders wake while
            // overlapping ragdoll limbs, the solver can launch the whole corpse.
            // Ignore this corpse before enabling the cells' loose physics.
            var corpseShapes = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i]) continue;
                var cell = cells[i];
                cells[i] = null;
                foreach (var cellShape in cell.GetComponentsInChildren<Collider>(true))
                    foreach (var corpseShape in corpseShapes)
                        if (cellShape && corpseShape && cellShape != corpseShape)
                            Physics.IgnoreCollision(cellShape, corpseShape);
                cell.SetLoose();
                // SetLoose changes trigger state; reapply the filter to the final
                // collider configuration before the next physics step.
                foreach (var cellShape in cell.GetComponentsInChildren<Collider>(true))
                    foreach (var corpseShape in corpseShapes)
                        if (cellShape && corpseShape && cellShape != corpseShape)
                            Physics.IgnoreCollision(cellShape, corpseShape);
            }
        }
        void OnDrawGizmosSelected()
        {
            var anchor = waist ? waist : transform;
            Gizmos.color = new Color(.2f, .7f, 1f);
            for (int i = 0; i < startingCells; i++) Gizmos.DrawWireSphere(anchor.TransformPoint(Slot(i)), .07f);
        }
    }
}
