using ScavengerNPC;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    // Optional bridge; leaves Scavenger AI, combat, and health code untouched.
    public sealed class ScavengerFacialExpressionBridge : MonoBehaviour
    {
        public NPCFacialExpressionController expressions;
        public ScavengerHealth health;
        public ScavengerEnemy enemy;
        bool wasInCombat;

        void OnEnable()
        {
            if (!expressions) expressions = GetComponentInChildren<NPCFacialExpressionController>();
            if (!health) health = GetComponent<ScavengerHealth>();
            if (!enemy) enemy = GetComponent<ScavengerEnemy>();
            if (health) { health.onDamaged.AddListener(OnDamaged); health.onDeath.AddListener(OnDeath); }
        }
        void OnDisable()
        {
            if (health) { health.onDamaged.RemoveListener(OnDamaged); health.onDeath.RemoveListener(OnDeath); }
        }
        void Update()
        {
            if (!expressions || !enemy || health && health.IsDead) return;
            bool inCombat = enemy.IsCombat;
            if (inCombat == wasInCombat) return;
            wasInCombat = inCombat;
            if (inCombat) expressions.SetExpression(ExpressionType.Angry, .65f, .25f);
            else expressions.ResetExpression(.4f);
        }
        void OnDamaged()
        {
            if (expressions) expressions.PlayTemporaryExpression(ExpressionType.Pain, 1f, .8f);
        }
        void OnDeath()
        {
            if (expressions) expressions.SetExpression(ExpressionType.Pain, 1f, .1f);
        }
    }
}
