#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRKoombar.Tools.Editor
{
    public sealed class GlbToFbxBatchPipeline : EditorWindow
    {
        private const string DefaultOutputFolder = "Assets/MyAssets/Imported/HunyuanFbx";
        private const string StageFolder = "Assets/MyAssets/Imported/_GlbStage";
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        private string _inputDirectory = string.Empty;
        private string _outputFolder = DefaultOutputFolder;
        private bool _overwriteExisting = true;

        [MenuItem("Zandea/VR Koombar/GLB to FBX Batch")]
        private static void OpenWindow()
        {
            var window = GetWindow<GlbToFbxBatchPipeline>("GLB -> FBX");
            window.minSize = new Vector2(540f, 220f);
            window.InitializeDefaults();
        }

        public static void RunBatchFromCommandLine()
        {
            var runner = CreateInstance<GlbToFbxBatchPipeline>();
            runner.InitializeDefaults();
            runner.ApplyCommandLineArgs();

            bool success = runner.RunBatchInternal(showDialogs: false, out string summary);
            Debug.Log($"[GlbToFbxBatchPipeline] CLI summary: {summary}");
            DestroyImmediate(runner);

            if (!success)
            {
                throw new InvalidOperationException(summary);
            }
        }

        private void InitializeDefaults()
        {
            if (!string.IsNullOrWhiteSpace(_inputDirectory))
            {
                return;
            }

            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            _inputDirectory = downloads.Replace("\\", "/");
        }

        private void OnGUI()
        {
            InitializeDefaults();

            EditorGUILayout.LabelField("Hunyuan GLB -> FBX Pipeline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Scans Downloads (root only) for .glb files, imports them, creates URP materials from GLB textures (_BaseMap), and exports .fbx + .mat assets to one Unity folder.",
                MessageType.Info);

            EditorGUILayout.Space();
            DrawInputDirectoryField();
            DrawOutputFolderField();
            _overwriteExisting = EditorGUILayout.ToggleLeft("Overwrite Existing FBX/Materials", _overwriteExisting);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                if (GUILayout.Button("Run Batch", GUILayout.Height(34f)))
                {
                    RunBatch();
                }
            }
        }

        private void DrawInputDirectoryField()
        {
            EditorGUILayout.LabelField("Input Directory (disk path)");
            using (new EditorGUILayout.HorizontalScope())
            {
                _inputDirectory = EditorGUILayout.TextField(_inputDirectory);
                if (GUILayout.Button("Browse", GUILayout.Width(84f)))
                {
                    string selected = EditorUtility.OpenFolderPanel("Select GLB Input Folder", _inputDirectory, string.Empty);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        _inputDirectory = selected.Replace("\\", "/");
                    }
                }
            }
        }

        private void DrawOutputFolderField()
        {
            EditorGUILayout.LabelField("Output Folder (Unity Asset Path)");
            _outputFolder = EditorGUILayout.TextField(_outputFolder);
        }

        private void RunBatch()
        {
            RunBatchInternal(showDialogs: true, out string _);
        }

        private bool RunBatchInternal(bool showDialogs, out string summary)
        {
            if (!TryValidateInputs(out string validationError))
            {
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog("GLB -> FBX Batch", validationError, "OK");
                }

                summary = validationError;
                return false;
            }

            if (!TryResolveFbxExporterMethod(out MethodInfo exportMethod))
            {
                const string missingFbxMessage = "Package 'com.unity.formats.fbx' is required. Install it in Package Manager, then run again.";
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog("FBX Exporter Missing", missingFbxMessage, "OK");
                }

                summary = missingFbxMessage;
                return false;
            }

            EnsureAssetFolderExists(_outputFolder);
            EnsureAssetFolderExists(StageFolder);

            string[] glbFiles = Directory.GetFiles(_inputDirectory, "*.glb", SearchOption.TopDirectoryOnly);
            if (glbFiles.Length == 0)
            {
                const string noFilesMessage = "No .glb files found in the selected folder.";
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog("GLB -> FBX Batch", noFilesMessage, "OK");
                }

                summary = noFilesMessage;
                return false;
            }

            int successCount = 0;
            int failCount = 0;
            Debug.Log($"[GlbToFbxBatchPipeline] PROGRESS_TOTAL={glbFiles.Length}");

            try
            {
                for (int i = 0; i < glbFiles.Length; i++)
                {
                    string file = glbFiles[i];
                    string name = Path.GetFileName(file);
                    Debug.Log($"[GlbToFbxBatchPipeline] PROGRESS_FILE_START={i + 1}|{name}");
                    bool ok = ProcessGlb(file, exportMethod);
                    if (ok)
                    {
                        successCount++;
                        Debug.Log($"[GlbToFbxBatchPipeline] PROGRESS_FILE_DONE={i + 1}|{name}|OK");
                    }
                    else
                    {
                        failCount++;
                        Debug.Log($"[GlbToFbxBatchPipeline] PROGRESS_FILE_DONE={i + 1}|{name}|FAIL");
                    }
                }
            }
            finally
            {
                AssetDatabase.Refresh();
            }

            summary = $"Processed {glbFiles.Length} GLB files.\nSuccess: {successCount}\nFailed: {failCount}";
            Debug.Log($"[GlbToFbxBatchPipeline] {summary}");

            if (showDialogs)
            {
                EditorUtility.DisplayDialog("GLB -> FBX Batch Complete", summary, "OK");
            }

            return failCount == 0;
        }

        private bool ProcessGlb(string sourceGlbPath, MethodInfo exportMethod)
        {
            string fileName = Path.GetFileNameWithoutExtension(sourceGlbPath);
            string safeName = SanitizeName(fileName);
            string stageAssetPath = $"{StageFolder}/{safeName}.glb";
            string modelOutputFolder = $"{_outputFolder}/{safeName}";
            string materialsFolder = $"{modelOutputFolder}/Materials";
            string texturesFolder = $"{modelOutputFolder}/Textures";
            string fbxAssetPath = $"{modelOutputFolder}/{safeName}.fbx";
            string absoluteStagePath = ToAbsolutePath(stageAssetPath);

            try
            {
                EnsureAssetFolderExists(modelOutputFolder);
                EnsureAssetFolderExists(materialsFolder);
                EnsureAssetFolderExists(texturesFolder);

                if (!_overwriteExisting)
                {
                    fbxAssetPath = AssetDatabase.GenerateUniqueAssetPath(fbxAssetPath);
                }

                File.Copy(sourceGlbPath, absoluteStagePath, true);
                AssetDatabase.ImportAsset(stageAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                GameObject importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(stageAssetPath);
                if (importedModel == null)
                {
                    Debug.LogWarning($"[GlbToFbxBatchPipeline] Could not load imported GLB: {sourceGlbPath}");
                    return false;
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(importedModel) as GameObject;
                if (instance == null)
                {
                    Debug.LogWarning($"[GlbToFbxBatchPipeline] Failed to instantiate imported GLB: {sourceGlbPath}");
                    return false;
                }

                try
                {
                    byte[] fallbackImageBytes = TryExtractFirstImageBytesFromGlb(sourceGlbPath);
                    RebuildMaterials(instance, materialsFolder, texturesFolder, safeName, fallbackImageBytes);
                    string absoluteFbxPath = ToAbsolutePath(fbxAssetPath);
                    exportMethod.Invoke(null, new object[] { absoluteFbxPath, instance });
                }
                finally
                {
                    DestroyImmediate(instance);
                }

                AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GlbToFbxBatchPipeline] Failed: {sourceGlbPath}\n{ex}");
                return false;
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(stageAssetPath) != null)
                {
                    AssetDatabase.DeleteAsset(stageAssetPath);
                }
            }
        }

        private void RebuildMaterials(GameObject instance, string materialsFolder, string texturesFolder, string safeName, byte[] fallbackImageBytes)
        {
            Shader shader = Shader.Find(UrpLitShader);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader not found: {UrpLitShader}");
            }

            var cache = new Dictionary<Material, Material>();
            var textureCache = new Dictionary<Texture, Texture>();
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                Material[] shared = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < shared.Length; i++)
                {
                    Material source = shared[i];
                    if (source == null)
                    {
                        continue;
                    }

                    if (!cache.TryGetValue(source, out Material mapped))
                    {
                        mapped = BuildMaterialAsset(source, shader, materialsFolder, texturesFolder, safeName, i, textureCache, fallbackImageBytes);
                        cache[source] = mapped;
                    }

                    if (mapped != null && shared[i] != mapped)
                    {
                        shared[i] = mapped;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        private Material BuildMaterialAsset(
            Material source,
            Shader shader,
            string materialsFolder,
            string texturesFolder,
            string safeName,
            int slotIndex,
            Dictionary<Texture, Texture> textureCache,
            byte[] fallbackImageBytes)
        {
            string baseName = $"{safeName}_{SanitizeName(source.name)}_{slotIndex}";
            string matPath = $"{materialsFolder}/{baseName}.mat";
            if (!_overwriteExisting)
            {
                matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);
            }

            Texture sourceTexture = null;
            if (source.HasProperty("_BaseMap"))
            {
                sourceTexture = source.GetTexture("_BaseMap");
            }
            else if (source.HasProperty("_MainTex"))
            {
                sourceTexture = source.GetTexture("_MainTex");
            }

            Color color = Color.white;
            if (source.HasProperty("_BaseColor"))
            {
                color = source.GetColor("_BaseColor");
            }
            else if (source.HasProperty("_Color"))
            {
                color = source.GetColor("_Color");
            }

            Texture bakedTexture = ExtractTextureAsset(sourceTexture, texturesFolder, baseName, textureCache, fallbackImageBytes);

            var material = new Material(shader)
            {
                name = baseName
            };
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", bakedTexture);

            AssetDatabase.CreateAsset(material, matPath);
            return material;
        }

        private Texture ExtractTextureAsset(
            Texture sourceTexture,
            string texturesFolder,
            string baseName,
            Dictionary<Texture, Texture> textureCache,
            byte[] fallbackImageBytes)
        {
            if (sourceTexture == null && fallbackImageBytes != null && fallbackImageBytes.Length > 0)
            {
                sourceTexture = CreateTextureFromBytes(fallbackImageBytes, baseName + "_fallback");
            }

            if (sourceTexture == null)
            {
                return null;
            }

            if (textureCache.TryGetValue(sourceTexture, out Texture cached))
            {
                return cached;
            }

            string texturePath = $"{texturesFolder}/{baseName}_BaseMap.png";
            if (!_overwriteExisting)
            {
                texturePath = AssetDatabase.GenerateUniqueAssetPath(texturePath);
            }

            Texture2D baked = BakeTextureToTexture2D(sourceTexture);
            if (baked == null)
            {
                Debug.LogWarning($"[GlbToFbxBatchPipeline] Could not bake texture '{sourceTexture.name}'.");
                return null;
            }

            try
            {
                byte[] png = baked.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    Debug.LogWarning($"[GlbToFbxBatchPipeline] Failed to encode texture '{sourceTexture.name}' to PNG.");
                    return null;
                }

                File.WriteAllBytes(ToAbsolutePath(texturePath), png);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Texture importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                textureCache[sourceTexture] = importedTexture;
                return importedTexture;
            }
            finally
            {
                DestroyImmediate(baked);
            }
        }

        private static Texture2D CreateTextureFromBytes(byte[] bytes, string name)
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = name
                };

                if (!texture.LoadImage(bytes, markNonReadable: false))
                {
                    DestroyImmediate(texture);
                    return null;
                }

                return texture;
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D BakeTextureToTexture2D(Texture sourceTexture)
        {
            int width = Mathf.Max(1, sourceTexture.width);
            int height = Mathf.Max(1, sourceTexture.height);
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(sourceTexture, rt);
                RenderTexture.active = rt;
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private bool TryValidateInputs(out string error)
        {
            if (string.IsNullOrWhiteSpace(_inputDirectory))
            {
                error = "Input folder is required.";
                return false;
            }

            if (!Directory.Exists(_inputDirectory))
            {
                error = $"Input folder does not exist:\n{_inputDirectory}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_outputFolder) || !_outputFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "Output folder must be a Unity asset path that starts with 'Assets/'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryResolveFbxExporterMethod(out MethodInfo method)
        {
            method = null;
            Type exporterType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
            if (exporterType == null)
            {
                return false;
            }

            method = exporterType.GetMethod("ExportObject", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(UnityEngine.Object) }, null);
            return method != null;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace("\\", "/");
            return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
        }

        private static void EnsureAssetFolderExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string leaf = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureAssetFolderExists(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string SanitizeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
        }

        private static byte[] TryExtractFirstImageBytesFromGlb(string glbPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(glbPath);
                if (data.Length < 20 || data[0] != (byte)'g' || data[1] != (byte)'l' || data[2] != (byte)'T' || data[3] != (byte)'F')
                {
                    return null;
                }

                uint totalLength = BitConverter.ToUInt32(data, 8);
                int offset = 12;
                string json = null;
                byte[] binChunk = null;

                while (offset + 8 <= totalLength && offset + 8 <= data.Length)
                {
                    uint chunkLength = BitConverter.ToUInt32(data, offset);
                    uint chunkType = BitConverter.ToUInt32(data, offset + 4);
                    offset += 8;
                    if (offset + chunkLength > data.Length)
                    {
                        return null;
                    }

                    if (chunkType == 0x4E4F534A) // JSON
                    {
                        json = Encoding.UTF8.GetString(data, offset, (int)chunkLength).TrimEnd('\0', ' ', '\t', '\r', '\n');
                    }
                    else if (chunkType == 0x004E4942) // BIN
                    {
                        binChunk = new byte[chunkLength];
                        Buffer.BlockCopy(data, offset, binChunk, 0, (int)chunkLength);
                    }

                    offset += (int)chunkLength;
                }

                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                int imagesIdx = json.IndexOf("\"images\"", StringComparison.Ordinal);
                if (imagesIdx < 0)
                {
                    return null;
                }

                int bufferViewIdx = json.IndexOf("\"bufferView\"", imagesIdx, StringComparison.Ordinal);
                if (bufferViewIdx < 0 || binChunk == null)
                {
                    return null;
                }

                int colon = json.IndexOf(':', bufferViewIdx);
                if (colon < 0)
                {
                    return null;
                }

                int cursor = colon + 1;
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
                {
                    cursor++;
                }

                int end = cursor;
                while (end < json.Length && char.IsDigit(json[end]))
                {
                    end++;
                }

                if (end <= cursor)
                {
                    return null;
                }

                int imageBufferView = int.Parse(json.Substring(cursor, end - cursor));
                string marker = "\"bufferViews\"";
                int bufferViewsIdx = json.IndexOf(marker, StringComparison.Ordinal);
                if (bufferViewsIdx < 0)
                {
                    return null;
                }

                int arrayStart = json.IndexOf('[', bufferViewsIdx);
                int arrayEnd = json.IndexOf(']', arrayStart + 1);
                if (arrayStart < 0 || arrayEnd < 0)
                {
                    return null;
                }

                string bufferViewsSlice = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                string[] entries = bufferViewsSlice.Split(new[] { "},{" }, StringSplitOptions.None);
                if (imageBufferView < 0 || imageBufferView >= entries.Length)
                {
                    return null;
                }

                string entry = entries[imageBufferView];
                int byteOffset = ParseJsonInt(entry, "\"byteOffset\"", 0);
                int byteLength = ParseJsonInt(entry, "\"byteLength\"", -1);
                if (byteLength <= 0 || byteOffset < 0 || byteOffset + byteLength > binChunk.Length)
                {
                    return null;
                }

                var imageBytes = new byte[byteLength];
                Buffer.BlockCopy(binChunk, byteOffset, imageBytes, 0, byteLength);
                return imageBytes;
            }
            catch
            {
                return null;
            }
        }

        private static int ParseJsonInt(string source, string key, int defaultValue)
        {
            int keyIdx = source.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx < 0)
            {
                return defaultValue;
            }

            int colon = source.IndexOf(':', keyIdx);
            if (colon < 0)
            {
                return defaultValue;
            }

            int cursor = colon + 1;
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
            }

            int end = cursor;
            while (end < source.Length && (char.IsDigit(source[end]) || source[end] == '-'))
            {
                end++;
            }

            if (end <= cursor)
            {
                return defaultValue;
            }

            return int.TryParse(source.Substring(cursor, end - cursor), out int result) ? result : defaultValue;
        }

        private void ApplyCommandLineArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("-glbInput", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _inputDirectory = args[++i].Replace("\\", "/");
                }
                else if (arg.Equals("-glbOutput", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _outputFolder = args[++i].Replace("\\", "/");
                }
                else if (arg.Equals("-glbOverwrite", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (bool.TryParse(args[++i], out bool parsed))
                    {
                        _overwriteExisting = parsed;
                    }
                }
            }
        }
    }
}
#endif
