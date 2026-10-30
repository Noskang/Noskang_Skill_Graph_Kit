using System;
using System.Collections.Generic;
using System.Linq;

namespace Noskang.SkillGraph
{
    // A designer-facing text in English and Korean. Written as {"en": ..., "ko": ...}.
    public readonly struct LocalText
    {
        public string En { get; }
        public string Ko { get; }
        public LocalText(string en,string ko){En=en??"";Ko=ko??en??"";}
        public string Get(string language)=>language=="ko"?Ko:En;
        public bool IsEmpty=>string.IsNullOrWhiteSpace(En)&&string.IsNullOrWhiteSpace(Ko);
        public JsonObject ToJson()=>new JsonObject().With("en",En).With("ko",Ko);
        public static LocalText From(JsonNode node)
        {
            if(node==null||node.IsNull)return new LocalText("","");
            if(node.Kind==JsonKind.String)return new LocalText(node.Text,node.Text);
            return new LocalText(node.Get("en")?.TextOr("")??"",node.Get("ko")?.TextOr("")??"");
        }
        public override string ToString()=>En;
    }

    // Editor-only presentation of a module: label, note, family colour, slot labels, stages (basic / advanced chips),
    // which slots a kind value shows, choose-one groups and slots the runtime does not run yet.
    // None of this is part of the dictionary hash: rewording never changes exported data.
    public sealed class ModuleView
    {
        public sealed class SlotText { public string Name; public LocalText Label, Description; }
        public sealed class StageGroup { public string Name; public bool Advanced; public string[] Slots; }
        public sealed class KindRule { public string[] Values, Events, Slots; public bool Verified; }
        public string Name { get; }
        public LocalText Label { get; }
        public string Family { get; }
        public LocalText Note { get; }
        public List<SlotText> Slots { get; } = new List<SlotText>();
        public List<StageGroup> Stages { get; } = new List<StageGroup>();
        public string KindSlot { get; private set; }
        public List<KindRule> KindRules { get; } = new List<KindRule>();
        public string ChooseName { get; private set; }
        public string[] ChooseSlots { get; private set; } = Array.Empty<string>();
        public string[] UnusedSlots { get; private set; } = Array.Empty<string>();
        public ModuleView(string name,LocalText label,string family,LocalText note){Name=name;Label=label;Family=family;Note=note;}
        public ModuleView Slot(string name,LocalText label,LocalText description){Slots.Add(new SlotText{Name=name,Label=label,Description=description});return this;}
        public ModuleView Stage(string name,params string[] slots){Stages.Add(new StageGroup{Name=name,Slots=slots});return this;}
        public ModuleView Advanced(string name,params string[] slots){Stages.Add(new StageGroup{Name=name,Advanced=true,Slots=slots});return this;}
        public ModuleView Kind(string slot){KindSlot=slot;return this;}
        // Slots shown when the kind slot holds one of values (comma list, "" = any) on one of events (comma list, "" = any). First match wins.
        public ModuleView When(string values,string events,bool verified,params string[] slots)
        {KindRules.Add(new KindRule{Values=Split(values),Events=Split(events),Slots=slots,Verified=verified});return this;}
        public ModuleView Choose(string name,params string[] slots){ChooseName=name;ChooseSlots=slots;return this;}
        public ModuleView Unused(params string[] slots){UnusedSlots=slots;return this;}
        private static string[] Split(string text)=>string.IsNullOrEmpty(text)?Array.Empty<string>():text.Split(',').Select(x=>x.Trim()).ToArray();
    }

    public sealed class ModuleFamily
    {
        public string Name { get; }
        public LocalText Label { get; }
        public string Color { get; }
        public string Icon { get; }
        public ModuleFamily(string name,LocalText label,string color,string icon){Name=name;Label=label;Color=color;Icon=icon;}
    }

    public sealed class ModuleDisplaySet
    {
        public const string DefaultFormat="SkillGraph Module Display 1";
        public string Format { get; set; } = DefaultFormat;
        public List<ModuleFamily> Families { get; } = new List<ModuleFamily>();
        public List<KeyValuePair<string,LocalText>> EventLabels { get; } = new List<KeyValuePair<string,LocalText>>();
        public List<KeyValuePair<string,List<KeyValuePair<string,LocalText>>>> EnumLabels { get; } = new List<KeyValuePair<string,List<KeyValuePair<string,LocalText>>>>();
        public List<ModuleView> Modules { get; } = new List<ModuleView>();

        public ModuleDisplaySet Family(string name,LocalText label,string color,string icon){Families.Add(new ModuleFamily(name,label,color,icon));return this;}
        public ModuleDisplaySet Event(string name,LocalText label){EventLabels.Add(new KeyValuePair<string,LocalText>(name,label));return this;}
        public ModuleDisplaySet EnumLabel(string name,params (string value,LocalText label)[] values)
        {EnumLabels.Add(new KeyValuePair<string,List<KeyValuePair<string,LocalText>>>(name,values.Select(v=>new KeyValuePair<string,LocalText>(v.value,v.label)).ToList()));return this;}
        public ModuleView Module(string name,LocalText label,string family,LocalText note){var v=new ModuleView(name,label,family,note);Modules.Add(v);return v;}

        // The display must cover every module and slot of the dictionary, in order. Returns problem sentences.
        public List<string> Check(ModuleSet set)
        {
            var problems=new List<string>();
            var families=new HashSet<string>(Families.Select(f=>f.Name));
            if(Modules.Count!=set.Modules.Count)problems.Add($"Module count differs: display {Modules.Count}, dictionary {set.Modules.Count}");
            for(int i=0;i<set.Modules.Count;i++)
            {
                var m=set.Modules[i];
                if(i>=Modules.Count||Modules[i].Name!=m.Name){problems.Add($"Module #{i} order differs: {m.Name}");continue;}
                var v=Modules[i];
                var names=m.Slots.Select(s=>s.Name).ToList();
                if(!families.Contains(v.Family))problems.Add($"{m.Name}: unknown family {v.Family}");
                if(!v.Slots.Select(s=>s.Name).SequenceEqual(names))problems.Add($"{m.Name}: slot list or order differs from the dictionary");
                foreach(var s in v.Slots)if(s.Label.IsEmpty)problems.Add($"{m.Name}.{s.Name}: no label");
                if(v.Label.IsEmpty)problems.Add($"{m.Name}: no label");
                var staged=v.Stages.SelectMany(st=>st.Slots).ToList();
                if(staged.Count!=staged.Distinct().Count()||!staged.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(names.OrderBy(x=>x,StringComparer.Ordinal)))
                    problems.Add($"{m.Name}: stages must hold every slot exactly once");
                if(v.Stages.Count>0&&v.Stages[0].Advanced)problems.Add($"{m.Name}: the first stage cannot be advanced");
                foreach(var name in v.ChooseSlots.Concat(v.UnusedSlots).Concat(v.KindRules.SelectMany(r=>r.Slots)))
                    if(!names.Contains(name))problems.Add($"{m.Name}: unknown slot {name}");
                if(v.KindSlot!=null)
                {
                    int k=m.SlotIndex(v.KindSlot);
                    if(k<0||!m.Slots[k].Type.StartsWith("enum:",StringComparison.Ordinal)){problems.Add($"{m.Name}: kind slot {v.KindSlot} is not an enum slot");continue;}
                    var allowed=set.SlotEnum(m.Slots[k].Type.Substring(5));
                    foreach(var r in v.KindRules)
                    {
                        foreach(var value in r.Values)if(!allowed.Contains(value))problems.Add($"{m.Name}: kind value {value} is not in the enum");
                        foreach(var e in r.Events)if(set.EventIndex(e)<0)problems.Add($"{m.Name}: unknown event {e}");
                    }
                }
                else if(v.KindRules.Count>0)problems.Add($"{m.Name}: kind rules without a kind slot");
            }
            foreach(var e in set.Events)if(!EventLabels.Any(x=>x.Key==e))problems.Add($"Event {e} has no label");
            return problems;
        }

        public JsonObject ToJson(string dictionaryHash)
        {
            var events=new JsonObject();
            foreach(var e in EventLabels)events.Add(e.Key,e.Value);
            var enums=new JsonObject();
            foreach(var e in EnumLabels)
            {
                var values=new JsonObject();
                foreach(var v in e.Value)values.Add(v.Key,v.Value);
                enums.Add(e.Key,values);
            }
            return new JsonObject()
                .With("format",Format).With("dictionaryHash",dictionaryHash)
                .With("families",Families.Select(f=>new JsonObject().With("name",f.Name).With("label",f.Label).With("color",f.Color).With("icon",f.Icon)))
                .With("events",events).With("enumLabels",enums)
                .With("modules",Modules.Select(v=>new JsonObject()
                    .With("name",v.Name).With("label",v.Label).With("family",v.Family).With("note",v.Note)
                    .With("slots",v.Slots.Select(s=>new object[]{s.Name,s.Label,s.Description}))
                    .With("stages",v.Stages.Select(s=>new JsonObject().With("name",s.Name).With("advanced",s.Advanced).With("slots",s.Slots)))
                    .With("kind",v.KindSlot==null?null:new JsonObject().With("slot",v.KindSlot)
                        .With("rules",v.KindRules.Select(r=>new JsonObject().With("values",r.Values).With("events",r.Events).With("slots",r.Slots).With("verified",r.Verified))))
                    .With("choose",v.ChooseName==null?null:new JsonObject().With("name",v.ChooseName).With("slots",v.ChooseSlots))
                    .With("unused",v.UnusedSlots)));
        }

        public string ExportJson(string dictionaryHash)=>JsonWriter.Write(ToJson(dictionaryHash));
    }

    // A host declares its modules by subclassing this (any assembly). The editor finds it to export the dictionary files.
    public abstract class ModuleSetProvider
    {
        public abstract ModuleSet Build();
        public virtual ModuleDisplaySet BuildDisplay(ModuleSet set)=>null;
    }
}
