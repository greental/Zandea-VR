using UnityEngine;

namespace JunkerEquipment
{
    /// <summary>Motor onset, uninterrupted running loop, and release tail.</summary>
    public sealed class PoweredToolAudio : MonoBehaviour
    {
        public AudioSource transitionSource, loopSource;
        public AudioClip windUp, runningLoop, windDown;
        public bool IsRunning { get; private set; }

        public void SetRunning(bool value)
        {
            if (value == IsRunning) return;
            IsRunning = value;
            if (transitionSource) transitionSource.Stop();
            if (loopSource) loopSource.Stop();
            if (!value)
            {
                if (transitionSource && windDown)
                { transitionSource.clip = windDown; transitionSource.loop = false; transitionSource.Play(); }
                return;
            }
            double onset = AudioSettings.dspTime + .01;
            if (transitionSource && windUp)
            {
                transitionSource.clip = windUp; transitionSource.loop = false;
                transitionSource.PlayScheduled(onset);
            }
            if (loopSource && runningLoop)
            {
                loopSource.clip = runningLoop; loopSource.loop = true;
                loopSource.PlayScheduled(onset + (transitionSource && windUp ? windUp.length : 0));
            }
        }
        void OnDisable()
        {
            IsRunning = false;
            if (transitionSource) transitionSource.Stop();
            if (loopSource) loopSource.Stop();
        }
    }
}
