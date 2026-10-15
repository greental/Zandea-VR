using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment
{
    [DisallowMultipleComponent]
    
    public sealed class JunkerPistolBatterySocket : MonoBehaviour
    {
        public string acceptedCellType = "R4";
        public Transform socketAnchor;
        [Tooltip("Cell root orientation relative to the socket. Turn around the cell's local Y axis to rotate its face without tipping it sideways.")]
        public Vector3 mountedEuler;
        public JunkerPowerCell startingCell;
        [Header("Ammunition")]
        [Min(1), Tooltip("Shots a fresh cell provides in this pistol. Used cells keep their remaining charge when reinserted.")]
        public int shotsPerCell = 24;
        [Min(.1f)] public float toolSecondsPerCell = 60f;
        float toolUseAccumulator;
        [Min(.01f)] public float insertionRadius = .18f;
        public AudioSource latchAudio;
        public AudioClip latchClick;
        public bool IsOpen { get; private set; }
        public JunkerPowerCell InsertedCell { get; private set; }
        InputAction rightA;
        XRGrabInteractable pistolGrab;

        void Awake()
        {
            pistolGrab = GetComponent<XRGrabInteractable>();
            if (startingCell) MountStartingCell(startingCell);
        }

        void OnEnable()
        {
            rightA = new InputAction("JKR-88 battery latch", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            rightA.Enable();
        }

        void OnDisable() { rightA?.Dispose(); rightA = null; }

        void Update()
        {
            if (pistolGrab && pistolGrab.isSelected && rightA != null && rightA.WasPressedThisFrame()) OpenLatch();
        }

        public void OpenLatch()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (InsertedCell) InsertedCell.SetLatchOpen(true);
            if (latchAudio && latchClick) latchAudio.PlayOneShot(latchClick);
        }

        public void CloseLatch()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (InsertedCell) InsertedCell.SetLatchOpen(false);
            if (latchAudio && latchClick) latchAudio.PlayOneShot(latchClick);
        }

        public void ToggleLatch()
        {
            if (IsOpen) CloseLatch(); else OpenLatch();
        }

        public bool CanFire() => !IsOpen && InsertedCell && InsertedCell.remainingShots > 0;
        public bool TryConsumeShot() => CanFire() && InsertedCell.ConsumeShot();
        public bool HasToolPower => !IsOpen && InsertedCell && InsertedCell.remainingShots > 0;
        public bool TryConsumeToolTime(float deltaTime)
        {
            if (!HasToolPower) return false;
            toolUseAccumulator += Mathf.Max(0, deltaTime) * Mathf.Max(1, shotsPerCell) / Mathf.Max(.1f, toolSecondsPerCell);
            while (toolUseAccumulator >= 1f)
            {
                if (!InsertedCell.ConsumeShot()) { toolUseAccumulator = 0; return false; }
                toolUseAccumulator -= 1f;
            }
            return true;
        }
        public bool CanInsert(JunkerPowerCell cell)
        {
            var inventory = GetComponent<VRInventoryItem>();
            return socketAnchor && !InsertedCell && cell && cell.cellType == acceptedCellType && !cell.Socket &&
                !cell.IsStoredInInventory && !(inventory && inventory.CurrentContainer);
        }

        public bool Insert(JunkerPowerCell cell)
        {
            if (!CanInsert(cell)) return false;
            ConfigureCellCharge(cell);
            InsertedCell = cell;
            toolUseAccumulator = 0;
            cell.SetMounted(this);
            if (IsOpen) CloseLatch();
            return true;
        }

        internal void MountStartingCell(JunkerPowerCell cell)
        {
            if (!cell || !socketAnchor || InsertedCell && InsertedCell != cell) return;
            ConfigureCellCharge(cell);
            InsertedCell = cell;
            toolUseAccumulator = 0;
            cell.SetMounted(this);
        }

        void ConfigureCellCharge(JunkerPowerCell cell)
        {
            int count = Mathf.Max(1, shotsPerCell);
            bool fresh = cell.remainingShots >= cell.capacity;
            cell.capacity = count;
            cell.remainingShots = fresh ? count : Mathf.Clamp(cell.remainingShots, 0, count);
        }

        public void Remove(JunkerPowerCell cell)
        {
            if (InsertedCell != cell || !IsOpen) return;
            InsertedCell = null;
            cell.SetLoose();
        }

        internal void Forget(JunkerPowerCell cell) { if (InsertedCell == cell) InsertedCell = null; }
    }
}
