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
}
