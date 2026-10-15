using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class WeaponHardpoint : MonoBehaviour, IWeaponHardpoint
    {
        [SerializeField] private float deployDuration = 0.5f;
        [SerializeField] private float undeployDuration = 0.35f;
        [SerializeField] private UndeployDefenseConfig undeployConfig;
        [SerializeField] private bool verboseLogs;

        public HardpointState State { get; private set; } = HardpointState.Stowed;
        public bool CanFire => State == HardpointState.Deployed;

        private float _transitionEndTime;
        private bool _targetDeployed;
        private UndeployDefenseGate _undeployGate;

        private void Awake()
        {
            if (undeployConfig != null)
            {
                _undeployGate = new UndeployDefenseGate(undeployConfig);
                _undeployGate.MarkStowed(Time.time);
            }
        }

        private void Update()
        {
            if (State == HardpointState.Deploying && Time.time >= _transitionEndTime)
            {
                SetState(HardpointState.Deployed);
            }
            else if (State == HardpointState.Undeploying && Time.time >= _transitionEndTime)
            {
                SetState(HardpointState.Stowed);
                _undeployGate?.MarkStowed(Time.time);
            }
        }

        public bool TrySetDeployed(bool deployed)
        {
            if (_targetDeployed == deployed && (State == HardpointState.Deploying || State == HardpointState.Undeploying))
            {
                return false;
            }

            _targetDeployed = deployed;
            if (deployed)
            {
                if (State == HardpointState.Deployed || State == HardpointState.Deploying)
                {
                    return false;
                }

                _transitionEndTime = Time.time + deployDuration;
                SetState(HardpointState.Deploying);
                return true;
            }

            if (State == HardpointState.Stowed || State == HardpointState.Undeploying)
            {
                return false;
            }

            _transitionEndTime = Time.time + undeployDuration;
            SetState(HardpointState.Undeploying);
            return true;
        }

        public bool TryConsumeUndeployDefense()
        {
            return _undeployGate != null && _undeployGate.TryConsume(Time.time);
        }

        private void SetState(HardpointState newState)
        {
            State = newState;
            DamageEventBus.RaiseHardpointStateChanged(State);
            if (verboseLogs) Debug.Log($"[Hardpoint] State => {State}", this);
        }
    }
}
