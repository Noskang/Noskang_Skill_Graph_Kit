using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Skill graph editor. The window sends "workbooks as first read + operation list" to the Python backend and redraws
    // the model it gets back, so undo / redo only shrink and grow the list and Save writes Excel in one go.
    public sealed partial class SkillGraphEditorWindow : EditorWindow
    {
        public enum View { Logic, Both, PresentationEdit }
        [Serializable] private sealed class Session { public string workspace="product";public string ops="[]";public string redo="[]";public string baseJson="";public int owner;public int layer;public View view=View.Both;public bool sync=true;public bool panel=true;public string tab="values";public string expanded="";public string chips="";public bool fold; }

        private SkillGraphModel model;private SkillGraphLayout layout;private Session s=new Session();
        private readonly List<Dictionary<string,object>> ops=new List<Dictionary<string,object>>();private readonly List<Dictionary<string,object>> redo=new List<Dictionary<string,object>>();
        private Dictionary<string,string> baseFiles;private readonly HashSet<int> expanded=new HashSet<int>();private readonly HashSet<string> chips=new HashSet<string>();
        private Dictionary<(int Bundle,int Node),int> runs;private SkillGraphLayout.Card selected;private string selectedSlot;private string markedValue;
        private SkillGraphCanvas top,bottom;private TwoPaneSplitView split,outer;private VisualElement toolbar,layerBar,panel,panelBody,issueList,bottomPane,topPane;private Label statusBar,issueTitle;
        private bool syncing;private string lastError;
        public SkillGraphModel Model=>model;public SkillGraphLayout Layout=>layout;public SkillGraphCanvas TopCanvas=>top;public SkillGraphCanvas BottomCanvas=>bottom;
        public string Tab=>s.tab;public View CurrentView=>s.view;public int Owner=>s.owner;public int Layer=>s.layer;public IReadOnlyList<Dictionary<string,object>> Ops=>ops;

        [MenuItem("Tools/SkillGraph Kit/Skill Graph Editor",priority=0)]
        public static void Open(){var w=GetWindow<SkillGraphEditorWindow>(T("window.title"));w.minSize=new Vector2(900,560);}
        private static string SessionPath(string ws)=>Path.GetFullPath("Library/SkillGraphKit/session-"+ws+".json");

        private void OnEnable()
        {
            var p=SessionPath("product");
            try{if(File.Exists(p))s=JsonUtility.FromJson<Session>(File.ReadAllText(p,Encoding.UTF8))??new Session();}catch(Exception){s=new Session();}
            LoadSession();
            LanguageChanged+=Rebuild;
        }
        private void Rebuild(){titleContent=new GUIContent(T("window.title"));if(rootVisualElement!=null&&top!=null)CreateGUI();}
        private void LoadSession()
        {
            ops.Clear();redo.Clear();expanded.Clear();chips.Clear();
            foreach(var o in ParseOps(s.ops))ops.Add(o);foreach(var o in ParseOps(s.redo))redo.Add(o);
            baseFiles=string.IsNullOrEmpty(s.baseJson)?null:JsonNode.Parse(s.baseJson).Fields.ToDictionary(k=>k.Key,k=>k.Value.AsText());
            foreach(var x in (s.expanded??"").Split(','))if(int.TryParse(x,out int k))expanded.Add(k);
            foreach(var x in (s.chips??"").Split('|'))if(x.Length>0)chips.Add(x);
        }
        private void SaveSession()
        {
            s.ops=SkillGraphJson.Write(ops);s.redo=SkillGraphJson.Write(redo);s.baseJson=baseFiles==null?"":SkillGraphJson.Write(baseFiles.ToDictionary(k=>k.Key,k=>(object)k.Value));
            s.expanded=string.Join(",",expanded);s.chips=string.Join("|",chips);
            try{Directory.CreateDirectory(Path.GetDirectoryName(SessionPath("product")));File.WriteAllText(SessionPath("product"),JsonUtility.ToJson(s),new UTF8Encoding(false));}catch(Exception){}
        }
        private static List<Dictionary<string,object>> ParseOps(string json)
        {
            var list=new List<Dictionary<string,object>>();if(string.IsNullOrEmpty(json))return list;
            try{foreach(var o in JsonNode.Parse(json).AsArray())list.Add(o.Keys.ToDictionary(k=>k,k=>(object)o.Fields[k]));}catch(Exception){}
            return list;
        }

        // ---------------------------------------------------------------- building the window
        private void CreateGUI()
        {
            var root=rootVisualElement;root.Clear();
            var sheet=AssetDatabase.LoadAssetAtPath<StyleSheet>(SkillGraphCanvas.StyleSheetPath);if(sheet!=null)root.styleSheets.Add(sheet);
            root.AddToClassList("sg-editor");
            toolbar=new VisualElement();toolbar.AddToClassList("sg-toolbar");root.Add(toolbar);
            outer=new TwoPaneSplitView(1,330,TwoPaneSplitViewOrientation.Horizontal){style={flexGrow=1}};root.Add(outer);
            var main=new VisualElement{style={flexGrow=1}};outer.Add(main);
            panel=new VisualElement();panel.AddToClassList("sg-panel");outer.Add(panel);
            split=new TwoPaneSplitView(0,420,TwoPaneSplitViewOrientation.Vertical){style={flexGrow=1}};main.Add(split);
            topPane=new VisualElement{style={flexGrow=1}};bottomPane=new VisualElement{style={flexGrow=1}};split.Add(topPane);split.Add(bottomPane);
            top=new SkillGraphCanvas(false){style={flexGrow=1}};bottom=new SkillGraphCanvas(true){style={flexGrow=1}};
            topPane.Add(top);bottomPane.Add(bottom);
            topPane.Add(PaneLabel(T("pane.logic")));bottomPane.Add(PaneLabel(T("pane.presentation")));
            var issues=new VisualElement();issues.AddToClassList("sg-issues");issueTitle=new Label(T("issues.title"));issueTitle.AddToClassList("sg-issues__title");issues.Add(issueTitle);
            issueList=new ScrollView{style={flexGrow=1}};issues.Add(issueList);main.Add(issues);
            statusBar=new Label("");statusBar.AddToClassList("sg-status");root.Add(statusBar);
            foreach(var c in new[]{top,bottom})
            {
                c.NodeSelected+=OnSelect;c.SlotClicked+=OnSlot;c.ChipClicked+=OnChip;c.NodeDropped+=OnDropped;c.AddRequested+=OnAdd;c.BoxToggled+=b=>{if(!expanded.Remove(b.Bundle))expanded.Add(b.Bundle);SaveSession();Relayout();};
                c.NodeMenu+=OnNodeMenu;c.viewTransformChanged+=v=>Sync(v);
                c.DeleteRequested+=OnDelete;c.NodeDragging+=OnDragging;c.NodeDragEnded+=ClearDrop;c.ColumnMenu+=OnColumnMenu;c.AddColumnRequested+=OnAddColumn;
            }
            root.RegisterCallback<KeyDownEvent>(OnKey,TrickleDown.TrickleDown);
            Refresh();
        }
        private static Label PaneLabel(string text){var l=new Label(text);l.AddToClassList("sg-pane-label");l.pickingMode=PickingMode.Ignore;return l;}

        private void BuildToolbar()
        {
            toolbar.Clear();
            toolbar.Add(Cls(new Label(T("window.title")),"sg-toolbar__title"));
            // Practice workspace badge next to the title, so a practice copy is never mistaken for product data.
            if(model!=null&&model.Practice){var practice=Cls(new Label(T("toolbar.practice")),"sg-toolbar__practice");practice.tooltip=T("toolbar.practiceTip");toolbar.Add(practice);}
            if(model!=null&&model.Owners.Count>0)
            {
                var choices=model.Owners.Select(OwnerLabel).ToList();var current=model.OwnerOf(s.owner)??model.Owners[0];
                var popup=new PopupField<string>(choices,choices.IndexOf(OwnerLabel(current))){style={minWidth=230}};popup.tooltip=T("toolbar.ownerTip");
                popup.RegisterValueChangedCallback(e=>{var o=model.Owners[choices.IndexOf(e.newValue)];SelectOwner(o.Key);});toolbar.Add(popup);
            }
            layerBar=new VisualElement();layerBar.AddToClassList("sg-layer");toolbar.Add(layerBar);BuildLayerBar();
            toolbar.Add(new VisualElement{style={flexGrow=1}});
            var views=Group();
            foreach(var (v,text,tip) in new[]{(View.Logic,T("view.logic"),T("view.logicTip")),(View.Both,T("view.both"),T("view.bothTip")),(View.PresentationEdit,T("view.presentation"),T("view.presentationTip"))})
                views.Add(Tool(text,()=>{s.view=v;ApplyView();SaveSession();Relayout();BuildToolbar();},tip,s.view==v));
            views.Add(Tool(s.fold?T("view.unfold"):T("view.fold"),()=>{s.fold=!s.fold;ApplyView();SaveSession();Relayout();BuildToolbar();},T("view.foldTip"),s.fold));
            toolbar.Add(views);
            toolbar.Add(Tool(T("toolbar.sync")+(s.sync?" ✓":""),()=>{s.sync=!s.sync;SaveSession();BuildToolbar();},T("toolbar.syncTip"),s.sync));
            int errors=model?.Issues.Count(i=>i.Level=="error")??0,warnings=model?.Issues.Count(i=>i.Level=="warning")??0;
            toolbar.Add(Cls(new Label("● "+errors),"sg-count","sg-count--error"));toolbar.Add(Cls(new Label("▲ "+warnings),"sg-count","sg-count--warning"));
            toolbar.Add(Tool(T("toolbar.frame"),()=>{top.FrameSelection();},T("toolbar.frameTip")));toolbar.Add(Tool(T("toolbar.all"),()=>{top.FrameAll();bottom.FrameAll();},T("toolbar.allTip")));
            toolbar.Add(Tool(T("toolbar.panel"),()=>{s.panel=!s.panel;ApplyPanel();SaveSession();BuildToolbar();},T("toolbar.panelTip"),s.panel));
            var edit=Group();
            edit.Add(Tool(T("toolbar.undo"),Undo,T("toolbar.undoTip"),false,ops.Count==0));edit.Add(Tool(T("toolbar.redo"),Redo,T("toolbar.redoTip"),false,redo.Count==0));
            edit.Add(Tool(T("toolbar.save")+(ops.Count>0?" ("+ops.Count+")":""),Save,T("toolbar.saveTip"),false,ops.Count==0,true));
            toolbar.Add(edit);
            var more=new ToolbarMenu{text=T("toolbar.more")};more.AddToClassList("sg-tool");moreMenu=more;
            more.menu.AppendAction(T("menu.export"),_=>ExportData(),_=>model!=null&&!model.Practice&&ops.Count==0?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
            more.menu.AppendSeparator();
            more.menu.AppendAction(T("menu.newSkill"),_=>NewSkill(false));more.menu.AppendAction(T("menu.newEffect"),_=>NewEffectBundle());more.menu.AppendAction(T("menu.newBuff"),_=>NewBuff());
            more.menu.AppendAction(T("menu.duplicate"),_=>NewSkill(true));
            more.menu.AppendAction(T("menu.childBundle"),_=>NewChildBundle());more.menu.AppendAction(T("menu.bundleDesc"),_=>EditBundleDesc());
            more.menu.AppendAction(T("menu.bands"),_=>EditBands());
            more.menu.AppendSeparator();
            more.menu.AppendAction(model!=null&&model.Practice?T("menu.toProduct"):T("menu.toPractice"),_=>SwitchWorkspace(model!=null&&model.Practice?"product":"practice"));
            more.menu.AppendAction(T("menu.resetPractice"),_=>ResetPractice());
            more.menu.AppendAction(T("menu.reload"),_=>Reload());
            more.menu.AppendAction(T("menu.runLog"),_=>{string p=EditorUtility.OpenFilePanel(T("menu.runLogPick"),Path.GetFullPath("."),"jsonl");if(p.Length>0){runs=CountRunLog(File.ReadLines(p));Relayout();}});
            more.menu.AppendAction(T("menu.runLogOff"),_=>{runs=null;Relayout();},_=>runs==null?DropdownMenuAction.Status.Disabled:DropdownMenuAction.Status.Normal);
            toolbar.Add(more);
            toolbar.Add(Tool(Language=="ko"?"EN":"한",()=>SetLanguage(Language=="ko"?"en":"ko"),T("toolbar.languageTip")));
        }
        private void BuildLayerBar()
        {
            layerBar.Clear();var owner=model?.OwnerOf(s.owner);if(owner==null)return;
            layerBar.Add(Cls(new Label(T("layer.title")),"sg-layer__note"));
            for(int i=0;i<owner.Chain.Length;i++)
            {
                int key=owner.Chain[i];bool last=i==owner.Chain.Length-1;
                if(last&&owner.Stage!=null&&owner.Stage.Family.Length>1)
                {
                    // The last layer is a tier family: one chip per tier, choosing a tier switches the owner.
                    foreach(int sk in owner.Stage.Family)
                    {
                        var so=model.OwnerOf(sk);if(so==null)continue;int lvl=Array.IndexOf(owner.Stage.Family,sk)+1;bool on=sk==owner.Key&&s.layer==so.Bundle;
                        var item=Cls(new Label((on?"● ":"")+(sk==owner.Key?SkillGraphLayout.Short(model.KeyName(so.Bundle),12)+" "+so.Bundle:F("tier.n",lvl))),"sg-layer__item");if(on)item.AddToClassList("sg-layer__item--on");
                        item.tooltip=F("layer.tierTip",lvl,so.Name);item.RegisterCallback<MouseDownEvent>(_=>{SelectOwner(sk);});layerBar.Add(item);
                    }
                    break;
                }
                var l=Cls(new Label((s.layer==key?"● ":"")+SkillGraphLayout.Short(model.KeyName(key),12)+" "+key),"sg-layer__item");if(s.layer==key)l.AddToClassList("sg-layer__item--on");
                string layerName=i<model.Layers.Length?model.Layers[i]+" · ":"";
                l.tooltip=layerName+F("layer.tip",model.Bundles.TryGetValue(key,out var b)?b.UsedBy.Length:0);
                l.RegisterCallback<MouseDownEvent>(_=>{s.layer=key;SaveSession();Relayout();BuildToolbar();});layerBar.Add(l);
                if(!last)layerBar.Add(Cls(new Label("›"),"sg-layer__arrow"));
            }
            var lb=model.Bundles.TryGetValue(s.layer,out var layerBundle)?layerBundle:null;
            if(lb!=null&&lb.Parent!=null)
            {
                int nodes=lb.Nodes.Count(n=>n.DefinedIn==lb.Key||n.Fields.Values.Contains(lb.Key)),slots=lb.Nodes.Sum(n=>n.Slots.Count(x=>x.DefinedIn==lb.Key&&n.DefinedIn!=lb.Key));
                var vb=model.Values.TryGetValue(owner.Values,out var v)?v.Entries.Count(e=>e.Own):0;
                layerBar.Add(Cls(new Label(F("layer.changed",nodes,slots)+(owner.Stage!=null?" · "+F("layer.changedValues",vb):"")),"sg-layer__note"));
            }
        }
        private VisualElement Group(){var g=new VisualElement();g.AddToClassList("sg-toolbar__group");return g;}
        private static TE Cls<TE>(TE e,params string[] cls) where TE:VisualElement{foreach(var c in cls)e.AddToClassList(c);return e;}
        private static Label Tool(string text,Action click,string tip,bool on=false,bool disabled=false,bool primary=false)
        {
            var b=new Label(text);b.AddToClassList("sg-tool");if(on)b.AddToClassList("sg-tool--on");if(disabled)b.AddToClassList("sg-tool--disabled");if(primary&&!disabled)b.AddToClassList("sg-tool--primary");
            b.tooltip=tip;if(!disabled)b.RegisterCallback<MouseDownEvent>(e=>{if(e.button==0)click();});return b;
        }
        private string OwnerLabel(SkillGraphModel.Owner o)=>o.Key+" "+o.Name;

        // ---------------------------------------------------------------- model and layout
        public void Refresh()
        {
            if(top==null)return;
            try
            {
                var res=Request("model",ops);
                if(!res["ok"].Bool){HandleOpFailure(res);return;}
                model=SkillGraphModel.Parse(res["model"]);
                if(baseFiles==null)baseFiles=new Dictionary<string,string>(model.Base);
                if(model.OwnerOf(s.owner)==null)s.owner=model.Owners.FirstOrDefault()?.Key??0;
                var owner=model.OwnerOf(s.owner);if(owner!=null&&!owner.Chain.Contains(s.layer))s.layer=owner.Bundle;
                lastError=null;Relayout();BuildToolbar();ApplyView();ApplyPanel();SaveSession();
                if(model.ExternalChange)SetStatus(F("status.external",ops.Count),true);
            }
            catch(Exception x){SetStatus(F("status.modelFailed",x.Message),true);Debug.LogError("[SkillGraph] "+x);}
        }
        private JsonNode Request(string cmd,IEnumerable<Dictionary<string,object>> list,bool allowErrors=false)
        {
            var req=new Dictionary<string,object>{{"cmd",cmd},{"workspace",s.workspace},{"ops",list.Cast<object>().ToList()}};
            if(baseFiles!=null)req["base"]=baseFiles.ToDictionary(k=>k.Key,k=>(object)k.Value);
            if(allowErrors)req["allowErrors"]=true;
            return SkillGraphBackend.Shared.Request(req);
        }
        private void HandleOpFailure(JsonNode res)
        {
            string msg=res.Get("error")?.TextOr(null)??T("status.opFailed");
            if(ops.Count>0){ops.RemoveAt(ops.Count-1);SaveSession();}
            lastError=msg;SetStatus(F("status.opReverted",msg),true);
            EditorUtility.DisplayDialog(T("window.title"),msg,T("ok"));
            RefreshQuiet();
        }
        private void RefreshQuiet(){try{var res=Request("model",ops);if(res["ok"].Bool){model=SkillGraphModel.Parse(res["model"]);Relayout();BuildToolbar();}}catch(Exception){}}
        public void Relayout()
        {
            if(model==null)return;
            var o=new SkillGraphLayout.Options{Owner=s.owner,Layer=s.layer,Expanded=expanded,OpenChips=chips,Runs=runs,Presentation=true,CompactPresentation=s.view==View.Both,FoldPresentation=s.fold&&s.view!=View.PresentationEdit};
            layout=new SkillGraphLayout(model,o);top.Show(layout);bottom.Show(layout);
            BuildPanel();BuildIssues();
            if(selected!=null){var again=layout.Cards.FirstOrDefault(c=>c.Bundle==selected.Bundle&&c.Node==selected.Node&&c.Presentation==selected.Presentation);if(again!=null){selected=again;(again.Presentation?bottom:top).Select(again);Highlight(again);}}
            SetStatus(StatusText(),lastError!=null);
        }
        private string StatusText()
        {
            var owner=model?.OwnerOf(s.owner);if(owner==null)return T("status.noOwner");
            var (max,avg)=layout.RowsRuler();var size=layout.MainSize();
            return (model.Practice?T("status.practice")+" ":"")+owner.Key+" "+owner.Name+" · "+F("status.layer",s.layer)+" · "+F("status.rows",max,avg.ToString("0.0",CultureInfo.InvariantCulture))
                +" · "+F("status.size",(int)size.x,(int)size.y)+" · "+F("status.pending",ops.Count)+(model.PendingAux?" · "+T("status.aux"):"")+" · "+T("status.keys");
        }
        private void SetStatus(string text,bool warn){if(statusBar!=null){statusBar.text=text;statusBar.style.color=warn?new Color(1f,.75f,.45f):new Color(.62f,.64f,.68f);}}
        private void ApplyView()
        {
            if(split==null)return;
            bottomPane.style.display=s.view==View.Logic?DisplayStyle.None:DisplayStyle.Flex;
            float h=Math.Max(300,position.height-150);
            float topH=s.view==View.PresentationEdit?h*0.3f:s.fold?h-130:h*0.7f;
            split.fixedPaneInitialDimension=topH;
            if(s.view==View.Logic)split.CollapseChild(1);else split.UnCollapse();
            split.fixedPane.style.height=topH;
            top.Dim(s.view==View.PresentationEdit);
            if(s.view==View.PresentationEdit&&s.tab=="values"){s.tab="resources";BuildPanel();}
        }
        private void ApplyPanel(){if(panel==null)return;if(s.panel)outer.UnCollapse();else outer.CollapseChild(1);}
        private void Sync(GraphView from)
        {
            if(syncing||!s.sync)return;syncing=true;
            // GraphView still reports its view through ITransform (obsolete in UI Toolkit, no GraphView replacement yet).
#pragma warning disable CS0618
            try{var other=from==top?(GraphView)bottom:top;var t=from.viewTransform;var o=other.viewTransform;other.UpdateViewTransform(new Vector3(t.position.x,o.position.y,0),t.scale);}
#pragma warning restore CS0618
            finally{syncing=false;}
        }

        // ---------------------------------------------------------------- edit operations
        public bool Apply(Dictionary<string,object> op)
        {
            ops.Add(op);redo.Clear();lastError=null;SaveSession();
            try
            {
                var res=Request("model",ops);
                if(!res["ok"].Bool){HandleOpFailure(res);return false;}
                model=SkillGraphModel.Parse(res["model"]);Relayout();BuildToolbar();SaveSession();return true;
            }
            catch(Exception x){ops.RemoveAt(ops.Count-1);SetStatus(F("status.applyFailed",x.Message),true);return false;}
        }
        public JsonNode LastResult=>model?.OpResults.LastOrDefault();
        private void Undo(){if(ops.Count==0)return;redo.Add(ops[ops.Count-1]);ops.RemoveAt(ops.Count-1);SaveSession();Refresh();}
        private void Redo(){if(redo.Count==0)return;ops.Add(redo[redo.Count-1]);redo.RemoveAt(redo.Count-1);SaveSession();Refresh();}
        private void OnKey(KeyDownEvent e)
        {
            if(e.actionKey&&e.keyCode==KeyCode.Z){Undo();e.StopPropagation();}
            else if(e.actionKey&&e.keyCode==KeyCode.Y){Redo();e.StopPropagation();}
            else if(e.actionKey&&e.keyCode==KeyCode.S){Save();e.StopPropagation();}
        }
        public void SelectOwner(int key)
        {
            var o=model?.OwnerOf(key);if(o==null)return;
            // Moving between tiers of one family keeps the same layer depth.
            var prev=model.OwnerOf(s.owner);int depth=prev!=null?Array.IndexOf(prev.Chain,s.layer):-1;
            s.owner=key;s.layer=depth>=0&&depth<o.Chain.Length&&prev.Stage!=null&&o.Stage!=null&&prev.Stage.Group==o.Stage.Group?o.Chain[depth]:o.Bundle;
            selected=null;SaveSession();Relayout();BuildToolbar();top.schedule.Execute(()=>{top.FrameAll();bottom.FrameAll();}).StartingIn(50);
        }

        private void OnSelect(SkillGraphLayout.Card c){selected=c;selectedSlot=null;Highlight(c);if(s.tab!="resources"||!c.Presentation)s.tab=c.Presentation&&s.view==View.PresentationEdit?"resources":"node";BuildPanel();}
        private void Highlight(SkillGraphLayout.Card c){top.HighlightColumn(c.Bundle,c.Column);bottom.HighlightColumn(c.Bundle,c.Column);}
        private void OnSlot(SkillGraphLayout.Card c,string slot)
        {
            selected=c;selectedSlot=slot;var n=Node(c);var v=n?.Slot(slot);
            markedValue=v!=null&&v.Source=="ref"?SkillGraphModel.Text(v.Value):null;
            MarkValue(markedValue);s.tab=markedValue!=null?"values":"node";BuildPanel();Highlight(c);
        }
        // Slot <-> value row cross highlight.
        private void MarkValue(string name){markedValue=name;ShowValueMarks(name);}
        // Hovering a value row marks every slot and column head that reads it; leaving returns to the chosen value.
        private void HoverValue(string name)=>ShowValueMarks(name??markedValue);
        private void ShowValueMarks(string name)
        {
            foreach(var cv in new[]{top,bottom})
            {
                if(cv==null)continue;
                cv.MarkSlots(name==null?null:(card,row)=>{var n=Node(card);var v=n?.Slot(row.Slot);return v!=null&&v.Source=="ref"&&SkillGraphModel.Text(v.Value)==name;});
                cv.MarkColumns(name==null?null:col=>model.IsTimed(col.Event)&&col.EventValue!=null&&col.EventValue.Kind==JsonKind.String&&col.EventValue.Text==name);
            }
        }
        private void OnChip(SkillGraphLayout.Card c,string stage)
        {string key=SkillGraphLayout.Options.Chip(c.Bundle,c.Node,stage);if(!chips.Remove(key))chips.Add(key);SaveSession();Relayout();}
        public SkillGraphModel.Node Node(SkillGraphLayout.Card c)=>model!=null&&model.Bundles.TryGetValue(c.Bundle,out var b)?b.Node(c.Node):null;

        // Dropping a node on another column changes its event; inside the same column it changes its order.
        private void OnDropped(SkillGraphLayout.Card c,Rect to)
        {
            ClearDrop();
            var box=layout.Boxes.FirstOrDefault(b=>b.Bundle==c.Bundle&&!b.Collapsed);if(box==null){Relayout();return;}
            var col=SkillGraphLayout.DropColumn(box,to.center.x);
            var op=SkillGraphJson.Op("moveNode",("bundle",TargetBundle(c)),("node",c.Node));
            if(col.Key!=c.Column)
            {
                op["event"]=col.Event;op["eventValue"]=col.EventValue==null||col.EventValue.IsNull?null:(object)col.EventValue;if(col.From!=null)op["fromNode"]=col.From;
                var siblings=layout.Cards.Where(x=>x.Bundle==c.Bundle&&x.Column==col.Key&&x.Presentation==c.Presentation&&!x.Removed).OrderBy(x=>x.Rect.y).ToList();
                op["order"]=NewOrder(siblings,to.center.y);
            }
            else
            {
                var siblings=layout.Cards.Where(x=>x.Bundle==c.Bundle&&x.Column==col.Key&&x.Presentation==c.Presentation&&!x.Removed&&x.Node!=c.Node).OrderBy(x=>x.Rect.y).ToList();
                int order=NewOrder(siblings,to.center.y);if(order==c.Order){Relayout();return;}
                op["order"]=order;
            }
            if(!Apply(op))Relayout();
        }
        // While dragging, the target column glows in both graphs; red when an inherited node cannot change its event on this layer.
        private string dropKey;
        private void OnDragging(SkillGraphLayout.Card c,Rect to)
        {
            var box=layout?.Boxes.FirstOrDefault(b=>b.Bundle==c.Bundle&&!b.Collapsed);var col=SkillGraphLayout.DropColumn(box,to.center.x);if(col==null)return;
            bool blocked=col.Key!=c.Column&&!EventEditable(c);string key=c.Bundle+"/"+col.Key+"/"+blocked;if(key==dropKey)return;dropKey=key;
            top.ShowDrop(c.Bundle,col.Key,blocked);bottom.ShowDrop(c.Bundle,col.Key,blocked);
            if(blocked)SetStatus(F("status.dropBlocked",TargetBundle(c)),true);
        }
        private void ClearDrop(){dropKey=null;top?.ClearDrop();bottom?.ClearDrop();}
        private bool EventEditable(SkillGraphLayout.Card c)=>EventEditable(model!=null&&model.Bundles.TryGetValue(TargetBundle(c),out var b)?b.Node(c.Node):null,TargetBundle(c));
        // Same rule as the backend moveNode: only nodes this layer defines (root nodes or '+') may change their event.
        public static bool EventEditable(SkillGraphModel.Node n,int target)=>n!=null&&n.DefinedIn==target;
        // Delete key: remove the selected nodes on this layer ('-' for inherited ones), one operation each.
        private void OnDelete(List<SkillGraphLayout.Card> cards)
        {
            int done=0;
            foreach(var c in cards.GroupBy(x=>(x.Bundle,x.Node)).Select(g=>g.First()))
            {
                if(!Apply(SkillGraphJson.Op("removeNode",("bundle",TargetBundle(c)),("node",c.Node))))break;
                done++;
            }
            if(done>0)SetStatus(F("status.removed",done),false);
        }
        private static int NewOrder(List<SkillGraphLayout.Card> siblings,float y)
        {
            if(siblings.Count==0)return 10;
            int i=siblings.Count(x=>x.Rect.center.y<y);
            if(i==0)return siblings[0].Order-10;if(i==siblings.Count)return siblings[i-1].Order+10;
            int a=siblings[i-1].Order,b=siblings[i].Order;return b-a>1?a+(b-a)/2:a+1;
        }
        private void OnAdd(SkillGraphLayout.Box box,SkillGraphLayout.Column col,Vector2 panelPos)
        {
            var screen=position.position+panelPos;
            var provider=CreateInstance<SkillGraphSearch>();provider.Init(this,box,col,top!=null&&box!=null&&bottomPane.worldBound.Contains(panelPos));
            SearchWindow.Open(new SearchWindowContext(screen),provider);
        }

        private void OnNodeMenu(SkillGraphLayout.Card c,DropdownMenu menu)
        {
            if(c.Removed){menu.AppendAction(T("node.restore"),_=>Apply(SkillGraphJson.Op("revert",("bundle",c.Bundle),("node",c.Node))));return;}
            // With a parent layer chosen, the menu writes to that layer (same target as the node tab, drag and Delete).
            int tb=TargetBundle(c);var b=model.Bundles[tb];var n=b.Node(c.Node)??Node(c);bool child=b.Parent!=null;
            if(n==null)return;
            bool overrides=n.Own&&!n.Added&&n.DefinedIn!=tb||n.Slots.Any(x=>x.Own);
            string parentName=child?SkillGraphLayout.Short(model.KeyName(b.Parent.Value),12)+" "+b.Parent:"";
            menu.AppendAction(child?F("node.revertTo",parentName):T("node.revertLayer"),_=>Apply(SkillGraphJson.Op("revert",("bundle",tb),("node",c.Node))),overrides&&!n.Added?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
            menu.AppendAction(child?F("node.applyToParentNamed",parentName):T("node.applyToParent"),_=>ApplyToParent(c,null),child&&(n.Added||overrides)?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
            if(selectedSlot!=null&&n.Slot(selectedSlot) is {} sv&&sv.Own)
            {
                menu.AppendAction(F("node.revertSlot",selectedSlot),_=>Apply(SkillGraphJson.Op("revert",("bundle",tb),("node",c.Node),("slot",selectedSlot))));
                menu.AppendAction(F("node.applySlot",selectedSlot),_=>ApplyToParent(c,selectedSlot),child?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
            }
            menu.AppendSeparator();
            menu.AppendAction(T("node.remove")+(n.DefinedIn!=tb?" "+T("node.removeInherited"):""),_=>Apply(SkillGraphJson.Op("removeNode",("bundle",tb),("node",c.Node))));
            menu.AppendAction(T("node.up"),_=>MoveOrder(c,-1));menu.AppendAction(T("node.down"),_=>MoveOrder(c,1));
            menu.AppendAction(T("node.memo"),_=>EditMemo(c));
            menu.AppendAction(T("node.saveCombo"),_=>SaveCombo(c));
            menu.AppendSeparator();
            menu.AppendAction(T("node.addHere"),_=>{var box=layout.Boxes.First(x=>x.Bundle==c.Bundle);OnAdd(box,box.Columns.First(x=>x.Key==c.Column),Vector2.zero);});
        }
        private void MoveOrder(SkillGraphLayout.Card c,int dir)
        {
            var siblings=layout.Cards.Where(x=>x.Bundle==c.Bundle&&x.Column==c.Column&&x.Presentation==c.Presentation&&!x.Removed).OrderBy(x=>x.Rect.y).ToList();
            int i=siblings.FindIndex(x=>x.Node==c.Node);int j=i+dir;if(i<0||j<0||j>=siblings.Count)return;
            int order=dir<0?(j==0?siblings[0].Order-10:(siblings[j-1].Order+siblings[j].Order)/2):(j==siblings.Count-1?siblings[j].Order+10:(siblings[j].Order+siblings[j+1].Order)/2);
            if(order==siblings[j].Order)order+=dir;
            Apply(SkillGraphJson.Op("moveNode",("bundle",TargetBundle(c)),("node",c.Node),("order",order)));
        }
        // Apply to parent: show the impact list first (every owner that expands the parent bundle).
        private void ApplyToParent(SkillGraphLayout.Card c,string slot)
        {
            int tb=TargetBundle(c);var b=model.Bundles[tb];if(b.Parent==null)return;var parent=model.Bundles[b.Parent.Value];
            var names=parent.UsedBy.Select(k=>model.OwnerOf(k)).Where(o=>o!=null).Select(o=>"· "+o.Key+" "+o.Name+(b.UsedBy.Contains(o.Key)?"":"  "+T("impact.changes"))).ToList();
            string text=F("impact.text",parent.Key,model.KeyName(parent.Key),names.Count,string.Join("\n",names));
            if(!EditorUtility.DisplayDialog(T("impact.title"),text,T("impact.apply"),T("cancel")))return;
            var op=SkillGraphJson.Op("applyToParent",("bundle",tb),("node",c.Node));if(slot!=null)op["slot"]=slot;Apply(op);
        }

        // ---------------------------------------------------------------- save, export, workspaces
        private void Save()
        {
            if(ops.Count==0)return;
            try
            {
                var res=Request("changes",ops);
                if(!res["ok"].Bool){EditorUtility.DisplayDialog(T("window.title"),res["error"].AsText(),T("ok"));return;}
                var blocked=res["blocked"].AsArray().Select(x=>x.AsText()).ToList();
                if(blocked.Count>0){EditorUtility.DisplayDialog(T("save.blockedTitle"),string.Join("\n\n",blocked)+"\n\n"+T("save.kept"),T("ok"));return;}
                if(res["errors"].AsInt()>0){EditorUtility.DisplayDialog(T("save.blockedTitle"),F("save.errors",res["errors"].AsInt()),T("ok"));return;}
                var lines=res["changes"].AsArray().Select(ChangeLine).ToList();if(res["aux"].Bool)lines.Add(T("save.auxLine"));
                SkillGraphSaveDialog.Show(this,lines,()=>DoSave());
            }
            catch(Exception x){EditorUtility.DisplayDialog(T("window.title"),F("save.prepareFailed",x.Message),T("ok"));}
        }
        public static string ChangeLine(JsonNode c)
        {
            string kind=c["kind"].AsText();string where=Path.GetFileNameWithoutExtension(c["doc"].AsText())+" › "+c["sheet"].AsText();
            if(kind=="change")return where+" "+F("save.row",SkillGraphModel.Text(c["row"]))+" "+c["column"].AsText()+": "+Show(c["old"])+" → "+Show(c["new"])+"   ("+c["key"].AsText()+")";
            if(kind=="insert")return where+" "+(c["after"].IsNull?T("save.insertTop"):F("save.insertAfter",SkillGraphModel.Text(c["after"])))+": "+c["key"].AsText();
            return where+" "+F("save.deleteRow",SkillGraphModel.Text(c["row"]))+": "+c["key"].AsText();
        }
        private static string Show(JsonNode v)=>v.IsNull?T("save.blank"):SkillGraphModel.Text(v);
        public bool DoSave()
        {
            var res=Request("save",ops);
            if(!res["ok"].Bool)
            {
                var blocked=res.Get("blocked") is JsonNode b?string.Join("\n\n",b.AsArray().Select(x=>x.AsText())):res.Get("error")?.TextOr(null)??T("save.failed");
                EditorUtility.DisplayDialog(T("save.failed"),blocked+"\n\n"+T("save.kept"),T("ok"));return false;
            }
            baseFiles=res["base"].Fields.ToDictionary(k=>k.Key,k=>k.Value.AsText());ops.Clear();redo.Clear();SaveSession();
            SetStatus(F("save.done",string.Join(", ",res["written"].AsArray().Select(x=>x.AsText()))),false);
            Refresh();return true;
        }
        // Writes the logic / presentation JSON with the Python exporter (product rules).
        private void ExportData()
        {
            string folder=EditorUtility.SaveFolderPanel(T("export.pick"),Path.GetFullPath("Assets"),"SkillData");
            if(string.IsNullOrEmpty(folder))return;
            try{string result=RunPython("export --out \""+folder+"\"");AssetDatabase.Refresh();SetStatus(F("export.done",folder),false);Debug.Log("[SkillGraph] "+result);}
            catch(Exception x){EditorUtility.DisplayDialog(T("menu.export"),x.Message,T("ok"));}
        }
        public static string RunPython(string args)
        {
            var info=new System.Diagnostics.ProcessStartInfo(SkillGraphPaths.PythonPath,"-X utf8 -B -m skillgraph "+args+" --config \""+SkillGraphPaths.ConfigPath+"\"")
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetFullPath(".")};
            info.EnvironmentVariables["PYTHONPATH"]=SkillGraphPaths.PythonFolder;info.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"]="1";info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
            var p=new System.Diagnostics.Process{StartInfo=info};
            p.Start();var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(300000))throw new InvalidOperationException("skillgraph "+args+": timeout");
            if(p.ExitCode!=0)throw new InvalidOperationException("skillgraph "+args+":\n"+e.Result+o.Result);return o.Result;
        }
        private void SwitchWorkspace(string ws)
        {
            if(ops.Count>0&&!EditorUtility.DisplayDialog(T("workspace.title"),F("workspace.discard",ops.Count),T("workspace.switch"),T("cancel")))return;
            if(ws=="practice")
            {
                var res=SkillGraphBackend.Shared.Request(new Dictionary<string,object>{{"cmd","practice"},{"action","create"}});
                if(!res["ok"].Bool){EditorUtility.DisplayDialog(T("toolbar.practice"),res["error"].AsText(),T("ok"));return;}
            }
            s.workspace=ws;ops.Clear();redo.Clear();baseFiles=null;selected=null;SaveSession();Refresh();
        }
        private void ResetPractice()
        {
            if(!EditorUtility.DisplayDialog(T("menu.resetPractice"),T("workspace.resetText"),T("workspace.reset"),T("cancel")))return;
            var res=SkillGraphBackend.Shared.Request(new Dictionary<string,object>{{"cmd","practice"},{"action","reset"}});
            if(!res["ok"].Bool){EditorUtility.DisplayDialog(T("toolbar.practice"),res["error"].AsText(),T("ok"));return;}
            if(s.workspace=="practice"){ops.Clear();redo.Clear();baseFiles=null;SaveSession();}Refresh();
        }
        private void Reload()
        {
            if(ops.Count>0&&!EditorUtility.DisplayDialog(T("menu.reload"),F("workspace.reloadText",ops.Count),T("workspace.reload"),T("cancel")))return;
            ops.Clear();redo.Clear();baseFiles=null;SaveSession();Refresh();
        }
        // Run log overlay: JSON Lines with "bundle" and "node" per executed node (e.g. written by a runner hook).
        public static Dictionary<(int Bundle,int Node),int> CountRunLog(IEnumerable<string> lines)
        {
            var result=new Dictionary<(int,int),int>();
            foreach(var line in lines)
            {
                if(string.IsNullOrWhiteSpace(line))continue;var row=JsonNode.Parse(line);
                var key=(row["bundle"].AsInt(),row["node"].AsInt());result[key]=result.TryGetValue(key,out int n)?n+1:1;
            }
            return result;
        }
        private void BuildIssues()
        {
            issueList.Clear();if(model==null)return;
            int errors=model.Issues.Count(i=>i.Level=="error"),warnings=model.Issues.Count(i=>i.Level=="warning");
            issueTitle.text=F("issues.count",errors,warnings)+(model.DisplayStale?" · "+T("issues.stale"):"");
            if(model.Issues.Count==0)issueList.Add(Cls(new Label(T("issues.none")),"sg-issue"));
            foreach(var i in model.Issues.OrderBy(i=>i.Level=="error"?0:1))
            {
                var l=Cls(new Label((i.Level=="error"?"● ":"▲ ")+i.Code+" · "+i.Where+" · "+i.Message+(i.Bundle!=null?"  "+T("issues.goto"):"")),"sg-issue",i.Level=="error"?"sg-issue--error":"sg-issue--warning");
                var issue=i;l.RegisterCallback<MouseDownEvent>(_=>GoTo(issue));issueList.Add(l);
            }
        }
        // Jump from the validation list: switch to an owner that uses the bundle, unfold it, frame the node.
        public void GoTo(SkillGraphModel.Issue i)
        {
            if(i.Bundle==null){if(i.Owner is int ok&&model.OwnerOf(ok)!=null)SelectOwner(ok);return;}
            int bundle=i.Bundle.Value;
            if(layout==null||!layout.Boxes.Any(b=>b.Bundle==bundle))
            {var o=model.Owners.FirstOrDefault(x=>x.Bundles.Contains(bundle)||x.Chain.Contains(bundle));if(o!=null)SelectOwner(o.Key);}
            if(layout.Boxes.FirstOrDefault(b=>b.Bundle==bundle) is {} box&&box.Collapsed){expanded.Add(bundle);Relayout();}
            var card=layout.Cards.FirstOrDefault(c=>c.Bundle==bundle&&(i.Node==null||c.Node==i.Node));
            if(card!=null){(card.Presentation?bottom:top).FrameCard(card);selected=card;s.tab="node";selectedSlot=i.Slot;BuildPanel();}
        }
        private void OnDisable(){SaveSession();LanguageChanged-=Rebuild;}
    }

    // Change list shown before writing Excel.
    public sealed class SkillGraphSaveDialog : EditorWindow
    {
        private List<string> lines;private Action save;private Vector2 scroll;
        // For the capture command: the last opened dialog and its button rectangles.
        internal static SkillGraphSaveDialog Last;internal readonly Dictionary<string,Rect> CaptureRects=new Dictionary<string,Rect>();
        public static void Show(EditorWindow owner,List<string> lines,Action save)
        {
            var w=CreateInstance<SkillGraphSaveDialog>();w.titleContent=new GUIContent(T("save.title"));w.lines=lines;w.save=save;
            w.position=new Rect(owner.position.x+80,owner.position.y+80,760,420);w.ShowUtility();Last=w;
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField(F("save.header",lines.Count),EditorStyles.boldLabel);
            scroll=EditorGUILayout.BeginScrollView(scroll);foreach(var l in lines)EditorGUILayout.LabelField(l,EditorStyles.wordWrappedLabel);EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(T("save.help"),MessageType.Info);
            EditorGUILayout.BeginHorizontal();GUILayout.FlexibleSpace();
            bool repaint=Event.current.type==EventType.Repaint;
            if(GUILayout.Button(T("toolbar.save"),GUILayout.Width(120))){var a=save;Close();a?.Invoke();}
            if(repaint)CaptureRects["save"]=GUILayoutUtility.GetLastRect();
            if(GUILayout.Button(T("cancel"),GUILayout.Width(120)))Close();
            if(repaint)CaptureRects["cancel"]=GUILayoutUtility.GetLastRect();
            EditorGUILayout.EndHorizontal();
        }
    }
}
