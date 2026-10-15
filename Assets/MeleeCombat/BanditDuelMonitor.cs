using ScavengerNPC;
using System.IO;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public sealed class BanditDuelMonitor : MonoBehaviour
    {
        public ScavengerHealth left, right;
        public bool captureSnapshots;
        GUIStyle label, title;
        bool announced;
        float firstDeathTime = -1;
        float started;
        int snapshotIndex;
        static readonly float[] SnapshotTimes = { 2f, 8f, 20f };

        void Start() { started = Time.time; }

        void Update()
        {
            if (captureSnapshots && left && right && snapshotIndex < SnapshotTimes.Length && Time.time - started >= SnapshotTimes[snapshotIndex])
            {
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BanditDuelSnapshots"));
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, $"duel-{snapshotIndex + 1}.png");
                ScreenCapture.CaptureScreenshot(file);
                Debug.Log($"Duel snapshot {snapshotIndex + 1}: Ash {left.Current:F1}/{left.maxHealth}, Rust {right.Current:F1}/{right.maxHealth}; states {left.GetComponent<StarvingBandit>().state}, {right.GetComponent<StarvingBandit>().state}; {file}");
                snapshotIndex++;
            }
            if (announced || !left || !right || (!left.IsDead && !right.IsDead)) return;
            if (firstDeathTime < 0) firstDeathTime = Time.time;
            if (Time.time - firstDeathTime < 1f) return;
            announced = true;
            Debug.Log("Bandit duel winner: " + (left.IsDead && right.IsDead ? "draw" : left.IsDead ? right.name : left.name));
        }

        void OnGUI()
        {
            if (!left || !right) return;
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            title ??= new GUIStyle(label) { fontSize = 25, fontStyle = FontStyle.Bold };
            float width = Mathf.Min(390, Screen.width * .38f);
            DrawHealth(left, 18, 18, width);
            DrawHealth(right, Screen.width - width - 18, 18, width);
            if (left.IsDead || right.IsDead)
                GUI.Label(new Rect(0, 85, Screen.width, 42), left.IsDead && right.IsDead ? "DRAW" : (left.IsDead ? right.name : left.name) + " WINS", title);
        }

        void DrawHealth(ScavengerHealth fighter, float x, float y, float width)
        {
            GUI.Box(new Rect(x, y, width, 56), GUIContent.none);
            GUI.Label(new Rect(x + 8, y + 3, width - 16, 25), fighter.name, label);
            GUI.Box(new Rect(x + 12, y + 33, width - 24, 13), GUIContent.none);
            var fraction = fighter.IsDead ? 0 : Mathf.Clamp01(fighter.Current / fighter.maxHealth);
            var old = GUI.color;
            GUI.color = Color.Lerp(new Color(.85f, .2f, .13f), new Color(.35f, .85f, .35f), fraction);
            GUI.DrawTexture(new Rect(x + 14, y + 35, (width - 28) * fraction, 9), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
