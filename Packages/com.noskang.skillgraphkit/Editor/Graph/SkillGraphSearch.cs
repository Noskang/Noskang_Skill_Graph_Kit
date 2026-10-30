using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Add-node search window: saved combos first, then family folders -> modules -> kind values.
    // Only modules allowed on the clicked column's event and line are listed; unimplemented ones are marked and refused.
    public sealed class SkillGraphSearch : ScriptableObject,ISearchWindowProvider
    {
        private SkillGraphEditorWindow window;private SkillGraphLayout.Box box;private SkillGraphLayout.Column column;private bool presentation;
        public void Init(SkillGraphEditorWindow window,SkillGraphLayout.Box box,SkillGraphLayout.Column column,bool presentation){this.window=window;this.box=box;this.column=column;this.presentation=presentation;}

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var model=window.Model;var tree=new List<SearchTreeEntry>{new SearchTreeGroupEntry(new GUIContent(F(presentation?"search.titlePresentation":"search.title",column.Label)),0)};
            if(model==null)return tree;
            if(model.Combos.Count>0&&!presentation)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent(T("search.combos")),1));
                foreach(var c in model.Combos)
                    tree.Add(new SearchTreeEntry(new GUIContent(F("search.combo",c.Name,c.Nodes)+(c.Desc.Length>0?" — "+c.Desc:""))){level=2,userData=("combo",c.Name,(string)null,(string)null)});
            }
            var families=model.Families.Values.ToList();
            if(model.Modules.Values.Any(m=>!model.Families.ContainsKey(m.Family)))families.Add(new SkillGraphModel.Family{Name="",Label=new LocalText("Other","기타"),Icon="•"});
            foreach(var fam in families)
            {
                var modules=model.Modules.Values.Where(m=>(model.Families.ContainsKey(m.Family)?m.Family:"")==fam.Name&&m.Presentation==presentation&&Allowed(model,m)).ToList();
                if(modules.Count==0)continue;
                tree.Add(new SearchTreeGroupEntry(new GUIContent(fam.Icon+" "+fam.Label.Get(Language)),1));
                foreach(var m in modules)
                {
                    string title=model.ModuleLabel(m.Name);
                    string label=title+(title!=m.Name?" "+m.Name:"")+(m.Implemented?"":" "+T("search.unimplemented"));
                    var kinds=m.KindSlot==null?null:m.KindRules.SelectMany(r=>r.Values).Distinct().ToList();
                    var info=m.KindSlot==null?null:m.Slot(m.KindSlot);
                    if(kinds==null||kinds.Count==0||info==null){tree.Add(new SearchTreeEntry(new GUIContent(label)){level=2,userData=("module",m.Name,(string)null,(string)null)});continue;}
                    tree.Add(new SearchTreeGroupEntry(new GUIContent(label),2));
                    foreach(var k in kinds)
                    {
                        bool supported=info.Supported==null||info.Supported.Contains(k);var rule=m.KindRules.First(r=>r.Values.Contains(k));
                        tree.Add(new SearchTreeEntry(new GUIContent(title+" ▸ "+model.EnumLabel(info.Type,k)+(supported&&m.Implemented?"":" "+T("search.unsupported"))+(rule.Verified?"":" "+T("search.guess")))){level=3,userData=("module",m.Name,m.KindSlot,k)});
                    }
                }
            }
            return tree;
        }
        private bool Allowed(SkillGraphModel model,SkillGraphModel.ModuleInfo m)
        {
            if(!m.Events.Contains(column.Event))return false;
            if(model.KindEvents.TryGetValue(box.Kind,out var ev)&&!ev.Contains(column.Event))return false;
            if(presentation&&!model.PresentationEvents.Contains(column.Event))return false;
            return true;
        }
        public bool OnSelectEntry(SearchTreeEntry entry,SearchWindowContext context)
        {
            var (what,name,kindSlot,kind)=((string,string,string,string))entry.userData;
            var model=window.Model;
            if(what=="combo")
            {
                var op=SkillGraphJson.Op("insertCombo",("bundle",Target()),("combo",name),("event",column.Event));
                if(column.EventValue!=null&&!column.EventValue.IsNull)op["eventValue"]=column.EventValue;if(column.From!=null)op["fromNode"]=column.From;
                return window.Apply(op);
            }
            var m=model.Module(name);
            if(!m.Implemented){EditorUtility.DisplayDialog(T("search.addNode"),F("search.unimplementedDialog",model.ModuleLabel(m.Name),m.Name),T("ok"));return false;}
            if(kind!=null)
            {
                var info=m.Slot(kindSlot);
                if(info.Supported!=null&&!info.Supported.Contains(kind))
                {EditorUtility.DisplayDialog(T("search.addNode"),F("search.unsupportedDialog",model.ModuleLabel(m.Name)+" ▸ "+model.EnumLabel(info.Type,kind),string.Join(", ",info.Supported)),T("ok"));return false;}
            }
            var links=new List<object>();if(kind!=null)links.Add(new Dictionary<string,object>{{"slot",kindSlot},{"source","Fixed"},{"value",kind}});
            var add=SkillGraphJson.Op("addNode",("bundle",Target()),("line",presentation?"Presentation":"Logic"),("event",column.Event),("module",name),("links",links));
            if(column.EventValue!=null&&!column.EventValue.IsNull)add["eventValue"]=column.EventValue;if(column.From!=null)add["fromNode"]=column.From;
            return window.Apply(add);
        }
        // The owner's own bundle is written on the chosen edit layer; effect and buff bundles are written to themselves.
        private int Target(){var owner=window.Model.OwnerOf(window.Owner);return owner!=null&&box.Bundle==owner.Bundle&&owner.Chain.Contains(window.Layer)?window.Layer:box.Bundle;}
    }
}
