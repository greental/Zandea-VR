using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;
using Zandea.FriendlyNPC;

namespace KenshiVR.MeleeCombat
{
    [DefaultExecutionOrder(-500)]
    public sealed class NpcGeneratedWeapon : MonoBehaviour, IDisarmCombatant
    {
        public PhysicalMeleeWeapon weaponPrefab;
        public Transform gripTarget;
        public Transform weaponPoseTarget;
        public StarvingBandit bandit;
        public ScavengerHealth health;
        public bool dropOnDestroy = true;
        public bool startEquipped = true;
        [Header("Inventory ownership")]
        public NpcInventory inventory;
        public NpcItemDefinition weaponItem;
        [Header("Unarmed acquisition")]
        public bool acquireNearbyWeapons = true;
        [Min(.25f)] public float searchRadius = 12f;
        [Min(.1f)] public float searchInterval = 1f;
        [Min(.1f)] public float pickupDistance = 1.25f;
        [Min(.1f)] public float safeEnemyDistance = 3f;
        [Min(1f)] public float pickupTimeout = 8f;

        PhysicalMeleeWeapon instance, candidate;
        NavMeshAgent agent;
        float nextSearch, deadline;
        public PhysicalMeleeWeapon Instance => instance;
        public Transform CombatantRoot => transform;
        public bool CanClash => enabled && instance && instance.State == PhysicalMeleeWeapon.HoldState.HeldByNpc &&
            instance.Owner == this && bandit && bandit.enabled && bandit.state != StarvingBandit.State.Dead &&
            (!health || health.State == NpcConsciousness.Conscious) && !(GetComponent<ScavengerRagdoll>()?.Active ?? false);
        public bool IsStriking => bandit && bandit.state == StarvingBandit.State.Strike;
        public bool Opposes(IDisarmCombatant other) => bandit && other != null &&
            other.CombatantRoot && bandit.target &&
            (bandit.target == other.CombatantRoot || bandit.target.IsChildOf(other.CombatantRoot));
        public bool ReleaseForDisarm(PhysicalMeleeWeapon weapon)
        {
            if (!CanClash || instance != weapon) return false;
            Drop();
            return weapon.State == PhysicalMeleeWeapon.HoldState.World;
        }
        public bool TryEquipWorldWeapon(PhysicalMeleeWeapon weapon)
        {
            if (!weapon || instance || !gripTarget || (inventory && inventory.Equipped(NpcEquipmentSlot.MainWeapon) != null) ||
                (health && health.State != NpcConsciousness.Conscious) ||
                !weapon.TryEquipNpc(this, gripTarget)) return false;
            Abandon();
            instance = weapon;
            if (inventory)
            {
                var marker = weapon.GetComponent<NpcWorldItem>();
                var worldItem = marker ? marker.Item : null;
                if (worldItem != null && inventory.TakeWorld(marker)) inventory.Equip(worldItem, NpcEquipmentSlot.MainWeapon);
                else if (weaponItem && (!marker || marker.Item == null)) RegisterWeapon(weapon);
            }
            BindWeapon();
            return true;
        }

        void RegisterWeapon(PhysicalMeleeWeapon weapon)
        {
            if (!inventory || !weaponItem || !weapon) return;
            var item = new NpcItemInstance(weaponItem);
            if (inventory.Add(item)) inventory.Equip(item, NpcEquipmentSlot.MainWeapon);
        }

        void Awake()
        {
            if (!bandit) bandit = GetComponent<StarvingBandit>();
            if (!health) health = GetComponent<ScavengerHealth>();
            if (!inventory) inventory = GetComponent<NpcInventory>();
            agent = GetComponent<NavMeshAgent>();
            if (health) health.onDeath.AddListener(Drop);
            if (startEquipped) EnsureStartingWeapon(false);
        }

        public bool EnsureStartingWeapon(bool bind = true)
        {
            if (instance) { if (bind) BindWeapon(); return true; }
            if (!weaponPrefab || !gripTarget) return false;
            if (!weaponItem)
            {
                var catalog = Resources.Load<NpcWearableCatalog>("ZandeaNpcWearables");
                if (catalog) weaponItem = catalog.FindWeapon(weaponPrefab.name);
            }
            instance = Instantiate(weaponPrefab, gripTarget.position, gripTarget.rotation);
            instance.name = weaponPrefab.name;
            if (weaponItem)
            {
                var trade = instance.GetComponent<TradeItem>();
                if (!trade) trade = instance.gameObject.AddComponent<TradeItem>();
                trade.definition = weaponItem;
                trade.ownerId = "player";
            }
            if (!instance.TryEquipNpc(this, gripTarget))
            {
                Destroy(instance.gameObject);
                instance = null;
                return false;
            }
            RegisterWeapon(instance);
            if (bind) BindWeapon();
            return true;
        }

        public void SetStartingWeapon(PhysicalMeleeWeapon prefab)
        {
            if (!prefab || weaponPrefab == prefab) return;
            if (instance)
            {
                var equipped = inventory ? inventory.Equipped(NpcEquipmentSlot.MainWeapon) : null;
                if (equipped != null) inventory.Remove(equipped);
                var previous = instance;
                instance = null;
                previous.Drop();
                Destroy(previous.gameObject);
            }
            weaponPrefab = prefab;
            EnsureStartingWeapon();
        }

        public void ClearStartingWeapon()
        {
            if (instance)
            {
                var equipped = inventory ? inventory.Equipped(NpcEquipmentSlot.MainWeapon) : null;
                if (equipped != null) inventory.Remove(equipped);
                var previous = instance;
                instance = null;
                previous.Drop();
                Destroy(previous.gameObject);
            }
            weaponPrefab = null;
            startEquipped = false;
            acquireNearbyWeapons = false;
            BindWeapon();
        }

        void Start() => BindWeapon();

        void BindWeapon()
        {
            if (bandit) bandit.SetEquippedStaff(instance, weaponPoseTarget ? weaponPoseTarget : gripTarget);
            var grip = GetComponentInChildren<MocapStaffPreview>(true);
            if (grip)
            {
                grip.staff = instance;
                grip.positiveTip = instance ? instance.transform.Find("PositiveStrikingTip") ??
                    instance.transform.Find("Positive striking tip") : null;
                grip.negativeTip = instance ? instance.transform.Find("NegativeStrikingTip") ??
                    instance.transform.Find("Negative striking tip") : null;
                grip.driveVisualStaff = instance && instance.useMeshContactShape;
            }
            var motion = GetComponent<BanditRigMotion>();
            if (motion) motion.staff = instance;
        }

        public void Drop()
        {
            Abandon();
            if (!instance) return;
            var held = instance;
            var owned = inventory ? inventory.Equipped(NpcEquipmentSlot.MainWeapon) : null;
            instance = null;
            BindWeapon();
            held.Drop();
            if (owned != null) inventory.Drop(owned, held.transform.position, held.transform.rotation, held.gameObject);
        }

        public void DropForKnockout()
        {
            acquireNearbyWeapons = false;
            Drop();
        }

        void Update()
        {
            if (!acquireNearbyWeapons || instance || !gripTarget || !weaponPrefab ||
                (health && health.State != NpcConsciousness.Conscious)) return;
            if (bandit && bandit.target && Vector3.Distance(transform.position, bandit.target.position) < safeEnemyDistance)
            { Abandon(); return; }
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!agent || !agent.enabled || !agent.isOnNavMesh) { Abandon(); return; }
            if (candidate)
            {
                if (candidate.State != PhysicalMeleeWeapon.HoldState.World || candidate.Reservation != this ||
                    Time.time > deadline || (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid))
                { Abandon(); return; }
                if (Vector3.Distance(transform.position, candidate.transform.position) <= pickupDistance)
                {
                    var picked = candidate;
                    Abandon();
                    TryEquipWorldWeapon(picked);
                    return;
                }
                if (!agent.pathPending && (!agent.hasPath || Vector3.Distance(agent.destination, candidate.transform.position) > .6f))
                    agent.SetDestination(candidate.transform.position);
                return;
            }
            if (Time.time < nextSearch) return;
            nextSearch = Time.time + searchInterval;
            float best = searchRadius * searchRadius;
            PhysicalMeleeWeapon nearest = null;
            foreach (var weapon in PhysicalMeleeWeapon.Active)
            {
                if (!weapon || weapon.State != PhysicalMeleeWeapon.HoldState.World ||
                    weapon.weaponId != weaponPrefab.weaponId) continue;
                float distance = (weapon.transform.position - transform.position).sqrMagnitude;
                if (distance < best && (!weapon.Reservation || weapon.Reservation == this))
                { best = distance; nearest = weapon; }
            }
            if (!nearest || !nearest.TryReserve(this, pickupTimeout)) return;
            if (!agent.SetDestination(nearest.transform.position)) { nearest.ReleaseReservation(this); return; }
            candidate = nearest; deadline = Time.time + pickupTimeout;
        }

        void Abandon()
        {
            if (candidate) candidate.ReleaseReservation(this);
            candidate = null;
            if (agent && agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
        }

        void OnDestroy()
        {
            if (health) health.onDeath.RemoveListener(Drop);
            Abandon();
            if (dropOnDestroy) Drop();
        }
    }
}
