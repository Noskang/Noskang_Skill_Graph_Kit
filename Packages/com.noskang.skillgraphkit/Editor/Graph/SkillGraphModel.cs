using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Noskang.SkillGraph.Editor
{
    // The edit model the Python backend returns (editor.build_model). The window only reads this model;
    // every change goes back to the backend as an operation list.
    public sealed class SkillGraphModel
    {
        public sealed class SlotInfo
        {
            public string Name,Type,Unit,Kind,Timing;public LocalText Label,Description;public JsonNode Default;public string[] Supported;
            public string BalanceKind="balance";
            public bool Balance=>Kind==BalanceKind;
            public bool Required=>Default.IsNull;
        }
        public sealed class Stage { public string Name;public bool Advanced;public string[] Slots; }
        public sealed class KindRule { public string[] Values,Events,Slots;public bool Verified; }
        public sealed class ModuleInfo
        {
            public string Name,Line,Family="",KindSlot,ChooseName;public LocalText Label,Note;public bool Implemented;
            public string[] Events,Exits,Choose=Array.Empty<string>(),Unused=Array.Empty<string>();
            public SlotInfo[] Slots;public List<Stage> Stages=new List<Stage>();public List<KindRule> KindRules=new List<KindRule>();
            public SlotInfo Slot(string name)=>Slots.FirstOrDefault(s=>s.Name==name);
            public bool Presentation=>Line=="Presentation";
        }
        public sealed class SlotValue { public string Name,Source;public JsonNode Value;public int? DefinedIn;public bool Own; }
        public sealed class Node
        {
            public int Bundle,Id,Order,Priority;public int? FromNode;public string Line,Event,Module,Note;public JsonNode EventValue;public int DefinedIn;public bool Added,Own;
            public Dictionary<string,int> Fields=new Dictionary<string,int>();public List<SlotValue> Slots=new List<SlotValue>();
            public SlotValue Slot(string name)=>Slots.FirstOrDefault(s=>s.Name==name);
            public bool Presentation=>Line=="Presentation";
        }
        public sealed class MotionKeys { public int Node;public string Clip,Error;public double Start,Speed;public List<(string Name,int Frame,double Rate,double Seconds)> Keys=new List<(string,int,double,double)>(); }
        public sealed class Bundle
        {
            public int Key;public string Kind,Desc;public int? Parent;public int[] Chain,UsedBy;public string[] Prefixes;public List<Node> Nodes=new List<Node>();
            public List<(int Id,string Module,string Event,string Line)> Removed=new List<(int,string,string,string)>();
            // Animation keyframes of each motion node and their cast-time seconds.
            public List<MotionKeys> Keyframes=new List<MotionKeys>();
            public Node Node(int id)=>Nodes.FirstOrDefault(n=>n.Id==id);
        }
        public sealed class StageInfo { public string Group;public int Level;public int[] Family; }
        public sealed class Owner { public int Key,Bundle,Values;public string Kind,Name,Note;public int[] Bundles,Buffs,Chain,ValueChain;public StageInfo Stage; }
        public sealed class ValueEntry { public string Name,Desc;public JsonNode Value;public int DefinedIn;public bool Own; }
        public sealed class ValueBundle { public int Key;public int? Parent;public string Desc;public int[] Chain,UsedBy;public List<ValueEntry> Entries=new List<ValueEntry>();public ValueEntry Entry(string name)=>Entries.FirstOrDefault(e=>e.Name==name); }
        public sealed class Issue { public string Level,Code,Where,Message,Slot;public int? Bundle,Node,Owner; }
        public sealed class Resource { public int Key,Skin;public string Clip,Effect,Sound,Desc; }
        public sealed class Family { public string Name,Color,Icon;public LocalText Label; }
        public sealed class Band { public string Name,From,To; }
        public sealed class Combo { public string Name,Desc;public int Nodes; }

        public JsonNode Root { get; private set; }
        public string Format,Workspace,DictionaryHash,Language="en";public bool Practice,PracticeExists,ExternalChange,DisplayStale;public int PendingChanges;public bool PendingAux;
        public Dictionary<string,string> Files=new Dictionary<string,string>(),Base=new Dictionary<string,string>();
        public Dictionary<string,ModuleInfo> Modules=new Dictionary<string,ModuleInfo>(StringComparer.Ordinal);
        public List<string> Events=new List<string>();public Dictionary<string,string[]> KindEvents=new Dictionary<string,string[]>();public Dictionary<string,string[]> SlotEnums=new Dictionary<string,string[]>();
        public Dictionary<string,string[]> Drawn=new Dictionary<string,string[]>();public string[] PresentationEvents=Array.Empty<string>();
        public string[] TimedEvents=Array.Empty<string>(),ExitEvents=Array.Empty<string>(),SubscriptionEvents=Array.Empty<string>(),Subjects=Array.Empty<string>();
        public string StartEvent="";
        // Host settings echoed by the backend.
        public string[] Layers=Array.Empty<string>();public string TimeUnit="s";public double TimeGrid;public int PracticeOwner;public string KeyframeFunction="";
        public Dictionary<string,string> KindRoles=new Dictionary<string,string>();
        // Modules that point at other bundles (drawn as edges to side boxes).
        public string SpawnModule="SpawnEffect",SpawnSlot="BundleKey",StatusModule="ApplyStatus",StatusSlot="BuffKey";
        public Dictionary<string,string[]> BaseColumnsByKind=new Dictionary<string,string[]>();
        // Key layout: band digits + owner digits + serial digits.
        public int KeyOwnerDigits=2,KeySerialDigits=2;
        public int OwnerNumber(int key){int d=1;for(int i=0;i<KeySerialDigits;i++)d*=10;int m=1;for(int i=0;i<KeyOwnerDigits;i++)m*=10;return key/d%m;}
        // Columns every bundle of a kind shows even when empty: settings, else the kind's first event.
        public IReadOnlyList<string> BaseColumns(string kind)
            =>BaseColumnsByKind.TryGetValue(kind??"",out var c)?c:KindEvents.TryGetValue(kind??"",out var e)&&e.Length>0?new[]{e[0]}:Array.Empty<string>();
        public Dictionary<string,Family> Families=new Dictionary<string,Family>(StringComparer.Ordinal);
        public Dictionary<string,LocalText> EventLabels=new Dictionary<string,LocalText>(StringComparer.Ordinal);
        public Dictionary<string,Dictionary<string,LocalText>> EnumLabels=new Dictionary<string,Dictionary<string,LocalText>>(StringComparer.Ordinal);
        public List<Owner> Owners=new List<Owner>();public Dictionary<int,Bundle> Bundles=new Dictionary<int,Bundle>();public Dictionary<int,ValueBundle> Values=new Dictionary<int,ValueBundle>();
        public List<Issue> Issues=new List<Issue>();public Dictionary<int,string> KeyNames=new Dictionary<int,string>();public List<Resource> Resources=new List<Resource>();
        public Dictionary<int,Dictionary<int,string>> Memos=new Dictionary<int,Dictionary<int,string>>();public Dictionary<int,List<Band>> Bands=new Dictionary<int,List<Band>>();public List<Combo> Combos=new List<Combo>();
        // Empty columns (no node yet) kept in the aux file.
        public Dictionary<int,List<(string Event,JsonNode Value,int? From)>> EmptyColumns=new Dictionary<int,List<(string,JsonNode,int?)>>();
        public List<JsonNode> OpResults=new List<JsonNode>();

        public Owner OwnerOf(int key)=>Owners.FirstOrDefault(o=>o.Key==key);
        public ModuleInfo Module(string name)=>name!=null&&Modules.TryGetValue(name,out var m)?m:null;
        public string KeyName(int key)=>KeyNames.TryGetValue(key,out var n)?n:"";
        public bool IsTimed(string e)=>Array.IndexOf(TimedEvents,e)>=0;
        public bool IsExit(string e)=>Array.IndexOf(ExitEvents,e)>=0;
        public bool IsSubscription(string e)=>Array.IndexOf(SubscriptionEvents,e)>=0;
        public string KindName(string role)=>KindRoles.TryGetValue(role,out var k)?k:role;
        public string EnumLabel(string type,string value)
        {
            string e=type.StartsWith("list:enum:",StringComparison.Ordinal)?type.Substring(10):type.StartsWith("enum:",StringComparison.Ordinal)?type.Substring(5):null;
            return e!=null&&EnumLabels.TryGetValue(e,out var l)&&value!=null&&l.TryGetValue(value,out var k)?k.Get(SkillGraphText.Language):value;
        }
        public string EventLabel(string e)=>EventLabels.TryGetValue(e??"",out var l)?l.Get(SkillGraphText.Language):e;
        public string ModuleLabel(string module){var m=Module(module);var t=m?.Label.Get(SkillGraphText.Language);return string.IsNullOrEmpty(t)?module:t;}
        public string Seconds(double v)=>v.ToString("0.###",CultureInfo.InvariantCulture)+TimeUnit;

        // Keyframe time reference '@{motion node}:{name}[+-offset]' resolved against the bundle's motion nodes.
        public static bool IsKeyframeRef(JsonNode v)=>v!=null&&v.Kind==JsonKind.String&&v.Text.StartsWith("@",StringComparison.Ordinal);
        public bool TryKeyframe(int bundle,string text,out MotionKeys motion,out (string Name,int Frame,double Rate,double Seconds) key)
        {
            if(!TryKeyframe(bundle,text,out motion,out key,out double offset))return false;
            key.Seconds+=offset;return true;
        }
        // A full keyframe name wins over an offset reading (same rule as the Python split_keyframe).
        public bool TryKeyframe(int bundle,string text,out MotionKeys motion,out (string Name,int Frame,double Rate,double Seconds) key,out double offset)
        {
            motion=null;key=default;offset=0;if(string.IsNullOrEmpty(text)||text[0]!='@')return false;int colon=text.IndexOf(':');
            if(colon<2||!int.TryParse(text.Substring(1,colon-1),NumberStyles.Integer,CultureInfo.InvariantCulture,out int node)||!Bundles.TryGetValue(bundle,out var b))return false;
            string name=text.Substring(colon+1);motion=b.Keyframes.FirstOrDefault(k=>k.Node==node);if(motion==null)return false;
            foreach(var k in motion.Keys)if(k.Name==name){key=k;return true;}
            var m=System.Text.RegularExpressions.Regex.Match(name,@"^(.+?)([+-]\d+(?:\.\d+)?)$");
            if(!m.Success)return false;
            foreach(var k in motion.Keys)if(k.Name==m.Groups[1].Value){key=k;offset=double.Parse(m.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture);return true;}
            return false;
        }
        // Column title '{keyframe} : {seconds}', with an offset '{keyframe} +0.1s : {seconds + offset}'.
        public string KeyframeLabel(string name,double seconds,double offset=0){var (k,r)=KeyframeParts(name,seconds,offset);return k+r;}
        // The keyframe part is drawn in its own colour.
        public (string Keyframe,string Remainder) KeyframeParts(string name,double seconds,double offset=0)
            =>(name+(Math.Abs(offset)>1e-9?" "+(offset>0?"+":"")+offset.ToString("0.###",CultureInfo.InvariantCulture)+TimeUnit:""),
               " : "+Seconds(seconds+offset));
        public static string KeyframeValue(string baseRef,double offset)
            =>Math.Abs(offset)<1e-9?baseRef:baseRef+(offset>0?"+":"")+offset.ToString("0.###",CultureInfo.InvariantCulture);
        public Family FamilyOf(string module)=>Families.TryGetValue(Module(module)?.Family??"",out var f)?f:new Family{Name="",Label=new LocalText("",""),Color="#555555",Icon="•"};

        public ValueEntry ResolveValue(int valueBundle,string name)=>Values.TryGetValue(valueBundle,out var v)?v.Entry(name):null;

        private static string[] Texts(JsonNode n)=>n==null||n.IsNull?Array.Empty<string>():n.AsArray().Select(e=>e.AsText()).ToArray();

        public static SkillGraphModel Parse(JsonNode root)
        {
            var m=new SkillGraphModel{Root=root,Format=root["format"].AsText()};
            if(root.Has("language"))m.Language=root["language"].AsText();
            var ws=root["workspace"];m.Workspace=ws["kind"].AsText();m.Practice=m.Workspace=="practice";m.PracticeExists=ws["practiceExists"].Bool;
            foreach(var kv in ws["files"].Fields)m.Files[kv.Key]=kv.Value.AsText();
            foreach(var kv in ws["base"].Fields)m.Base[kv.Key]=kv.Value.AsText();
            m.DictionaryHash=root["dictionaryHash"].AsText();m.DisplayStale=root["displayStale"].Bool;
            if(root.Has("externalChange"))m.ExternalChange=root["externalChange"].Bool;
            if(root.Has("pending")){m.PendingChanges=root["pending"]["changes"].AsInt();m.PendingAux=root["pending"]["aux"].Bool;}
            string balance="balance";
            if(root.Has("settings"))
            {
                var st=root["settings"];
                m.Layers=Texts(st.Get("layers"));
                if(st.Get("time") is JsonNode time){m.TimeUnit=time.Get("unit")?.TextOr("s")??"s";var g=time.Get("grid");m.TimeGrid=g==null||g.IsNull?0:g.Number;}
                if(st.Get("practiceOwner") is JsonNode po&&!po.IsNull)m.PracticeOwner=po.AsInt();
                m.KeyframeFunction=st.Get("keyframeFunction")?.TextOr("")??"";
                if(st.Get("kinds") is JsonNode kinds&&kinds.Kind==JsonKind.Object)foreach(var kv in kinds.Fields)m.KindRoles[kv.Key]=kv.Value.AsText();
                balance=st.Get("balanceKind")?.TextOr(balance)??balance;
                if(st.Get("modules") is JsonNode mods&&mods.Kind==JsonKind.Object)
                {
                    if(mods.Get("spawn") is JsonNode sp){m.SpawnModule=sp["module"].AsText();m.SpawnSlot=sp["slot"].AsText();}
                    if(mods.Get("status") is JsonNode su){m.StatusModule=su["module"].AsText();m.StatusSlot=su["slot"].AsText();}
                }
                if(st.Get("keys") is JsonNode keys&&keys.Kind==JsonKind.Object){m.KeyOwnerDigits=keys["owner"].AsInt();m.KeySerialDigits=keys["serial"].AsInt();}
                if(st.Get("baseColumns") is JsonNode bc&&bc.Kind==JsonKind.Object)foreach(var kv in bc.Fields)m.BaseColumnsByKind[kv.Key]=Texts(kv.Value);
            }
            var dict=root["dictionary"];
            m.Events=dict["events"].AsArray().Select(e=>e.AsText()).ToList();
            foreach(var kv in dict["kindEvents"].Fields)m.KindEvents[kv.Key]=Texts(kv.Value);
            foreach(var kv in dict["slotEnums"].Fields)m.SlotEnums[kv.Key]=Texts(kv.Value);
            foreach(var kv in dict["drawn"].Fields)m.Drawn[kv.Key]=Texts(kv.Value);
            m.PresentationEvents=Texts(dict["presentationEvents"]);
            m.TimedEvents=Texts(dict.Get("timedEvents"));m.ExitEvents=Texts(dict.Get("exitEvents"));m.SubscriptionEvents=Texts(dict.Get("subscriptionEvents"));
            m.Subjects=Texts(dict.Get("subjects"));m.StartEvent=dict.Get("startEvent")?.TextOr("")??"";
            var display=root["display"];
            var views=new Dictionary<string,JsonNode>(StringComparer.Ordinal);
            if(!display.IsNull)
            {
                foreach(var f in display["families"].AsArray())
                    m.Families[f["name"].AsText()]=new Family{Name=f["name"].AsText(),Label=LocalText.From(f.Get("label")??f.Get("ko")),Color=f["color"].AsText(),Icon=f["icon"].AsText()};
                foreach(var kv in display["events"].Fields)m.EventLabels[kv.Key]=LocalText.From(kv.Value);
                foreach(var kv in display["enumLabels"].Fields)m.EnumLabels[kv.Key]=kv.Value.Fields.ToDictionary(x=>x.Key,x=>LocalText.From(x.Value));
                foreach(var v in display["modules"].AsArray())views[v["name"].AsText()]=v;
            }
            foreach(var x in dict["modules"].AsArray())
            {
                var mod=new ModuleInfo{Name=x["name"].AsText(),Label=LocalText.From(x.Get("label")),Line=x["line"].AsText(),Implemented=x["implemented"].Bool,Note=LocalText.From(x.Get("note")),
                    Events=Texts(x["events"]),Exits=Texts(x["exits"])};
                mod.Slots=x["slots"].AsArray().Select(s=>new SlotInfo{Name=s["name"].AsText(),Type=s["type"].AsText(),Unit=s["unit"].AsText(),Kind=s["kind"].AsText(),Default=s["default"],
                    Timing=s["timing"].AsText(),Description=LocalText.From(s.Get("description")),Label=new LocalText(s["name"].AsText(),s["name"].AsText()),BalanceKind=balance,
                    Supported=s["supported"].IsNull?null:Texts(s["supported"])}).ToArray();
                if(views.TryGetValue(mod.Name,out var v))
                {
                    mod.Family=v["family"].AsText();
                    var label=LocalText.From(v.Get("label")??v.Get("ko"));if(!label.IsEmpty)mod.Label=label;
                    var note=LocalText.From(v.Get("note"));if(!note.IsEmpty)mod.Note=note;
                    foreach(var s in v["slots"].AsArray())
                    {
                        var slot=mod.Slot(s[0].AsText());
                        if(slot==null)continue;
                        slot.Label=LocalText.From(s[1]);
                        var desc=LocalText.From(s[2]);if(!desc.IsEmpty)slot.Description=desc;
                    }
                    foreach(var st in v["stages"].AsArray())mod.Stages.Add(new Stage{Name=st["name"].AsText(),Advanced=st["advanced"].Bool,Slots=Texts(st["slots"])});
                    if(!v["kind"].IsNull)
                    {
                        mod.KindSlot=v["kind"]["slot"].AsText();
                        foreach(var r in v["kind"]["rules"].AsArray())
                            mod.KindRules.Add(new KindRule{Values=Texts(r["values"]),Events=Texts(r["events"]),Slots=Texts(r["slots"]),Verified=r["verified"].Bool});
                    }
                    if(!v["choose"].IsNull){mod.ChooseName=v["choose"]["name"].AsText();mod.Choose=Texts(v["choose"]["slots"]);}
                    mod.Unused=Texts(v["unused"]);
                }
                if(mod.Stages.Count==0&&mod.Slots.Length>0)mod.Stages.Add(new Stage{Name=SkillGraphText.T("stage.basic"),Slots=mod.Slots.Select(s=>s.Name).ToArray()});
                m.Modules[mod.Name]=mod;
            }
            foreach(var o in root["owners"].AsArray())
            {
                var owner=new Owner{Key=o["key"].AsInt(),Kind=o["kind"].AsText(),Name=o["name"].AsText(),Bundle=o["bundle"].AsInt(),Values=o["values"].AsInt(),
                    Bundles=Ints(o["bundles"]),Buffs=Ints(o["buffs"]),Chain=Ints(o["chain"]),ValueChain=Ints(o["valueChain"]),Note=o.Get("note")?.TextOr("")??""};
                var stage=o.Get("stage");
                if(stage!=null&&!stage.IsNull)owner.Stage=new StageInfo{Group=Text(stage["group"]),Level=stage["level"].AsInt(),Family=Ints(stage["family"])};
                m.Owners.Add(owner);
            }
            foreach(var b in root["bundles"].AsArray())
            {
                var bundle=new Bundle{Key=b["key"].AsInt(),Kind=b["kind"].AsText(),Desc=b["desc"].AsText(),Parent=IntOrNull(b["parent"]),Chain=Ints(b["chain"]),UsedBy=Ints(b["usedBy"]),
                    Prefixes=Texts(b["prefixes"])};
                foreach(var n in b["nodes"].AsArray())
                {
                    var node=new Node{Bundle=bundle.Key,Id=n["id"].AsInt(),Line=n["line"].AsText(),Event=n["event"].AsText(),EventValue=n["eventValue"],FromNode=IntOrNull(n["fromNode"]),
                        Order=n["order"].IsNull?0:n["order"].AsInt(),Priority=n["priority"].AsInt(),Module=n["module"].AsText(),Note=n.Get("note")?.TextOr("")??"",
                        DefinedIn=n["definedIn"].AsInt(),Added=n["added"].Bool,Own=n["own"].Bool};
                    foreach(var kv in n["fields"].Fields)node.Fields[kv.Key]=kv.Value.AsInt();
                    foreach(var s in n["slots"].AsArray())node.Slots.Add(new SlotValue{Name=s["name"].AsText(),Source=s["source"].AsText(),Value=s["value"],DefinedIn=IntOrNull(s["definedIn"]),Own=s["own"].Bool});
                    bundle.Nodes.Add(node);
                }
                foreach(var r in b["removed"].AsArray())bundle.Removed.Add((r["id"].AsInt(),r["module"].AsText(),r["event"].AsText(),r["line"].AsText()));
                if(b.Get("keyframes") is JsonNode kfs)foreach(var k in kfs.AsArray())
                {
                    var mk=new MotionKeys{Node=k["node"].AsInt(),Clip=k.Get("clip")?.TextOr(null),Error=k.Get("error")?.TextOr(null),
                        Start=k.Get("start")?.Number??0,Speed=k.Get("speed")?.Number??1};
                    foreach(var x in k["keys"].AsArray())mk.Keys.Add((x["name"].AsText(),x["frame"].AsInt(),x["rate"].Number,x["seconds"].Number));
                    bundle.Keyframes.Add(mk);
                }
                m.Bundles[bundle.Key]=bundle;
            }
            foreach(var v in root["values"].AsArray())
            {
                var vb=new ValueBundle{Key=v["key"].AsInt(),Parent=IntOrNull(v["parent"]),Desc=v["desc"].AsText(),Chain=Ints(v["chain"]),UsedBy=Ints(v["usedBy"])};
                foreach(var e in v["entries"].AsArray())vb.Entries.Add(new ValueEntry{Name=e["name"].AsText(),Value=e["value"],DefinedIn=e["definedIn"].AsInt(),Own=e["own"].Bool,Desc=e["desc"].AsText()});
                m.Values[vb.Key]=vb;
            }
            foreach(var i in root["issues"].AsArray())
                m.Issues.Add(new Issue{Level=i["level"].AsText(),Code=i["code"].AsText(),Where=i["where"].AsText(),Message=i["message"].AsText(),Bundle=IntOrNull(i.Get("bundle")),
                    Node=IntOrNull(i.Get("node")),Owner=IntOrNull(i.Get("owner")),Slot=i.Get("slot")?.TextOr(null)});
            foreach(var kv in root["keyNames"].Fields)m.KeyNames[int.Parse(kv.Key,CultureInfo.InvariantCulture)]=kv.Value.AsText();
            foreach(var r in root["resources"].AsArray())m.Resources.Add(new Resource{Key=r["key"].AsInt(),Skin=r["skin"].AsInt(),Clip=r["clip"].AsText(),Effect=r["effect"].AsText(),Sound=r["sound"].AsText(),Desc=r["desc"].AsText()});
            var aux=root["aux"];
            foreach(var kv in aux["bundles"].Fields)
            {
                int key=int.Parse(kv.Key,CultureInfo.InvariantCulture);
                if(kv.Value.Get("memos") is JsonNode memos)m.Memos[key]=memos.Fields.ToDictionary(x=>int.Parse(x.Key,CultureInfo.InvariantCulture),x=>x.Value.AsText());
                if(kv.Value.Get("bands") is JsonNode bands)m.Bands[key]=bands.AsArray().Select(x=>new Band{Name=x["name"].AsText(),From=x["from"].AsText(),To=x["to"].AsText()}).ToList();
                if(kv.Value.Get("columns") is JsonNode cols)m.EmptyColumns[key]=cols.AsArray().Select(x=>(x["event"].AsText(),x["value"],IntOrNull(x["from"]))).ToList();
            }
            foreach(var c in aux["combos"].AsArray())m.Combos.Add(new Combo{Name=c["name"].AsText(),Desc=c.Get("desc")?.TextOr("")??"",Nodes=c["nodes"].AsArray().Count});
            m.OpResults=root["ops"].AsArray().ToList();
            return m;
        }
        public static int[] Ints(JsonNode n)=>n==null||n.IsNull?Array.Empty<int>():n.AsArray().Select(x=>x.AsInt()).ToArray();
        public static int? IntOrNull(JsonNode n)=>n==null||n.IsNull?(int?)null:n.AsInt();
        public static string Text(JsonNode v)
        {
            if(v==null)return "";
            switch(v.Kind)
            {
                case JsonKind.Null:return "";
                case JsonKind.Number:return v.Number.ToString("0.#####",CultureInfo.InvariantCulture);
                case JsonKind.String:return v.Text;
                case JsonKind.Bool:return v.Bool?"true":"false";
                case JsonKind.Array:return string.Join(",",v.AsArray().Select(Text));
                default:return "…";
            }
        }
    }
}
