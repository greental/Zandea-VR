using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    /// <summary>Transfers CC_Base weights and body morphs to the fitted Downloads previews.</summary>
    public static class RiggedWardrobeBaker
    {
        const string Root = "Assets/CharacterCreator/ImportedDownloads";
        const string Items = Root + "/Items";
        const string Output = Root + "/RiggedItems";
        const string Model = "Assets/CharacterCreator/Models/male_creator.glb";
        static readonly string[] Names = { "armored_torso", "scavenger_vest", "ragged_skirt_a",
            "ragged_skirt_b", "ragged_skirt_c", "wrapped_boot", "shoulder_harness_a",
            "shoulder_harness_b", "scavenger_outfit", "leather_glove_a", "leather_glove_b" };
        static readonly string[] Morphs = { "WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular" };

        sealed class Sample
        {
            public Vector3 position;
            public BoneWeight weight;
            public readonly Vector3[] deltas = new Vector3[4];
        }

        sealed class Reference
        {
            public Transform[] bones;
            public Matrix4x4[] bindposes;
            public float[] frameWeights = new float[4];
            public readonly List<Sample> samples = new List<Sample>();
            public readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
        }

        [MenuItem("Zandea/Character Creator/Bake Fitted Wardrobe Rig")]
        public static void Bake()
        {
            BakeItems(Names);
        }

        public static void BakeItems(IEnumerable<string> names)
        {
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Root, "RiggedItems");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (!model) throw new InvalidOperationException("Missing creator reference model");
            var reference = (GameObject)PrefabUtility.InstantiatePrefab(model);
            reference.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            int baked = 0;
            try
            {
                var data = BuildReference(reference);
                foreach (string name in names)
                {
                    try { BakeItem(name, reference, data); baked++; }
                    catch (Exception error) { Debug.LogError("Rig bake failed for " + name + ": " + error); }
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Rigged wardrobe baked: " + baked + " items. The boot prefab contains independently bound left and right meshes.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        static Reference BuildReference(GameObject model)
        {
            var result = new Reference();
            var boneList = new List<Transform>();
            var boneIds = new Dictionary<string, int>();
            foreach (var transform in model.GetComponentsInChildren<Transform>(true))
                if (transform.name.StartsWith("CC_Base_", StringComparison.Ordinal) && !boneIds.ContainsKey(transform.name))
                { boneIds.Add(transform.name, boneList.Count); boneList.Add(transform); }
            result.bones = boneList.ToArray();
            result.bindposes = new Matrix4x4[result.bones.Length];
            for (int i = 0; i < result.bones.Length; i++) result.bindposes[i] = result.bones[i].worldToLocalMatrix;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (!mesh || mesh.vertexCount == 0 || mesh.boneWeights.Length != mesh.vertexCount) continue;
                if (mesh.GetBlendShapeIndex("WeightHeavy") < 0) continue;
                var vertices = mesh.vertices;
                var sourceWeights = mesh.boneWeights;
                var shapeDeltas = new Vector3[4][];
                for (int m = 0; m < Morphs.Length; m++)
                {
                    int index = mesh.GetBlendShapeIndex(Morphs[m]);
                    shapeDeltas[m] = new Vector3[vertices.Length];
                    if (index < 0) continue;
                    int frame = mesh.GetBlendShapeFrameCount(index) - 1;
                    result.frameWeights[m] = mesh.GetBlendShapeFrameWeight(index, frame);
                    mesh.GetBlendShapeFrameVertices(index, frame, shapeDeltas[m], null, null);
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    var sample = new Sample();
                    sample.position = renderer.transform.TransformPoint(vertices[i]);
                    var weight = sourceWeights[i];
                    sample.weight = new BoneWeight
                    {
                        boneIndex0 = MapBone(renderer, weight.boneIndex0, boneIds), weight0 = weight.weight0,
                        boneIndex1 = MapBone(renderer, weight.boneIndex1, boneIds), weight1 = weight.weight1,
                        boneIndex2 = MapBone(renderer, weight.boneIndex2, boneIds), weight2 = weight.weight2,
                        boneIndex3 = MapBone(renderer, weight.boneIndex3, boneIds), weight3 = weight.weight3
                    };
                    for (int m = 0; m < Morphs.Length; m++)
                        sample.deltas[m] = renderer.transform.TransformVector(shapeDeltas[m][i]);
                    int id = result.samples.Count;
                    result.samples.Add(sample);
                    var cell = Cell(sample.position);
                    if (!result.grid.TryGetValue(cell, out var list)) result.grid[cell] = list = new List<int>();
                    list.Add(id);
                }
            }
            if (result.samples.Count == 0) throw new InvalidOperationException("Reference model has no weighted body morph vertices");
            Debug.Log("Wardrobe rig reference: " + result.samples.Count + " weighted body vertices, " + result.bones.Length + " bones");
            return result;
        }

        static int MapBone(SkinnedMeshRenderer renderer, int sourceIndex, Dictionary<string, int> ids)
        {
            if (sourceIndex < 0 || sourceIndex >= renderer.bones.Length || !renderer.bones[sourceIndex] ||
                !ids.TryGetValue(renderer.bones[sourceIndex].name, out int id)) return 0;
            return id;
        }

        static Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x * 20f),
            Mathf.FloorToInt(p.y * 20f), Mathf.FloorToInt(p.z * 20f));

        static Sample Nearest(Reference data, Vector3 point)
        {
            var c = Cell(point);
            int best = -1;
            float distance = float.MaxValue;
            for (int radius = 0; radius <= 12 && best < 0; radius++)
                for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                {
                    if (!data.grid.TryGetValue(c + new Vector3Int(x, y, z), out var list)) continue;
                    foreach (int id in list)
                    {
                        float d = (data.samples[id].position - point).sqrMagnitude;
                        if (d < distance) { best = id; distance = d; }
                    }
                }
            if (best < 0)
                for (int i = 0; i < data.samples.Count; i++)
                {
                    float d = (data.samples[i].position - point).sqrMagnitude;
                    if (d < distance) { best = i; distance = d; }
                }
            return data.samples[best];
        }

        static void BakeItem(string name, GameObject reference, Reference data)
        {
            var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/" + name + ".asset");
            var preview = AssetDatabase.LoadAssetAtPath<GameObject>(Items + "/" + name + ".prefab");
            if (!item || !preview) throw new InvalidOperationException("Missing fitted preview item");
            Transform attach = null;
            foreach (var transform in reference.GetComponentsInChildren<Transform>(true))
                if (transform.name == item.attachBoneName) { attach = transform; break; }
            if (!attach) throw new InvalidOperationException("Missing attach bone " + item.attachBoneName);
            var source = (GameObject)PrefabUtility.InstantiatePrefab(preview);
            source.transform.SetParent(attach, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one;
            GameObject pairedGlove = null;
            if (name.StartsWith("leather_glove_", StringComparison.Ordinal))
            {
                string partner = name == "leather_glove_a" ? "leather_glove_b" : "leather_glove_a";
                var partnerItem = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/" + partner + ".asset");
                var partnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Items + "/" + partner + ".prefab");
                if (!partnerItem || !partnerPrefab) throw new InvalidOperationException("Missing paired glove " + partner);
                Transform partnerBone = null;
                foreach (var transform in reference.GetComponentsInChildren<Transform>(true))
                    if (transform.name == partnerItem.attachBoneName) { partnerBone = transform; break; }
                if (!partnerBone) throw new InvalidOperationException("Missing paired glove attach bone");
                pairedGlove = (GameObject)PrefabUtility.InstantiatePrefab(partnerPrefab);
                pairedGlove.transform.SetParent(partnerBone, false);
                pairedGlove.transform.localPosition = Vector3.zero;
                pairedGlove.transform.localRotation = Quaternion.identity;
                pairedGlove.transform.localScale = Vector3.one;
            }
            var prefab = new GameObject(name + " rigged");
            try
            {
                var dummy = new Transform[data.bones.Length];
                for (int i = 0; i < dummy.Length; i++)
                {
                    dummy[i] = new GameObject(data.bones[i].name).transform;
                    dummy[i].SetParent(prefab.transform, false);
                    dummy[i].position = data.bones[i].position;
                    dummy[i].rotation = data.bones[i].rotation;
                    dummy[i].localScale = data.bones[i].lossyScale;
                }
                int part = 0;
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh || !filter.GetComponent<MeshRenderer>()) continue;
                    AddPart(name, part++, filter, prefab, data, dummy, false, name == "wrapped_boot");
                    if (name == "wrapped_boot") AddPart(name, part++, filter, prefab, data, dummy, true, true);
                }
                if (pairedGlove)
                    foreach (var filter in pairedGlove.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh && filter.GetComponent<MeshRenderer>())
                            AddPart(name, part++, filter, prefab, data, dummy, false, false);
                if (name.StartsWith("ragged_skirt_", StringComparison.Ordinal))
                    part += AddLegPlates(name, part, prefab, data, dummy);
                if (name == "wrapped_boot")
                    part += AddBootShells(name, part, prefab, data, dummy);
                if (part == 0) throw new InvalidOperationException("No static meshes in preview");
                string prefabPath = Output + "/" + name + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                if (!saved) throw new InvalidOperationException("Could not save rigged prefab");
                item.prefab = saved;
                item.skinned = true;
                item.requiredMorphs = (string[])Morphs.Clone();
                item.fitNote = name == "wrapped_boot"
                    ? "Paired mirrored boots, each skinned to its own foot. Review ankle bends."
                    : "CC_Base skin weights and four body-size morphs transferred from the creator body. Review extreme poses and loose fabric.";
                EditorUtility.SetDirty(item);
                Debug.Log("Rigged item: " + name + " (" + part + " meshes)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (pairedGlove) UnityEngine.Object.DestroyImmediate(pairedGlove);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        static void AddPart(string name, int part, MeshFilter filter, GameObject prefab,
            Reference data, Transform[] dummy, bool mirror, bool boot)
        {
            var source = filter.sharedMesh;
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = name + (mirror ? " left" : " part " + part);
            var vertices = source.vertices;
            var normals = source.normals;
            var weights = new BoneWeight[vertices.Length];
            var deltas = new Vector3[4][];
            for (int m = 0; m < 4; m++) deltas[m] = new Vector3[vertices.Length];
            int footIndex = Array.FindIndex(data.bones, b => b.name ==
                (mirror ? "CC_Base_L_Foot_06" : "CC_Base_R_Foot_022"));
            if (boot && footIndex < 0) throw new InvalidOperationException("Missing foot bone for boot");
            int calfIndex = boot ? Array.FindIndex(data.bones, b => b.name ==
                (mirror ? "CC_Base_L_Calf_05" : "CC_Base_R_Calf_021")) : -1;
            if (boot && calfIndex < 0) throw new InvalidOperationException("Missing calf bone for boot");
            var upright = Quaternion.Euler(-90f, 0f, 0f);
            var bootPivot = new Vector3(.11f, .14f, .02f);
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = filter.transform.TransformPoint(vertices[i]);
                if (boot) point = bootPivot + upright * (point - bootPivot);
                if (mirror) point.x = -point.x;
                vertices[i] = point;
                if (normals.Length == vertices.Length)
                {
                    var normal = filter.transform.TransformDirection(normals[i]);
                    if (boot) normal = upright * normal;
                    if (mirror) normal.x = -normal.x;
                    normals[i] = normal;
                }
                if (boot)
                {
                    float upper = Mathf.Clamp01((point.y - .12f) / .13f) * .8f;
                    weights[i] = new BoneWeight { boneIndex0 = footIndex, weight0 = 1f - upper,
                        boneIndex1 = calfIndex, weight1 = upper };
                }
                else
                {
                    var nearest = Nearest(data, point);
                    weights[i] = nearest.weight;
                    for (int m = 0; m < 4; m++) deltas[m][i] = nearest.deltas[m] * .9f;
                }
            }
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            if (mirror)
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    var indices = mesh.GetIndices(submesh);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    { int swap = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = swap; }
                    mesh.SetIndices(indices, MeshTopology.Triangles, submesh);
                }
            mesh.boneWeights = weights;
            mesh.bindposes = data.bindposes;
            for (int m = 0; m < 4; m++)
                mesh.AddBlendShapeFrame(Morphs[m], data.frameWeights[m] > 0 ? data.frameWeights[m] : 1f,
                    deltas[m], null, null);
            mesh.RecalculateBounds();
            string meshPath = Output + "/" + name + "_part" + part + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            var partObject = new GameObject(mesh.name);
            partObject.transform.SetParent(prefab.transform, false);
            var skin = partObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.sharedMaterials = filter.GetComponent<MeshRenderer>().sharedMaterials;
            skin.bones = dummy;
            skin.rootBone = dummy[0];
            skin.updateWhenOffscreen = true;
        }

        static int AddLegPlates(string name, int firstPart, GameObject prefab, Reference data, Transform[] dummy)
        {
            const string materialPath = Output + "/leg_armor_plate.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.name = "Worn steel leg plates";
                material.SetColor("_BaseColor", new Color(.34f, .39f, .41f));
                material.SetFloat("_Metallic", .42f);
                material.SetFloat("_Smoothness", .36f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", new Color(.34f, .39f, .41f));
            EditorUtility.SetDirty(material);
            int count = 0;
            foreach (string side in new[] { "L", "R" })
            {
                var thigh = Bone(data, side == "L" ? "CC_Base_L_Thigh_04" : "CC_Base_R_Thigh_00");
                var calf = Bone(data, side == "L" ? "CC_Base_L_Calf_05" : "CC_Base_R_Calf_021");
                var foot = Bone(data, side == "L" ? "CC_Base_L_Foot_06" : "CC_Base_R_Foot_022");
                AddPlate(name, firstPart + count++, prefab, data, dummy, material,
                    Vector3.Lerp(thigh.position, calf.position, .2f),
                    Vector3.Lerp(thigh.position, calf.position, .76f), thigh, .13f);
                AddPlate(name, firstPart + count++, prefab, data, dummy, material,
                    Vector3.Lerp(calf.position, foot.position, .16f),
                    Vector3.Lerp(calf.position, foot.position, .78f), calf, .095f);
            }
            return count;
        }

        static Transform Bone(Reference data, string name)
        {
            foreach (var bone in data.bones) if (bone.name == name) return bone;
            throw new InvalidOperationException("Leg armor bone missing: " + name);
        }

        static void AddPlate(string name, int part, GameObject prefab, Reference data, Transform[] dummy,
            Material material, Vector3 upper, Vector3 lower, Transform bone, float radius)
        {
            const int columns = 9, rows = 3;
            var mesh = new Mesh { name = name + " plate " + part };
            var vertices = new Vector3[columns * rows];
            var uv = new Vector2[vertices.Length];
            var weights = new BoneWeight[vertices.Length];
            var deltas = new Vector3[4][];
            for (int m = 0; m < 4; m++) deltas[m] = new Vector3[vertices.Length];
            int boneIndex = Array.IndexOf(data.bones, bone);
            for (int row = 0; row < rows; row++)
            {
                float v = row / (float)(rows - 1);
                var center = Vector3.Lerp(upper, lower, v);
                for (int column = 0; column < columns; column++)
                {
                    float u = column / (float)(columns - 1);
                    float angle = Mathf.Lerp(-1.2f, 1.2f, u);
                    int index = row * columns + column;
                    float r = radius * (1f - .09f * v);
                    vertices[index] = center + new Vector3(Mathf.Sin(angle) * r, 0f, Mathf.Cos(angle) * r + .014f);
                    uv[index] = new Vector2(u, v);
                    weights[index] = new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
                    var nearest = Nearest(data, vertices[index]);
                    for (int m = 0; m < 4; m++) deltas[m][index] = nearest.deltas[m] * 1.1f;
                }
            }
            var triangles = new int[(columns - 1) * (rows - 1) * 6];
            int write = 0;
            for (int row = 0; row < rows - 1; row++)
            for (int column = 0; column < columns - 1; column++)
            {
                int a = row * columns + column, b = a + 1, c = a + columns, d = c + 1;
                triangles[write++] = a; triangles[write++] = c; triangles[write++] = b;
                triangles[write++] = b; triangles[write++] = c; triangles[write++] = d;
            }
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.boneWeights = weights;
            mesh.bindposes = data.bindposes;
            mesh.RecalculateNormals();
            for (int m = 0; m < 4; m++)
                mesh.AddBlendShapeFrame(Morphs[m], data.frameWeights[m] > 0 ? data.frameWeights[m] : 1f,
                    deltas[m], null, null);
            mesh.RecalculateBounds();
            string meshPath = Output + "/" + name + "_part" + part + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            var partObject = new GameObject(mesh.name);
            partObject.transform.SetParent(prefab.transform, false);
            var skin = partObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.sharedMaterial = material;
            skin.bones = dummy;
            skin.rootBone = dummy[0];
            skin.updateWhenOffscreen = true;
        }

        static int AddBootShells(string name, int firstPart, GameObject prefab, Reference data, Transform[] dummy)
        {
            const string materialPath = Output + "/boot_leather.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.name = "Wrapped boot leather";
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", new Color(.13f, .105f, .075f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", .25f);
            EditorUtility.SetDirty(material);
            int count = 0;
            foreach (string footName in new[] { "CC_Base_L_Foot_06", "CC_Base_R_Foot_022" })
            {
                var foot = Bone(data, footName);
                float[] along = { -.075f, -.03f, .045f, .12f, .205f, .305f };
                float[] halfWidth = { .045f, .075f, .09f, .087f, .067f, .012f };
                float[] centerY = { .045f, .055f, .05f, .039f, .025f, .01f };
                float[] halfHeight = { .055f, .075f, .076f, .063f, .045f, .014f };
                const int around = 16;
                int rings = along.Length - 1;
                var vertices = new Vector3[(around + 1) * (rings + 1)];
                var uv = new Vector2[vertices.Length];
                var weights = new BoneWeight[vertices.Length];
                var triangles = new int[around * rings * 6];
                int boneIndex = Array.IndexOf(data.bones, foot);
                for (int ring = 0; ring <= rings; ring++)
                {
                    for (int segment = 0; segment <= around; segment++)
                    {
                        float angle = 2f * Mathf.PI * segment / around;
                        int index = ring * (around + 1) + segment;
                        vertices[index] = new Vector3(
                            foot.position.x + Mathf.Cos(angle) * halfWidth[ring],
                            centerY[ring] + Mathf.Sin(angle) * halfHeight[ring],
                            foot.position.z + along[ring]);
                        uv[index] = new Vector2(segment / (float)around, ring / (float)rings);
                        weights[index] = new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
                    }
                }
                int write = 0;
                for (int ring = 0; ring < rings; ring++)
                for (int segment = 0; segment < around; segment++)
                {
                    int a = ring * (around + 1) + segment, b = a + 1, c = a + around + 1, d = c + 1;
                    triangles[write++] = a; triangles[write++] = b; triangles[write++] = c;
                    triangles[write++] = b; triangles[write++] = d; triangles[write++] = c;
                }
                var mesh = new Mesh { name = name + " full " + (footName.Contains("_L_") ? "left" : "right") };
                mesh.vertices = vertices;
                mesh.uv = uv;
                mesh.triangles = triangles;
                mesh.boneWeights = weights;
                mesh.bindposes = data.bindposes;
                mesh.RecalculateNormals();
                for (int m = 0; m < 4; m++)
                    mesh.AddBlendShapeFrame(Morphs[m], data.frameWeights[m] > 0 ? data.frameWeights[m] : 1f,
                        new Vector3[vertices.Length], null, null);
                mesh.RecalculateBounds();
                string meshPath = Output + "/" + name + "_part" + (firstPart + count) + ".asset";
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
                var partObject = new GameObject(mesh.name);
                partObject.transform.SetParent(prefab.transform, false);
                var skin = partObject.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.sharedMaterial = material;
                skin.bones = dummy;
                skin.rootBone = dummy[0];
                skin.updateWhenOffscreen = true;
                count++;
            }
            return count;
        }
    }
}
