using System.Collections.Generic;
using UnityEngine;

namespace JunkerScanner
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class AuthoredCutPath : MonoBehaviour
    {
        public Transform[] controlPoints = System.Array.Empty<Transform>();
        public bool loop;
        public ScanVisualProfile visualProfile;
        readonly HashSet<object> owners = new();
        LineRenderer line;
        public bool IsVisible => owners.Count > 0;
        void Awake() { line = GetComponent<LineRenderer>(); line.enabled = false; }
        public void RequestVisibility(object owner, bool visible)
        {
            if (owner == null) return;
            if (visible) owners.Add(owner); else owners.Remove(owner);
            if (!line) line = GetComponent<LineRenderer>();
            line.enabled = isActiveAndEnabled && IsVisible;
            if (line.enabled) RefreshPath();
        }
        void LateUpdate() { if (IsVisible) RefreshPath(); }
        public void RefreshPath()
        {
            if (!line) line = GetComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = loop;
            int count = 0; foreach (var point in controlPoints) if (point) count++;
            line.positionCount = count; int index = 0;
            foreach (var point in controlPoints) if (point) line.SetPosition(index++, transform.InverseTransformPoint(point.position));
            if (visualProfile) line.startColor = line.endColor = visualProfile.cut;
        }
        void OnDisable() { owners.Clear(); if (line) line.enabled = false; }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, .4f, 0);
            for (int i = 0; i < controlPoints.Length; i++)
            {
                if (!controlPoints[i]) continue;
                Gizmos.DrawWireSphere(controlPoints[i].position, .01f);
                int next = (i + 1) % controlPoints.Length;
                if ((loop || next != 0) && controlPoints[next]) Gizmos.DrawLine(controlPoints[i].position, controlPoints[next].position);
            }
        }
    }
}
