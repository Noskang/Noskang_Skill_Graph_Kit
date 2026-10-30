using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Noskang.SkillGraph.Editor
{
    // Writes the module dictionary and display files from the project's ModuleSetProvider (the C# declarations are the source of truth).
    // Target folder: settings dataDir/folders.generated next to the settings file, unless given.
    //   Unity.exe -batchmode -projectPath <project> -executeMethod Noskang.SkillGraph.Editor.ModuleDictionaryExport.Batch [-skillgraphOut <folder>]
    public static class ModuleDictionaryExport
    {
        public static ModuleSetProvider FindProvider()
        {
            var types=TypeCache.GetTypesDerivedFrom<ModuleSetProvider>().Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).OrderBy(t=>t.FullName,StringComparer.Ordinal).ToList();
            if(types.Count==0)throw new InvalidOperationException("No ModuleSetProvider subclass in the project");
            if(types.Count>1)Debug.LogWarning("[SkillGraph] several ModuleSetProvider classes, using "+types[0].FullName);
            return (ModuleSetProvider)Activator.CreateInstance(types[0]);
        }

        public static string DefaultFolder()
        {
            string config=SkillGraphPaths.ConfigPath;string root=Path.GetDirectoryName(config);
            string data="Data",generated="Generated";
            if(File.Exists(config))
            {
                var cfg=JsonNode.Parse(File.ReadAllBytes(config));
                data=cfg.Get("dataDir")?.TextOr(data)??data;
                generated=cfg.Get("folders")?.Get("generated")?.TextOr(generated)??generated;
            }
            return Path.Combine(root,data,generated);
        }

        // Returns (dictionary path, display path or null, problems).
        public static (string Dictionary,string Display,string[] Problems) Export(string folder=null)
        {
            var provider=FindProvider();
            var set=provider.Build();
            var display=provider.BuildDisplay(set);
            var problems=set.Check().Concat(display?.Check(set)??Enumerable.Empty<string>()).ToArray();
            folder??=DefaultFolder();
            Directory.CreateDirectory(folder);
            string dict=Path.Combine(folder,"module_dictionary.json");
            File.WriteAllBytes(dict,JsonWriter.Utf8(set.ExportJson()));
            string disp=null;
            if(display!=null){disp=Path.Combine(folder,"module_display.json");File.WriteAllBytes(disp,JsonWriter.Utf8(display.ExportJson(set.Hash)));}
            return (dict,disp,problems);
        }

        [MenuItem("Tools/SkillGraph Kit/Export Module Dictionary",priority=10)]
        public static void ExportMenu()
        {
            try
            {
                var (dict,disp,problems)=Export();
                AssetDatabase.Refresh();
                Debug.Log("[SkillGraph] module dictionary: "+dict+(disp!=null?" · display: "+disp:""));
                if(problems.Length>0)Debug.LogWarning("[SkillGraph] declaration problems:\n"+string.Join("\n",problems));
            }
            catch(Exception e){Debug.LogError("[SkillGraph] export failed: "+e.Message);}
        }

        public static void Batch()
        {
            try
            {
                var (_,_,problems)=Export(SkillGraphPaths.Option("-skillgraphOut"));
                if(problems.Length>0){Debug.LogError("[SkillGraph] declaration problems:\n"+string.Join("\n",problems));EditorApplication.Exit(1);return;}
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogError("[SkillGraph] export failed: "+e);EditorApplication.Exit(3);}
        }
    }
}
