using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class CharacterStatArenaBatch
    {
        const string Pending = "KenshiVR.CharacterStatArenaBatch.Pending";
        const string MatchupKey = "KenshiVR.CharacterStatArenaBatch.Matchup";
        static float started;
        static bool initialized;
        static int seed;
        static float leftControl, rightControl, leftSpeed, rightSpeed;
        static CharacterStatArenaBatch() => EditorApplication.update += Tick;
        public static void WeakWeak() => Run(CharacterStatArenaScenario.Matchup.WeakWeak);
        public static void WeakVeteran() => Run(CharacterStatArenaScenario.Matchup.WeakVeteran);
        public static void AverageAverage() => Run(CharacterStatArenaScenario.Matchup.AverageAverage);
        public static void VeteranVeteran() => Run(CharacterStatArenaScenario.Matchup.VeteranVeteran);
        public static void VeteranWeak() => Run(CharacterStatArenaScenario.Matchup.VeteranWeak);

        static void Run(CharacterStatArenaScenario.Matchup matchup)
        {
            EditorPrefs.SetInt(MatchupKey, (int)matchup);
            EditorPrefs.SetBool(Pending, true);
            EditorSceneManager.OpenScene(CharacterAttributeSetupBuilder.StatArena);
            EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (!Application.isBatchMode || !EditorPrefs.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            var scenario = UnityEngine.Object.FindFirstObjectByType<CharacterStatArenaScenario>();
            var telemetry = UnityEngine.Object.FindFirstObjectByType<BanditArenaTelemetry>();
            var resolver = WeaponDisarmResolver.Active;
            if (!scenario || !telemetry || !resolver) return;
            if (!initialized)
            {
                initialized = true;
                started = Time.realtimeSinceStartup;
                scenario.matchup = (CharacterStatArenaScenario.Matchup)EditorPrefs.GetInt(MatchupKey);
                scenario.Apply();
                var leftWeapon = telemetry.left.GetComponent<NpcGeneratedWeapon>()?.Instance;
                var rightWeapon = telemetry.right.GetComponent<NpcGeneratedWeapon>()?.Instance;
                leftControl = leftWeapon ? leftWeapon.ActiveControlMultiplier : 0f;
                rightControl = rightWeapon ? rightWeapon.ActiveControlMultiplier : 0f;
                leftSpeed = telemetry.left.AttackAnimationSpeed;
                rightSpeed = telemetry.right.AttackAnimationSpeed;
                var value = Environment.GetEnvironmentVariable("KENSHI_COMBAT_SEED");
                seed = int.TryParse(value, out var parsed) ? parsed : 1201;
                UnityEngine.Random.InitState(seed);
                resolver.mode = WeaponDisarmResolver.ProbabilityMode.Normal;
                resolver.useSeededRandom = true;
                resolver.ResetSeed(seed);
            }
            if (Time.realtimeSinceStartup - started < 30f) return;
            var left = telemetry.left;
            var right = telemetry.right;
            Debug.Log($"STAT_ARENA_RESULT matchup={scenario.matchup} seed={seed} elapsed={telemetry.CombatDuration:F1} winner={telemetry.Winner} " +
                $"leftAttacks={telemetry.LeftAttacks} rightAttacks={telemetry.RightAttacks} " +
                $"leftHits={telemetry.LeftHits} rightHits={telemetry.RightHits} " +
                $"leftDamage={telemetry.LeftDamage:F1} rightDamage={telemetry.RightDamage:F1} " +
                $"leftBlocks={telemetry.LeftBlocks} rightBlocks={telemetry.RightBlocks} " +
                $"leftParries={telemetry.LeftParries} rightParries={telemetry.RightParries} " +
                $"clashes={telemetry.QualifyingClashes} disarmRolls={telemetry.DisarmAttempts} disarms={telemetry.DisarmSuccesses} " +
                $"leftRecovery={telemetry.LeftRecoveries} rightRecovery={telemetry.RightRecoveries} " +
                $"leftHealth={left.health.Current:F1} rightHealth={right.health.Current:F1} " +
                $"leftControl={leftControl:F2} rightControl={rightControl:F2} " +
                $"leftSpeed={leftSpeed:F2} rightSpeed={rightSpeed:F2}");
            EditorPrefs.DeleteKey(Pending);
            EditorPrefs.DeleteKey(MatchupKey);
            EditorApplication.Exit(0);
        }
    }
}
