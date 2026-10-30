using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Column (event condition) editing: column head context menu = change / remove / add, and '+ column' on the box title.
    // A column comes from its nodes' event, time and source node; an empty column lives in the aux file until a node moves in.
    public sealed partial class SkillGraphEditorWindow
    {
        // The owner's own bundle is written on the edit layer; effect and buff bundles on themselves.
        private int ColumnTarget(SkillGraphLayout.Box box)
        {
            var owner=model?.OwnerOf(s.owner);
            return box.Main&&owner!=null&&owner.Chain.Contains(s.layer)?s.layer:box.Bundle;
        }
        // Columns a bundle kind always shows (removing one only removes its nodes).
        public bool BaseColumn(string kind,string key)=>model!=null&&model.BaseColumns(kind).Contains(key);

        private void OnColumnMenu(SkillGraphLayout.Box box,SkillGraphLayout.Column col,DropdownMenu menu)
        {
            int target=ColumnTarget(box);bool baseCol=BaseColumn(box.Kind,col.Key);var nodes=ColumnNodes(model,target,col.Key);
            menu.AppendAction(T("column.change"),_=>ColumnDialog.Show(this,box,col,target),nodes.Count>0||col.Empty?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
            menu.AppendAction(baseCol?T("column.clear"):T("column.remove"),_=>RemoveColumn(box,col,target),baseCol&&nodes.Count==0?DropdownMenuAction.Status.Disabled:DropdownMenuAction.Status.Normal);
            menu.AppendSeparator();
            menu.AppendAction(T("column.add"),_=>ColumnDialog.Show(this,box,null,target));
            menu.AppendAction(T("node.addHere"),_=>OnAdd(box,col,Vector2.zero));
        }
        private void OnAddColumn(SkillGraphLayout.Box box)=>ColumnDialog.Show(this,box,null,ColumnTarget(box));

        // Nodes of the column (logic and presentation) plus the nodes of exit columns they raise (same scope as the backend removeColumn).
        public static List<SkillGraphModel.Node> ColumnNodes(SkillGraphModel m,int bundle,string key)
        {
            var outList=new List<SkillGraphModel.Node>();if(m==null||!m.Bundles.TryGetValue(bundle,out var b))return outList;
            var todo=new Queue<string>();todo.Enqueue(key);var seen=new HashSet<string>();
            while(todo.Count>0)
            {
                string k=todo.Dequeue();if(!seen.Add(k))continue;
                foreach(var n in b.Nodes.Where(n=>SkillGraphLayout.ColumnKey(m,n.Event,n.EventValue,n.FromNode)==k).OrderBy(n=>n.Line).ThenBy(n=>n.Order))
                {outList.Add(n);foreach(var e in m.ExitEvents)todo.Enqueue(e+":"+n.Id);}
            }
            return outList;
        }
        private void RemoveColumn(SkillGraphLayout.Box box,SkillGraphLayout.Column col,int target)
        {
            var nodes=ColumnNodes(model,target,col.Key);bool baseCol=BaseColumn(box.Kind,col.Key);
            string list=string.Join("\n",nodes.Take(20).Select(n=>"· "+(n.Presentation?T("line.presentation"):T("line.logic"))+" "+model.ModuleLabel(n.Module)+" #"+n.Id+(n.DefinedIn!=target?"  "+T("column.inheritedMark"):"")))+(nodes.Count>20?"\n…":"");
            string text=(baseCol?T("column.baseNote")+"\n\n":"")+(nodes.Count==0?F("column.removeEmpty",target,col.Label)
                :F("column.removeNodes",target,col.Label,nodes.Count,nodes.Count(n=>!n.Presentation),nodes.Count(n=>n.Presentation))+"\n\n"+list)+"\n\n"+T("column.undoHint");
            if(!EditorUtility.DisplayDialog(baseCol?T("column.clearTitle"):T("column.removeTitle"),text,baseCol?T("column.clearButton"):T("column.removeButton"),T("cancel")))return;
            var op=SkillGraphJson.Op("removeColumn",("bundle",target),("event",col.Event));
            if(col.EventValue!=null&&!col.EventValue.IsNull)op["eventValue"]=col.EventValue;if(col.From!=null)op["fromNode"]=col.From;
            if(Apply(op))SetStatus(F("column.removed",col.Label,nodes.Count),false);
        }
        internal void ApplyColumn(SkillGraphLayout.Box box,SkillGraphLayout.Column from,int target,string ev,object value,int? fromNode)
        {
            Dictionary<string,object> op;
            if(from==null)op=SkillGraphJson.Op("addColumn",("bundle",target),("event",ev));
            else
            {
                op=SkillGraphJson.Op("setColumn",("bundle",target),("event",from.Event),("toEvent",ev));
                if(from.EventValue!=null&&!from.EventValue.IsNull)op["eventValue"]=from.EventValue;if(from.From!=null)op["fromNode"]=from.From;
            }
            string vk=from==null?"eventValue":"toEventValue",fk=from==null?"fromNode":"toFromNode";
            if(value!=null)op[vk]=value;if(fromNode!=null)op[fk]=fromNode;
            if(Apply(op))SetStatus(from==null?T("column.added"):T("column.changed"),false);
        }
        // Time text: a number is seconds, anything else is a value name (or a keyframe reference).
        public static object ParseColumnValue(string text,string unit="s")
        {
            text=(text??"").Trim();if(text.Length==0)return null;
            string number=unit.Length>0&&text.EndsWith(unit,StringComparison.Ordinal)?text.Substring(0,text.Length-unit.Length).Trim():text;
            return double.TryParse(number,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?(object)d:text;
        }
        internal IEnumerable<string> ValueNames()
        {
            var owner=model?.OwnerOf(s.owner);if(owner==null)return Enumerable.Empty<string>();
            var chains=new List<int>(owner.ValueChain);foreach(int buff in owner.Buffs)if(model.OwnerOf(buff) is {} bo)chains.AddRange(bo.ValueChain);
            return chains.Where(k=>model.Values.ContainsKey(k)).SelectMany(k=>model.Values[k].Entries.Select(e=>e.Name)).Distinct().OrderBy(x=>x,StringComparer.Ordinal);
        }
    }

    // Add / change column window: event (only those the bundle kind allows), time (number or value name, or a keyframe), source node for exits.
    public sealed class ColumnDialog : EditorWindow
    {
        private SkillGraphEditorWindow owner;private SkillGraphLayout.Box box;private SkillGraphLayout.Column from;private int target;
        private string[] events;private int eventIndex;private string valueText="";private int fromIndex;private (int Id,string Label)[] sources;private string[] valueNames;private int valuePick;
        // Animation keyframes of this bundle: label '{keyframe} : {seconds}', value '@{motion node}:{name}'.
        private (string Value,string Label)[] keyframes;private int keyPick;private string[] keyframeNotes;private double keyOffset;
        // For the capture command: the last opened window and its field / button rectangles.
        internal static ColumnDialog Last;internal readonly Dictionary<string,Rect> CaptureRects=new Dictionary<string,Rect>();
        // The capture command preselects event, time (seconds, value name or '@node:Name[+offset]') and source node; nothing is applied.
        internal void SetCapture(string ev,string value,int? fromNode=null)
        {
            if(ev!=null){int i=Array.IndexOf(events,ev);if(i>=0)eventIndex=i;}
            if(fromNode!=null){int i=Array.FindIndex(sources,x=>x.Id==fromNode);if(i>=0)fromIndex=i;}
            if(value!=null)
            {
                valueText=value;int k=Array.FindIndex(keyframes,x=>x.Value.Length>0&&value.StartsWith(x.Value,StringComparison.Ordinal));keyPick=Math.Max(0,k);
                keyOffset=k>=0&&double.TryParse(value.Substring(keyframes[k].Value.Length),NumberStyles.Float,CultureInfo.InvariantCulture,out double ko)?ko:0;
                int v=Array.IndexOf(valueNames,value);valuePick=Math.Max(0,v);
            }
        }
        public static void Show(SkillGraphEditorWindow owner,SkillGraphLayout.Box box,SkillGraphLayout.Column from,int target)
        {
            var w=CreateInstance<ColumnDialog>();w.owner=owner;w.box=box;w.from=from;w.target=target;
            var m=owner.Model;var b=m.Bundles[target];
            w.events=m.KindEvents.TryGetValue(b.Kind,out var ev)?ev:m.Events.ToArray();
            string first=w.events.FirstOrDefault(m.IsTimed)??w.events.FirstOrDefault();
            w.eventIndex=Math.Max(0,Array.IndexOf(w.events,from?.Event??first));
            w.valueText=from!=null&&from.EventValue!=null&&!from.EventValue.IsNull?SkillGraphModel.Text(from.EventValue):"";
            // '{module}#{id}' without a space: Unity popups read ' #' as a shortcut marker.
            w.sources=b.Nodes.Where(n=>(m.Module(n.Module)?.Exits?.Length??0)>0).Select(n=>(n.Id,m.ModuleLabel(n.Module)+"#"+n.Id+" ("+F("node.order",n.Order)+")")).ToArray();
            w.fromIndex=Math.Max(0,Array.FindIndex(w.sources,x=>x.Id==from?.From));
            w.valueNames=new[]{T("columnDialog.pickValue")}.Concat(owner.ValueNames()).ToArray();
            w.keyframes=KeyframeOptions(m,target);w.keyframeNotes=b.Keyframes.Where(k=>k.Error!=null).Select(k=>F("columnDialog.motionNote",k.Node,k.Error)).ToArray();
            if(m.TryKeyframe(target,w.valueText,out var pm,out var pk,out double poff)){w.keyOffset=poff;w.keyPick=Math.Max(0,Array.FindIndex(w.keyframes,x=>x.Value=="@"+pm.Node+":"+pk.Name));}
            w.titleContent=new GUIContent(from==null?T("columnDialog.addTitle"):T("columnDialog.changeTitle"));
            var main=EditorGUIUtility.GetMainWindowPosition();w.position=new Rect(main.center.x-260,main.center.y-150,560,300);w.ShowUtility();Last=w;
        }
        private void Mark(string name){if(Event.current.type==EventType.Repaint)CaptureRects[name]=GUILayoutUtility.GetLastRect();}
        public static (string Value,string Label)[] KeyframeOptions(SkillGraphModel m,int bundle)
        {
            var list=new List<(string,string)>{("",T("columnDialog.pickKeyframe"))};
            if(m.Bundles.TryGetValue(bundle,out var b))
                foreach(var mk in b.Keyframes.Where(k=>k.Error==null))
                    foreach(var k in mk.Keys)
                        list.Add(("@"+mk.Node+":"+k.Name,m.KeyframeLabel(k.Name,k.Seconds)+"   — "+F("column.keyframe",mk.Clip,k.Frame).TrimStart('←',' ')+(mk.Start>0?", "+F("column.motionStart",m.Seconds(mk.Start)):"")+" (#"+mk.Node+")"));
            return list.ToArray();
        }
        private void OnGUI()
        {
            if(owner==null||owner.Model==null){Close();return;}
            var m=owner.Model;
            EditorGUILayout.LabelField(from==null?F("columnDialog.addText",target):F("columnDialog.changeText",target,from.Label),EditorStyles.wordWrappedLabel);
            eventIndex=EditorGUILayout.Popup(T("columnDialog.event"),eventIndex,events.Select(e=>m.EventLabel(e)+"  ("+e+")").ToArray());Mark("event");
            string ev=events[eventIndex];
            bool timed=m.IsTimed(ev);bool sub=m.IsSubscription(ev);bool exit=m.IsExit(ev);
            if(timed||sub)
            {
                valueText=EditorGUILayout.TextField(timed?F("columnDialog.time",m.TimeUnit):T("columnDialog.subject"),valueText);Mark(timed?"time":"value");
                if(sub&&m.Subjects.Length>0){int pick=EditorGUILayout.Popup(T("columnDialog.subjectPick"),Math.Max(0,Array.IndexOf(m.Subjects,valueText)),m.Subjects);valueText=m.Subjects[pick];}
                if(timed){int pick=EditorGUILayout.Popup(T("columnDialog.valueName"),valuePick,valueNames);Mark("valueName");if(pick!=valuePick){valuePick=pick;if(pick>0)valueText=valueNames[pick];}}
                if(timed)
                {
                    // A keyframe turns into seconds automatically; the column head reads '{keyframe} : {seconds}'.
                    int kp=EditorGUILayout.Popup(T("columnDialog.keyframe"),keyPick,keyframes.Select(k=>k.Label).ToArray());Mark("keyframe");
                    // Offset from the keyframe (may be negative): condition time = keyframe seconds + offset.
                    double off=EditorGUILayout.DoubleField(F("columnDialog.offset",m.TimeUnit),keyOffset);Mark("offset");
                    if(kp!=keyPick||Math.Abs(off-keyOffset)>1e-12){keyPick=kp;keyOffset=off;if(kp>0)valueText=SkillGraphModel.KeyframeValue(keyframes[kp].Value,keyOffset);}
                    if(keyframes.Length<=1)EditorGUILayout.HelpBox(T("columnDialog.noMotion"),MessageType.None);
                    foreach(string note in keyframeNotes)EditorGUILayout.HelpBox(note,MessageType.None);
                    if(m.TryKeyframe(target,valueText,out var mk,out var key,out double ko))
                    {
                        EditorGUILayout.LabelField("→ "+m.KeyframeLabel(key.Name,key.Seconds,ko),F("columnDialog.math",mk.Clip,key.Frame,key.Rate.ToString("0.##",CultureInfo.InvariantCulture))
                            +(mk.Start>0?" + "+F("column.motionStart",m.Seconds(mk.Start)):"")+(Math.Abs(ko)>1e-9?" "+(ko>0?"+":"")+m.Seconds(ko):""));
                        if(key.Seconds+ko<0)EditorGUILayout.HelpBox(T("columnDialog.negative"),MessageType.Warning);
                    }
                }
            }
            if(exit)
            {
                if(sources.Length==0)EditorGUILayout.HelpBox(T("columnDialog.noExits"),MessageType.Warning);
                else{fromIndex=EditorGUILayout.Popup(T("columnDialog.source"),fromIndex,sources.Select(x=>x.Label).ToArray());Mark("source");}
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();GUILayout.FlexibleSpace();
            bool ok=!(exit&&sources.Length==0)&&!(timed&&SkillGraphEditorWindow.ParseColumnValue(valueText,m.TimeUnit)==null);
            using(new EditorGUI.DisabledScope(!ok))
            {
                if(GUILayout.Button(T("ok"),GUILayout.Width(100)))
                {
                    var o=owner;var b=box;var f=from;int t=target;object value=timed||sub?SkillGraphEditorWindow.ParseColumnValue(valueText,m.TimeUnit):null;int? src=exit?sources[fromIndex].Id:(int?)null;
                    Close();o.ApplyColumn(b,f,t,ev,value,src);GUIUtility.ExitGUI();
                }
                Mark("ok");
            }
            if(GUILayout.Button(T("cancel"),GUILayout.Width(100)))Close();Mark("cancel");
            EditorGUILayout.EndHorizontal();
        }
    }
}
