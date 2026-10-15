using UnityEngine;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class UndeployDefenseGate
    {
        private readonly UndeployDefenseConfig _config;
        private float _nextEligibleTime;
        private float _stowStartTime;

        public UndeployDefenseGate(UndeployDefenseConfig config)
        {
            _config = config;
        }

        public void MarkStowed(float now)
        {
            _stowStartTime = now;
        }

        public bool TryConsume(float now)
        {
            if (now < _nextEligibleTime)
            {
                return false;
            }

            float stowedDuration = now - _stowStartTime;
            if (stowedDuration < _config.minimumStowSeconds)
            {
                return false;
            }

            _nextEligibleTime = now + _config.cooldownSeconds;
            return true;
        }
    }
}
