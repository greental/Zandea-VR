using System.Collections.Generic;
using UnityEngine;

namespace ScavengerNPC
{
    // Place the root behind cover. Side points sit on the NavMesh; over points
    // remain at the root and describe the height of the firing sight line.
    public sealed class ScavengerCover : MonoBehaviour
    {
        public enum PeekStyle { Side, Over }
        [System.Serializable]
        public sealed class PeekPoint
        {
            public Transform point;
            public PeekStyle style;
            [Min(.5f)] public float sightHeight = 1.45f;
        }
        public List<PeekPoint> peekPoints = new List<PeekPoint>();
        [Tooltip("Legacy side point; used if Peek Points is empty.")]
        public Transform peek;
        public bool crouchWhenHiding = true;
        [Range(.1f,.6f)] public float crouchDepth = .35f;
        [Range(.4f,1.5f)] public float crouchedSightHeight = .85f;
        public static readonly HashSet<ScavengerCover> Active = new HashSet<ScavengerCover>();
        public ScavengerEnemy Occupant { get; private set; }
        public int PeekCount => peekPoints != null && peekPoints.Count > 0 ? peekPoints.Count : peek ? 1 : 0;
        public PeekPoint GetPeek(int index)
        {
            if (peekPoints != null && peekPoints.Count > 0)
                return index >= 0 && index < peekPoints.Count ? peekPoints[index] : null;
            return index == 0 && peek ? new PeekPoint { point = peek, style = PeekStyle.Side } : null;
        }
        public static float EffectiveSightHeight(PeekPoint marker) => marker != null && marker.sightHeight >= .5f ? marker.sightHeight : 1.45f;
        public bool IsOver(int index) { var marker = GetPeek(index); return marker != null && marker.style == PeekStyle.Over; }
        public bool Available(ScavengerEnemy enemy) => !Occupant || Occupant == enemy;
        public bool Claim(ScavengerEnemy enemy) { if (!Available(enemy)) return false; Occupant = enemy; return true; }
        public void Release(ScavengerEnemy enemy) { if (Occupant == enemy) Occupant = null; }
        void OnEnable() { Active.Add(this); }
        void OnDisable() { Active.Remove(this); Occupant = null; }
        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, .25f);
            for (int i = 0; i < PeekCount; i++)
            {
                var marker = GetPeek(i);
                if (marker == null || !marker.point) continue;
                Gizmos.color = marker.style == PeekStyle.Over ? Color.yellow : Color.cyan;
                Vector3 end = marker.style == PeekStyle.Over ? marker.point.position + Vector3.up * EffectiveSightHeight(marker) : marker.point.position;
                Gizmos.DrawLine(transform.position, end);
                Gizmos.DrawWireSphere(end, .2f);
            }
        }
    }
}
