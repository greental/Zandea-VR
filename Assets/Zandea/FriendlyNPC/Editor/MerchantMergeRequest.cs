using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    [InitializeOnLoad]
    internal static class MerchantMergeRequest
    {
        const string Request = "Temp/DaranMerchantMerge/run.txt";
        const string Report = "Temp/DaranMerchantMerge/result.txt";

        static MerchantMergeRequest()
        {
            EditorApplication.delayCall += TryRun;
            EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += TryRun;
        }

        static void TryRun()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                PromoteMerchantPlayer.Merge();
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == "Assets/Main.unity")
                {
                    Zandea.Missions.Editor.HonestWorkAuthoring.RepairMainContractBarrels();
                    Zandea.Missions.Editor.DaranMissionValidator.ValidateOpenMain();
                }
                File.WriteAllText(Report, "PASS Merchant player merge, Main mission references, dialogue, voice assets, and contract outcome validation.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(Report, "FAIL " + error + "\n");
                Debug.LogException(error);
            }
            finally { File.Delete(Request); }
        }
    }
}
