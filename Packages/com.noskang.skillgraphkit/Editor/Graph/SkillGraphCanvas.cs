using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // One graph view (top = logic, bottom = presentation). GraphView only zooms, pans, selects and drags;
    // boxes, cards and edges draw the SkillGraphLayout result. GraphView is an experimental Unity API,
    // so nothing outside this file depends on it.
    public sealed class SkillGraphCanvas : GraphView
    {
        public const string StyleSheetPath="Packages/com.noskang.skillgraphkit/Editor/Graph/SkillGraph.uss";
        public bool Bottom { get; }
        public SkillGraphLayout Layout { get; private set; }
        public event Action<SkillGraphLayout.Card> NodeSelected;
        public event Action<SkillGraphLayout.Card,string> SlotClicked;
        public event Action<SkillGraphLayout.Card,string> ChipClicked;
        public event Action<SkillGraphLayout.Card,Rect> NodeDropped;
        public event Action<SkillGraphLayout.Box,SkillGraphLayout.Column,Vector2> AddRequested;
        public event Action<SkillGraphLayout.Box> BoxToggled;
        public event Action<SkillGraphLayout.Card,DropdownMenu> NodeMenu;
        public event Action<List<SkillGraphLayout.Card>> DeleteRequested;
        public event Action<SkillGraphLayout.Card,Rect> NodeDragging;
        public event Action NodeDragEnded;
        public event Action<SkillGraphLayout.Box,SkillGraphLayout.Column,DropdownMenu> ColumnMenu;
        public event Action<SkillGraphLayout.Box> AddColumnRequested;
        private readonly List<CardElement> cards=new List<CardElement>();
        private readonly List<(SkillGraphLayout.Column Column,VisualElement Head)> heads=new List<(SkillGraphLayout.Column,VisualElement)>();
        private VisualElement highlight,drop;

        public SkillGraphCanvas(bool bottom)
        {
            Bottom=bottom;
            AddToClassList("sg-graph");if(bottom)AddToClassList("sg-graph--bottom");
            var sheet=AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);if(sheet!=null)styleSheets.Add(sheet);
            SetupZoom(0.2f,2f,0.1f,1f);
            this.AddManipulator(new ContentDragger());this.AddManipulator(new SelectionDragger());this.AddManipulator(new RectangleSelector());
            var grid=new GridBackground();Insert(0,grid);grid.StretchToParentSize();grid.AddToClassList("sg-grid");
            // Delete key = remove the selected nodes, as an edit operation (undoable). Elements are never deleted directly; cut is ignored.
            deleteSelection=(op,ask)=>
            {
                if(op=="Cut")return;
                var list=selection.OfType<CardElement>().Where(c=>!c.Card.Removed).Select(c=>c.Card).ToList();
                if(list.Count>0)DeleteRequested?.Invoke(list);
            };
            RegisterCallback<MouseUpEvent>(_=>EndDrag(),TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape)EndDrag();},TrickleDown.TrickleDown);
            graphViewChanged=change=>
            {
                change.elementsToRemove?.Clear();change.edgesToCreate?.Clear();
                if(change.movedElements!=null)foreach(var e in change.movedElements.OfType<CardElement>())NodeDropped?.Invoke(e.Card,e.GetPosition());
                return change;
            };
        }

        public void Show(SkillGraphLayout layout)
        {
            Layout=layout;var guard=graphViewChanged;graphViewChanged=null;DeleteElements(graphElements.ToList());graphViewChanged=guard;cards.Clear();heads.Clear();highlight?.RemoveFromHierarchy();highlight=null;ClearDrop();
            if(layout==null)return;
            foreach(var box in layout.Boxes)
            {
                if(Bottom&&box.Bottom.width<=0)continue;
                AddElement(new BoxElement(this,box,layout));
            }
            AddElement(new EdgesElement(layout.Edges.Where(x=>x.Bottom==Bottom).ToList(),Bottom?layout.BottomBounds:layout.TopBounds));
            foreach(var c in layout.Cards.Where(c=>c.Presentation==Bottom)){var e=new CardElement(this,c);cards.Add(e);AddElement(e);}
        }
        public IEnumerable<SkillGraphLayout.Card> Cards=>cards.Select(c=>c.Card);
        public bool CanDeleteSelected=>canDeleteSelection;
        public void Select(SkillGraphLayout.Card card)
        {
            ClearSelection();var e=cards.FirstOrDefault(c=>c.Card.Bundle==card.Bundle&&c.Card.Node==card.Node);if(e!=null)AddToSelection(e);
        }
        public void FrameCard(SkillGraphLayout.Card card)
        {
            var e=cards.FirstOrDefault(c=>c.Card.Bundle==card.Bundle&&c.Card.Node==card.Node);if(e==null)return;ClearSelection();AddToSelection(e);FrameSelection();
        }
        // The selected node's column gets a dashed frame in both graphs.
        public void HighlightColumn(int bundle,string column)
        {
            highlight?.RemoveFromHierarchy();highlight=ColumnFrame(bundle,column,"sg-column-highlight");
        }
        // While dragging a node, the target column glows in both graphs. blocked = the node cannot move to that column on this layer.
        public void ShowDrop(int bundle,string column,bool blocked)
        {
            ClearDrop();drop=ColumnFrame(bundle,column,"sg-column-drop");if(drop==null)return;
            drop.EnableInClassList("sg-column-drop--blocked",blocked);
            var glow=new VisualElement{pickingMode=PickingMode.Ignore};glow.AddToClassList("sg-column-drop__glow");drop.Add(glow);
        }
        public void ClearDrop(){drop?.RemoveFromHierarchy();drop=null;}
        private VisualElement ColumnFrame(int bundle,string column,string cls)
        {
            var box=Layout?.Boxes.FirstOrDefault(b=>b.Bundle==bundle&&!b.Collapsed);var col=box?.Columns.FirstOrDefault(c=>c.Key==column);
            if(col==null)return null;var r=Bottom?box.Bottom:box.Top;if(r.width<=0)return null;
            var e=new VisualElement{pickingMode=PickingMode.Ignore};e.AddToClassList(cls);
            e.style.position=Position.Absolute;e.style.left=col.X-4;e.style.top=r.y+(Bottom?SkillGraphLayout.BottomTitle-4:SkillGraphLayout.TitleHeight-4);
            e.style.width=SkillGraphLayout.NodeWidth+8;e.style.height=r.yMax-(r.y+(Bottom?SkillGraphLayout.BottomTitle:SkillGraphLayout.TitleHeight))-4;
            contentViewContainer.Add(e);e.SendToBack();return e;
        }
        private void EndDrag(){if(drop!=null)NodeDragEnded?.Invoke();}
        public void MarkSlots(Func<SkillGraphLayout.Card,SkillGraphLayout.RowView,bool> marked){foreach(var c in cards)c.Mark(marked);}
        // Hovering a value also marks the column heads that use it as their time.
        public void MarkColumns(Func<SkillGraphLayout.Column,bool> marked){foreach(var (col,head) in heads)head.EnableInClassList("sg-colhead--marked",marked!=null&&marked(col));}
        internal void RegisterHead(SkillGraphLayout.Column col,VisualElement head)=>heads.Add((col,head));
        // Used by the capture command to find a column head or its add button.
        internal VisualElement HeadOf(SkillGraphLayout.Column col)=>heads.FirstOrDefault(h=>ReferenceEquals(h.Column,col)).Head;
        public int MarkedColumnCount=>heads.Count(h=>h.Head.ClassListContains("sg-colhead--marked"));
        public void Dim(bool dim){EnableInClassList("sg-graph--dim",dim);}
        internal void RaiseSelected(SkillGraphLayout.Card c)=>NodeSelected?.Invoke(c);
        internal void RaiseSlot(SkillGraphLayout.Card c,string slot)=>SlotClicked?.Invoke(c,slot);
        internal void RaiseChip(SkillGraphLayout.Card c,string stage)=>ChipClicked?.Invoke(c,stage);
        internal void RaiseAdd(SkillGraphLayout.Box b,SkillGraphLayout.Column c,Vector2 screen)=>AddRequested?.Invoke(b,c,screen);
        internal void RaiseToggle(SkillGraphLayout.Box b)=>BoxToggled?.Invoke(b);
        internal void RaiseMenu(SkillGraphLayout.Card c,DropdownMenu m)=>NodeMenu?.Invoke(c,m);
        internal void RaiseDragging(SkillGraphLayout.Card c,Rect r)=>NodeDragging?.Invoke(c,r);
        internal void RaiseColumnMenu(SkillGraphLayout.Box b,SkillGraphLayout.Column c,DropdownMenu m)=>ColumnMenu?.Invoke(b,c,m);
        internal void RaiseAddColumn(SkillGraphLayout.Box b)=>AddColumnRequested?.Invoke(b);
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt){}
    }

    // Bundle box: title = group description, small key, named bands, column heads with '+'. Effects and buffs start as one-line boxes.
    internal sealed class BoxElement : GraphElement
    {
        public BoxElement(SkillGraphCanvas canvas,SkillGraphLayout.Box box,SkillGraphLayout layout)
        {
            capabilities=0;layer=-20;pickingMode=PickingMode.Position;
            bool bottom=canvas.Bottom;var r=bottom?box.Bottom:box.Top;var model=layout.Model;
            string role=model.KindRoles.FirstOrDefault(k=>k.Value==box.Kind).Key??"other";
            AddToClassList("sg-box");AddToClassList("sg-box--"+role);if(box.Collapsed)AddToClassList("sg-box--collapsed");if(bottom)AddToClassList("sg-box--bottom");
            SetPosition(r);style.width=r.width;style.height=r.height;
            if(box.Collapsed)
            {
                var row=new VisualElement();row.AddToClassList("sg-box__summary");
                row.Add(Label("▸ "+box.Title,"sg-box__summary-title"));row.Add(Label(box.KeyText,"sg-box__key"));
                if(!bottom)Add(row);
                var detail=Label(bottom?box.BottomSummary:box.Summary,"sg-box__summary-detail");
                if(bottom){row.Add(detail);Add(row);}else Add(detail);
                tooltip=T("box.expandTip");
                RegisterCallback<MouseDownEvent>(e=>{if(e.button==0){canvas.RaiseToggle(box);e.StopPropagation();}});
                return;
            }
            var title=new VisualElement();title.AddToClassList("sg-box__title");
            string tag=role=="other"?box.Kind:T("kind."+role);
            if(bottom)title.Add(Label(F("box.presentationTitle",box.Title),"sg-box__title-text"));
            else{title.Add(Label(tag,"sg-box__tag"));title.Add(Label(box.Title,"sg-box__title-text"));}
            title.Add(Label(box.KeyText,"sg-box__key"));if(!bottom&&box.Inherit!=null)title.Add(Label(box.Inherit,"sg-box__inherit"));
            if(!bottom)
            {
                var addc=Label(T("box.addColumn"),"sg-box__addcol");addc.tooltip=T("box.addColumnTip");
                addc.RegisterCallback<MouseDownEvent>(e=>{if(e.button==0){canvas.RaiseAddColumn(box);e.StopPropagation();}});title.Add(addc);
            }
            if(!box.Main){var fold=Label(T("box.fold"),"sg-box__fold");fold.RegisterCallback<MouseDownEvent>(e=>{canvas.RaiseToggle(box);e.StopPropagation();});title.Add(fold);}
            Add(title);
            float top=bottom?SkillGraphLayout.BottomTitle:SkillGraphLayout.TitleHeight;
            if(!bottom&&box.Bands.Count>0)
            {
                foreach(var (name,x0,x1) in box.Bands)
                {var band=Label(name,"sg-band");band.style.position=Position.Absolute;band.style.left=x0-r.x;band.style.width=x1-x0;band.style.top=top;Add(band);}
                top+=SkillGraphLayout.BandHeight;
            }
            if(bottom&&box.BottomCounts.Count>0&&layout.Cards.All(k=>k.Bundle!=box.Bundle||!k.Presentation))
                foreach(var (key,text) in box.BottomCounts)
                {
                    var col=box.Columns.First(x=>x.Key==key);var l=Label(text,"sg-bottom-count");l.style.position=Position.Absolute;l.style.left=col.X-r.x;l.style.top=top+SkillGraphLayout.BottomHeader+4;l.style.width=SkillGraphLayout.NodeWidth;Add(l);
                }
            foreach(var col in box.Columns)
            {
                var head=new VisualElement();head.AddToClassList("sg-colhead");if(bottom)head.AddToClassList("sg-colhead--bottom");if(col.Empty)head.AddToClassList("sg-colhead--empty");
                var column=col;head.AddManipulator(new ContextualMenuManipulator(evt=>{canvas.RaiseColumnMenu(box,column,evt.menu);evt.StopPropagation();}));
                head.tooltip=T("column.tip");
                head.style.position=Position.Absolute;head.style.left=col.X-r.x;head.style.top=top;head.style.width=SkillGraphLayout.NodeWidth;
                var text=new VisualElement();text.AddToClassList("sg-colhead__text");
                if(col.KeyframePart!=null)
                {
                    // Keyframe column: the keyframe name (and offset) in its own colour, ' : seconds' in normal text.
                    var line=new VisualElement{style={flexDirection=FlexDirection.Row}};var kf=Label(col.KeyframePart,"sg-colhead__label");kf.AddToClassList("sg-colhead__keyframe");
                    line.Add(kf);line.Add(Label(col.RestPart,"sg-colhead__label"));text.Add(line);
                }
                else text.Add(Label(col.Label,"sg-colhead__label"));
                if(!bottom&&!string.IsNullOrEmpty(col.Sub))text.Add(Label(col.Sub,"sg-colhead__sub"));
                canvas.RegisterHead(col,head);
                head.Add(text);
                var plus=Label(bottom?T("column.addPresentation"):"＋","sg-colhead__add");
                plus.tooltip=bottom?T("column.addPresentationTip"):T("column.addTip");
                plus.RegisterCallback<MouseDownEvent>(e=>{if(e.button==0){canvas.RaiseAdd(box,column,e.mousePosition);e.StopPropagation();}});
                head.Add(plus);Add(head);
            }
        }
        private static Label Label(string text,string cls){var l=new Label(text);l.AddToClassList(cls);return l;}
    }

    // Node card: family colour band and icon, slot rows (← value reference / ■ fixed / ▲ balance fixed in graph),
    // stage chips, '▸ N defaults', run count footer, pass/fail ports.
    internal sealed class CardElement : GraphElement
    {
        public SkillGraphLayout.Card Card { get; }
        private readonly SkillGraphCanvas canvas;private readonly bool placed;private readonly List<(SkillGraphLayout.RowView Row,VisualElement Element)> rows=new List<(SkillGraphLayout.RowView,VisualElement)>();
        public CardElement(SkillGraphCanvas canvas,SkillGraphLayout.Card c)
        {
            this.canvas=canvas;Card=c;capabilities=Capabilities.Selectable|Capabilities.Movable|Capabilities.Ascendable|(c.Removed?0:Capabilities.Deletable);layer=0;
            var model=canvas.Layout.Model;
            this.AddManipulator(new ContextualMenuManipulator(evt=>{canvas.RaiseMenu(Card,evt.menu);evt.StopPropagation();}));
            AddToClassList("sg-card");if(c.Removed)AddToClassList("sg-card--removed");if(c.Added)AddToClassList("sg-card--added");if(!c.Implemented)AddToClassList("sg-card--unimplemented");
            if(c.Errors>0)AddToClassList("sg-card--error");else if(c.Warnings>0)AddToClassList("sg-card--warning");
            SetPosition(c.Rect);placed=true;style.width=c.Rect.width;style.height=c.Height;
            var head=new VisualElement();head.AddToClassList("sg-card__head");head.style.backgroundColor=Color(c.Color,c.Removed?0.45f:1f);
            if(c.Added)head.Add(Badge("＋","sg-badge--added"));if(c.Removed)head.Add(Badge("−","sg-badge--removed"));
            head.Add(Text(c.Icon+" "+c.Title+(c.Suffix!=null?" ▸ "+c.Suffix:""),"sg-card__title"));
            if(c.English!=c.Title)head.Add(Text(c.English,"sg-card__english"));
            var spacer=new VisualElement{style={flexGrow=1}};head.Add(spacer);
            if(c.Inherited!=null){var tag=Badge(c.Inherited,"sg-badge--inherited");tag.AddToClassList("sg-tint-"+Math.Max(0,c.InheritedTint)%4);tag.tooltip=F("card.inheritedTip",c.Inherited);head.Add(tag);}
            if(c.Errors>0)head.Add(Badge("●"+c.Errors,"sg-badge--error"));else if(c.Warnings>0)head.Add(Badge("▲"+c.Warnings,"sg-badge--warning"));
            if(c.HasNote){var o=Badge("ⓘ","sg-badge--note");o.tooltip=T("card.noteTip");head.Add(o);}
            head.Add(Badge("#"+c.Order,"sg-badge--order"));
            Add(head);
            if(c.Removed){tooltip=T("card.removedTip");return;}
            if(c.Compact){Add(Text(c.Summary,"sg-card__summary"));tooltip=T("card.compactTip");return;}
            if(!c.Implemented)tooltip=T("card.unimplementedTip");
            if(c.Memo!=null)Add(Text("✎ "+c.Memo,"sg-card__memo"));
            foreach(var r in c.Rows)
            {
                var row=new VisualElement();row.AddToClassList("sg-row");
                if(r.Overridden)row.AddToClassList("sg-row--override");if(r.Missing)row.AddToClassList("sg-row--missing");if(r.Unread)row.AddToClassList("sg-row--unread");
                var label=Text((r.StageDot?"● ":"")+r.Label,"sg-row__label");if(r.StageDot)label.AddToClassList("sg-row__label--stage");row.Add(label);
                string kind=r.Symbol=="←"?"ref":r.Symbol=="■"?"fixed":r.Symbol=="▲"?"balance":r.Missing?"missing":"default";
                var value=Text(r.Symbol+" "+r.Text,"sg-row__value");value.AddToClassList("sg-row__value--"+kind);row.Add(value);
                if(r.Tag!=null)row.Add(Text(r.Tag,"sg-row__tag"));
                if(r.Unread)row.Add(Text(T("row.notRun"),"sg-row__tag"));
                row.tooltip=r.Label+" ("+r.Slot+")"+(r.Stage!=null?" · "+F("row.stage",r.Stage):"")+"\n"+r.Tooltip+(r.Unread?"\n"+T("row.notRunTip"):"");
                var slot=r.Slot;row.RegisterCallback<MouseDownEvent>(e=>{if(e.button==0&&e.clickCount==1){canvas.RaiseSlot(Card,slot);}});
                Add(row);rows.Add((r,row));
            }
            if(c.Chips.Count>0)
            {
                var chips=new VisualElement();chips.AddToClassList("sg-chips");
                foreach(var ch in c.Chips)
                {
                    string text=ch.Open?"▾ "+ch.Stage:ch.Unused?"＋"+ch.Stage:"▸ "+ch.Stage+" "+ch.Count;
                    var chip=Text(text,"sg-chip");if(ch.Unused)chip.AddToClassList("sg-chip--unused");if(ch.Advanced)chip.AddToClassList("sg-chip--advanced");if(ch.Open)chip.AddToClassList("sg-chip--open");
                    chip.tooltip=ch.Unused?T("chip.unusedTip"):ch.Open?T("chip.close"):F("chip.foldedTip",ch.Count,SkillGraphLayout.MaxRows);
                    var stage=ch.Stage;chip.RegisterCallback<MouseDownEvent>(e=>{if(e.button==0){canvas.RaiseChip(Card,stage);e.StopPropagation();}});
                    chips.Add(chip);
                }
                if(c.DefaultsInChips&&c.Defaults>0)chips.Add(Text(F("card.defaults",c.Defaults),"sg-chips__defaults"));
                Add(chips);
            }
            bool foot=c.Defaults>0&&!c.DefaultsInChips;
            if(foot||c.Runs!=null)Add(Text((foot?F("card.defaults",c.Defaults):"")+(c.Runs!=null?(foot?" · ":"")+F("card.runs",c.Runs):""),"sg-card__foot"));
            if(c.Exits.Length>0&&model.ExitEvents.Length==2)
            {
                var ports=new VisualElement();ports.AddToClassList("sg-ports");
                var pass=Text(model.EventLabel(model.ExitEvents[0])+" ●","sg-port");pass.AddToClassList("sg-port--pass");
                var fail=Text(model.EventLabel(model.ExitEvents[1])+" ○","sg-port");fail.AddToClassList("sg-port--fail");
                ports.Add(pass);ports.Add(fail);Add(ports);
            }
        }
        // After the first placement, SetPosition calls come from dragging (SelectionDragger).
        public override void SetPosition(Rect p){base.SetPosition(p);if(placed)canvas.RaiseDragging(Card,p);}
        public override void OnSelected(){base.OnSelected();AddToClassList("sg-card--selected");canvas.RaiseSelected(Card);}
        public override void OnUnselected(){base.OnUnselected();RemoveFromClassList("sg-card--selected");}
        public void Mark(Func<SkillGraphLayout.Card,SkillGraphLayout.RowView,bool> marked){foreach(var (r,e) in rows)e.EnableInClassList("sg-row--marked",marked!=null&&marked(Card,r));}
        private static Label Text(string text,string cls){var l=new Label(text);l.AddToClassList(cls);return l;}
        private static Label Badge(string text,string cls){var l=Text(text,"sg-badge");l.AddToClassList(cls);return l;}
        public static Color Color(string hex,float alpha){ColorUtility.TryParseHtmlString(hex,out var c);c.a=alpha;return c;}
    }

    // Spawn edges (node -> box) and exit edges (pass / fail port -> exit column), drawn as Painter2D curves.
    internal sealed class EdgesElement : GraphElement
    {
        private readonly List<SkillGraphLayout.Edge> edges;
        public EdgesElement(List<SkillGraphLayout.Edge> edges,Rect bounds)
        {
            this.edges=edges;capabilities=0;layer=-10;pickingMode=PickingMode.Ignore;
            var r=new Rect(bounds.x-50,bounds.y-50,bounds.width+100,bounds.height+100);SetPosition(r);style.width=r.width;style.height=r.height;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;var origin=GetPosition().position;
            foreach(var e in edges)
            {
                var a=e.From-origin;var b=e.To-origin;float dx=Mathf.Max(40,Mathf.Abs(b.x-a.x)*0.5f);
                p.strokeColor=e.Kind=="pass"?new Color(.36f,.82f,.45f,.9f):e.Kind=="fail"?new Color(.85f,.4f,.4f,.85f):e.Kind=="buff"?new Color(.6f,.45f,.9f,.85f):new Color(.75f,.78f,.85f,.75f);
                p.lineWidth=e.Kind=="pass"||e.Kind=="fail"?2f:1.6f;
                p.BeginPath();p.MoveTo(a);p.BezierCurveTo(a+new Vector2(dx,0),b-new Vector2(dx,0),b);p.Stroke();
                p.fillColor=p.strokeColor;p.BeginPath();p.MoveTo(b);p.LineTo(b+new Vector2(-8,-4));p.LineTo(b+new Vector2(-8,4));p.ClosePath();p.Fill();
            }
        }
    }
}
