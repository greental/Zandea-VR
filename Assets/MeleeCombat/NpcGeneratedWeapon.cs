using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    /// <summary>Put this on an NPC, assign a generated weapon prefab and a hand/weapon-goal transform.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class NpcGeneratedWeapon : MonoBehaviour
    {
        public PhysicalMeleeWeapon weaponPrefab;
        public Transform gripTarget;
        public StarvingBandit bandit;
        public ScavengerNPC.ScavengerHealth health;
        public bool dropOnDestroy = true;

        PhysicalMeleeWeapon instance;
        public PhysicalMeleeWeapon Instance => instance;

        void Awake()
        {
            if (!weaponPrefab || !gripTarget)
            {
                Debug.LogWarning("NPC weapon needs a prefab and grip target.", this);
                return;
            }
            instance = Instantiate(weaponPrefab, gripTarget.position, gripTarget.rotation);
            instance.name = weaponPrefab.name;
            instance.ownerRoot = transform;
            instance.npcGripTarget = gripTarget;
            if (bandit)
            {
                bandit.staff = instance;
                bandit.weaponGoal = gripTarget;
                bandit.positiveStrikingTip = instance.transform.Find("Positive striking tip");
                bandit.negativeStrikingTip = instance.transform.Find("Negative striking tip");
                var motion = bandit.GetComponent<BanditRigMotion>();
                if (motion) motion.staff = instance;
            }
            if (health) health.onDeath.AddListener(Drop);
        }

        public void Drop()
        {
            if (!instance) return;
            instance.DropFromNpc();
            instance = null;
        }

        void OnDestroy()
        {
            if (health) health.onDeath.RemoveListener(Drop);
            if (dropOnDestroy) Drop();
        }
    }
}
