using System.Collections.Generic;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    public class LaserPressureTracker
    {
        private readonly float _consecutiveWindowSeconds;
        private readonly float _consecutiveMultiplier;
        private readonly Dictionary<ShieldArc, float> _lastHitTimeByArc = new Dictionary<ShieldArc, float>();

        public LaserPressureTracker(float consecutiveWindowSeconds, float consecutiveMultiplier)
        {
            _consecutiveWindowSeconds = Mathf.Max(0.01f, consecutiveWindowSeconds);
            _consecutiveMultiplier = Mathf.Max(1f, consecutiveMultiplier);
        }

        public float GetInstabilityMultiplier(ShieldArc arc, float currentTime)
        {
            if (_lastHitTimeByArc.TryGetValue(arc, out float previousTime))
            {
                if (currentTime - previousTime <= _consecutiveWindowSeconds)
                {
                    _lastHitTimeByArc[arc] = currentTime;
                    return _consecutiveMultiplier;
                }
            }

            _lastHitTimeByArc[arc] = currentTime;
            return 1f;
        }
    }
}
