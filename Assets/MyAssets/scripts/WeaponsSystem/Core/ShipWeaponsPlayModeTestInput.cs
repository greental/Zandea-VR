using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>
    /// Optional play-mode test: deploy hardpoints, then hold mouse buttons to fire.
    /// Add to the ship root next to <see cref="WeaponGroupController"/>. Remove or disable for real XR/cockpit wiring.
    /// </summary>
    [AddComponentMenu("VR Koombar/Combat/Ship Weapons Play Mode Test Input")]
    public class ShipWeaponsPlayModeTestInput : MonoBehaviour
    {
        [SerializeField] private WeaponGroupController weaponGroups;
        [Tooltip("Leave empty to collect all WeaponHardpoint under this ship.")]
        [SerializeField] private WeaponHardpoint[] hardpoints;
        [SerializeField] private bool deployAllHardpointsOnStart = true;
        [SerializeField] private bool verboseLogs;

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
        }

        private void Start()
        {
            if (!deployAllHardpointsOnStart || hardpoints == null)
            {
                return;
            }

            foreach (WeaponHardpoint hp in hardpoints)
            {
                if (hp != null)
                {
                    hp.TrySetDeployed(true);
                }
            }

            if (verboseLogs)
            {
                Debug.Log("[ShipWeaponsPlayModeTestInput] Hardpoints set to deploy. Hold Left Mouse = primary, Right Mouse = secondary.", this);
            }
        }

        private void Update()
        {
            if (weaponGroups == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.gKey.wasPressedThisFrame)
                {
                    SetAllHardpointsDeployed(true);
                }

                if (kb.hKey.wasPressedThisFrame)
                {
                    SetAllHardpointsDeployed(false);
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.isPressed)
            {
                weaponGroups.FirePrimary();
            }

            if (mouse.rightButton.isPressed)
            {
                weaponGroups.FireSecondary();
            }
#else
            if (Input.GetKeyDown(KeyCode.G))
            {
                SetAllHardpointsDeployed(true);
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                SetAllHardpointsDeployed(false);
            }

            if (Input.GetMouseButton(0))
            {
                weaponGroups.FirePrimary();
            }

            if (Input.GetMouseButton(1))
            {
                weaponGroups.FireSecondary();
            }
#endif
        }

        private void SetAllHardpointsDeployed(bool deployed)
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
    }
}
