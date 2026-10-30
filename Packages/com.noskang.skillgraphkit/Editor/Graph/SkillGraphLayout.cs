using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Layout as pure calculation (no UI), so EditMode tests can measure it.
    // Node anatomy (header, slot rows, stage chips, footer), event columns (timed columns in the viewed owner's time order),
    // exit columns placed as a staircase next to their check node, collapsed boxes for spawned effects and applied buffs,
    // and the same columns for the logic graph (top) and the presentation graph (bottom).
    // Every size is a constant, so the overlap-free placement is decided by arithmetic alone.
    public sealed class SkillGraphLayout
    {
        public const float NodeWidth=244,ColumnGap=14,BoxPad=12,TitleHeight=30,BandHeight=20,HeaderHeight=34,HeadRow=28,Row=18,ChipRow=22,FootRow=16,MemoRow=16,NodePad=8,NodeGap=12;
        public const float SummaryWidth=320,SummaryHeight=46,BoxGap=48,BottomTitle=24,BottomHeader=22,BottomSummaryHeight=28;
        public const int MaxRows=5;

        public sealed class Options
        {
            public int Owner;public int Layer;public HashSet<int> Expanded=new HashSet<int>();public HashSet<string> OpenChips=new HashSet<string>();
            public Dictionary<(int Bundle,int Node),int> Runs;public bool Presentation=true;
            // In the split view the bottom graph uses one-line presentation cards; folded shows only per-column counts.
            public bool CompactPresentation;public bool FoldPresentation;
            public static string Chip(int bundle,int node,string stage)=>bundle+"/"+node+"/"+stage;
        }
        public sealed class RowView { public string Slot,Label,Text,Symbol,Tag,Tooltip,Stage;public bool StageDot,Overridden,Unread,Missing,Balance; }
        public sealed class ChipView { public string Stage;public int Count;public bool Unused,Advanced,Open; }
        public sealed class Card
        {
            public int Bundle,Node,Order;public string Line,Module,Title,English,Suffix,Inherited,Memo,Column,Color,Icon;public bool Added,Removed,Implemented=true,HasNote,Presentation;
            public List<RowView> Rows=new List<RowView>();public List<ChipView> Chips=new List<ChipView>();public int Defaults,Errors,Warnings,InheritedTint=-1;public int? Runs;public string[] Exits=Array.Empty<string>();
            public bool Compact,DefaultsInChips;public string Summary;
            public Rect Rect;public float Height;
        }
        public sealed class Column { public string Key,Event,Label,Sub;public JsonNode EventValue;public int? From;public double Time;public float X;public int Index;public bool Empty;public string KeyframePart,RestPart; }
        // Column closest to a dragged node's centre (the same column is highlighted while dragging and used on drop).
        public static Column DropColumn(Box box,float centerX)=>box?.Columns.OrderBy(x=>Math.Abs(x.X+NodeWidth/2-centerX)).FirstOrDefault();
        public sealed class Box
        {
            public int Bundle;public string Kind,Title,KeyText,Inherit,Summary,BottomSummary;public bool Collapsed,Main;public Rect Top,Bottom;public List<Column> Columns=new List<Column>();
            public List<(string Name,float X0,float X1)> Bands=new List<(string,float,float)>();public List<(string Column,string Text)> BottomCounts=new List<(string,string)>();
        }
        public sealed class Edge { public Vector2 From,To;public string Kind;public bool Bottom;public int FromBundle,FromNode,ToBundle; }

        private readonly HashSet<(int,int)> removed=new HashSet<(int,int)>();
        public SkillGraphModel Model { get; }
        public SkillGraphModel.Owner Owner { get; }
        public List<Box> Boxes { get; } = new List<Box>();
        public List<Card> Cards { get; } = new List<Card>();
        public List<Edge> Edges { get; } = new List<Edge>();
        public Rect TopBounds,BottomBounds;

        public SkillGraphLayout(SkillGraphModel model,Options options)
        {
            Model=model;Owner=model.OwnerOf(options.Owner)??model.Owners.FirstOrDefault();
            if(Owner==null)return;
            var main=MakeBox(Owner.Bundle,true,options,0,0,0);
            float right=Math.Max(main.Top.xMax,main.Presentation(options)?main.Bottom.xMax:0)+BoxGap,topY=0,bottomY=0;
            foreach(int key in SideBundles())
            {
                if(!model.Bundles.ContainsKey(key))continue;
                var box=MakeBox(key,false,options,right,topY,bottomY);
                topY=box.Top.yMax+NodeGap;bottomY=box.Collapsed?bottomY+BottomSummaryHeight+8:box.Bottom.yMax+NodeGap;
            }
            MakeEdges();
            TopBounds=Union(Boxes.Select(b=>b.Top));BottomBounds=Union(Boxes.Where(b=>b.Bottom.width>0).Select(b=>b.Bottom));
        }

        // Value bundle that slot references in a box read: a buff box reads its buff's own values, everything else the viewed owner's.
        public int ValuesFor(int bundle)=>Model.Owners.FirstOrDefault(o=>o.Bundle==bundle&&o.Key!=Owner.Key&&o.Kind==Model.KindName("buff"))?.Values??Owner.Values;

        // Bundle a node points at: a spawned effect bundle or the bundle of an applied buff.
        public int? Target(SkillGraphModel.Node n,out string kind)
        {
            kind=null;
            if(n.Module==Model.SpawnModule){var s=n.Slot(Model.SpawnSlot);if(s!=null&&s.Source=="fixed"&&s.Value.Kind==JsonKind.Number){kind="spawn";return s.Value.AsInt();}}
            if(n.Module==Model.StatusModule){var s=n.Slot(Model.StatusSlot);if(s!=null&&s.Source=="fixed"&&s.Value.Kind==JsonKind.Number){kind="buff";return Model.OwnerOf(s.Value.AsInt())?.Bundle;}}
            return null;
        }

        // Effects the owner's bundle spawns (in spawn order, including what effects spawn) and buffs it applies.
        public List<int> SideBundles()
        {
            var order=new List<int>();var todo=new Queue<int>();todo.Enqueue(Owner.Bundle);var seen=new HashSet<int>{Owner.Bundle};
            while(todo.Count>0)
            {
                var b=Model.Bundles.TryGetValue(todo.Dequeue(),out var bb)?bb:null;if(b==null)continue;
                foreach(var n in b.Nodes.Where(n=>!n.Presentation).OrderBy(n=>ColumnIndexHint(n)).ThenBy(n=>n.Priority).ThenBy(n=>n.Order))
                    if(Target(n,out _) is int t&&seen.Add(t)){order.Add(t);todo.Enqueue(t);}
            }
            return order;
        }
        private int ColumnIndexHint(SkillGraphModel.Node n)=>Model.Events.IndexOf(n.Event)*1000+(int)Math.Min(999,Math.Max(0,Time(n)*100));

        private Box MakeBox(int key,bool main,Options o,float x,float topY,float bottomY)
        {
            var b=Model.Bundles[key];
            var box=new Box{Bundle=key,Kind=b.Kind,Main=main,Collapsed=!main&&!o.Expanded.Contains(key),Title=string.IsNullOrEmpty(b.Desc)?b.Kind+" "+key:b.Desc,KeyText=key.ToString(CultureInfo.InvariantCulture)};
            if(b.Parent is int p)box.Inherit=F("box.inherits",Short(Model.KeyName(p)),p);
            Boxes.Add(box);
            var logic=b.Nodes.Where(n=>!n.Presentation).ToList();var pres=b.Nodes.Where(n=>n.Presentation).ToList();
            // Nodes this bundle removed ('-') stay in their column as faded parent cards.
            var ghosts=new List<SkillGraphModel.Node>();
            if(b.Parent is int parentKey&&Model.Bundles.TryGetValue(parentKey,out var pb))
                foreach(var r in b.Removed)
                    if(pb.Node(r.Id) is {} pn)
                    {
                        ghosts.Add(new SkillGraphModel.Node{Bundle=key,Id=pn.Id,Line=pn.Line,Event=pn.Event,EventValue=pn.EventValue,FromNode=pn.FromNode,Order=pn.Order,Priority=pn.Priority,
                            Module=pn.Module,Note=pn.Note,DefinedIn=pn.DefinedIn,Slots=pn.Slots});
                        removed.Add((key,pn.Id));
                    }
            if(box.Collapsed)
            {
                box.Summary=Summary(b,logic);
                box.Top=new Rect(x,topY,SummaryWidth,SummaryHeight);
                box.BottomSummary=Counts(pres);
                box.Bottom=new Rect(x,bottomY,SummaryWidth,BottomSummaryHeight);
                return box;
            }
            box.Columns=Columns(b,ghosts);
            float bands=Model.Bands.TryGetValue(key,out var bl)&&bl.Count>0?BandHeight:0;
            float colTop=topY+TitleHeight+bands+HeaderHeight;
            for(int i=0;i<box.Columns.Count;i++){box.Columns[i].X=x+BoxPad+i*(NodeWidth+ColumnGap);box.Columns[i].Index=i;}
            // At least two columns wide, so the title, key and inherit label never run past the box.
            int span=Math.Max(2,box.Columns.Count);
            float width=BoxPad*2+span*NodeWidth+(span-1)*ColumnGap;
            float logicBottom=Place(box,b,logic.Concat(ghosts.Where(g=>!g.Presentation)).ToList(),colTop,o,false);
            box.Top=new Rect(x,topY,width,Math.Max(logicBottom-topY,TitleHeight+bands+HeaderHeight+40)+BoxPad);
            if(bl!=null)foreach(var band in bl){var from=box.Columns.FirstOrDefault(c=>c.Key==band.From);var to=box.Columns.LastOrDefault(c=>c.Key==band.To);if(from!=null&&to!=null)box.Bands.Add((band.Name,from.X,to.X+NodeWidth));}
            if(o.Presentation&&o.FoldPresentation)
                box.Bottom=new Rect(x,bottomY,width,BottomTitle+BottomHeader+BottomSummaryHeight);
            else if(o.Presentation)
            {
                float pTop=bottomY+BottomTitle+BottomHeader;
                float pBottom=Place(box,b,pres.Concat(ghosts.Where(g=>g.Presentation)).ToList(),pTop,o,true);
                box.Bottom=new Rect(x,bottomY,width,Math.Max(pBottom-bottomY,BottomTitle+BottomHeader+30)+BoxPad);
            }
            if(o.Presentation)
                foreach(var c in box.Columns)
                {
                    var here=pres.Where(n=>ColumnKey(n)==c.Key).ToList();
                    if(here.Count>0)box.BottomCounts.Add((c.Key,Counts(here)));
                }
            return box;
        }
        private string Counts(IEnumerable<SkillGraphModel.Node> nodes)=>string.Join("  ",nodes.GroupBy(n=>n.Module).Select(g=>Model.FamilyOf(g.Key).Icon+" "+ShortModule(g.Key)+" ×"+g.Count()));

        // Stack nodes per column by priority and order. The first node of an exit column lines up with its check node.
        private float Place(Box box,SkillGraphModel.Bundle b,List<SkillGraphModel.Node> nodes,float top,Options o,bool presentation)
        {
            float bottom=top;var cursor=box.Columns.ToDictionary(c=>c.Key,c=>top);
            foreach(var c in box.Columns)
            {
                var here=nodes.Where(n=>ColumnKey(n)==c.Key).OrderBy(n=>n.Priority).ThenBy(n=>n.Order).ThenBy(n=>n.Id).ToList();
                float y=cursor[c.Key];
                if(!presentation&&c.From is int from)
                {
                    var src=Cards.FirstOrDefault(k=>k.Bundle==b.Key&&k.Node==from&&!k.Presentation);
                    if(src!=null)y=Math.Max(y,src.Rect.y);
                }
                foreach(var n in here)
                {
                    var card=removed.Contains((n.Bundle,n.Id))&&!b.Nodes.Contains(n)?Ghost(n):Build(n,o);card.Column=c.Key;card.Rect=new Rect(c.X,y,NodeWidth,card.Height);Cards.Add(card);
                    y+=card.Height+NodeGap;
                }
                cursor[c.Key]=y;bottom=Math.Max(bottom,y);
            }
            return bottom;
        }

        public List<Column> Columns(SkillGraphModel.Bundle b,IEnumerable<SkillGraphModel.Node> extra=null)
        {
            var map=new Dictionary<string,Column>();
            void Add(string key,string e,JsonNode value,int? from)
            {
                if(map.ContainsKey(key))return;
                var c=new Column{Key=key,Event=e,EventValue=value,From=from};
                c.Label=Model.EventLabel(e);
                if(Model.IsTimed(e)&&SkillGraphModel.IsKeyframeRef(value))
                {
                    if(Model.TryKeyframe(b.Key,value.Text,out var mk,out var k,out double off))
                    {
                        c.Time=k.Seconds+off;c.Label=Model.KeyframeLabel(k.Name,k.Seconds,off);(c.KeyframePart,c.RestPart)=Model.KeyframeParts(k.Name,k.Seconds,off);
                        c.Sub=F("column.keyframe",mk.Clip,k.Frame)+(Math.Abs(off)>1e-9?" ("+Model.Seconds(k.Seconds)+" "+(off>0?"+":"")+Model.Seconds(off)+")":"")
                            +(mk.Start>0?" "+F("column.motionStart",Model.Seconds(mk.Start)):"");
                    }
                    else{c.Time=0;c.Label=value.Text.Substring(value.Text.IndexOf(':')+1)+" : ?";c.Sub=F("column.keyframeMissing",value.Text);}
                }
                else if(Model.IsTimed(e)){c.Time=ResolveTime(value);c.Label=Model.Seconds(c.Time);c.Sub=value!=null&&value.Kind==JsonKind.String?"← "+value.Text:Model.EventLabel(e);}
                else if(Model.IsExit(e)&&from is int f)
                {
                    var src=b.Node(f);string what=src==null?"#"+f:Model.ModuleLabel(src.Module)+"#"+f;
                    c.Label=what+" "+Model.EventLabel(e);c.Sub=e+" ← #"+f;
                }
                else if(Model.IsSubscription(e))c.Sub=e+" · "+SkillGraphModel.Text(value);
                else c.Sub=e;
                map[key]=c;
            }
            foreach(var e in Model.BaseColumns(b.Kind))Add(e,e,null,null);
            foreach(var n in b.Nodes.Concat(extra??Enumerable.Empty<SkillGraphModel.Node>()))Add(ColumnKey(n),n.Event,n.EventValue,n.FromNode);
            // Empty columns kept in the aux file by this bundle or a parent layer (the backend drops them once a node moves in).
            foreach(int k in b.Chain.Length>0?b.Chain:new[]{b.Key})
                if(Model.EmptyColumns.TryGetValue(k,out var empties))
                    foreach(var (e,v,f) in empties)
                    {
                        string key=ColumnKey(e,v,f);if(map.ContainsKey(key))continue;
                        Add(key,e,v,f);map[key].Empty=true;map[key].Sub=(string.IsNullOrEmpty(map[key].Sub)?"":map[key].Sub+" · ")+T("column.empty");
                    }
            var basic=map.Values.Where(c=>c.From==null).OrderBy(c=>Model.Events.IndexOf(c.Event)).ThenBy(c=>c.Time).ThenBy(c=>c.Key,StringComparer.Ordinal).ToList();
            var exits=map.Values.Where(c=>c.From!=null).ToList();
            var ordered=new List<Column>();var placed=new HashSet<string>();
            void Emit(Column c)
            {
                if(!placed.Add(c.Key))return;ordered.Add(c);
                var sources=b.Nodes.Where(n=>ColumnKey(n)==c.Key&&!n.Presentation).OrderBy(n=>n.Priority).ThenBy(n=>n.Order).ThenBy(n=>n.Id).Select(n=>n.Id).ToList();
                foreach(int s in sources)foreach(var x in exits.Where(x=>x.From==s).OrderBy(x=>Array.IndexOf(Model.ExitEvents,x.Event)))Emit(x);
            }
            foreach(var c in basic)Emit(c);
            foreach(var c in exits)Emit(c);
            return ordered;
        }
        public string ColumnKey(SkillGraphModel.Node n)=>ColumnKey(n.Event,n.EventValue,n.FromNode);
        public string ColumnKey(string ev,JsonNode value,int? from)=>ColumnKey(Model,ev,value,from);
        public static string ColumnKey(SkillGraphModel model,string ev,JsonNode value,int? from)
        {
            if(model.IsTimed(ev)||model.IsSubscription(ev))return ev+":"+SkillGraphModel.Text(value);
            if(model.IsExit(ev))return ev+":"+from;
            return ev;
        }
        public double Time(SkillGraphModel.Node n)=>Model.IsTimed(n.Event)?(SkillGraphModel.IsKeyframeRef(n.EventValue)?(Model.TryKeyframe(n.Bundle,n.EventValue.Text,out _,out var k)?k.Seconds:0):ResolveTime(n.EventValue)):0;
        // Timed columns follow the viewed owner's values (a tier can reorder them).
        public double ResolveTime(JsonNode v)
        {
            if(v==null||v.IsNull)return 0;
            if(v.Kind==JsonKind.Number)return v.Number;
            var e=Model.ResolveValue(Owner.Values,v.Text);
            return e!=null&&e.Value.Kind==JsonKind.Number?e.Value.Number:0;
        }

        // ---- node anatomy
        public Card Build(SkillGraphModel.Node n,Options o)
        {
            var m=Model.Module(n.Module);var fam=Model.FamilyOf(n.Module);
            var card=new Card{Bundle=n.Bundle,Node=n.Id,Order=n.Order,Line=n.Line,Module=n.Module,Title=Model.ModuleLabel(n.Module),English=n.Module,Added=n.Added,Presentation=n.Presentation,
                Color=fam.Color,Icon=fam.Icon,Implemented=m?.Implemented??false,HasNote=!string.IsNullOrEmpty(n.Note)};
            if(n.DefinedIn!=n.Bundle){card.Inherited=Short(Model.KeyName(n.DefinedIn));card.InheritedTint=Array.IndexOf(Model.Bundles.TryGetValue(n.Bundle,out var bb)?bb.Chain:Array.Empty<int>(),n.DefinedIn);}
            if(Model.Memos.TryGetValue(n.Bundle,out var memos)&&memos.TryGetValue(n.Id,out var memo))card.Memo=memo;
            if(o.Runs!=null)card.Runs=o.Runs.TryGetValue((n.Bundle,n.Id),out int r)?r:0;
            card.Errors=Model.Issues.Count(i=>i.Bundle==n.Bundle&&i.Node==n.Id&&i.Level=="error");card.Warnings=Model.Issues.Count(i=>i.Bundle==n.Bundle&&i.Node==n.Id&&i.Level=="warning");
            if(m==null){card.Height=HeadRow+NodePad;return card;}
            card.Exits=m.Exits;
            if(n.Presentation&&o.CompactPresentation)
            {
                card.Compact=true;card.Summary=CompactSummary(m,n);
                if(m.KindSlot!=null){var kv=n.Slot(m.KindSlot);var info=m.Slot(m.KindSlot);if(kv!=null&&kv.Source!="default")card.Suffix=Model.EnumLabel(info.Type,SkillGraphModel.Text(kv.Value));}
                card.Height=HeadRow+Row+NodePad;return card;
            }
            var applicable=Applicable(m,n);
            bool Set(string s){var v=n.Slot(s);return v!=null&&(v.Source!="default"||v.Own);}
            // Choose-one group: only the chosen slot shows, the others hide.
            if(m.Choose.Length>0){var chosen=m.Choose.Where(Set).ToList();foreach(var s in m.Choose.Except(chosen))applicable.Remove(s);if(chosen.Count==1)card.Suffix=Label(m.Slot(chosen[0]));}
            if(m.KindSlot!=null&&card.Suffix==null)
            {
                var kv=n.Slot(m.KindSlot);var info=m.Slot(m.KindSlot);
                if(kv!=null&&(kv.Source!="default"||info.Required))card.Suffix=Model.EnumLabel(info.Type,SkillGraphModel.Text(kv.Value));
            }
            bool overflow=false;int rows=0;
            for(int si=0;si<m.Stages.Count;si++)
            {
                var st=m.Stages[si];bool core=si==0;
                var slots=st.Slots.Where(applicable.Contains).ToList();if(slots.Count==0)continue;
                var shown=slots.Where(s=>Set(s)||core&&m.Slot(s).Required).ToList();
                bool open=o.OpenChips.Contains(Options.Chip(n.Bundle,n.Id,st.Name));
                if(core){foreach(var s in shown)card.Rows.Add(RowFor(m,n,s,false,o.Layer));rows+=shown.Count;continue;}
                if(shown.Count==0){card.Chips.Add(new ChipView{Stage=st.Name,Unused=true,Advanced=st.Advanced,Open=open});if(open){foreach(var s in slots)card.Rows.Add(RowFor(m,n,s,true,o.Layer));}continue;}
                if(!open&&(overflow||rows+shown.Count>MaxRows)){overflow=true;card.Chips.Add(new ChipView{Stage=st.Name,Count=shown.Count,Advanced=st.Advanced});continue;}
                foreach(var s in open?slots:shown)card.Rows.Add(RowFor(m,n,s,true,o.Layer));rows+=shown.Count;
                if(open)card.Chips.Add(new ChipView{Stage=st.Name,Count=shown.Count,Advanced=st.Advanced,Open=true});
            }
            var visible=new HashSet<string>(card.Rows.Select(r=>r.Slot));
            card.Defaults=applicable.Count(s=>!visible.Contains(s)&&!Set(s));
            // With a chip row, '▸ N defaults' sits at its end (saves a row). With run counts it goes to the footer.
            card.DefaultsInChips=card.Chips.Count>0&&card.Runs==null&&card.Chips.Count<=3;
            card.Height=HeadRow+(card.Memo!=null?MemoRow:0)+card.Rows.Count*Row+(card.Chips.Count>0?ChipRow:0)+((card.Defaults>0&&!card.DefaultsInChips)||card.Runs!=null?FootRow:0)+(card.Exits.Length>0?Row:0)+NodePad;
            return card;
        }

        private Card Ghost(SkillGraphModel.Node n)
        {
            var fam=Model.FamilyOf(n.Module);
            return new Card{Bundle=n.Bundle,Node=n.Id,Order=n.Order,Line=n.Line,Module=n.Module,Title=Model.ModuleLabel(n.Module),English=n.Module,Removed=true,Presentation=n.Presentation,
                Color=fam.Color,Icon=fam.Icon,Inherited=Short(Model.KeyName(n.DefinedIn)),Height=HeadRow+NodePad};
        }

        private static string Label(SkillGraphModel.SlotInfo s)=>s.Label.Get(Language) is {Length:>0} l?l:s.Name;

        // One-line card summary: the first key slot with its name, up to two other set slots, and the count of set slots.
        private string CompactSummary(SkillGraphModel.ModuleInfo m,SkillGraphModel.Node n)
        {
            var parts=new List<string>();
            var set=n.Slots.Where(s=>s.Source!="default").ToList();
            var key=set.FirstOrDefault(s=>m.Slot(s.Name)?.Type.StartsWith("key:",StringComparison.Ordinal)==true&&s.Value.Kind==JsonKind.Number);
            if(key!=null){int k=(int)key.Value.Number;parts.Add(k+" "+Short(Model.KeyName(k),12));}
            foreach(var s in set.Where(s=>s!=key&&s.Name!=m.KindSlot).Take(2))
            {
                var info=m.Slot(s.Name);if(info==null)continue;
                var value=s.Source=="ref"?Model.ResolveValue(ValuesFor(n.Bundle),SkillGraphModel.Text(s.Value))?.Value:s.Value;
                parts.Add(Label(info)+" "+ValueText(info,value));
            }
            parts.Add(F("card.setSlots",set.Count));
            return string.Join(" · ",parts);
        }

        // Slots that apply to the node's kind value and event. Slots outside every kind rule always show.
        public static HashSet<string> Applicable(SkillGraphModel.ModuleInfo m,SkillGraphModel.Node n)
        {
            var all=new HashSet<string>(m.Slots.Select(s=>s.Name));
            if(m.KindSlot==null||m.KindRules.Count==0)return all;
            var kv=n.Slot(m.KindSlot);string value=kv==null?null:SkillGraphModel.Text(kv.Value);
            var rule=m.KindRules.FirstOrDefault(r=>(r.Values.Length==0||r.Values.Contains(value))&&(r.Events.Length==0||r.Events.Contains(n.Event)));
            if(rule==null)return all;
            var controlled=new HashSet<string>(m.KindRules.SelectMany(r=>r.Slots));controlled.ExceptWith(rule.Slots);
            all.ExceptWith(controlled);return all;
        }

        public RowView RowFor(SkillGraphModel.ModuleInfo m,SkillGraphModel.Node n,string slot,bool stageDot,int layer)
        {
            var info=m.Slot(slot);var v=n.Slot(slot);
            var row=new RowView{Slot=slot,Label=Label(info),StageDot=stageDot,Unread=m.Unused.Contains(slot),Balance=info.Balance,Tooltip=info.Description.Get(Language),
                Stage=m.Stages.FirstOrDefault(s=>s.Slots.Contains(slot))?.Name};
            row.Overridden=v!=null&&v.DefinedIn==layer&&n.DefinedIn!=layer&&layer!=0;
            if(v==null||v.Source=="default")
            {
                if(info.Required){row.Missing=true;row.Symbol="!";row.Text=T("row.required");}
                else{row.Symbol="·";row.Text=ValueText(info,v?.Value??info.Default)+" "+(v!=null&&v.Own?T("row.toDefault"):T("row.default"));}
                return row;
            }
            if(v.Source=="ref")
            {
                row.Symbol="←";string name=SkillGraphModel.Text(v.Value);int values=ValuesFor(n.Bundle);var e=Model.ResolveValue(values,name);
                row.Text=name+" "+(e==null?T("row.missing"):ValueText(info,e.Value));
                if(e!=null&&values==Owner.Values&&Owner.Stage!=null&&Owner.ValueChain.Length>1&&e.DefinedIn==Owner.Values)row.Tag=F("row.tierTag",Owner.Stage.Level);
                if(e!=null&&!string.IsNullOrEmpty(e.Desc))row.Tooltip=e.Desc+"\n— "+info.Description.Get(Language);
                return row;
            }
            row.Symbol=info.Balance&&!info.Type.StartsWith("key:",StringComparison.Ordinal)&&!info.Type.StartsWith("enum:",StringComparison.Ordinal)?"▲":"■";
            row.Text=ValueText(info,v.Value);
            return row;
        }
        public string ValueText(SkillGraphModel.SlotInfo info,JsonNode value)
        {
            if(value==null||value.IsNull)return "—";
            if(info.Type.StartsWith("enum:",StringComparison.Ordinal)||info.Type.StartsWith("list:enum:",StringComparison.Ordinal))
                return string.Join(",",SkillGraphModel.Text(value).Split(',').Select(x=>Model.EnumLabel(info.Type,x.Trim())));
            if(info.Type.StartsWith("key:",StringComparison.Ordinal)&&value.Kind==JsonKind.Number){int k=(int)value.Number;string name=Model.KeyName(k);return k+(name.Length>0?" "+Short(name,14):"");}
            if(info.Type=="bool"||info.Unit=="0/1")return (value.Kind==JsonKind.Bool?value.Bool:value.Kind==JsonKind.Number&&value.Number!=0)?T("value.on"):T("value.off");
            if(value.Kind==JsonKind.String&&!info.Type.StartsWith("time",StringComparison.Ordinal))return value.Text;
            string text=SkillGraphModel.Text(value);
            if(value.Kind!=JsonKind.Number)return text;
            if(info.Type=="time")return Model.Seconds(value.Number);
            return info.Unit.Length>0&&info.Unit.Length<=4?text+info.Unit:text;
        }

        private string Summary(SkillGraphModel.Bundle b,List<SkillGraphModel.Node> logic)
        {
            var parts=new List<string>();
            if(b.Parent is int p)parts.Add(F("box.inheritsShort",Short(Model.KeyName(p),10)));
            // Up to two balance values the effect sets (what a designer usually looks for: damage, lifetime, ...).
            foreach(var n in logic)
            {
                var m=Model.Module(n.Module);if(m==null)continue;
                foreach(var s in n.Slots.Where(s=>s.Source!="default"))
                {
                    var info=m.Slot(s.Name);
                    if(info==null||!info.Balance||parts.Count>=3)continue;
                    var value=s.Source=="ref"?Model.ResolveValue(ValuesFor(b.Key),SkillGraphModel.Text(s.Value))?.Value:s.Value;
                    parts.Add(Label(info)+" "+ValueText(info,value));
                }
            }
            int added=b.Nodes.Count(n=>n.Added);
            parts.Add(F("box.nodes",logic.Count)+(added>0?" (+"+added+")":""));
            return string.Join(" · ",parts);
        }

        private void MakeEdges()
        {
            foreach(var card in Cards.Where(c=>!c.Presentation))
            {
                var b=Model.Bundles[card.Bundle];var n=b.Node(card.Node);if(n==null)continue;
                if(Target(n,out string kind) is int t&&Boxes.FirstOrDefault(x=>x.Bundle==t) is {} box)
                    Edges.Add(new Edge{From=new Vector2(card.Rect.xMax,card.Rect.y+HeadRow/2),To=new Vector2(box.Top.x,box.Top.y+(box.Collapsed?SummaryHeight/2:TitleHeight/2)),Kind=kind,FromBundle=card.Bundle,FromNode=card.Node,ToBundle=t});
            }
            // Exit edges: the pass / fail port of a check node -> the first node of that exit column.
            foreach(var box in Boxes.Where(x=>!x.Collapsed))
                foreach(var col in box.Columns.Where(c=>c.From!=null))
                {
                    var src=Cards.FirstOrDefault(c=>c.Bundle==box.Bundle&&c.Node==col.From&&!c.Presentation);
                    var first=Cards.Where(c=>c.Bundle==box.Bundle&&c.Column==col.Key&&!c.Presentation).OrderBy(c=>c.Rect.y).FirstOrDefault();
                    bool pass=Array.IndexOf(Model.ExitEvents,col.Event)==0;
                    if(src!=null&&first!=null)Edges.Add(new Edge{From=PortPosition(src,pass),To=new Vector2(first.Rect.x,first.Rect.y+HeadRow/2),Kind=pass?"pass":"fail",FromBundle=box.Bundle,FromNode=src.Node,ToBundle=box.Bundle});
                }
        }
        // Exit ports sit on the node's last row: ● pass on the right, ○ fail at the far right.
        public static Vector2 PortPosition(Card c,bool pass)=>new Vector2(c.Rect.xMax-(pass?60:6),c.Rect.yMax-NodePad-Row/2);

        // ---- rulers used by tests
        public (int Max,double Average) RowsRuler(){var cards=Cards.Where(c=>!c.Removed).ToList();return cards.Count==0?(0,0):(cards.Max(c=>c.Rows.Count),cards.Average(c=>c.Rows.Count));}
        public Vector2 MainSize(){var m=Boxes.FirstOrDefault(b=>b.Main);if(m==null)return Vector2.zero;return new Vector2(Math.Max(m.Top.width,m.Bottom.width),m.Top.height+(m.Bottom.width>0?m.Bottom.height:0));}
        public (float Top,float Bottom,int TallestTop,int TallestBottom) MainHeights()
        {
            var m=Boxes.FirstOrDefault(b=>b.Main);if(m==null)return (0,0,0,0);
            int Tallest(bool bottom)=>Cards.Where(c=>c.Bundle==m.Bundle&&c.Presentation==bottom).GroupBy(c=>c.Column).Select(g=>g.Count()).DefaultIfEmpty(0).Max();
            return (m.Top.height,m.Bottom.width>0?m.Bottom.height:0,Tallest(false),Tallest(true));
        }
        // Presentation nodes must sit in a column that has (or may have) logic nodes above them.
        public List<Card> PresentationOutsideLogicColumns()
        {
            var bad=new List<Card>();
            foreach(var c in Cards.Where(c=>c.Presentation))
            {
                var box=Boxes.First(b=>b.Bundle==c.Bundle);var col=box.Columns.FirstOrDefault(x=>x.Key==c.Column);
                var top=Cards.FirstOrDefault(x=>!x.Presentation&&x.Bundle==c.Bundle&&x.Column==c.Column);
                bool always=col!=null&&Model.BaseColumns(box.Kind).Contains(col.Key);
                if(col==null||top==null&&!always||top!=null&&Math.Abs(top.Rect.x-c.Rect.x)>0.5f)bad.Add(c);
            }
            return bad;
        }

        public static string Short(string text,int max=10)
        {
            if(string.IsNullOrEmpty(text))return "";
            int cut=text.IndexOf('(');
            string s=cut>0?text.Substring(0,cut).TrimEnd():text;
            return s.Length>max?s.Substring(0,max)+"…":s;
        }
        private string ShortModule(string module)=>Short(Model.ModuleLabel(module),12);
        public static string Fmt(double v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
        private static Rect Union(IEnumerable<Rect> rects)
        {
            var list=rects.Where(r=>r.width>0).ToList();if(list.Count==0)return Rect.zero;
            float x0=list.Min(r=>r.xMin),y0=list.Min(r=>r.yMin),x1=list.Max(r=>r.xMax),y1=list.Max(r=>r.yMax);return Rect.MinMaxRect(x0,y0,x1,y1);
        }
    }

    internal static class SkillGraphLayoutExtensions
    {
        public static bool Presentation(this SkillGraphLayout.Box box,SkillGraphLayout.Options o)=>o.Presentation&&box.Bottom.width>0;
    }
}
