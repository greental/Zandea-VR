using UnityEngine;
using JunkerBody;
using KenshiVR.MeleeCombat;
namespace Zandea.Missions
{
    [RequireComponent(typeof(JunkerSprint))]
    public sealed class StrengthAndEncumbrance : MonoBehaviour
    {
        public float strength=1f, strengthXp;
        CharacterAttributes attributes;
        CharacterCombatProgression progression;
        public float EffectiveStrength => attributes ? attributes.Get(CharacterAttribute.Strength) : strength;
        public float baseCapacity=8f, capacityPerStrength=2f;
        public float loadSpeedPenalty=.18f, minimumSpeedFactor=.55f;
        public float xpPerMeter=.02f, minimumTrainingWeight=8f, maxTrainingStep=1.5f;
        public Transform worldTrackingRoot;
        public float Capacity => baseCapacity+EffectiveStrength*capacityPerStrength;
        public float SpeedFactor => Mathf.Clamp(1f-loadSpeedPenalty*Mathf.Max(0,CarryWeight.Total/Capacity-0.1f),minimumSpeedFactor,1f);
        Vector3 previous;
        void OnEnable()
        {
            attributes=GetComponentInChildren<CharacterAttributes>(true);
            progression=GetComponentInChildren<CharacterCombatProgression>(true);
            if(attributes) strength=attributes.Get(CharacterAttribute.Strength);
            if(!worldTrackingRoot) worldTrackingRoot=transform;
            previous=worldTrackingRoot.position;
        }
        void Start()
        {
            if(!attributes) attributes=GetComponentInChildren<CharacterAttributes>(true);
            if(!progression) progression=GetComponentInChildren<CharacterCombatProgression>(true);
        }
        void Update()
        {
            if(attributes) strength=attributes.Get(CharacterAttribute.Strength);
            if(!worldTrackingRoot) return;
            var now=worldTrackingRoot.position;
            float distance=Vector3.ProjectOnPlane(now-previous,Vector3.up).magnitude;
            previous=now;
            if(distance<.015f || distance>maxTrainingStep || CarryWeight.Total<minimumTrainingWeight) return;
            var sprint=GetComponent<JunkerSprint>();
            if(!sprint || sprint.MeasuredSpeed<.05f) return;
            float earned=distance*xpPerMeter*Mathf.Clamp(CarryWeight.Total/Capacity,.1f,2f);
            if(!progression) progression=GetComponentInChildren<CharacterCombatProgression>(true);
            if(progression && attributes)
            {
                progression.RecordCarryingTraining(earned);
                strength=attributes.Get(CharacterAttribute.Strength);
                return;
            }
            strengthXp+=earned;
            while(strengthXp>=EffectiveStrength*10f)
            {
                strengthXp-=EffectiveStrength*10f;
                if(attributes) attributes.SetRuntime(CharacterAttribute.Strength, attributes.Get(CharacterAttribute.Strength)+1);
                else strength+=1f;
                Debug.Log("Strength increased to "+EffectiveStrength, this);
            }
            if(attributes) strength=attributes.Get(CharacterAttribute.Strength);
        }
    }
}
