using System;
using System.Collections.Generic;
using System.Linq;

namespace Noskang.SkillGraph
{
    public enum LinkSource { Default, Fixed, Ref }

    // A node of exported data bound to its module declaration. Slot values are looked up by slot index at run time;
    // Ref slots read the owner's value table, so one bundle serves every owner (tier) that inherits it.
    public sealed class BoundNode
    {
        public int Id { get; }
        public string Event { get; }
        public int EventIndex { get; }
        // Fixed time of a timed event (NaN when none or when AtRef is set).
        public double At { get; }
        // Value index of a timed event that reads its time from the value sheet (-1 = none).
        public int AtRef { get; }
        // Subject index of a subscription event (-1 = none).
        public int Subject { get; }
        public int From { get; }
        public int Priority { get; }
        public int Order { get; }
        public ModuleDeclaration Module { get; }
        private readonly LinkSource[] sources;
        private readonly object[] fixedValues;
        private readonly int[] refs;

        internal BoundNode(int id,string e,int eventIndex,double at,int atRef,int subject,int from,int priority,int order,ModuleDeclaration module,LinkSource[] sources,object[] fixedValues,int[] refs)
        {Id=id;Event=e;EventIndex=eventIndex;At=at;AtRef=atRef;Subject=subject;From=from;Priority=priority;Order=order;Module=module;this.sources=sources;this.fixedValues=fixedValues;this.refs=refs;}

        public LinkSource Source(int slot)=>sources[slot];
        public double Time(ValueTable values)=>AtRef>=0?values.Number(AtRef):At;
        public object Value(int slot,ValueTable values)
        {
            switch(sources[slot])
            {
                case LinkSource.Ref:return values.Get(refs[slot]);
                case LinkSource.Fixed:return fixedValues[slot];
                default:return Module.Slots[slot].Default;
            }
        }
        public double Number(int slot,ValueTable values)=>Convert.ToDouble(Value(slot,values)??0.0,System.Globalization.CultureInfo.InvariantCulture);
        public int Integer(int slot,ValueTable values)=>(int)Math.Round(Number(slot,values));
        public string Text(int slot,ValueTable values)=>Value(slot,values)?.ToString()??"";
        public double Number(string slot,ValueTable values)=>Number(Module.SlotIndex(slot),values);
        public int Integer(string slot,ValueTable values)=>Integer(Module.SlotIndex(slot),values);
        public string Text(string slot,ValueTable values)=>Text(Module.SlotIndex(slot),values);
    }

    public sealed class BoundBundle
    {
        public int Key { get; }
        public string Kind { get; }
        public IReadOnlyList<BoundNode> Nodes { get; }
        internal BoundBundle(int key,string kind,IEnumerable<BoundNode> nodes)
        {
            Key=key;Kind=kind;
            // Inside one event: time, then priority (smaller first), then order, then node id.
            Nodes=nodes.OrderBy(n=>n.EventIndex).ThenBy(n=>double.IsNaN(n.At)?0:n.At).ThenBy(n=>n.Priority).ThenBy(n=>n.Order).ThenBy(n=>n.Id).ToList().AsReadOnly();
        }
        public IEnumerable<BoundNode> On(string e,int from=-1)=>Nodes.Where(n=>n.Event==e&&(from<0||n.From==from));
        public BoundNode Find(int id)=>Nodes.FirstOrDefault(n=>n.Id==id);
    }

    // Values an owner reads by value index, after modifiers.
    public sealed class ValueTable
    {
        private readonly Dictionary<int,object> values;
        public ValueTable(Dictionary<int,object> values){this.values=values;}
        public object Get(int index)=>values.TryGetValue(index,out var v)?v:null;
        public double Number(int index)=>Get(index) is double d?d:Convert.ToDouble(Get(index)??0.0,System.Globalization.CultureInfo.InvariantCulture);
        public int Count=>values.Count;
        internal Dictionary<int,object> Raw=>values;
    }

    public sealed class SkillOwner
    {
        public int Key { get; }
        public string List { get; }
        public BoundBundle Bundle { get; }
        public ValueTable Values { get; }
        public IReadOnlyDictionary<string,JsonNode> Extra { get; }
        internal SkillOwner(int key,string list,BoundBundle bundle,ValueTable values,IReadOnlyDictionary<string,JsonNode> extra){Key=key;List=list;Bundle=bundle;Values=values;Extra=extra;}
    }

    // Exported logic (or presentation) JSON bound to a module set. Loading checks that both came from the same dictionary.
    public sealed class SkillData
    {
        public ModuleSet Modules { get; }
        public IReadOnlyDictionary<int,BoundBundle> Bundles { get; }
        public IReadOnlyDictionary<int,SkillOwner> Owners { get; }
        public IReadOnlyList<string> ValueNames { get; }
        private static readonly HashSet<string> Known=new HashSet<string>{"format","dictionaryHash","bundles","values","modifiers","valueNames","metas","resources"};

        private SkillData(ModuleSet set,Dictionary<int,BoundBundle> bundles,Dictionary<int,SkillOwner> owners,List<string> names)
        {Modules=set;Bundles=bundles;Owners=owners;ValueNames=names.AsReadOnly();}

        public static SkillData Load(byte[] json,ModuleSet set,IReadOnlyList<string> valueNames=null)
        {
            var root=JsonNode.Parse(json);
            if(root["dictionaryHash"].AsText()!=set.Hash)throw new FormatException("Skill data was exported with a different module dictionary");
            var kinds=set.KindEvents.Select(k=>k.Key).ToList();
            var bundles=new Dictionary<int,BoundBundle>();
            foreach(var b in root["bundles"].AsArray())
            {
                int key=b["key"].AsInt();
                bundles[key]=new BoundBundle(key,kinds[b["kind"].AsInt()],b["nodes"].AsArray().Select(n=>Bind(n,set)));
            }
            var tables=new Dictionary<int,Dictionary<int,object>>();
            if(root.Has("values"))
                foreach(var v in root["values"].AsArray())
                    tables[v["key"].AsInt()]=v["entries"].AsArray().ToDictionary(e=>e[0].AsInt(),e=>Plain(e[1]));
            var owners=new Dictionary<int,SkillOwner>();
            foreach(var field in root.Keys.Where(k=>!Known.Contains(k)&&root[k].Kind==JsonKind.Array))
            {
                foreach(var o in root[field].AsArray())
                {
                    if(o.Kind!=JsonKind.Object||!o.Has("key")||!o.Has("bundle")||!o.Has("values"))continue;
                    int key=o["key"].AsInt();
                    var table=tables.TryGetValue(o["values"].AsInt(),out var t)?new Dictionary<int,object>(t):new Dictionary<int,object>();
                    bundles.TryGetValue(o["bundle"].AsInt(),out var bundle);
                    owners[key]=new SkillOwner(key,field,bundle,new ValueTable(table),o.Fields);
                }
            }
            if(root.Has("modifiers"))ApplyModifiers(root["modifiers"].AsArray(),owners);
            var names=root.Has("valueNames")?root["valueNames"].AsArray().Select(x=>x.AsText()).ToList():(valueNames??Array.Empty<string>()).ToList();
            return new SkillData(set,bundles,owners,names);
        }

        // Modifiers: Override (highest priority wins), then Add, then Multiply (0.1 = +10%).
        private static void ApplyModifiers(IReadOnlyList<JsonNode> modifiers,Dictionary<int,SkillOwner> owners)
        {
            foreach(var owner in owners.Values.ToList())
            {
                var mine=modifiers.Where(m=>m["targets"].AsArray().Any(t=>t.AsInt()==owner.Key)).ToList();
                if(mine.Count==0)continue;
                var table=new Dictionary<int,object>(owner.Values.Raw);
                foreach(var group in mine.GroupBy(m=>m["name"].AsInt()))
                {
                    double v=table.TryGetValue(group.Key,out var raw)&&raw is double d?d:0;
                    var over=group.Where(m=>m["op"].AsInt()==0).OrderByDescending(m=>m["priority"].AsInt()).FirstOrDefault();
                    if(over!=null)v=over["value"].AsNumber();
                    foreach(var m in group.Where(m=>m["op"].AsInt()==1))v+=m["value"].AsNumber();
                    foreach(var m in group.Where(m=>m["op"].AsInt()==2))v*=1+m["value"].AsNumber();
                    table[group.Key]=v;
                }
                owners[owner.Key]=new SkillOwner(owner.Key,owner.List,owner.Bundle,new ValueTable(table),owner.Extra);
            }
        }

        private static object Plain(JsonNode n)=>n.Kind==JsonKind.Number?n.Number:n.Kind==JsonKind.String?(object)n.Text:n.Kind==JsonKind.Bool?n.Bool:null;

        private static BoundNode Bind(JsonNode n,ModuleSet set)
        {
            var module=set.Modules[n["module"].AsInt()];
            int e=n["event"].AsInt();
            string name=set.Events[e];
            var at=n["at"];
            double time=double.NaN;int atRef=-1,subject=-1;
            if(set.IsSubscription(name)&&at.Kind==JsonKind.Number)subject=at.AsInt();
            else if(at.Kind==JsonKind.Number)time=at.Number;
            else if(at.Kind==JsonKind.Array)atRef=at[1].AsInt();
            int count=module.Slots.Count;
            var sources=new LinkSource[count];var values=new object[count];var refs=new int[count];
            foreach(var link in n["links"].AsArray())
            {
                int slot=link[0].AsInt();
                if(link[1].AsText()=="ref"){sources[slot]=LinkSource.Ref;refs[slot]=link[2].AsInt();}
                else
                {
                    sources[slot]=LinkSource.Fixed;
                    var raw=link[2];var type=module.Slots[slot].Type;
                    if(type.StartsWith("enum:",StringComparison.Ordinal)&&raw.Kind==JsonKind.Number)values[slot]=set.SlotEnum(type.Substring(5))[raw.AsInt()];
                    else values[slot]=Plain(raw);
                }
            }
            return new BoundNode(n["id"].AsInt(),name,e,time,atRef,subject,n["from"].IsNull?-1:n["from"].AsInt(),n["priority"].AsInt(),n["order"].IsNull?0:n["order"].AsInt(),module,sources,values,refs);
        }
    }
}
