using System;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;
#endif

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>
    /// Single entry point for weapon groups, hardpoint deploy, and cockpit fire gating (seat + joystick grab + trigger).
    /// Wire UI buttons to <see cref="SelectPrimary"/> / <see cref="SelectSecondary"/> / <see cref="SelectUtility"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VR Koombar/Combat/Ship Weapons Orchestrator")]
    [DefaultExecutionOrder(-200)]
    public class ShipWeaponsOrchestrator : MonoBehaviour
    {
        public enum WeaponGroupSelection
        {
            Primary,
            Secondary,
            Utility
        }

        [Header("Core")]
        [SerializeField] private WeaponGroupController weaponGroups;
        [Tooltip("Leave empty to collect WeaponHardpoint under this ship.")]
        [SerializeField] private WeaponHardpoint[] hardpoints;

        [Header("Hardpoints")]
        [Tooltip("On Play, deploy all hardpoints so weapons can fire before you wire UI deploy buttons.")]
        [SerializeField] private bool deployHardpointsOnStart = true;

        [Header("Cockpit fire gate (XR)")]
        [Tooltip("Pilot seat on this ship. Fire only when seated if set. If wrong chair is assigned, any SitInCockpit under this ship that is seated still counts.")]
        [SerializeField] private SitInCockpit sitInCockpit;
        [Tooltip("Main stick the pilot grabs. Fire only when grabbed if set. If wrong stick is assigned, any HOTASJoystick on this ship that is grabbed still counts.")]
        [SerializeField] private HOTASJoystick pilotJoystick;
        [Tooltip("If set, weapons (and gatling audio) only when 24V / HUD power is on. Auto-filled from parents when empty.")]
        [SerializeField] private ShipPowerController shipPower;
#if ENABLE_INPUT_SYSTEM
        [Tooltip("Optional. If unset, XR hand trigger is read directly (see XR Trigger Hand).")]
        [SerializeField] private InputActionReference fireTriggerAction;
#endif
        [Tooltip("Which XR controller trigger to use when no Input Action is assigned, or combined with it.")]
        [SerializeField] private XRNode xrTriggerHand = XRNode.RightHand;
        [SerializeField] private bool joystickTriggerFireEnabled = true;
        [SerializeField] [Range(0f, 1f)] private float triggerPressThreshold = 0.35f;

        [Header("Weapon selection")]
        [SerializeField] private WeaponGroupSelection selectedGroup = WeaponGroupSelection.Primary;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs;
        [Tooltip("Logs a full shoot pipeline snapshot on a timer (Console). Turn off after fixing issues.")]
        [SerializeField] private bool shootPipelineDiagnostics = true;
        [SerializeField] [Range(0.25f, 4f)] private float shootDiagnosticsInterval = 0.6f;

        private float _nextShootDiagTime;

        public WeaponGroupSelection SelectedGroup
        {
            get => selectedGroup;
            set => selectedGroup = value;
        }

        public event Action<WeaponGroupSelection> SelectedGroupChanged;

        private void Awake()
        {
            if (weaponGroups == null)
            {
                weaponGroups = GetComponent<WeaponGroupController>();
            }

            if (hardpoints == null || hardpoints.Length == 0)
            {
                hardpoints = GetComponentsInChildren<WeaponHardpoint>(true);
            }

            if (shipPower == null)
            {
                shipPower = GetComponentInParent<ShipPowerController>();
            }

            if (shipPower == null)
            {
                shipPower = GetComponentInChildren<ShipPowerController>(true);
            }

            if (shipPower == null)
            {
                shipPower = transform.root.GetComponentInChildren<ShipPowerController>(true);
            }
        }

        private void Start()
        {
            if (deployHardpointsOnStart)
            {
                DeployAllHardpoints();
            }
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (fireTriggerAction != null && fireTriggerAction.action != null)
            {
                fireTriggerAction.action.actionMap?.Enable();
                fireTriggerAction.action.Enable();
            }
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (fireTriggerAction != null && fireTriggerAction.action != null)
            {
                fireTriggerAction.action.Disable();
            }
#endif
        }

        private void Update()
        {
            if (!joystickTriggerFireEnabled)
            {
                return;
            }

            float trigger = ReadTriggerAmount();
            bool triggerHeld = trigger >= triggerPressThreshold;

            bool weaponsContextOk = CanFireWeapons();
            if (weaponGroups != null)
            {
                weaponGroups.NotifyGatlingTriggerHeld(selectedGroup, triggerHeld && weaponsContextOk);
            }

            if (!weaponsContextOk)
            {
                return;
            }

            if (!triggerHeld)
            {
                return;
            }

            FireCurrentSelection();
        }

        private void LateUpdate()
        {
            if (!shootPipelineDiagnostics)
            {
                return;
            }

            if (Time.time < _nextShootDiagTime)
            {
                return;
            }

            _nextShootDiagTime = Time.time + shootDiagnosticsInterval;
            LogShootPipelineSnapshot();
        }

        private void AppendPowerDiag(StringBuilder sb)
        {
            if (shipPower == null)
            {
                sb.AppendLine("   ShipPowerController: (not assigned — power not required for weapons)");
            }
            else
            {
                sb.Append("   ShipPowerController '").Append(shipPower.name).Append("': hudPowered=")
                    .AppendLine(shipPower.hudPowered.ToString());
            }
        }

        private void AppendSitAndStickDiag(StringBuilder sb)
        {
            if (sitInCockpit == null)
            {
                sb.AppendLine("   SitInCockpit: (not assigned — seat not required for fire gate)");
            }
            else
            {
                sb.Append("   Assigned seat '").Append(sitInCockpit.name).Append("': IsSeated=")
                    .AppendLine(sitInCockpit.IsSeated.ToString());
            }

            foreach (SitInCockpit seat in GetComponentsInChildren<SitInCockpit>(true))
            {
                if (seat == null)
                {
                    continue;
                }

                string mark = sitInCockpit != null && seat == sitInCockpit ? " [orchestrator ref]" : string.Empty;
                sb.Append("   • ").Append(seat.gameObject.name).Append(": IsSeated=").Append(seat.IsSeated).AppendLine(mark);
            }

            bool anyHotas = false;
            foreach (HOTASJoystick j in GetComponentsInChildren<HOTASJoystick>(true))
            {
                if (j != null)
                {
                    anyHotas = true;
                    break;
                }
            }

            if (!anyHotas && pilotJoystick == null)
            {
                sb.AppendLine("   HOTASJoystick: (none on ship — grab not required for fire gate)");
            }
            else if (pilotJoystick != null)
            {
                sb.Append("   Assigned stick '").Append(pilotJoystick.name).Append("': IsGrabbed=")
                    .AppendLine(pilotJoystick.IsGrabbed.ToString());
            }
            else
            {
                sb.AppendLine("   HOTASJoystick: (at least one on ship — one must be grabbed)");
            }

            foreach (HOTASJoystick stick in GetComponentsInChildren<HOTASJoystick>(true))
            {
                if (stick == null)
                {
                    continue;
                }

                string mark = pilotJoystick != null && stick == pilotJoystick ? " [orchestrator ref]" : string.Empty;
                sb.Append("   • ").Append(stick.gameObject.name).Append(": IsGrabbed=").Append(stick.IsGrabbed).AppendLine(mark);
            }
        }

        private void LogShootPipelineSnapshot()
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine("[ShootDiag] -------- shoot pipeline --------");

            sb.Append("1) joystickTriggerFireEnabled: ").AppendLine(joystickTriggerFireEnabled ? "yes" : "NO — Update() never reaches trigger read / fire");

            sb.AppendLine("2) Power + cockpit gate (CanFireWeapons):");
            AppendPowerDiag(sb);
            AppendSitAndStickDiag(sb);
            sb.Append("   → ").Append(CanFireWeapons() ? "PASS" : "BLOCKED").AppendLine();

#if ENABLE_INPUT_SYSTEM
            bool hasAction = fireTriggerAction != null && fireTriggerAction.action != null;
            float inputActionValue = 0f;
            if (hasAction)
            {
                try
                {
                    inputActionValue = fireTriggerAction.action.ReadValue<float>();
                }
                catch
                {
                    inputActionValue = fireTriggerAction.action.IsPressed() ? 1f : 0f;
                }
            }

            sb.Append("3) InputActionReference: ").Append(hasAction ? $"assigned, read≈{inputActionValue:F2}" : "not assigned (using XR only is fine)").AppendLine();
#else
            sb.AppendLine("3) Input System: not compiled (ENABLE_INPUT_SYSTEM off)");
#endif

            UnityEngine.XR.InputDevice xrDev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(xrTriggerHand);
            float xrTriggerOnly = 0f;
            bool xrRead = xrDev.isValid &&
                          xrDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out xrTriggerOnly);
            sb.Append("4) XR ").Append(xrTriggerHand).Append(": device valid=").Append(xrDev.isValid)
                .Append(", trigger read=").Append(xrRead ? xrTriggerOnly.ToString("F2") : "n/a").AppendLine();

            float combined = ReadTriggerAmount();
            sb.Append("5) Combined trigger (max): ").Append(combined.ToString("F2"))
                .Append(" vs threshold ").Append(triggerPressThreshold.ToString("F2"))
                .Append(combined >= triggerPressThreshold ? " → would attempt fire this frame" : " → trigger too low for fire")
                .AppendLine();

            sb.Append("6) WeaponGroupController: ");
            if (weaponGroups == null)
            {
                sb.AppendLine("MISSING — add component or assign reference");
                Debug.Log(sb.ToString(), this);
                return;
            }

            sb.AppendLine($"OK on '{weaponGroups.name}'");
            sb.Append("7) Selected group: ").AppendLine(selectedGroup.ToString());
            sb.AppendLine("8) Weapons in groups:");
            weaponGroups.AppendShootDiagnostics(sb, selectedGroup.ToString());

            sb.AppendLine("[ShootDiag] (disable 'Shoot Pipeline Diagnostics' on ShipWeaponsOrchestrator when done)");
            Debug.Log(sb.ToString(), this);
        }

        /// <summary>Input Action (if assigned) plus XR CommonUsages.trigger on the chosen hand.</summary>
        private float ReadTriggerAmount()
        {
            float v = 0f;
#if ENABLE_INPUT_SYSTEM
            if (fireTriggerAction != null && fireTriggerAction.action != null)
            {
                try
                {
                    v = Mathf.Max(v, fireTriggerAction.action.ReadValue<float>());
                }
                catch
                {
                    // Wrong control type; try button value
                    v = Mathf.Max(v, fireTriggerAction.action.IsPressed() ? 1f : 0f);
                }
            }
#endif
            UnityEngine.XR.InputDevice xrDev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(xrTriggerHand);
            if (xrDev.isValid &&
                xrDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float xrTriggerAmount))
            {
                v = Mathf.Max(v, xrTriggerAmount);
            }

            if (verboseLogs && v > 0.05f && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[ShipWeaponsOrchestrator] trigger={v:F2} threshold={triggerPressThreshold:F2} (InputAction+XR)", this);
            }

            return v;
        }

        /// <summary>24V / HUD power must be on when <see cref="shipPower"/> is assigned.</summary>
        public bool IsShipPoweredForWeapons()
        {
            return shipPower == null || shipPower.hudPowered;
        }

        /// <summary>Seat requirement only (for HUD PIPs). Matches the seated part of the fire gate.</summary>
        public bool IsCockpitSeatGateSatisfied()
        {
            if (sitInCockpit != null)
            {
                return sitInCockpit.IsSeated || IsAnySitInCockpitSeatedUnderShip();
            }

            return true;
        }

        /// <summary>Power + seated + joystick grab when applicable.</summary>
        public bool CanFireWeapons()
        {
            return IsShipPoweredForWeapons() && CanFireFromCockpit();
        }

        /// <summary>Weapons in the currently selected group (for HUD aim dots). May be null or contain nulls.</summary>
        public bool TryGetSelectedGroupWeapons(out WeaponController[] weapons)
        {
            weapons = null;
            if (weaponGroups == null)
            {
                return false;
            }

            weapons = weaponGroups.GetWeaponsForGroup(selectedGroup);
            return weapons != null;
        }

        /// <summary>Used by joystick + trigger path and test helpers.</summary>
        public bool CanFireFromCockpit()
        {
            if (sitInCockpit != null)
            {
                if (!sitInCockpit.IsSeated && !IsAnySitInCockpitSeatedUnderShip())
                {
                    return false;
                }
            }

            bool hasHotasUnderShip = false;
            foreach (HOTASJoystick j in GetComponentsInChildren<HOTASJoystick>(true))
            {
                if (j != null)
                {
                    hasHotasUnderShip = true;
                    break;
                }
            }

            if (hasHotasUnderShip)
            {
                if (!IsAnyHotasJoystickGrabbedUnderShip())
                {
                    return false;
                }
            }
            else if (pilotJoystick != null && !pilotJoystick.IsGrabbed)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves wrong inspector wiring (e.g. orchestrator references PilotChairOther but the player uses another SitInCockpit).
        /// </summary>
        private bool IsAnySitInCockpitSeatedUnderShip()
        {
            foreach (SitInCockpit seat in GetComponentsInChildren<SitInCockpit>(true))
            {
                if (seat != null && seat.IsSeated)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAnyHotasJoystickGrabbedUnderShip()
        {
            foreach (HOTASJoystick stick in GetComponentsInChildren<HOTASJoystick>(true))
            {
                if (stick != null && stick.IsGrabbed)
                {
                    return true;
                }
            }

            return false;
        }

        public void FireCurrentSelection()
        {
            if (weaponGroups == null)
            {
                return;
            }

            switch (selectedGroup)
            {
                case WeaponGroupSelection.Primary:
                    weaponGroups.FirePrimary();
                    break;
                case WeaponGroupSelection.Secondary:
                    weaponGroups.FireSecondary();
                    break;
                case WeaponGroupSelection.Utility:
                    weaponGroups.FireUtility();
                    break;
            }
        }

        public void SelectPrimary()
        {
            SetSelectedGroup(WeaponGroupSelection.Primary);
        }

        public void SelectSecondary()
        {
            SetSelectedGroup(WeaponGroupSelection.Secondary);
        }

        public void SelectUtility()
        {
            SetSelectedGroup(WeaponGroupSelection.Utility);
        }

        public void SetSelectedGroup(WeaponGroupSelection group)
        {
            if (selectedGroup == group)
            {
                return;
            }

            selectedGroup = group;
            SelectedGroupChanged?.Invoke(selectedGroup);
            if (verboseLogs)
            {
                Debug.Log($"[ShipWeaponsOrchestrator] Selected weapon group: {selectedGroup}", this);
            }
        }

        public int GetHardpointCount()
        {
            return hardpoints == null ? 0 : hardpoints.Length;
        }

        public WeaponHardpoint GetHardpoint(int index)
        {
            if (hardpoints == null || index < 0 || index >= hardpoints.Length)
            {
                return null;
            }

            return hardpoints[index];
        }

        public void SetHardpointDeployed(int index, bool deployed)
        {
            WeaponHardpoint hp = GetHardpoint(index);
            hp?.TrySetDeployed(deployed);
        }

        public void DeployAllHardpoints()
        {
            SetAllHardpointsDeployed(true);
        }

        public void StowAllHardpoints()
        {
            SetAllHardpointsDeployed(false);
        }

        public void SetAllHardpointsDeployed(bool deployed)
        {
            if (hardpoints == null)
            {
                return;
            }

            foreach (WeaponHardpoint hp in hardpoints)
            {
                hp?.TrySetDeployed(deployed);
            }
        }

        /// <summary>Single test burst (one frame of fire calls). Use from editor button or debug.</summary>
        public void TestFireCurrentSelection()
        {
            FireCurrentSelection();
        }
    }
}

#if UNITY_EDITOR
namespace VRKoombar.WeaponsStaging.Core
{
    [CustomEditor(typeof(ShipWeaponsOrchestrator))]
    internal sealed class ShipWeaponsOrchestratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            var orchestrator = (ShipWeaponsOrchestrator)target;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Weapon definitions (project-wide assets)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edits the same ScriptableObjects assigned on each WeaponController (fire rate, projectile speed, prefab, etc.). " +
                "Save the scene/project to persist asset changes.",
                MessageType.None);

            var seen = new HashSet<WeaponDefinition>();
            foreach (WeaponController wc in orchestrator.GetComponentsInChildren<WeaponController>(true))
            {
                if (wc == null)
                {
                    continue;
                }

                SerializedObject wcSo = new SerializedObject(wc);
                SerializedProperty defProp = wcSo.FindProperty("definition");
                if (defProp == null || defProp.objectReferenceValue is not WeaponDefinition def || def == null || !seen.Add(def))
                {
                    continue;
                }

                EditorGUILayout.Space(6f);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{def.name} ({wc.gameObject.name})", EditorStyles.boldLabel);
                    SerializedObject defSo = new SerializedObject(def);
                    defSo.Update();
                    UnityEditor.Editor.DrawPropertiesExcluding(defSo, "m_Script");
                    defSo.ApplyModifiedProperties();
                }
            }

            if (seen.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No WeaponController with a Weapon Definition found under this ship. Add weapons or assign definitions on WeaponController.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Deploy, shoot & test (runtime)", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode for hardpoint toggles, Deploy All / Stow All, Shoot, and weapon group buttons. " +
                    "Optional: assign Fire Trigger Action for XR trigger fire.",
                    MessageType.Info);
                return;
            }

            int count = orchestrator.GetHardpointCount();
            for (int i = 0; i < count; i++)
            {
                WeaponHardpoint hp = orchestrator.GetHardpoint(i);
                if (hp == null)
                {
                    continue;
                }

                bool isDeployed = hp.State == HardpointState.Deployed;
                string label = $"Hardpoint {i}: {hp.gameObject.name}";
                bool wantDeployed = EditorGUILayout.ToggleLeft(label, isDeployed);
                if (wantDeployed != isDeployed)
                {
                    orchestrator.SetHardpointDeployed(i, wantDeployed);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Deploy all hardpoints"))
            {
                orchestrator.DeployAllHardpoints();
            }

            if (GUILayout.Button("Stow all hardpoints"))
            {
                orchestrator.StowAllHardpoints();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Selected group (same as dropdown above)", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select primary"))
            {
                orchestrator.SelectPrimary();
            }

            if (GUILayout.Button("Select secondary"))
            {
                orchestrator.SelectSecondary();
            }

            if (GUILayout.Button("Select utility"))
            {
                orchestrator.SelectUtility();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Manual shoot (runtime)", EditorStyles.boldLabel);
            if (GUILayout.Button("Shoot — fire current group once", GUILayout.Height(30f)))
            {
                orchestrator.TestFireCurrentSelection();
            }

            EditorGUILayout.HelpBox(
                "Ignores seat and joystick grab (same code path as the in-game trigger). " +
                "You still need deployed hardpoints, projectile prefabs on Weapon Definitions, etc.",
                MessageType.Info);

            if (!orchestrator.CanFireWeapons())
            {
                EditorGUILayout.HelpBox(
                    "In-game trigger: needs HUD power (if Ship Power is assigned), seated if a SitInCockpit is set, " +
                    "and a HOTAS on this ship grabbed when the ship has sticks. " +
                    "Use Shoot above to bypass.",
                    MessageType.Warning);
            }
        }
    }
}
#endif
