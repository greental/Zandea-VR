using UnityEngine;
namespace Zandea.Wildlife
{
    [CreateAssetMenu(menuName = "Zandea/Creature Profile")]
    public sealed class CreatureProfile : ScriptableObject
    {
        [Min(.1f)] public float walkSpeed = 1.35f, trotSpeed = 2.8f, runSpeed = 3.6f;
        [Min(.1f)] public float acceleration = .8f, deceleration = 1.5f, turnRate = 35f;
        [Min(1)] public float wanderRadius = 20f, detectionRadius = 12f, fleeDistance = 23f;
        [Min(.1f)] public float fleeThreshold = 5f, perceptionInterval = .3f;
        public Vector2 idleSeconds = new Vector2(3, 8), grazeSeconds = new Vector2(7, 14);
        [Range(0, 50)] public float maximumSlope = 30;
        public LayerMask groundLayers = ~0;
        [Min(.01f)] public float footProbeHeight = 1.1f, footCorrection = .5f, groundingSharpness = 12;
        [Min(1)] public float maxHealth = 250;
        [Min(0)] public float bodyHeightOffset = .02f;
    }
}
