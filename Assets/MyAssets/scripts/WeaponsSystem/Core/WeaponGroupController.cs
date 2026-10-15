using System;
using System.Text;
using UnityEngine;

namespace VRKoombar.WeaponsStaging.Core
{
    public class WeaponGroupController : MonoBehaviour
    {
        [SerializeField] private WeaponController[] primaryWeapons;
        [SerializeField] private WeaponController[] secondaryWeapons;
        [SerializeField] private WeaponController[] utilityWeapons;

        [Header("Auto wiring")]
        [Tooltip("If primary/secondary arrays are empty, find WeaponController under this ship (by hardpoint name Primary / Secondary, else first/second weapon).")]
        [SerializeField] private bool autoFillWeaponArraysIfEmpty = true;

        private void Awake()
        {
            if (autoFillWeaponArraysIfEmpty)
            {
                AutoFillWeaponGroupsIfNeeded();
            }
        }

        private void AutoFillWeaponGroupsIfNeeded()
        {
            WeaponController[] all = GetComponentsInChildren<WeaponController>(true);
            if (all == null || all.Length == 0)
            {
                return;
            }

            bool needPrimary = NeedsFill(primaryWeapons);
            bool needSecondary = NeedsFill(secondaryWeapons);
            if (!needPrimary && !needSecondary)
            {
                return;
            }

            var prim = new System.Collections.Generic.List<WeaponController>();
            var sec = new System.Collections.Generic.List<WeaponController>();
            foreach (WeaponController w in all)
            {
                if (w == null)
                {
                    continue;
                }

                Transform parent = w.transform.parent;
                string p = parent != null ? parent.name : string.Empty;
                if (p.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    prim.Add(w);
                }
                else if (p.IndexOf("Secondary", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sec.Add(w);
                }
            }

            if (needPrimary && prim.Count > 0)
            {
                primaryWeapons = prim.ToArray();
            }

            if (needSecondary && sec.Count > 0)
            {
                secondaryWeapons = sec.ToArray();
            }

            if (NeedsFill(primaryWeapons) && all.Length >= 1)
            {
                primaryWeapons = new[] { all[0] };
            }

            if (NeedsFill(secondaryWeapons) && all.Length >= 2)
            {
                secondaryWeapons = new[] { all[1] };
            }
        }

        private static bool NeedsFill(WeaponController[] group)
        {
            if (group == null || group.Length == 0)
            {
                return true;
            }

            foreach (WeaponController w in group)
            {
                if (w != null)
                {
                    return false;
                }
            }

            return true;
        }

        public void FirePrimary() => FireGroup(primaryWeapons);
        public void FireSecondary() => FireGroup(secondaryWeapons);
        public void FireUtility() => FireGroup(utilityWeapons);

        /// <summary>Weapons assigned to the given group (may contain null entries).</summary>
        public WeaponController[] GetWeaponsForGroup(ShipWeaponsOrchestrator.WeaponGroupSelection group)
        {
            return group switch
            {
                ShipWeaponsOrchestrator.WeaponGroupSelection.Primary => primaryWeapons,
                ShipWeaponsOrchestrator.WeaponGroupSelection.Secondary => secondaryWeapons,
                ShipWeaponsOrchestrator.WeaponGroupSelection.Utility => utilityWeapons,
                _ => null
            };
        }

        /// <summary>Forward XR trigger to gatling audio on the selected weapon group (loop while held, end on release).</summary>
        public void NotifyGatlingTriggerHeld(ShipWeaponsOrchestrator.WeaponGroupSelection group, bool held)
        {
            WeaponController[] list = group switch
            {
                ShipWeaponsOrchestrator.WeaponGroupSelection.Primary => primaryWeapons,
                ShipWeaponsOrchestrator.WeaponGroupSelection.Secondary => secondaryWeapons,
                ShipWeaponsOrchestrator.WeaponGroupSelection.Utility => utilityWeapons,
                _ => null
            };

            if (list == null)
            {
                return;
            }

            foreach (WeaponController w in list)
            {
                if (w != null)
                {
                    w.SetGatlingTriggerHeld(held);
                }
            }
        }

        /// <summary>Used by <see cref="ShipWeaponsOrchestrator"/> shoot diagnostics.</summary>
        public void AppendShootDiagnostics(StringBuilder sb, string selectedGroupLabel)
        {
            AppendWeaponArrayDiag(sb, "Primary", primaryWeapons, selectedGroupLabel);
            AppendWeaponArrayDiag(sb, "Secondary", secondaryWeapons, selectedGroupLabel);
            AppendWeaponArrayDiag(sb, "Utility", utilityWeapons, selectedGroupLabel);
        }

        private static void AppendWeaponArrayDiag(StringBuilder sb, string label, WeaponController[] group, string selectedGroupLabel)
        {
            bool selected = string.Equals(label, selectedGroupLabel, StringComparison.OrdinalIgnoreCase);
            sb.Append("  ").Append(label);
            if (selected)
            {
                sb.Append(" ← SELECTED");
            }

            sb.AppendLine(":");

            if (group == null)
            {
                sb.AppendLine("    (array is null — nothing will fire)");
                return;
            }

            if (group.Length == 0)
            {
                sb.AppendLine("    (empty array — nothing will fire)");
                return;
            }

            for (int i = 0; i < group.Length; i++)
            {
                WeaponController w = group[i];
                if (w == null)
                {
                    sb.Append("    [").Append(i).AppendLine("] null WeaponController reference");
                    continue;
                }

                sb.Append("    [").Append(i).Append("] ").Append(w.gameObject.name).Append(": ")
                    .AppendLine(w.GetShootBlockReason());
            }
        }

        private static void FireGroup(WeaponController[] group)
        {
            if (group == null)
            {
                return;
            }

            foreach (WeaponController weapon in group)
            {
                if (weapon != null)
                {
                    weapon.TryFire();
                }
            }
        }
    }
}
