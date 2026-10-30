using UnityEditor;
using UnityEngine;

namespace Noskang.SkillGraph.Editor
{
    // Edit › Preferences › SkillGraph Kit: Python executable, settings file and editor language (stored per user in EditorPrefs).
    // Command-line options and environment variables still win (see SkillGraphPaths).
    internal static class SkillGraphPreferences
    {
        [SettingsProvider]
        public static SettingsProvider Create()=>new SettingsProvider("Preferences/SkillGraph Kit",SettingsScope.User)
        {
            label="SkillGraph Kit",
            keywords=new[]{"skill","graph","python","openpyxl"},
            guiHandler=_=>
            {
                EditorGUILayout.HelpBox("Python 3.10+ with openpyxl runs the editor backend. Nothing is installed automatically.\nPython 3.10 이상과 openpyxl이 편집기 백엔드를 돌립니다. 자동 설치하지 않습니다.",MessageType.Info);
                Field("Python","Python executable (python.exe)",SkillGraphPaths.PythonPref,"exe");
                Field("Settings","Settings file (skillgraph.json)",SkillGraphPaths.ConfigPref,"json");
                EditorGUILayout.LabelField("In use",SkillGraphPaths.PythonPath+"\n"+SkillGraphPaths.ConfigPath,EditorStyles.wordWrappedMiniLabel);
                int lang=EditorGUILayout.Popup("Editor language",SkillGraphText.Language=="ko"?1:0,new[]{"English","한국어"});
                SkillGraphText.SetLanguage(lang==1?"ko":"en");
                if(GUILayout.Button("Restart backend",GUILayout.Width(160)))SkillGraphBackend.CloseShared();
            },
        };

        private static void Field(string label,string title,string pref,string extension)
        {
            EditorGUILayout.BeginHorizontal();
            string value=EditorGUILayout.TextField(label,EditorPrefs.GetString(pref,""));
            if(GUILayout.Button("…",GUILayout.Width(28)))
            {
                string picked=EditorUtility.OpenFilePanel(title,"",extension);
                if(picked.Length>0)value=picked;
            }
            EditorGUILayout.EndHorizontal();
            if(value!=EditorPrefs.GetString(pref,"")){EditorPrefs.SetString(pref,value);SkillGraphBackend.CloseShared();}
        }
    }
}
