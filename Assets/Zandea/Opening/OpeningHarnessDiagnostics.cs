using System.Text;
using JunkerBody;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEngine;
using Zandea.Opening.Capture;

namespace Zandea.Opening
{
    // A development scene observer. No opening gameplay state is simulated here.
    public sealed class OpeningHarnessDiagnostics : MonoBehaviour
    {
        public VRBodyController player;
        public ScavengerPlayerHealth playerHealth;
        public BanditGroup group;
        public StarvingBandit[] bandits;
        public PlayerKnockout knockout;
        public PlayerSurrenderGesture surrender;
        public PlayerWristRestraints restraints;
        public OpeningCaptureGroup capture;
        public OpeningPrisonerEscort escort;
        public OpeningDirector director;
        [Min(1f)] public float logInterval = 10f;
        float nextLog;

        public string Snapshot()
        {
            var result = new StringBuilder("Opening harness | ");
            result.Append("player health=").Append(playerHealth ? playerHealth.Current.ToString("F0") : "missing");
            result.Append(" | consciousness=").Append(knockout ? knockout.State.ToString() : "missing");
            result.Append(" surrender=").Append(surrender ? surrender.State.ToString() : "missing");
            result.Append(" restraint=").Append(restraints ? restraints.IsRestrained.ToString() : "missing");
            result.Append(" capture=").Append(capture ? capture.State.ToString() : "missing");
            result.Append(" escort=").Append(escort ? escort.State.ToString() : "missing");
            result.Append(" director=").Append(director ? director.Phase.ToString() : "missing");
            result.Append(" | tracking=").Append(player && player.hmd && player.leftController && player.rightController ? "wired" : "missing");
            result.Append(" | group=").Append(group ? "wired" : "missing");
            if (bandits != null)
                foreach (var bandit in bandits)
                    result.Append(" | ").Append(bandit ? bandit.name : "missing bandit")
                        .Append('=').Append(bandit ? bandit.state.ToString() : "missing")
                        .Append(" contacts=").Append(bandit ? bandit.GetComponent<BanditDuelStrikeDamage>()?.ContactHits ?? 0 : 0)
                        .Append(" tipOverlaps=").Append(bandit ? bandit.GetComponent<BanditDuelStrikeDamage>()?.TipOverlaps ?? 0 : 0);
            return result.ToString();
        }

        void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + logInterval;
            Debug.Log(Snapshot(), this);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 900, 92), "Opening Slice 01 diagnostics");
            GUI.Label(new Rect(24, 36, 875, 44), Snapshot());
#if UNITY_EDITOR
            if (GUI.Button(new Rect(24, 78, 110, 22), "Restart scene")) Restart();
#endif
        }
#endif

        public void Restart()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(gameObject.scene.path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
        }
    }
}
