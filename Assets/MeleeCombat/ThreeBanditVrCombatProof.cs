using JunkerBody;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    /// <summary>Scene-local combat readout; it does not alter AI or grant blocks.</summary>
    public sealed class ThreeBanditVrCombatProof : MonoBehaviour
    {
        public VRBodyController player;
        public PhysicalMeleeWeapon playerSword;
        public StarvingBandit[] bandits;
        public int attackStarts, parryAttempts, playerWeaponContacts;
        public int[] attacksByBandit = new int[3];
        public int maxConcurrentAttackers;
        public float widestSurroundAngle;
        public bool setupValid;
        readonly StarvingBandit.State[] previous = new StarvingBandit.State[3];
        float started;
        bool reported;

        void Start()
        {
            if (attacksByBandit == null || attacksByBandit.Length != 3) attacksByBandit = new int[3];
            started = Time.time;
            setupValid = player && player.hmd && playerSword && bandits != null && bandits.Length == 3;
            if (setupValid)
                for (int i = 0; i < 3; i++)
                    setupValid &= bandits[i] && bandits[i].parryIncomingNpcAttacks &&
                        bandits[i].GetComponent<NpcGeneratedWeapon>() &&
                        bandits[i].GetComponent<NpcGeneratedWeapon>().weaponPrefab;
            Debug.Log("THREE_BANDIT_SETUP_" + (setupValid ? "PASS" : "FAIL"));
        }

        void Update()
        {
            if (!setupValid) return;
            int concurrentAttackers = 0;
            for (int i = 0; i < 3; i++)
            {
                var bandit = bandits[i];
                if (!bandit) continue;
                if (bandit.state == StarvingBandit.State.Strike && previous[i] != StarvingBandit.State.Strike)
                {
                    attackStarts++;
                    attacksByBandit[i]++;
                }
                if (bandit.state == StarvingBandit.State.Anticipate || bandit.state == StarvingBandit.State.Strike ||
                    bandit.state == StarvingBandit.State.FollowThrough) concurrentAttackers++;
                if (bandit.state == StarvingBandit.State.Parry && previous[i] != StarvingBandit.State.Parry)
                    parryAttempts++;
                previous[i] = bandit.state;
            }
            if (playerSword.State == PhysicalMeleeWeapon.HoldState.HeldByPlayer)
                playerWeaponContacts = playerSword.WeaponContactEntries;
            maxConcurrentAttackers = Mathf.Max(maxConcurrentAttackers, concurrentAttackers);
            Vector3 center = player.hmd.position;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                {
                    if (!bandits[i] || !bandits[j]) continue;
                    Vector3 a = Vector3.ProjectOnPlane(bandits[i].transform.position - center, Vector3.up);
                    Vector3 b = Vector3.ProjectOnPlane(bandits[j].transform.position - center, Vector3.up);
                    if (a.sqrMagnitude > .1f && b.sqrMagnitude > .1f)
                        widestSurroundAngle = Mathf.Max(widestSurroundAngle, Vector3.Angle(a, b));
                }
            if (!reported && Time.time - started >= 25f)
            {
                reported = true;
                Debug.Log($"THREE_BANDIT_PROOF setup={setupValid} attacks={attackStarts} " +
                    $"byBandit={attacksByBandit[0]}/{attacksByBandit[1]}/{attacksByBandit[2]} " +
                    $"maxConcurrent={maxConcurrentAttackers} " +
                    $"parries={parryAttempts} playerWeaponContacts={playerWeaponContacts} " +
                    $"surroundAngle={widestSurroundAngle:F1}");
            }
        }

        void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 380, 172), "Three sword bandits versus VR player");
            GUI.Label(new Rect(24, 38, 355, 20), $"Attacks: {attackStarts}   Parry attempts: {parryAttempts}");
            GUI.Label(new Rect(24, 60, 355, 20), $"Player sword contacts: {playerWeaponContacts}");
            GUI.Label(new Rect(24, 82, 355, 20), $"Bandit spread: {widestSurroundAngle:F0}°   Setup: {(setupValid ? "ready" : "invalid")}");
            if (!setupValid) return;
            for (int i = 0; i < 3; i++)
            {
                var bandit = bandits[i];
                if (!bandit || !bandit.staff) continue;
                float baseRadius = bandit.combatGroup ? bandit.combatGroup.surroundRadius : 1.72f;
                float standing = Vector3.ProjectOnPlane(bandit.transform.position - player.hmd.position, Vector3.up).magnitude;
                GUI.Label(new Rect(24, 104 + i * 20, 355, 20),
                    $"{bandit.name}: reach {bandit.staff.CombatReach:F2} m, slot {bandit.PreferredGroupCombatDistance(baseRadius):F2} m, now {standing:F2} m");
            }
        }
    }
}
