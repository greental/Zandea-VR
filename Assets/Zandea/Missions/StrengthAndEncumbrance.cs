using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerBody;
namespace Zandea.Missions
{
    [RequireComponent(typeof(JunkerSprint))]
    public sealed class StrengthAndEncumbrance : MonoBehaviour
    {
        public float strength=1f, strengthXp;
        public float baseCapacity=8f, capacityPerStrength=2f;
        public float loadSpeedPenalty=.18f, minimumSpeedFactor=.55f;
        public float xpPerMeter=.02f, minimumTrainingWeight=8f, maxTrainingStep=1.5f;
        public Transform worldTrackingRoot;
        public float Capacity => baseCapacity+strength*capacityPerStrength;
        public float SpeedFactor => Mathf.Clamp(1f-loadSpeedPenalty*Mathf.Max(0,CarryWeight.Total/Capacity-0.1f),minimumSpeedFactor,1f);
        Vector3 previous;
        void OnEnable() { if(!worldTrackingRoot)worldTrackingRoot=transform; previous=worldTrackingRoot.position; }
        void Update() { if(!worldTrackingRoot)return; var now=worldTrackingRoot.position; float distance=Vector3.ProjectOnPlane(now-previous,Vector3.up).magnitude; previous=now; if(distance<.015f || distance>maxTrainingStep || CarryWeight.Total<minimumTrainingWeight)return; var sprint=GetComponent<JunkerSprint>(); if(!sprint || sprint.MeasuredSpeed<.05f)return; strengthXp+=distance*xpPerMeter*Mathf.Clamp(CarryWeight.Total/Capacity,.1f,2f); while(strengthXp>=strength*10f){strengthXp-=strength*10f;strength+=1f;Debug.Log("Strength increased to "+strength); } }
    }
}
