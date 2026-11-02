using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    [CreateAssetMenu(menuName = "Zandea/POI/Modular Building Catalog")]
    public sealed class ModularBuildingCatalog : ScriptableObject
    {
        public enum Kind { SolidWall, DoorWall, WindowWall, RoofSlope }
        [Serializable]
        public sealed class Module
        {
            public GameObject prefab;
            public Kind kind;
            public bool upperFloor;
            [Range(4, 8)] public int length = 8;
            public float height = 2.3f;
            public float thickness = 0.3f;
            public Vector3 pivotToCenter;
            public Vector3 lengthAxis = Vector3.right;
            public Vector3 outwardAxis = Vector3.forward;
            public Vector3 upAxis = Vector3.up;
            public bool allowQuarterTurns = true;
            public float weight = 1f;
            public string variation;
            [Tooltip("Roof surface length along the ridge, measured on the actual prefab.")]
            public float roofRidgeLength = 6f;
            [Tooltip("Roof horizontal run from eave to ridge before scaling.")]
            public float roofHorizontalRun = 3f;
            [Tooltip("Rise from eave to ridge before scaling.")]
            public float roofRise = 1.5f;
            [Tooltip("Local direction from eave toward ridge.")]
            public Vector3 roofRunAxis = Vector3.forward;
            public Vector3 roofRidgeAxis = Vector3.right;
            [Tooltip("Local-space eave midpoint. Measure on the actual mesh.")]
            public Vector3 roofEavePoint;
        }

        public List<Module> modules = new List<Module>();
        [Range(8, 16)] public int minimumSize = 8;
        [Range(8, 16)] public int maximumSize = 16;
        [Range(0, 1)] public float twoStoryProbability = .7f;
        [Range(0, 1)] public float connectedProbability;
        [Range(0, 3)] public int minimumWindows = 1;
        [Range(0, 3)] public int maximumWindows = 3;
        [Min(0)] public float roofOverhang = .35f;
        [Min(0)] public float cornerOverlap = .05f;
        [Min(0)] public float maximumTerrainVariation = .6f;
        [Range(0, 45)] public float maximumTerrainSlope = 8f;
        [Min(0)] public float buildingSpacing = 3f;
        [Min(0)] public float entranceClearance = 2f;
        public int seed;

        public bool Has(Kind kind, bool upper, int length)
        {
            foreach (var m in modules)
                if (m != null && m.prefab && m.kind == kind && m.upperFloor == upper &&
                    (kind == Kind.RoofSlope || m.length == length) && m.weight > 0) return true;
            return false;
        }

        public Module Pick(Kind kind, bool upper, int length, System.Random random)
        {
            float total = 0;
            foreach (var m in modules)
                if (m != null && m.prefab && m.kind == kind && m.upperFloor == upper &&
                    (kind == Kind.RoofSlope || m.length == length) && m.weight > 0) total += m.weight;
            if (total <= 0) return null;
            double choice = random.NextDouble() * total;
            foreach (var m in modules)
                if (m != null && m.prefab && m.kind == kind && m.upperFloor == upper &&
                    (kind == Kind.RoofSlope || m.length == length) && m.weight > 0 &&
                    (choice -= m.weight) <= 0) return m;
            return null;
        }
    }
}

