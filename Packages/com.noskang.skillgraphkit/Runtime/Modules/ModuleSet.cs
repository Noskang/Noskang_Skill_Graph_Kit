using System;
using System.Collections.Generic;
using System.Linq;

namespace Noskang.SkillGraph
{
    // The module dictionary: every event, bundle kind and module a host game supports. Code is the source of truth;
    // ExportJson() writes the file the Python tools and the editor read, and Hash ties exported skill data to it.
    public sealed class ModuleSet
    {
        public const string DefaultFormat="SkillGraph Module Dictionary 1";
        public string Format { get; }
        public IReadOnlyList<string> Events { get; }
        // Bundle kind -> events a bundle of that kind may use, in declaration order.
        public IReadOnlyList<KeyValuePair<string,IReadOnlyList<string>>> KindEvents { get; }
        public IReadOnlyList<string> PresentationEvents { get; }
        public IReadOnlyList<string> SubscriptionEvents { get; }
        public IReadOnlyList<string> Subjects { get; }
        public IReadOnlyList<string> TimedEvents { get; }
        public IReadOnlyList<string> ExitEvents { get; }
        public string StartEvent { get; }
        public IReadOnlyList<KeyValuePair<string,IReadOnlyList<string>>> SlotEnums { get; }
        public IReadOnlyList<ModuleDeclaration> Modules { get; }
        private readonly Dictionary<string,ModuleDeclaration> byName;
        private readonly Dictionary<string,int> eventIndex;
        private string json,hash;

        private ModuleSet(Builder b)
        {
            Format=b.format;
            Events=b.events.AsReadOnly();
            KindEvents=b.kinds.Select(k=>new KeyValuePair<string,IReadOnlyList<string>>(k.Key,k.Value.AsReadOnly())).ToList().AsReadOnly();
            PresentationEvents=b.presentation.AsReadOnly();
            SubscriptionEvents=b.subscription.AsReadOnly();
            Subjects=b.subjects.AsReadOnly();
            TimedEvents=b.timed.AsReadOnly();
            ExitEvents=b.exits.AsReadOnly();
            StartEvent=b.start;
            SlotEnums=b.enums.Select(k=>new KeyValuePair<string,IReadOnlyList<string>>(k.Key,k.Value.AsReadOnly())).ToList().AsReadOnly();
            for(int i=0;i<b.modules.Count;i++)b.modules[i].Index=i;
            Modules=b.modules.AsReadOnly();
            byName=b.modules.ToDictionary(m=>m.Name,StringComparer.Ordinal);
            eventIndex=Events.Select((e,i)=>(e,i)).ToDictionary(x=>x.e,x=>x.i,StringComparer.Ordinal);
        }

        public bool TryGet(string name,out ModuleDeclaration module)=>byName.TryGetValue(name??"",out module);
        public ModuleDeclaration Get(string name)=>TryGet(name,out var m)?m:throw new KeyNotFoundException("Unknown module: "+name);
        public int EventIndex(string name)=>eventIndex.TryGetValue(name??"",out var i)?i:-1;
        public IReadOnlyList<string> EventsFor(string kind)=>KindEvents.FirstOrDefault(k=>k.Key==kind).Value??Array.Empty<string>();
        public IReadOnlyList<string> SlotEnum(string name)=>SlotEnums.FirstOrDefault(k=>k.Key==name).Value??throw new KeyNotFoundException("Unknown slot enum: "+name);
        public bool IsTimed(string e)=>TimedEvents.Contains(e);
        public bool IsExit(string e)=>ExitEvents.Contains(e);
        public bool IsSubscription(string e)=>SubscriptionEvents.Contains(e);

        // Problems in the declarations (unknown events, duplicate names, enum slots without an enum). Empty = fine.
        public List<string> Check()
        {
            var problems=new List<string>();
            var events=new HashSet<string>(Events);
            if(events.Count!=Events.Count)problems.Add("Duplicate event names");
            if(!events.Contains(StartEvent))problems.Add("Start event is not an event: "+StartEvent);
            foreach(var e in KindEvents.SelectMany(k=>k.Value).Concat(PresentationEvents).Concat(SubscriptionEvents).Concat(TimedEvents).Concat(ExitEvents))
                if(!events.Contains(e))problems.Add("Unknown event: "+e);
            if(ExitEvents.Count!=0&&ExitEvents.Count!=2)problems.Add("Exit events come as a pass/fail pair");
            if(byName.Count!=Modules.Count)problems.Add("Duplicate module names");
            foreach(var m in Modules)
            {
                foreach(var e in m.Events)if(!events.Contains(e))problems.Add($"{m.Name}: unknown event {e}");
                if(m.Slots.Select(s=>s.Name).Distinct().Count()!=m.Slots.Count)problems.Add($"{m.Name}: duplicate slot names");
                foreach(var s in m.Slots)
                {
                    string type=s.Type.StartsWith("list:",StringComparison.Ordinal)?s.Type.Substring(5):s.Type;
                    if(type.StartsWith("enum:",StringComparison.Ordinal))
                    {
                        var values=SlotEnums.FirstOrDefault(k=>k.Key==type.Substring(5)).Value;
                        if(values==null)problems.Add($"{m.Name}.{s.Name}: enum {type.Substring(5)} is not declared");
                        else if(s.Default is string d&&!values.Contains(d))problems.Add($"{m.Name}.{s.Name}: default {d} is not in the enum");
                    }
                }
            }
            return problems;
        }

        public JsonObject ToJson()
        {
            var kinds=new JsonObject();
            foreach(var k in KindEvents)kinds.Add(k.Key,k.Value);
            var enums=new JsonObject();
            foreach(var k in SlotEnums)enums.Add(k.Key,k.Value);
            return new JsonObject()
                .With("format",Format).With("events",Events).With("kindEvents",kinds).With("presentationEvents",PresentationEvents)
                .With("subscriptionEvents",SubscriptionEvents).With("subjects",Subjects).With("timedEvents",TimedEvents).With("exitEvents",ExitEvents)
                .With("startEvent",StartEvent).With("slotEnums",enums)
                .With("modules",Modules.Select(m=>new JsonObject()
                    .With("name",m.Name).With("line",m.Line.ToString()).With("group",m.Group).With("events",m.Events)
                    .With("slots",m.Slots.Select(s=>new object[]{s.Name,s.Type,s.Unit,s.Role==SlotRole.Balance?"balance":"structure",s.Default,s.Timing,s.Supported}))
                    .With("exits",m.Exits).With("random",m.Random).With("implemented",m.Implemented)));
        }

        public string ExportJson()=>json??=JsonWriter.Write(ToJson());
        // SHA-256 of the exported UTF-8 bytes. The Python tools compute the same value from the file.
        public string Hash=>hash??=JsonWriter.Sha256(ExportJson());

        public sealed class Builder
        {
            internal string format=DefaultFormat,start;
            internal readonly List<string> events=new List<string>(),presentation=new List<string>(),subscription=new List<string>(),subjects=new List<string>(),timed=new List<string>(),exits=new List<string>();
            internal readonly List<KeyValuePair<string,List<string>>> kinds=new List<KeyValuePair<string,List<string>>>(),enums=new List<KeyValuePair<string,List<string>>>();
            internal readonly List<ModuleDeclaration> modules=new List<ModuleDeclaration>();
            public Builder Named(string format){this.format=format;return this;}
            // All events in order. The order is the event number in exported data.
            public Builder Events(params string[] names){events.AddRange(names);return this;}
            public Builder Kind(string kind,params string[] allowed){kinds.Add(new KeyValuePair<string,List<string>>(kind,allowed.ToList()));return this;}
            public Builder Presentation(params string[] names){presentation.AddRange(names);return this;}
            // Subscription events listen to other actors; their event value is a subject instead of a time.
            public Builder Subscriptions(string[] names,params string[] subjectNames){subscription.AddRange(names);subjects.AddRange(subjectNames);return this;}
            public Builder Timed(params string[] names){timed.AddRange(names);return this;}
            public Builder Exits(string pass,string fail){exits.Add(pass);exits.Add(fail);return this;}
            public Builder Start(string name){start=name;return this;}
            public Builder Enum(string name,params string[] values){enums.Add(new KeyValuePair<string,List<string>>(name,values.ToList()));return this;}
            public Builder Module(ModuleDeclaration module){modules.Add(module);return this;}
            public Builder Module(string name,ModuleLine line,string[] events,params ModuleSlot[] slots){modules.Add(new ModuleDeclaration(name,line,"common",events,slots));return this;}
            public ModuleSet Build()
            {
                if(start==null&&events.Count>0)start=events[0];
                return new ModuleSet(this);
            }
        }

        // Shorthands for declaration files.
        public static ModuleSlot Slot(string name,string type,string unit,SlotRole role,object value,string timing="",string[] supported=null)
            =>new ModuleSlot(name,type,unit,role,value,timing,supported);
    }
}
