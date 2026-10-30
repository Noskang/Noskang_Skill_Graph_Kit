using System;
using System.Collections.Generic;

namespace Noskang.SkillGraph
{
    public enum ModuleLine { Logic, Presentation }

    // Balance slots are numbers a designer tunes per skill (they should come from the value sheet);
    // structure slots shape behaviour (often fixed in the graph).
    public enum SlotRole { Balance, Structure }

    // One input of a module. Default null = required. Supported null = every value the type allows is run by the runtime.
    public sealed class ModuleSlot
    {
        public string Name { get; }
        // string, int, float, time, bool, enum:<NAME>, list:enum:<NAME>, key:<band role or digits>
        public string Type { get; }
        public string Unit { get; }
        public SlotRole Role { get; }
        // null, bool, int, long, double or string
        public object Default { get; }
        // When the runtime reads the slot (free text the host defines, e.g. cast, spawn, hit)
        public string Timing { get; }
        public IReadOnlyList<string> Supported { get; }
        public bool Required=>Default==null;
        public ModuleSlot(string name,string type,string unit,SlotRole role,object value,string timing,string[] supported=null)
        {
            Name=name;Type=type;Unit=unit??"";Role=role;Default=value;Timing=timing??"";
            Supported=supported==null?null:Array.AsReadOnly(supported);
        }
    }

    public sealed class ModuleDeclaration
    {
        public int Index { get; internal set; }
        public string Name { get; }
        public ModuleLine Line { get; }
        public string Group { get; }
        public IReadOnlyList<string> Events { get; }
        public IReadOnlyList<ModuleSlot> Slots { get; }
        // Named exits (e.g. Pass, Fail) raise the exit events of the module set with this node as the source.
        public IReadOnlyList<string> Exits { get; }
        public bool Random { get; }
        // false = declared for planning; product data may not use it yet.
        public bool Implemented { get; }
        public ModuleDeclaration(string name,ModuleLine line,string group,string[] events,ModuleSlot[] slots,string[] exits=null,bool random=false,bool implemented=true)
        {
            Name=name;Line=line;Group=group??"common";Events=Array.AsReadOnly(events);Slots=Array.AsReadOnly(slots??Array.Empty<ModuleSlot>());
            Exits=Array.AsReadOnly(exits??Array.Empty<string>());Random=random;Implemented=implemented;
        }
        public int SlotIndex(string name)
        {for(int i=0;i<Slots.Count;i++)if(Slots[i].Name==name)return i;return -1;}
    }
}
