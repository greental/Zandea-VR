using UnityEngine;
using UnityEngine.InputSystem;
namespace JunkerEquipment
{
    [RequireComponent(typeof(JunkerEquipmentItem))]
    [DefaultExecutionOrder(50)]
    public sealed class JunkerBinoculars : MonoBehaviour
    {
        public Transform viewingDirection, eyepiece;
        public Camera hmd;
        public TextMesh readout;
        public GameObject reticle;
        public float faceDistance=.22f, alignmentAngle=25, maxDistance=500;
        public OpticalDevice opticalDevice;
        public InputActionProperty zoomAction = new InputActionProperty(new InputAction("Binocular zoom", InputActionType.Button, "<XRController>{RightHand}/triggerPressed"));
        public InputActionProperty visionAction = new InputActionProperty(new InputAction("Binocular vision", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed"));
        public bool viewing { get; private set; }
        public float distance { get; private set; } = -1;
        JunkerEquipmentItem item;
        bool ownsZoom, ownsVision;
        void Awake()
        {
            item=GetComponent<JunkerEquipmentItem>();
            if (!opticalDevice) opticalDevice=GetComponent<OpticalDevice>();
            if (opticalDevice && !GetComponent<BinocularScannerHUD>()) gameObject.AddComponent<BinocularScannerHUD>();
        }
        void OnEnable() { ownsZoom=zoomAction.action!=null&&!zoomAction.action.enabled; if(ownsZoom) zoomAction.action.Enable(); ownsVision=visionAction.action!=null&&!visionAction.action.enabled; if(ownsVision) visionAction.action.Enable(); }
        void OnDisable() { if(ownsZoom) zoomAction.action.Disable(); if(ownsVision) visionAction.action.Disable(); }
        void Update()
        {
            bool was=viewing;
            viewing=opticalDevice ? opticalDevice.IsViewing : item.Grab && item.Grab.isSelected && hmd && eyepiece && viewingDirection
                && Vector3.Distance(eyepiece.position,hmd.transform.position)<faceDistance
                && Vector3.Angle(viewingDirection.forward,hmd.transform.forward)<alignmentAngle;
            if(viewing && zoomAction.action?.WasPressedThisFrame()==true) opticalDevice?.CycleZoom();
            if(viewing && visionAction.action?.WasPressedThisFrame()==true) opticalDevice?.CycleVisionMode();
            if(viewing && !was) item.Pulse();
        }
        void LateUpdate()
        {
            var aim = opticalDevice && opticalDevice.IsViewing && opticalDevice.opticsCamera
                ? opticalDevice.opticsCamera.transform : viewingDirection;
            distance=viewing && aim && item.Raycast(aim,maxDistance,~0,out var hit)?hit.distance:-1;
            if(readout) { readout.gameObject.SetActive(viewing); readout.text=distance>=0?distance.ToString("F1")+" m":"— m"; }
            if(reticle) reticle.SetActive(viewing);
        }
    }
}
