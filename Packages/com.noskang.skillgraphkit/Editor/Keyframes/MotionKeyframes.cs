using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Noskang.SkillGraph.Editor
{
    // Motion keyframes: animation events with one function name (settings: keyframes.function) whose string is the keyframe name.
    // Timed conditions in skill data may refer to them ('@node:Name'); the Python tools turn them into seconds.
    // Names are ASCII only (letter first, then letters, digits, underscores). Automatic names are Start and Pose1, Pose2, ...
    public static class MotionKeyframes
    {
        public const string Start="Start",PosePrefix="Pose";
        public static readonly Regex NameRule=new Regex("^[A-Za-z][A-Za-z0-9_]*$");
        public static bool ValidName(string name)=>name!=null&&NameRule.IsMatch(name);
        private static string function;
        // Function name from the settings file, else the package default.
        public static string Function
        {
            get
            {
                if(function!=null)return function;
                function=SkillKeyframeReceiver.DefaultFunction;
                try
                {
                    var path=SkillGraphPaths.ConfigPath;
                    if(File.Exists(path)&&JsonNode.Parse(File.ReadAllBytes(path)).Get("keyframes")?.Get("function") is JsonNode f)function=f.AsText();
                }
                catch(Exception){}
                return function;
            }
            set=>function=value;
        }

        public readonly struct Key
        {
            public readonly string Name;public readonly float Time;public readonly int Frame;
            public Key(string name,float time,float rate){Name=name;Time=time;Frame=Mathf.RoundToInt(time*rate);}
        }
        public static List<Key> Read(AnimationClip clip)=>AnimationUtility.GetAnimationEvents(clip).Where(e=>e.functionName==Function).OrderBy(e=>e.time).Select(e=>new Key(e.stringParameter,e.time,clip.frameRate)).ToList();

        // Times where the pose changes: a curve key whose value differs from the previous key. 0 is always Start.
        public static List<float> PoseTimes(IEnumerable<AnimationClip> clips)
        {
            var list=clips.Where(c=>c!=null).ToList();float rate=list.Count>0?list[0].frameRate:60;
            var frames=new SortedSet<int>{0};
            foreach(var clip in list)
                foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var keys=AnimationUtility.GetEditorCurve(clip,binding)?.keys;if(keys==null)continue;
                    for(int i=1;i<keys.Length;i++)if(Mathf.Abs(keys[i].value-keys[i-1].value)>1e-5f)frames.Add(Mathf.RoundToInt(keys[i].time*rate));
                }
            return frames.Select(f=>f/rate).ToList();
        }
        public static AnimationEvent[] Auto(IReadOnlyList<float> times)
        {
            var events=new List<AnimationEvent>();int n=0;
            foreach(float t in times.OrderBy(x=>x))
                events.Add(new AnimationEvent{time=t,functionName=Function,stringParameter=t<=1e-6f?Start:PosePrefix+(++n),messageOptions=SendMessageOptions.DontRequireReceiver});
            return events.ToArray();
        }
        // Clips of one motion (e.g. body parts) share the same keyframes. Clips that already have keyframes are left alone
        // (names and positions an animator or designer changed are kept).
        public static int AssignMissing(IReadOnlyCollection<AnimationClip> motion)
        {
            var missing=motion.Where(c=>c!=null&&Read(c).Count==0).ToList();if(missing.Count==0)return 0;
            var times=PoseTimes(motion);
            foreach(var clip in missing)
            {
                var others=AnimationUtility.GetAnimationEvents(clip).Where(e=>e.functionName!=Function);
                AnimationUtility.SetAnimationEvents(clip,others.Concat(Auto(times)).OrderBy(e=>e.time).ToArray());EditorUtility.SetDirty(clip);
            }
            return missing.Count;
        }
        // When a generator rebuilds a clip (CopySerialized), keep its keyframe events.
        public static void ReplaceKeepingKeyframes(AnimationClip replacement,AnimationClip clip)
        {
            var kept=AnimationUtility.GetAnimationEvents(clip).Where(e=>e.functionName==Function).ToArray();
            EditorUtility.CopySerialized(replacement,clip);
            if(kept.Length>0)AnimationUtility.SetAnimationEvents(clip,AnimationUtility.GetAnimationEvents(clip).Where(e=>e.functionName!=Function).Concat(kept).OrderBy(e=>e.time).ToArray());
            EditorUtility.SetDirty(clip);
        }

        [MenuItem("Tools/SkillGraph Kit/Add Missing Keyframes to Selected Clips",priority=40)]
        public static void AssignSelected()
        {
            var clips=Selection.GetFiltered<AnimationClip>(SelectionMode.Assets);
            int changed=AssignMissing(clips);AssetDatabase.SaveAssets();
            var bad=clips.SelectMany(c=>Read(c).Where(k=>!ValidName(k.Name)).Select(k=>c.name+"/"+k.Name)).ToList();
            Debug.Log("[SkillGraph keyframes] clips given keyframes: "+changed+(bad.Count>0?", names outside the ASCII rule: "+string.Join(", ",bad.Take(10)):""));
        }
        [MenuItem("Tools/SkillGraph Kit/Add Missing Keyframes to Selected Clips",true)]
        private static bool AssignSelectedValid()=>Selection.GetFiltered<AnimationClip>(SelectionMode.Assets).Length>0;
    }
}
