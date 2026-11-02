using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.Props;
using Zandea.FriendlyNPC;

namespace Zandea.Missions
{
    [RequireComponent(typeof(PhysicalProp),typeof(BreakableProp),typeof(XRGrabInteractable))]
    public sealed class ContractBarrel : MonoBehaviour
    {
        public TransportMissionDefinition definition;
        public string objectId;
        public TransportState State => definition != null && MissionSessions.Get(definition.missionId).barrels.TryGetValue(objectId,out var state) ? state : TransportState.Missing;
        public event Action<ContractBarrel> Changed;
        PhysicalProp prop; BreakableProp breakable; XRGrabInteractable grab;
        void Awake() { prop=GetComponent<PhysicalProp>(); breakable=GetComponent<BreakableProp>(); grab=GetComponent<XRGrabInteractable>(); if(prop && !string.IsNullOrEmpty(objectId))prop.AssignId(objectId); }
        void OnEnable() { if(breakable)breakable.Broken+=OnBroken; if(grab){grab.selectEntered.AddListener(OnGrab);grab.selectExited.AddListener(OnRelease);} }
        void OnDisable() { if(breakable)breakable.Broken-=OnBroken; if(grab){grab.selectEntered.RemoveListener(OnGrab);grab.selectExited.RemoveListener(OnRelease);} }
        void OnGrab(SelectEnterEventArgs _) { if(State==TransportState.AwaitingPickup) Set(TransportState.InTransit); }
        void OnRelease(SelectExitEventArgs _) { Changed?.Invoke(this); }
        void OnBroken(BreakableProp _) { if(State!=TransportState.DeliveredIntact)Set(TransportState.Destroyed); }
        public bool Deliver() { if(!definition || breakable.IsBroken || grab.isSelected || State==TransportState.DeliveredIntact || State==TransportState.Destroyed)return false; var s=MissionSessions.Get(definition.missionId); if(s.state!=MissionState.Accepted && s.state!=MissionState.InProgress)return false; Set(TransportState.DeliveredIntact); return true; }
        void Set(TransportState next) { if(!definition || string.IsNullOrEmpty(objectId))return; var s=MissionSessions.Get(definition.missionId); if(s.state!=MissionState.Accepted && s.state!=MissionState.InProgress && s.state!=MissionState.ReadyToTurnIn)return; var old=State; if(old==TransportState.DeliveredIntact || old==TransportState.Destroyed || old==next)return; s.barrels[objectId]=next; if(s.state==MissionState.Accepted)s.state=MissionState.InProgress; if(s.Terminal)s.state=MissionState.ReadyToTurnIn; Changed?.Invoke(this); }
    }
}
