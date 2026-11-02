using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class WeaponReachBaker
    {
        const string Marker = "KenshiVR.MeleeCombat.PhysicalMeleeWeapon";

        [MenuItem("Zandea/Melee Combat/Bake physical weapon reach")]
        public static void BakeAll() => Process(true);

        // Used by isolated validation to obtain values without replacing authored prefabs.
        public static void ExportMeasurements() => Process(false);

        static void Process(bool save)
        {
            int count = 0;
            var report = new System.Text.StringBuilder();
            foreach (var diskPath in Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories))
            {
                string path = diskPath.Replace('\\', '/');
                if (!File.ReadAllText(path).Contains(Marker)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var weapons = root.GetComponentsInChildren<PhysicalMeleeWeapon>(true);
                    if (weapons.Length != 1) throw new InvalidOperationException(
                        $"Expected one physical weapon in {path}; found {weapons.Length}");
                    var weapon = weapons[0];
                    if (!StaffAxisCalibration.TryMeasure(weapon.transform, out var positive, out var negative))
                        throw new InvalidOperationException("Could not measure weapon mesh: " + path);
                    Vector3 hand = weapon.grip ? weapon.grip.position : weapon.transform.position;
                    float reach = Mathf.Clamp(Mathf.Max(
                        Vector3.Distance(hand, weapon.transform.TransformPoint(positive)),
                        Vector3.Distance(hand, weapon.transform.TransformPoint(negative))), .3f, 2.5f);
                    if (save)
                    {
                        weapon.combatReachMeters = reach;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    report.Append(path).Append('\t').Append(reach.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            const string output = "Library/WeaponReachBake.tsv";
            File.WriteAllText(output, report.ToString());
            Debug.Log($"WEAPON_REACH_BAKE_{(save ? "SAVED" : "EXPORTED")} count={count} report={output}");
        }
    }
}
