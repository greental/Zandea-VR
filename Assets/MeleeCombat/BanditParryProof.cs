using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    /// <summary>Observable acceptance probe for the authored parry duel.</summary>
    public sealed class BanditParryProof : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallInProofScene()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "BanditParryProof" &&
                !FindFirstObjectByType<BanditParryProof>())
                new GameObject("Parry proof results").AddComponent<BanditParryProof>();
        }
        public StarvingBandit defender, attacker;
        public float duration = 30f;
        public int parryAttempts, physicalContacts, parryContacts;
        public float peakContactImpulse;
        float started;
        float lastParryTime = -10f;
        int previousContactEntries;
        bool wasParrying, reported;

        void Start()
        {
            started = Time.time;
            foreach (var bandit in FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
            {
                if (!defender && bandit.defensiveOnly) defender = bandit;
                else if (!attacker && bandit != defender) attacker = bandit;
            }
        }

        void Update()
        {
            if (!defender || !attacker || !defender.staff || !attacker.staff) return;
            bool parrying = defender.state == StarvingBandit.State.Parry;
            if (parrying && !wasParrying)
            {
                parryAttempts++;
                lastParryTime = Time.time;
                Debug.Log("PARRY_PROOF_ATTEMPT " + parryAttempts);
            }
            wasParrying = parrying;
            int entries = defender.staff.WeaponContactEntries;
            if (entries > previousContactEntries && Time.time - lastParryTime < .5f)
                parryContacts += entries - previousContactEntries;
            previousContactEntries = entries;
            physicalContacts = Mathf.Max(physicalContacts, entries);
            peakContactImpulse = Mathf.Max(peakContactImpulse, defender.staff.MaximumWeaponContactImpulse);
            if (!reported && Time.time - started >= duration)
            {
                reported = true;
                Debug.Log((parryAttempts > 0 && parryContacts > 0 && peakContactImpulse > 0.01f
                    ? "PARRY_PROOF_PASS" : "PARRY_PROOF_FAIL") +
                    $" attempts={parryAttempts} contacts={physicalContacts} parryContacts={parryContacts} peakImpulse={peakContactImpulse:F3}");
            }
        }

        void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 390, 100), "Physical parry proof");
            GUI.Label(new Rect(24, 38, 370, 20), $"Attempts: {parryAttempts}   Parry contacts: {parryContacts}");
            GUI.Label(new Rect(24, 60, 370, 20), $"Peak solver impulse: {peakContactImpulse:F2} N·s");
            GUI.Label(new Rect(24, 80, 370, 20), "Restart: stop and play the scene again.");
        }
    }
}
