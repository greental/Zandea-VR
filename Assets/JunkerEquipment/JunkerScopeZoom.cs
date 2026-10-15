using UnityEngine;
using UnityEngine.InputSystem;

namespace JunkerEquipment
{
    [RequireComponent(typeof(OpticalDevice))]
    public sealed class JunkerScopeZoom : MonoBehaviour
    {
        [Tooltip("Right thumbstick press cycles magnification while looking through the scope.")]
        public InputActionProperty zoomAction = new InputActionProperty(
            new InputAction("Scope zoom", InputActionType.Button, "<XRController>{RightHand}/primary2DAxisClick"));

        OpticalDevice optics;
        bool ownsAction;

        void Awake() { optics = GetComponent<OpticalDevice>(); }
        void OnEnable()
        {
            ownsAction = zoomAction.action != null && !zoomAction.action.enabled;
            if (ownsAction) zoomAction.action.Enable();
        }
        void OnDisable() { if (ownsAction) zoomAction.action.Disable(); }
        void Update()
        {
            if (optics && optics.IsViewing && zoomAction.action?.WasPressedThisFrame() == true)
                optics.CycleZoom();
        }
    }
}
