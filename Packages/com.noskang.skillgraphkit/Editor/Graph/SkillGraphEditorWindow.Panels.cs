using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Side panel: [Values] tier table, [Node] note, memo, slot editing and link suggestions, [Resources] per-skin presentation resources.
    public sealed partial class SkillGraphEditorWindow
    {
        private void BuildPanel()
        {
            if(panel==null)return;panel.Clear();if(model==null)return;
            var tabs=new VisualElement();tabs.AddToClassList("sg-tabs");
            foreach(var key in new[]{"values","node","resources"})
                tabs.Add(Tool(T("tab."+key),()=>{s.tab=key;SaveSession();BuildPanel();},T("tab."+key+"Tip"),s.tab==key));
            panel.Add(tabs);
            panelBody=new ScrollView{style={flexGrow=1}};panel.Add(panelBody);
            if(s.tab=="values")BuildValues();else if(s.tab=="node")BuildNode();else BuildResources();
        }

        // ---- values: rows = value names, columns = tiers. Inherited values grey, values a tier sets itself bold.
        // The owner's values first, then each buff it applies (with its own values).
        private void BuildValues()
        {
            var owner=model.OwnerOf(s.owner);if(owner==null)return;
            var columns=owner.Stage!=null&&owner.Stage.Family.Length>1?owner.Stage.Family.Select(k=>model.OwnerOf(k)).Where(o=>o!=null).ToList():new List<SkillGraphModel.Owner>{owner};
            var pass=PassedNames(owner);
            panelBody.Add(Cls(new Label(F("values.title",owner.Values,model.KeyName(owner.Values))),"sg-panel__title"));
            panelBody.Add(ValueTable(columns,owner,pass));
            foreach(int buffKey in owner.Buffs)
            {
                var buff=model.OwnerOf(buffKey);if(buff==null)continue;
                panelBody.Add(Cls(new Label(F("values.buffTitle",buff.Values,buff.Name)),"sg-panel__title"));
                panelBody.Add(ValueTable(new List<SkillGraphModel.Owner>{buff},buff,new Dictionary<string,string>()));
            }
            panelBody.Add(Tool(T("values.add"),()=>AddValuePrompt(owner),T("values.addTip")));
            panelBody.Add(Cls(new Label(T("values.help")+(pass.Count>0?" "+T("values.passHelp"):"")),"sg-note"));
        }
        // Values the buff-applying node passes to the buff (duration, strength, ...) -> tag.
        private Dictionary<string,string> PassedNames(SkillGraphModel.Owner owner)
        {
            var map=new Dictionary<string,string>();
            foreach(int b in owner.Bundles)if(model.Bundles.TryGetValue(b,out var bundle))
                foreach(var n in bundle.Nodes.Where(n=>n.Module==model.StatusModule))
                    foreach(var sv in n.Slots.Where(x=>x.Source=="ref"&&x.Name!=model.StatusSlot))map[SkillGraphModel.Text(sv.Value)]="→ "+sv.Name;
            return map;
        }
        private VisualElement ValueTable(List<SkillGraphModel.Owner> columns,SkillGraphModel.Owner current,Dictionary<string,string> pass)
        {
            var table=new VisualElement();
            var head=Cls(new VisualElement(),"sg-vtable__row","sg-vtable__row--head");head.Add(Cls(new Label(T("values.name")),"sg-vtable__name"));
            foreach(var o in columns)
            {
                string label=columns.Count>1?F("tier.n",Array.IndexOf(o.Stage.Family,o.Key)+1):T("values.value");
                var h=Cls(new Label((o.Key==current.Key&&columns.Count>1?"● ":"")+label),"sg-vtable__head");if(o.Key==current.Key)h.AddToClassList("sg-vtable__head--current");
                var key=o.Key;h.RegisterCallback<MouseDownEvent>(_=>SelectOwner(key));h.tooltip=F("values.headTip",o.Name);head.Add(h);
            }
            table.Add(head);
            var names=columns.SelectMany(o=>model.Values.TryGetValue(o.Values,out var v)?v.Entries.Select(e=>e.Name):Enumerable.Empty<string>()).Distinct().OrderBy(n=>n,StringComparer.Ordinal).ToList();
            foreach(var name in names)
            {
                // One line of name and tier values, then the description line. Hovering marks every use in the graph.
                var item=Cls(new VisualElement(),"sg-vtable__item");if(name==markedValue)item.AddToClassList("sg-vtable__item--marked");
                var row=Cls(new VisualElement(),"sg-vtable__row");item.Add(row);var hovered=name;
                item.RegisterCallback<MouseEnterEvent>(_=>HoverValue(hovered));item.RegisterCallback<MouseLeaveEvent>(_=>HoverValue(null));
                var nl=Cls(new Label(name),"sg-vtable__name");var nm=name;nl.RegisterCallback<MouseDownEvent>(_=>{MarkValue(nm);BuildPanel();});
                var entry0=model.ResolveValue(current.Values,name);nl.tooltip=(entry0?.Desc??"")+(pass.TryGetValue(name,out var tag)?"\n"+tag:"");row.Add(nl);
                foreach(var o in columns)
                {
                    var e=model.ResolveValue(o.Values,name);
                    var cell=Cls(new Label(e==null?"—":SkillGraphModel.Text(e.Value)),"sg-vtable__cell");
                    if(e!=null&&e.DefinedIn==o.Values)cell.AddToClassList("sg-vtable__cell--own");if(o.Key==current.Key&&columns.Count>1)cell.AddToClassList("sg-vtable__cell--current");
                    var owner=o;cell.tooltip=e==null?T("values.missing"):F("values.cellTip",e.DefinedIn);
                    cell.RegisterCallback<MouseDownEvent>(ev=>
                    {
                        if(ev.button==1){ValueMenu(owner,name,e);return;}
                        EditValuePrompt(owner,name);
                    });
                    row.Add(cell);
                }
                if(pass.ContainsKey(name))row.Add(Cls(new Label(T("values.passed")),"sg-vtable__tag"));
                if(!string.IsNullOrEmpty(entry0?.Desc))item.Add(Cls(new Label(entry0.Desc),"sg-vtable__desc"));
                table.Add(item);
            }
            return table;
        }
        private void ValueMenu(SkillGraphModel.Owner owner,string name,SkillGraphModel.ValueEntry e)
        {
            var menu=new GenericMenu();
            ValueMenuItems(owner,name,e,(label,act,on)=>{if(on)menu.AddItem(new GUIContent(label),false,()=>act());else menu.AddDisabledItem(new GUIContent(label));});
            menu.ShowAsContext();
        }
        // Value cell context menu items (the capture command draws the same items).
        internal void ValueMenuItems(SkillGraphModel.Owner owner,string name,SkillGraphModel.ValueEntry e,Action<string,Action,bool> add)
        {
            var vb=model.Values.TryGetValue(owner.Values,out var v)?v:null;bool own=e!=null&&e.DefinedIn==owner.Values;
            if(own&&vb?.Parent!=null)
            {
                add(F("values.revertTo",vb.Parent),()=>Apply(SkillGraphJson.Op("setValue",("values",owner.Values),("name",name),("value",null))),true);
                add(F("values.applyToParent",vb.Parent,string.Join(", ",model.Values[vb.Parent.Value].UsedBy)),()=>Apply(SkillGraphJson.Op("applyValueToParent",("values",owner.Values),("name",name))),true);
            }
            else add(T("values.notOwn"),null,false);
            if(own&&vb?.Parent==null)add(T("values.delete"),()=>Apply(SkillGraphJson.Op("setValue",("values",owner.Values),("name",name),("value",null))),true);
            add(T("values.editDesc"),()=>Prompt.Show(T("values.descTitle"),name,new[]{(T("values.desc"),e?.Desc??"")},x=>Apply(SkillGraphJson.Op("setValue",("values",e!=null?e.DefinedIn:owner.Values),("name",name),("value",e?.Value),("desc",x[0])))),true);
        }

        // ---- node tab
        private int TargetBundle(SkillGraphLayout.Card c)
        {
            var owner=model.OwnerOf(s.owner);
            if(owner!=null&&c.Bundle==owner.Bundle&&owner.Chain.Contains(s.layer)&&model.Bundles.TryGetValue(s.layer,out var lb)&&lb.Node(c.Node)!=null)return s.layer;
            return c.Bundle;
        }
        private void AddValuePrompt(SkillGraphModel.Owner owner)
        {
            Prompt.Show(T("values.add"),T("values.addText"),new[]{(T("values.name"),""),(T("values.value"),"0"),(T("values.desc"),"")},v=>
            {
                object value=double.TryParse(v[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double d)?(object)d:v[1];
                var op=SkillGraphJson.Op("setValue",("values",owner.Values),("name",v[0]),("value",value));if(v[2].Length>0)op["desc"]=v[2];Apply(op);
            });
        }
        private void BuildNode()
        {
            if(selected==null){panelBody.Add(Cls(new Label(T("node.pick")),"sg-note"));return;}
            var c=selected;var n=Node(c);var m=model.Module(c.Module);
            panelBody.Add(Cls(new Label(c.Icon+" "+c.Title+" ("+c.Module+") #"+c.Order+" · "+F("node.id",c.Node)),"sg-panel__title"));
            if(c.Removed){panelBody.Add(Cls(new Label(T("node.removedText")),"sg-note"));return;}
            if(n==null||m==null)return;
            int target=TargetBundle(c);
            panelBody.Add(Cls(new Label(F("node.where",c.Bundle,n.DefinedIn,target)+(n.Added?" "+T("node.added"):"")+(m.Implemented?"":" · "+T("node.unimplemented"))),"sg-panel__sub"));
            panelBody.Add(Cls(new Label(T("node.note")),"sg-section"));panelBody.Add(Cls(new Label(string.IsNullOrEmpty(n.Note)?T("node.none"):n.Note),"sg-note"));
            var note=m.Note.Get(Language);
            if(!string.IsNullOrEmpty(note)){panelBody.Add(Cls(new Label(T("node.moduleNote")),"sg-section"));panelBody.Add(Cls(new Label(note),"sg-note"));}
            panelBody.Add(Cls(new Label(T("node.memoTitle")),"sg-section"));
            var memo=new TextField{value=model.Memos.TryGetValue(c.Bundle,out var mm)&&mm.TryGetValue(c.Node,out var t)?t:"",multiline=false};memo.style.marginLeft=8;memo.style.marginRight=8;panelBody.Add(memo);
            panelBody.Add(Tool(T("node.memoSave"),()=>Apply(SkillGraphJson.Op("setMemo",("bundle",c.Bundle),("node",c.Node),("text",memo.value))),T("node.memoTip")));
            panelBody.Add(Cls(new Label(F("node.event",model.EventLabel(n.Event))+(n.EventValue.IsNull?"":" · "+SkillGraphModel.Text(n.EventValue))+(n.FromNode!=null?" · "+F("node.from",n.FromNode):"")
                +" · "+F("node.order",n.Order)+(n.Priority!=0?" · "+F("node.priority",n.Priority):"")+"  "+T("node.dragHint")),"sg-note"));
            if(SkillGraphModel.IsKeyframeRef(n.EventValue)&&model.TryKeyframe(c.Bundle,n.EventValue.Text,out var kmo,out var kk,out double koff))
                panelBody.Add(Cls(new Label(F("node.keyframeTime",model.KeyframeLabel(kk.Name,kk.Seconds,koff),kmo.Clip,kk.Frame)),"sg-note"));
            // Motion node: the keyframes of its animation and their cast-time seconds.
            if(model.Bundles.TryGetValue(c.Bundle,out var kb)&&kb.Keyframes.FirstOrDefault(k=>k.Node==c.Node) is {} motion)
            {
                panelBody.Add(Cls(new Label(T("node.keyframes")),"sg-section"));
                panelBody.Add(Cls(new Label(motion.Error??(F("node.motion",motion.Clip,model.Seconds(motion.Start),SkillGraphLayout.Fmt(motion.Speed))+"\n"
                    +string.Join(" · ",motion.Keys.Select(k=>model.KeyframeLabel(k.Name,k.Seconds)+"("+k.Frame+"f)")))),"sg-note"));
            }
            var applicable=SkillGraphLayout.Applicable(m,n);
            var candidates=Candidates(c,n,m);
            if(candidates.Count>0)
            {
                panelBody.Add(Cls(new Label(T("node.suggest")),"sg-section"));
                foreach(var (slot,name,exists) in candidates)
                {
                    var row=Cls(new VisualElement(),"sg-field-row");row.Add(Cls(new Label(SlotLabel(m.Slot(slot))),"sg-field-row__label"));
                    row.Add(new Label((exists?"← ":T("node.newValue")+" ← ")+name){style={flexGrow=1,color=new Color(.5f,.82f,.55f)}});
                    var sl=slot;var nm=name;var ex=exists;row.Add(Tool(T("node.link"),()=>Link(c,target,sl,nm,ex),ex?T("node.linkTip"):T("node.linkNewTip")));panelBody.Add(row);
                }
                panelBody.Add(Tool(T("node.linkAll"),()=>LinkAll(c,target,candidates),T("node.linkAllTip")));
            }
            panelBody.Add(Cls(new Label(T("node.slots")),"sg-section"));
            foreach(var info in m.Slots.OrderBy(x=>applicable.Contains(x.Name)?0:1))
                panelBody.Add(SlotEditor(c,n,m,info,target,applicable.Contains(info.Name)));
            var actions=Group();
            actions.Add(Tool(T("node.removeShort"),()=>Apply(SkillGraphJson.Op("removeNode",("bundle",TargetBundle(c)),("node",c.Node))),T("node.removeTip")));
            actions.Add(Tool(T("node.revertShort"),()=>Apply(SkillGraphJson.Op("revert",("bundle",TargetBundle(c)),("node",c.Node))),T("node.revertTip")));
            panelBody.Add(actions);
        }
        private static string SlotLabel(SkillGraphModel.SlotInfo info)=>info.Label.Get(Language) is {Length:>0} l?l:info.Name;
        // Link suggestions: the object names this bundle mostly reads + the slot name, for numeric slots that are not linked yet.
        public List<(string Slot,string Name,bool Exists)> Candidates(SkillGraphLayout.Card c,SkillGraphModel.Node n,SkillGraphModel.ModuleInfo m)
        {
            var list=new List<(string,string,bool)>();var owner=model.OwnerOf(s.owner);if(owner==null||!model.Bundles.TryGetValue(c.Bundle,out var b))return list;
            var applicable=SkillGraphLayout.Applicable(m,n);
            foreach(var info in m.Slots.Where(x=>applicable.Contains(x.Name)&&(x.Type=="time"||x.Type=="float"||x.Type=="int")))
            {
                var sv=n.Slot(info.Name);if(sv!=null&&sv.Source!="default")continue;
                foreach(var p in b.Prefixes)
                {
                    string name=p+"."+info.Name;bool exists=model.ResolveValue(owner.Values,name)!=null;
                    if(exists||info.Required){list.Add((info.Name,name,exists));break;}
                }
            }
            return list;
        }
        private void Link(SkillGraphLayout.Card c,int target,string slot,string name,bool exists)
        {
            var owner=model.OwnerOf(s.owner);
            if(!exists)ops.Add(SkillGraphJson.Op("setValue",("values",owner.Values),("name",name),("value",0),("desc",T("node.suggestDesc"))));
            Apply(SkillGraphJson.Op("setLink",("bundle",target),("node",c.Node),("slot",slot),("source","Ref"),("value",name)));
        }
        private void LinkAll(SkillGraphLayout.Card c,int target,List<(string Slot,string Name,bool Exists)> list)
        {
            var owner=model.OwnerOf(s.owner);
            foreach(var (slot,name,exists) in list.Take(list.Count-1))
            {
                if(!exists)ops.Add(SkillGraphJson.Op("setValue",("values",owner.Values),("name",name),("value",0),("desc",T("node.suggestDesc"))));
                ops.Add(SkillGraphJson.Op("setLink",("bundle",target),("node",c.Node),("slot",slot),("source","Ref"),("value",name)));
            }
            var last=list.Last();Link(c,target,last.Slot,last.Name,last.Exists);
        }
        private VisualElement SlotEditor(SkillGraphLayout.Card c,SkillGraphModel.Node n,SkillGraphModel.ModuleInfo m,SkillGraphModel.SlotInfo info,int target,bool applicable)
        {
            var box=new VisualElement{style={marginBottom=2}};var sv=n.Slot(info.Name);
            var row=Cls(new VisualElement(),"sg-field-row");if(!applicable)row.style.opacity=0.5f;if(info.Name==selectedSlot)row.style.backgroundColor=new Color(.95f,.79f,.3f,.15f);
            var label=Cls(new Label(SlotLabel(info)+(info.Required?" *":"")),"sg-field-row__label");
            label.tooltip=info.Name+" · "+info.Type+(info.Unit.Length>0?" · "+info.Unit:"")+" · "+info.Kind+" · "+info.Timing+"\n"+info.Description.Get(Language)+(m.Unused.Contains(info.Name)?"\n"+T("row.notRunTip"):"")+(applicable?"":"\n"+T("slot.notApplicable"));
            row.Add(label);
            string sDefault=T("slot.default"),sRef=T("slot.ref"),sFixed=T("slot.fixed");
            var sources=new List<string>{sDefault,sRef,sFixed};bool refable=info.Type=="time"||info.Type=="float"||info.Type=="int"||info.Type.StartsWith("key:",StringComparison.Ordinal);
            if(!refable)sources.Remove(sRef);
            string cur=sv==null||sv.Source=="default"?sDefault:sv.Source=="ref"?sRef:sFixed;
            var src=new PopupField<string>(sources,sources.Contains(cur)?cur:sDefault){style={width=92}};row.Add(src);
            VisualElement field=null;Func<object> read=()=>null;
            void Build()
            {
                if(field!=null)row.Remove(field);
                string choice=src.value;var owner=model.OwnerOf(s.owner);
                if(choice==sRef)
                {
                    var names=model.Values.TryGetValue(owner.Values,out var vb)?vb.Entries.Select(e=>e.Name).ToList():new List<string>();
                    string now=sv!=null&&sv.Source=="ref"?SkillGraphModel.Text(sv.Value):names.FirstOrDefault()??"";if(!names.Contains(now))names.Insert(0,now);
                    var p=new PopupField<string>(names,now){style={flexGrow=1}};field=p;read=()=>p.value;
                }
                else if(choice==sFixed)
                {
                    string e=info.Type.StartsWith("enum:",StringComparison.Ordinal)?info.Type.Substring(5):null;
                    if(e!=null&&model.SlotEnums.TryGetValue(e,out var values))
                    {
                        var shown=values.Select(v=>model.EnumLabel(info.Type,v)+" ("+v+")").ToList();string now=sv!=null&&sv.Source=="fixed"?SkillGraphModel.Text(sv.Value):SkillGraphModel.Text(info.Default);
                        int i=Math.Max(0,Array.IndexOf(values,now));var p=new PopupField<string>(shown,i){style={flexGrow=1}};field=p;read=()=>values[shown.IndexOf(p.value)];
                    }
                    else{var t=new TextField{value=sv!=null&&sv.Source=="fixed"?SkillGraphModel.Text(sv.Value):SkillGraphModel.Text(info.Default),style={flexGrow=1}};field=t;read=()=>Parse(info,t.value);}
                }
                else{field=new Label(SkillGraphModel.Text(info.Default)==""?T("slot.requiredValue"):F("slot.defaultValue",SkillGraphModel.Text(info.Default))){style={flexGrow=1,color=new Color(.55f,.57f,.6f)}};read=()=>null;}
                row.Insert(2,field);
            }
            Build();src.RegisterValueChangedCallback(_=>Build());
            row.Add(Tool(T("slot.apply"),()=>
            {
                string choice=src.value;
                if(choice==sDefault)Apply(SkillGraphJson.Op("setLink",("bundle",target),("node",c.Node),("slot",info.Name),("source",model.Bundles[target].Parent!=null&&sv!=null&&sv.Source!="default"?"Default":null)));
                else Apply(SkillGraphJson.Op("setLink",("bundle",target),("node",c.Node),("slot",info.Name),("source",choice==sRef?"Ref":"Fixed"),("value",read())));
            },F("slot.applyTip",target)));
            if(sv!=null&&sv.Own)row.Add(Tool("↺",()=>Apply(SkillGraphJson.Op("revert",("bundle",target),("node",c.Node),("slot",info.Name))),T("slot.revertTip")));
            box.Add(row);return box;
        }
        private static object Parse(SkillGraphModel.SlotInfo info,string text)
        {
            if(info.Type=="string"||info.Type.StartsWith("list:",StringComparison.Ordinal))return text;
            return double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double d)?(object)d:text;
        }

        // ---- resources: the presentation key of the chosen presentation node -> clip, effect and sound per skin.
        // Effects and sounds are looked up as project assets by name, so the preview works without any host-specific index.
        private void BuildResources()
        {
            panelBody.Add(Cls(new Label(T("res.title")),"sg-panel__title"));
            if(selected==null||!selected.Presentation){panelBody.Add(Cls(new Label(T("res.pick")),"sg-note"));return;}
            var n=Node(selected);var m=model.Module(selected.Module);
            var key=n?.Slots.FirstOrDefault(x=>x.Source=="fixed"&&x.Value.Kind==JsonKind.Number&&m?.Slot(x.Name)?.Type.StartsWith("key:",StringComparison.Ordinal)==true&&model.Resources.Any(r=>r.Key==x.Value.AsInt()));
            if(key==null){panelBody.Add(Cls(new Label(T("res.noKey")),"sg-note"));return;}
            int k=key.Value.AsInt();panelBody.Add(Cls(new Label(k+" · "+model.KeyName(k)),"sg-panel__sub"));
            foreach(var r in model.Resources.Where(r=>r.Key==k).OrderBy(r=>r.Skin))
            {
                panelBody.Add(Cls(new Label(r.Skin==0?T("res.defaultSkin"):F("res.skin",r.Skin)),"sg-section"));
                foreach(var (label,value) in new[]{(T("res.clip"),r.Clip),(T("res.effect"),r.Effect),(T("res.sound"),r.Sound)})
                {var row=Cls(new VisualElement(),"sg-field-row");row.Add(Cls(new Label(label),"sg-field-row__label"));row.Add(new Label(string.IsNullOrEmpty(value)?"—":value){style={flexGrow=1,whiteSpace=WhiteSpace.Normal}});panelBody.Add(row);}
                var tex=Find<Texture2D>(r.Effect);if(tex!=null){var img=new Image{image=tex,scaleMode=ScaleMode.ScaleToFit};img.style.width=96;img.style.height=96;img.style.marginLeft=10;panelBody.Add(img);}
                var clip=Find<AudioClip>(r.Sound);if(clip!=null){var c=clip;panelBody.Add(Tool(T("res.play"),()=>PlayClip(c),T("res.playTip")));}
            }
            panelBody.Add(Cls(new Label(T("res.help")),"sg-note"));
        }
        private static TA Find<TA>(string names) where TA:UnityEngine.Object
        {
            if(string.IsNullOrEmpty(names))return null;
            string name=names.Split(',')[0].Trim();
            foreach(var guid in AssetDatabase.FindAssets(name+" t:"+typeof(TA).Name))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                if(System.IO.Path.GetFileNameWithoutExtension(path)==name)return AssetDatabase.LoadAssetAtPath<TA>(path);
            }
            return null;
        }
        private static void PlayClip(AudioClip clip)
        {
            var util=typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            var stop=util?.GetMethod("StopAllPreviewClips",BindingFlags.Static|BindingFlags.Public);stop?.Invoke(null,null);
            var play=util?.GetMethods(BindingFlags.Static|BindingFlags.Public).FirstOrDefault(x=>x.Name=="PlayPreviewClip");
            if(play!=null){var args=play.GetParameters().Select(p=>p.ParameterType==typeof(AudioClip)?(object)clip:p.ParameterType==typeof(int)?0:(object)false).ToArray();play.Invoke(null,args);}
        }

        // ---- creating owners and bundles
        private string OwnerNumberText(){var owner=model.OwnerOf(s.owner);return model.Practice?model.PracticeOwner.ToString(CultureInfo.InvariantCulture):owner!=null?model.OwnerNumber(owner.Key).ToString(CultureInfo.InvariantCulture):"1";}
        private string RootBundleText(){var owner=model.OwnerOf(s.owner);return owner!=null&&owner.Chain.Length>0?owner.Chain[0].ToString(CultureInfo.InvariantCulture):"";}
        private void NewSkill(bool duplicate)
        {
            var owner=model.OwnerOf(s.owner);
            if(duplicate)
            {
                if(owner==null)return;
                int choice=EditorUtility.DisplayDialogComplex(T("dup.title"),F("dup.text",owner.Key,owner.Name),T("dup.withEffects"),T("cancel"),T("dup.only"));
                if(choice==1)return;
                DuplicatePrompt(owner,choice==0);
                return;
            }
            Prompt.Show(T("menu.newSkill").TrimEnd('…'),T("new.skillText"),new[]{(T("new.name"),""),(T("new.ownerNumber"),OwnerNumberText()),(T("new.moduleParent"),RootBundleText()),(T("new.valueParent"),"")},v=>
            {
                var op=SkillGraphJson.Op("newSkill",("kind",model.KindName("skill")),("name",v[0]));
                if(int.TryParse(v[1],out int h))op["ownerNumber"]=h;if(int.TryParse(v[2],out int pm))op["moduleParent"]=pm;if(int.TryParse(v[3],out int pv))op["valueParent"]=pv;
                if(Apply(op))SelectOwner(LastResult["key"].AsInt());
            });
        }
        // Value cell click opens this prompt (also used by the capture command).
        internal void EditValuePrompt(SkillGraphModel.Owner owner,string name)
        {
            var e=model.ResolveValue(owner.Values,name);
            Prompt.Show(T("values.editTitle"),F("values.editText",owner.Name,name,owner.Values),new[]{(T("values.value"),e==null?"":SkillGraphModel.Text(e.Value))},v=>
            {object value=double.TryParse(v[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double d)?(object)d:v[0];Apply(SkillGraphJson.Op("setValue",("values",owner.Values),("name",name),("value",value)));});
        }
        private void NewEffectBundle()
        {
            Prompt.Show(T("menu.newEffect").TrimEnd('…'),T("new.effectText"),new[]{(T("new.desc"),""),(T("new.ownerNumber"),OwnerNumberText()),(T("new.moduleParent"),"")},v=>
            {
                var op=SkillGraphJson.Op("newBundle",("kind",model.KindName("effect")),("desc",v[0]));if(int.TryParse(v[1],out int h))op["ownerNumber"]=h;if(int.TryParse(v[2],out int pm))op["parent"]=pm;
                if(Apply(op))SetStatus(F("new.effectDone",LastResult["key"].AsInt()),false);
            });
        }
        private void NewBuff()
        {
            Prompt.Show(T("menu.newBuff").TrimEnd('…'),T("new.buffText"),new[]{(T("new.name"),""),(T("new.ownerNumber"),OwnerNumberText()),(T("new.moduleParent"),""),(T("new.valueParent"),"")},v=>
            {
                var op=SkillGraphJson.Op("newSkill",("kind",model.KindName("buff")),("name",v[0]));
                if(int.TryParse(v[1],out int h))op["ownerNumber"]=h;if(int.TryParse(v[2],out int pm))op["moduleParent"]=pm;if(int.TryParse(v[3],out int pv))op["valueParent"]=pv;
                if(Apply(op))SelectOwner(LastResult["key"].AsInt());
            });
        }
        internal void DuplicatePrompt(SkillGraphModel.Owner owner,bool withEffects)
        {
            Prompt.Show(T("dup.title"),T("dup.nameText"),new[]{(T("new.name"),F("dup.defaultName",owner.Name))},v=>{if(Apply(SkillGraphJson.Op("duplicateSkill",("owner",owner.Key),("withEffects",withEffects),("name",v[0]))))SelectOwner(LastResult["key"].AsInt());});
        }
        private void NewChildBundle()
        {
            var b=model.Bundles.TryGetValue(s.layer,out var lb)?lb:null;if(b==null)return;
            Prompt.Show(T("menu.childBundle").TrimEnd('…'),F("new.childText",b.Key,b.Desc),new[]{(T("new.desc"),F("new.childDefault",b.Desc))},v=>Apply(SkillGraphJson.Op("newBundle",("parent",b.Key),("desc",v[0]))));
        }
        private void EditBundleDesc()
        {
            var b=model.Bundles.TryGetValue(s.layer,out var lb)?lb:null;if(b==null)return;
            Prompt.Show(T("menu.bundleDesc").TrimEnd('…'),T("new.descText"),new[]{(T("new.desc"),b.Desc)},v=>Apply(SkillGraphJson.Op("setBundle",("bundle",b.Key),("desc",v[0]))));
        }
        private void EditBands()
        {
            var box=layout?.Boxes.FirstOrDefault(x=>x.Main);if(box==null)return;
            var cur=model.Bands.TryGetValue(box.Bundle,out var list)?string.Join("\n",list.Select(x=>x.Name+"="+x.From+".."+x.To)):"";
            Prompt.Show(T("menu.bands").TrimEnd('…'),F("bands.text",string.Join(", ",box.Columns.Select(c=>c.Key))),new[]{(T("bands.field"),cur)},v=>
            {
                var bands=new List<object>();
                foreach(var line in v[0].Split('\n').Select(x=>x.Trim()).Where(x=>x.Contains("=")))
                {var p=line.Split('=');var r=p[1].Split(new[]{".."},StringSplitOptions.None);bands.Add(new Dictionary<string,object>{{"name",p[0].Trim()},{"from",r[0].Trim()},{"to",(r.Length>1?r[1]:r[0]).Trim()}});}
                Apply(SkillGraphJson.Op("setBands",("bundle",box.Bundle),("bands",bands)));
            },true);
        }
        private void EditMemo(SkillGraphLayout.Card c)
        {
            string cur=model.Memos.TryGetValue(c.Bundle,out var m)&&m.TryGetValue(c.Node,out var t)?t:"";
            Prompt.Show(T("memo.title"),F("memo.text",c.Bundle,c.Node),new[]{(T("memo.field"),cur)},v=>Apply(SkillGraphJson.Op("setMemo",("bundle",c.Bundle),("node",c.Node),("text",v[0]))));
        }
        private void SaveCombo(SkillGraphLayout.Card c)
        {
            var nodes=top.selection.Concat(bottom.selection).OfType<CardElement>().Where(x=>x.Card.Bundle==c.Bundle&&!x.Card.Removed).Select(x=>x.Card.Node).Distinct().ToList();if(nodes.Count==0)nodes.Add(c.Node);
            Prompt.Show(T("combo.title"),F("combo.text",nodes.Count),new[]{(T("combo.name"),""),(T("new.desc"),"")},v=>Apply(SkillGraphJson.Op("saveCombo",("name",v[0]),("desc",v[1]),("bundle",c.Bundle),("nodes",nodes.Cast<object>().ToList()))));
        }
    }

    // Small input window (the Unity editor has no text input dialog).
    public sealed class Prompt : EditorWindow
    {
        private string message;private (string Label,string Value)[] fields;private Action<string[]> done;private bool multiline;private bool focused;
        // For the capture command: the last opened prompt and its field / button rectangles (recorded on repaint).
        internal static Prompt Last;internal readonly Dictionary<string,Rect> CaptureRects=new Dictionary<string,Rect>();
        public static void Show(string title,string message,(string,string)[] fields,Action<string[]> done,bool multiline=false)
        {
            var w=CreateInstance<Prompt>();w.titleContent=new GUIContent(title);w.message=message;w.fields=fields.ToArray();w.done=done;w.multiline=multiline;
            var main=EditorGUIUtility.GetMainWindowPosition();w.position=new Rect(main.center.x-260,main.center.y-120,520,multiline?300:140+fields.Length*22);w.ShowUtility();Last=w;
        }
        // The capture command fills visible values before the first draw (nothing is applied: OK is never pressed).
        internal void SetCaptureValues(IReadOnlyDictionary<string,string> values)
        {for(int i=0;i<fields.Length;i++)if(values.TryGetValue(fields[i].Label,out var v)||values.TryGetValue(i.ToString(CultureInfo.InvariantCulture),out v))fields[i].Value=v;}
        private void OnGUI()
        {
            bool repaint=Event.current.type==EventType.Repaint;
            EditorGUILayout.LabelField(message,EditorStyles.wordWrappedLabel);
            for(int i=0;i<fields.Length;i++)
            {
                GUI.SetNextControlName("f"+i);
                fields[i].Value=multiline?EditorGUILayout.TextArea(fields[i].Value,GUILayout.MinHeight(120)):EditorGUILayout.TextField(fields[i].Label,fields[i].Value);
                if(repaint)CaptureRects[fields[i].Label]=GUILayoutUtility.GetLastRect();
            }
            if(!focused){EditorGUI.FocusTextInControl("f0");focused=true;}
            bool enter=Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Return&&!multiline;
            EditorGUILayout.BeginHorizontal();GUILayout.FlexibleSpace();
            if(GUILayout.Button(T("ok"),GUILayout.Width(100))||enter){var d=done;var v=fields.Select(f=>f.Value??"").ToArray();Close();d?.Invoke(v);GUIUtility.ExitGUI();}
            if(repaint)CaptureRects["ok"]=GUILayoutUtility.GetLastRect();
            if(GUILayout.Button(T("cancel"),GUILayout.Width(100)))Close();
            if(repaint)CaptureRects["cancel"]=GUILayoutUtility.GetLastRect();
            EditorGUILayout.EndHorizontal();
        }
    }
}
