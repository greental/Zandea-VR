#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRKoombar.Tools.Editor
{
    /// <summary>
    /// Batch-mode bridge: imports an externally produced FBX folder (FBX + Textures) and builds URP Lit materials with BaseMap.
    /// Called from external pipeline: -executeMethod ... ExternalImportMaterialBridge.RunFromCommandLine -extImportFolder Assets/...
    /// </summary>
    public static class ExternalImportMaterialBridge
    {
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        public static void RunFromCommandLine()
        {
            string folder = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-extImportFolder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    folder = args[++i].Replace("\\", "/");
                }
            }

            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Missing or invalid -extImportFolder (must start with Assets/).");
            }

            ProcessFolder(folder);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ExternalImportMaterialBridge] Done: {folder}");
        }

        private static void ProcessFolder(string assetFolder)
        {
            string abs = ToAbsolute(assetFolder);
            if (!Directory.Exists(abs))
            {
                throw new InvalidOperationException($"Folder not found: {assetFolder}");
            }

            Shader shader = Shader.Find(UrpLitShader);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader not found: {UrpLitShader}");
            }

            string[] fbxFiles = Directory.GetFiles(abs, "*.fbx", SearchOption.TopDirectoryOnly);
            if (fbxFiles.Length == 0)
            {
                Debug.LogWarning($"[ExternalImportMaterialBridge] No FBX in {assetFolder}");
                return;
            }

            string fbxName = Path.GetFileName(fbxFiles[0]);
            string fbxAssetPath = $"{assetFolder}/{fbxName}".Replace("\\", "/");
            AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            string texDir = $"{assetFolder}/Textures".Replace("\\", "/");
            string texAbs = ToAbsolute(texDir);
            Texture2D firstTex = null;
            if (Directory.Exists(texAbs))
            {
                foreach (string png in Directory.GetFiles(texAbs, "*.png", SearchOption.TopDirectoryOnly).OrderBy(f => f))
                {
                    string rel = ToAssetPath(png);
                    AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                    if (t != null)
                    {
                        firstTex = t;
                        break;
                    }
                }

                if (firstTex == null)
                {
                    foreach (string jpg in Directory.GetFiles(texAbs, "*.jpg", SearchOption.TopDirectoryOnly).OrderBy(f => f))
                    {
                        string rel = ToAssetPath(jpg);
                        AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                        if (t != null)
                        {
                            firstTex = t;
                            break;
                        }
                    }
                }
            }

            string matsDir = $"{assetFolder}/Materials".Replace("\\", "/");
            EnsureFolder(matsDir);

            var subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
            Material[] embedded = subAssets.OfType<Material>().ToArray();

            if (embedded.Length > 0)
            {
                int i = 0;
                foreach (Material m in embedded)
                {
                    m.shader = shader;
                    if (firstTex != null && m.HasProperty("_BaseMap"))
                    {
                        m.SetTexture("_BaseMap", firstTex);
                    }

                    if (firstTex != null && m.HasProperty("_BaseColor"))
                    {
                        m.SetColor("_BaseColor", Color.white);
                    }

                    EditorUtility.SetDirty(m);
                    i++;
                }
            }
            else
            {
                string matPath = $"{matsDir}/{Path.GetFileNameWithoutExtension(fbxName)}_URP.mat";
                var mat = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(fbxName) + "_URP"
                };
                if (firstTex != null)
                {
                    mat.SetTexture("_BaseMap", firstTex);
                }

                mat.SetColor("_BaseColor", Color.white);
                AssetDatabase.CreateAsset(mat, matPath);

                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
                if (root != null)
                {
                    string prefabPath = $"{assetFolder}/{Path.GetFileNameWithoutExtension(fbxName)}_withMat.prefab".Replace("\\", "/");
                    GameObject instance = PrefabUtility.InstantiatePrefab(root) as GameObject;
                    if (instance != null)
                    {
                        try
                        {
                            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                            {
                                int n = r.sharedMaterials.Length;
                                var mats = new Material[n];
                                for (int k = 0; k < n; k++)
                                {
                                    mats[k] = mat;
                                }

                                r.sharedMaterials = mats;
                            }

                            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(instance);
                        }
                    }
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "Assets";
            string leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string ToAbsolute(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath).Replace("/", Path.DirectorySeparatorChar.ToString());
        }

        private static string ToAssetPath(string absoluteFile)
        {
            string full = Path.GetFullPath(absoluteFile).Replace("\\", "/");
            string data = Application.dataPath.Replace("\\", "/");
            if (!full.StartsWith(data, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string rel = "Assets" + full.Substring(data.Length);
            return rel.Replace("\\", "/");
        }
    }
}
#endif
