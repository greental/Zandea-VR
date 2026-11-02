using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Zandea.FriendlyNPC.Editor
{
    [InitializeOnLoad]
    public static class TradeCatalogEditorRequest
    {
        const string Folder = "Temp/TradeCatalogV2";
        static TradeCatalogEditorRequest() => EditorApplication.update += Tick;
        static void Tick()
        {
            string request = Folder + "/request.txt";
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                if (command == "build") TradeCatalogV2Builder.Build();
                else if (command == "validate") TradeCatalogV2Validation.Run();
                else if (command == "validate-main")
                {
                    Zandea.Missions.Editor.DaranMissionValidator.ValidateOpenMain();
                    Zandea.Pueblo.Editor.PuebloMainPlayValidation.Begin();
                }
                else throw new InvalidOperationException(command);
                File.WriteAllText(Folder + "/result.txt", command == "validate-main" ? "STARTED" : "OK");
            }
            catch (Exception error)
            {
                File.WriteAllText(Folder + "/result.txt", "FAIL " + error);
                Debug.LogException(error);
            }
        }
    }
}
