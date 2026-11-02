using UnityEngine;
using ScavengerNPC;

namespace KenshiVR.MeleeCombat
{
    /// <summary>Sets up the saved arena for a four-on-one combat review.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class BanditSurroundTestSetup : MonoBehaviour
    {
        public StarvingBandit[] weaponPrefabs;
        BanditGroup group;
        ScavengerPlayerHealth targetHealth;

        void Awake()
        {
            foreach (var old in FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            var duelMonitor = FindFirstObjectByType<BanditDuelMonitor>();
            if (duelMonitor) duelMonitor.gameObject.SetActive(false);

            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.name = "Player target - move to test pursuit";
            target.transform.position = new Vector3(0, 1, 0);
            target.transform.localScale = new Vector3(.5f, 1f, .5f);
            targetHealth = target.AddComponent<ScavengerPlayerHealth>();
            targetHealth.maxHealth = 10000f;
            targetHealth.Restore();

            group = new GameObject("Bandit group - surround player").AddComponent<BanditGroup>();
            group.buildLocalNavMesh = false;
            group.player = target.transform;
            group.sightRange = 20f;
            group.surroundRadius = 2.15f;
            group.authoredMembers = new StarvingBandit[weaponPrefabs.Length];
            for (int i = 0; i < weaponPrefabs.Length; i++)
            {
                if (!weaponPrefabs[i]) continue;
                float angle = i * Mathf.PI * 2f / weaponPrefabs.Length;
                var position = new Vector3(Mathf.Sin(angle) * 3.7f, 0, Mathf.Cos(angle) * 3.7f);
                var fighter = Instantiate(weaponPrefabs[i], position,
                    Quaternion.LookRotation(-new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle))));
                fighter.name = "Bandit " + (i + 1) + " - " + weaponPrefabs[i].name;
                fighter.previewEnabled = false;
                fighter.showTrajectory = false;
                fighter.cycleAttackDirections = true;
                fighter.firstAttackDirection = i * 2;
                fighter.dodgeIncomingNpcAttacks = true;
                group.authoredMembers[i] = fighter;
            }
            if (weaponPrefabs.Length > 0) group.banditPrefab = weaponPrefabs[0];
            var camera = Camera.main;
            if (camera)
            {
                camera.transform.position = new Vector3(0, 7.5f, -8.5f);
                camera.transform.LookAt(new Vector3(0, 1, 0));
            }
        }

        void OnGUI()
        {
            if (!group || !targetHealth) return;
            GUI.Box(new Rect(12, 12, 350, 76), "Bandit surround test");
            GUI.Label(new Rect(24, 37, 330, 22), "Move the Player target object to test pursuit and flanking.");
            GUI.Label(new Rect(24, 57, 330, 22), "Target health: " + targetHealth.Current.ToString("F0"));
        }
    }
}
