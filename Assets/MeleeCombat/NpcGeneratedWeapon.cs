using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;

namespace KenshiVR.MeleeCombat
{
    [DefaultExecutionOrder(-500)]
    public sealed class NpcGeneratedWeapon : MonoBehaviour
    {
        public PhysicalMeleeWeapon weaponPrefab;
        public Transform gripTarget;
        public StarvingBandit bandit;
        public ScavengerHealth health;
        public bool dropOnDestroy = true;
        public bool startEquipped = true;
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

        void Awake()
        {
            if (!bandit) bandit = GetComponent<StarvingBandit>();
            if (!health) health = GetComponent<ScavengerHealth>();
            agent = GetComponent<NavMeshAgent>();
            if (health) health.onDeath.AddListener(Drop);
            if (startEquipped && weaponPrefab && gripTarget)
            {
                instance = Instantiate(weaponPrefab, gripTarget.position, gripTarget.rotation);
                instance.name = weaponPrefab.name;
                if (!instance.TryEquipNpc(this, gripTarget))
                { Destroy(instance.gameObject); instance = null; }
            }
        }

        void Start() => BindWeapon();

        void BindWeapon()
        {
            if (bandit) bandit.SetEquippedStaff(instance, gripTarget);
            var grip = GetComponentInChildren<MocapStaffPreview>(true);
            if (grip)
            {
                grip.staff = instance;
                grip.positiveTip = instance ? instance.transform.Find("Positive striking tip") : null;
                grip.negativeTip = instance ? instance.transform.Find("Negative striking tip") : null;
                grip.driveVisualStaff = false;
            }
            var motion = GetComponent<BanditRigMotion>();
            if (motion) motion.staff = instance;
        }

        public void Drop()
        {
            Abandon();
            if (!instance) return;
            var held = instance;
            instance = null;
            BindWeapon();
            held.Drop();
        }

        void Update()
        {
            if (!acquireNearbyWeapons || instance || !gripTarget || !weaponPrefab || (health && health.IsDead)) return;
            if (bandit && bandit.target && Vector3.Distance(transform.position, bandit.target.position) < safeEnemyDistance)
            { Abandon(); return; }
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
                    if (picked.TryEquipNpc(this, gripTarget)) { instance = picked; BindWeapon(); }
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
