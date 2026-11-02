using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.Props;
using Zandea.FriendlyNPC;

namespace Zandea.Missions
{
    [RequireComponent(typeof(Collider))]
    public sealed class ContractDeliveryZone : MonoBehaviour
    {
        public TransportMissionDefinition definition;
        readonly Dictionary<ContractBarrel,float> inside=new();
        void Awake() { GetComponent<Collider>().isTrigger=true; }
        void OnTriggerEnter(Collider hit) { var b=hit.GetComponentInParent<ContractBarrel>(); if(b && b.definition==definition && !inside.ContainsKey(b))inside.Add(b,Time.time); }
        void OnTriggerExit(Collider hit) { var b=hit.GetComponentInParent<ContractBarrel>(); if(b)inside.Remove(b); }
        void Update() { foreach(var pair in new List<KeyValuePair<ContractBarrel,float>>(inside)) { var b=pair.Key; if(!b){inside.Remove(b);continue;} if(Time.time-pair.Value>=definition.stabilizationSeconds && b.Deliver()) Debug.Log("Barrel delivered: "+MissionSessions.Get(definition.missionId).Delivered+"/"+definition.barrelCount); } }
    }}
