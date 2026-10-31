using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.Props;
using Zandea.FriendlyNPC;

namespace Zandea.Missions
{
    public enum MissionState { Available, Accepted, InProgress, ReadyToTurnIn, Completed, Failed, Abandoned }
    public enum TransportState { AwaitingPickup, InTransit, DeliveredIntact, Destroyed, Missing }
    [CreateAssetMenu(menuName = "Zandea/Missions/Transport Contract")]
    public sealed class TransportMissionDefinition : ScriptableObject
    {
        public string missionId = "daran-honest-work-v1";
        public string title = "An Honest Day's Work";
        [TextArea] public string description = "Move three barrels to Daran's stall.";
        public string giverId = "merchant-daran";
        public int barrelCount = 3;
        public int rewardPerDelivery = 10;
        public int penaltyPerBreakage = 10;
        public bool repeatable;
        public float stabilizationSeconds = 1f;
    }
    public sealed class TransportMissionSession
    {
        public MissionState state;
        public readonly Dictionary<string, TransportState> barrels = new();
        public string payoutId;
        public int debt;
        public readonly HashSet<int> acknowledgedDeliveries = new();
        public int acknowledgedBroken;
        public bool paymentCompleteSpoken;
        public int Delivered { get { int n=0; foreach(var v in barrels.Values) if(v==TransportState.DeliveredIntact)n++; return n; } }
        public int Broken { get { int n=0; foreach(var v in barrels.Values) if(v==TransportState.Destroyed)n++; return n; } }
        public int Net(TransportMissionDefinition d) => Delivered*d.rewardPerDelivery-Broken*d.penaltyPerBreakage;
        public bool Terminal => Delivered+Broken==barrels.Count && barrels.Count>0;
    }
    public static class MissionSessions
    {
        static readonly Dictionary<string, TransportMissionSession> sessions = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => sessions.Clear();
        public static TransportMissionSession Get(string id) { if(!sessions.TryGetValue(id,out var s)) sessions[id]=s=new TransportMissionSession(); return s; }
    }
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
    [RequireComponent(typeof(Collider))]
    public sealed class ContractDeliveryZone : MonoBehaviour
    {
        public TransportMissionDefinition definition;
        readonly Dictionary<ContractBarrel,float> inside=new();
        void Awake() { GetComponent<Collider>().isTrigger=true; }
        void OnTriggerEnter(Collider hit) { var b=hit.GetComponentInParent<ContractBarrel>(); if(b && b.definition==definition && !inside.ContainsKey(b))inside.Add(b,Time.time); }
        void OnTriggerExit(Collider hit) { var b=hit.GetComponentInParent<ContractBarrel>(); if(b)inside.Remove(b); }
        void Update() { foreach(var pair in new List<KeyValuePair<ContractBarrel,float>>(inside)) { var b=pair.Key; if(!b){inside.Remove(b);continue;} if(Time.time-pair.Value>=definition.stabilizationSeconds && b.Deliver()) Debug.Log("Barrel delivered: "+MissionSessions.Get(definition.missionId).Delivered+"/"+definition.barrelCount); } }
    }
}
