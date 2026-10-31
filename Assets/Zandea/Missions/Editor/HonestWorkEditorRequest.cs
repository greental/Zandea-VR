using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Zandea.Missions.Editor
{
    [InitializeOnLoad]
    public static class HonestWorkEditorRequest
    {
        const string Request="Temp/ZandeaHonestWork.request";
        const string Result="Temp/ZandeaHonestWork.result.txt";
        static HonestWorkEditorRequest() => EditorApplication.update += Tick;
        static void Tick()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)return;
            string command=File.ReadAllText(Request).Trim();File.Delete(Request);
            try { if(command!="configure" && command!="validate")throw new InvalidOperationException(command); var prior=UnityEngine.SceneManagement.SceneManager.GetActiveScene(); var main=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Main.unity"); bool close=!main.isLoaded; if(close)main=EditorSceneManager.OpenScene("Assets/Main.unity",UnityEditor.SceneManagement.OpenSceneMode.Additive); try { UnityEngine.SceneManagement.SceneManager.SetActiveScene(main); if(command=="configure")HonestWorkAuthoring.Configure(); else HonestWorkAuthoring.ValidateMain(); } finally { if(prior.IsValid() && prior.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prior); if(close) EditorSceneManager.CloseScene(main,true); } File.WriteAllText(Result,"OK "+command); }
            catch(Exception ex){File.WriteAllText(Result,"FAIL "+ex);Debug.LogException(ex);}
        }
    }
}
