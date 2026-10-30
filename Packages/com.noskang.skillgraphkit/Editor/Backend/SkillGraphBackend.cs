using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Where the Python backend lives and which settings file it uses.
    // Python:   -skillgraphPython <exe>  >  $SKILLGRAPH_PYTHON  >  EditorPrefs  >  "python" on PATH
    // Settings: -skillgraphConfig <file> >  $SKILLGRAPH_CONFIG  >  EditorPrefs  >  <project>/skillgraph.json
    public static class SkillGraphPaths
    {
        public const string PythonPref="Noskang.SkillGraph.Python",ConfigPref="Noskang.SkillGraph.Config";
        public static string PythonPath=>Option("-skillgraphPython")??Environment.GetEnvironmentVariable("SKILLGRAPH_PYTHON")??Pref(PythonPref)??"python";
        public static string ConfigPath=>Path.GetFullPath(Option("-skillgraphConfig")??Environment.GetEnvironmentVariable("SKILLGRAPH_CONFIG")??Pref(ConfigPref)??"skillgraph.json");
        public static string PackageRoot
        {
            get
            {
                var info=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SkillGraphPaths).Assembly);
                return info!=null?info.resolvedPath:Path.GetFullPath("Packages/com.noskang.skillgraphkit");
            }
        }
        public static string PythonFolder=>Path.Combine(PackageRoot,"Tools~","python");
        public static string Option(string name)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]==name)return args[i+1];
            return null;
        }
        private static string Pref(string key){var v=EditorPrefs.GetString(key,"");return string.IsNullOrEmpty(v)?null:v;}
    }

    // Connection to the Python editor backend ("python -m skillgraph serve"): one JSON request per line, one JSON response per line.
    // The process stays up while the editor runs and closes on script reload or quit.
    public sealed class SkillGraphBackend : IDisposable
    {
        private static SkillGraphBackend shared;
        private Process process;private int next;private readonly StringBuilder errors=new StringBuilder();

        public static SkillGraphBackend Shared
        {
            get
            {
                if(shared==null)
                {
                    shared=new SkillGraphBackend();
                    AssemblyReloadEvents.beforeAssemblyReload+=CloseShared;EditorApplication.quitting+=CloseShared;
                }
                return shared;
            }
        }
        public static void CloseShared(){shared?.Dispose();shared=null;AssemblyReloadEvents.beforeAssemblyReload-=CloseShared;EditorApplication.quitting-=CloseShared;}

        private void Start()
        {
            if(process!=null&&!process.HasExited)return;
            string config=SkillGraphPaths.ConfigPath;
            if(!File.Exists(config))throw new InvalidOperationException(F("backend.noConfig",config));
            var info=new ProcessStartInfo(SkillGraphPaths.PythonPath,"-X utf8 -B -m skillgraph serve --config \""+config+"\"")
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
             StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false),WorkingDirectory=Path.GetFullPath(".")};
            info.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"]="1";
            info.EnvironmentVariables["PYTHONPATH"]=SkillGraphPaths.PythonFolder;
            info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
            process=new Process{StartInfo=info};
            process.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};
            try{process.Start();}
            catch(Exception e){process=null;throw new InvalidOperationException(F("backend.noPython",SkillGraphPaths.PythonPath,e.Message));}
            process.BeginErrorReadLine();
        }

        // One request. A response with ok:false comes back as is (the window shows operation errors).
        public JsonNode Request(Dictionary<string,object> request,int timeoutMs=120000)
        {
            Start();
            int id=++next;request["id"]=id;
            var bytes=new UTF8Encoding(false).GetBytes(SkillGraphJson.Write(request)+"\n");
            process.StandardInput.BaseStream.Write(bytes,0,bytes.Length);process.StandardInput.BaseStream.Flush();
            var line=process.StandardOutput.ReadLineAsync();
            if(!line.Wait(timeoutMs)){Dispose();throw new InvalidOperationException(F("backend.timeout",timeoutMs/1000));}
            if(line.Result==null){string err;lock(errors)err=errors.ToString();Dispose();throw new InvalidOperationException(T("backend.ended")+"\n"+err);}
            var node=JsonNode.Parse(line.Result);
            if(node["id"].Kind!=JsonKind.Number||node["id"].AsInt()!=id)throw new InvalidOperationException(T("backend.order"));
            return node;
        }
        public void Dispose()
        {
            if(process==null)return;
            try{if(!process.HasExited){process.StandardInput.Close();if(!process.WaitForExit(2000))process.Kill();}}catch(Exception){}
            process.Dispose();process=null;
        }
    }

    // Requests and operations as compact JSON (dictionaries, lists, strings, numbers, booleans, null, JsonNode).
    public static class SkillGraphJson
    {
        public static string Write(object value){var b=new StringBuilder();Write(b,value);return b.ToString();}
        private static void Write(StringBuilder b,object v)
        {
            switch(v)
            {
                case null:b.Append("null");return;
                case string s:JsonWriter.Quote(b,s);return;
                case bool f:b.Append(f?"true":"false");return;
                case int i:b.Append(i.ToString(CultureInfo.InvariantCulture));return;
                case long l:b.Append(l.ToString(CultureInfo.InvariantCulture));return;
                case float x:b.Append(((double)x).ToString("R",CultureInfo.InvariantCulture));return;
                case double d:b.Append(d==Math.Floor(d)&&Math.Abs(d)<1e15?((long)d).ToString(CultureInfo.InvariantCulture):d.ToString("R",CultureInfo.InvariantCulture));return;
                case JsonNode n:Node(b,n);return;
                case IDictionary dict:
                    b.Append('{');bool first=true;
                    foreach(DictionaryEntry e in dict){if(!first)b.Append(',');first=false;JsonWriter.Quote(b,(string)e.Key);b.Append(':');Write(b,e.Value);}
                    b.Append('}');return;
                case IEnumerable list:
                    b.Append('[');bool one=true;foreach(var x in list){if(!one)b.Append(',');one=false;Write(b,x);}b.Append(']');return;
                default:throw new ArgumentException("Cannot write as JSON: "+v.GetType());
            }
        }
        private static void Node(StringBuilder b,JsonNode n)
        {
            switch(n.Kind)
            {
                case JsonKind.Null:b.Append("null");break;
                case JsonKind.Bool:b.Append(n.Bool?"true":"false");break;
                case JsonKind.Number:Write(b,n.Number);break;
                case JsonKind.String:JsonWriter.Quote(b,n.Text);break;
                case JsonKind.Array:b.Append('[');for(int i=0;i<n.Items.Count;i++){if(i>0)b.Append(',');Node(b,n.Items[i]);}b.Append(']');break;
                default:b.Append('{');bool first=true;foreach(var key in n.Keys){if(!first)b.Append(',');first=false;JsonWriter.Quote(b,key);b.Append(':');Node(b,n.Fields[key]);}b.Append('}');break;
            }
        }
        // One operation: Op("setValue",("values",2300201),("name","Bolt.Damage"),("value",26))
        public static Dictionary<string,object> Op(string op,params (string Key,object Value)[] fields)
        {var d=new Dictionary<string,object>{{"op",op}};foreach(var (k,v) in fields)d[k]=v;return d;}
    }
}
