using System;
using Noskang.SkillGraph.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkillGraphDemo.Editor
{
    // Builds the demo scene and re-exports skill data.
    //   Unity.exe -batchmode -projectPath DemoProject -executeMethod SkillGraphDemo.Editor.DemoSetup.Batch
    public static class DemoSetup
    {
        public const string ScenePath="Assets/SkillGraphDemo/Demo.unity";

        [MenuItem("Tools/SkillGraph Kit/Demo/Rebuild Demo Scene",priority=100)]
        public static void BuildScene()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
            var cam=Camera.main;
            cam.transform.position=new Vector3(5,9,-9);cam.transform.rotation=Quaternion.Euler(42,0,0);
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.11f,.12f,.14f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Ground";ground.transform.position=new Vector3(5,0,0);ground.transform.localScale=new Vector3(2,1,1);
            new GameObject("Demo").AddComponent<DemoRunner>();
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            Debug.Log("[SkillGraph demo] scene saved: "+ScenePath);
        }

        [MenuItem("Tools/SkillGraph Kit/Demo/Export Skill Data to StreamingAssets",priority=101)]
        public static void ExportData()
        {
            var result=SkillGraphEditorWindow.RunPython("export --out \""+System.IO.Path.GetFullPath("Assets/StreamingAssets/SkillData")+"\"");
            AssetDatabase.Refresh();Debug.Log("[SkillGraph demo] "+result);
        }

        public static void Batch()
        {
            try
            {
                var (_,_,problems)=ModuleDictionaryExport.Export();
                if(problems.Length>0)throw new InvalidOperationException(string.Join("\n",problems));
                BuildScene();AssetDatabase.SaveAssets();EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogError("[SkillGraph demo] "+e);EditorApplication.Exit(3);}
        }
    }
}
