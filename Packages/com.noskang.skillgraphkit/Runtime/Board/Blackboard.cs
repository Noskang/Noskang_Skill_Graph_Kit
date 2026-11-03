using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Noskang.SkillGraph
{
    // One key of the exported boardKeys list. Type: int, float, bool, text or key:<band>. Scope: Cast or Actor (or a host scope).
    public sealed class BoardKey
    {
        public int Index { get; }
        public string Name { get; }
        public string Type { get; }
        public string Scope { get; }
        public object Default { get; }
        public BoardKey(int index,string name,string type,string scope,object value)
        {Index=index;Name=name;Type=type;Scope=scope;Default=Normalize(type,value);}

        // A value as this key keeps it: int (int and key types), double, bool or string.
        public object Normalize(object value)=>Normalize(Type,value);
        public static object Normalize(string type,object value)
        {
            switch(type)
            {
                case "bool":return value is bool b?b:value is string s?string.Equals(s,"true",StringComparison.OrdinalIgnoreCase):ToNumber(value)!=0;
                case "text":return value is string t?t:value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);
                case "float":return ToNumber(value);
                default:return (int)Math.Round(ToNumber(value));
            }
        }
        private static double ToNumber(object value)
            =>value==null?0:value is bool b?(b?1:0):value is string s?(double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0):Convert.ToDouble(value,CultureInfo.InvariantCulture);
    }

    // A change on a board: the key, its value before and after (both normalized). Raised once per key whose value changed.
    public readonly struct BoardChange
    {
        public Blackboard Board { get; }
        public string Key { get; }
        public object Old { get; }
        public object New { get; }
        public BoardChange(Blackboard board,string key,object old,object value){Board=board;Key=key;Old=old;New=value;}
    }

    // Where the boards of a running node live: one board per scope (Cast = a cast and the effects it spawns, Actor = a character).
    public interface IBoardScopes
    {
        Blackboard Board(string scope);
    }

    // Named values nodes write and read while skills run. A board holds the keys of one scope; each key starts at its default.
    // For syncing (e.g. server to client) a game sends Values() and the other side calls Load(); Changed tells views what moved.
    public sealed class Blackboard
    {
        public string Scope { get; }
        public IReadOnlyList<BoardKey> Keys { get; }
        public event Action<BoardChange> Changed;
        private readonly Dictionary<string,BoardKey> byName;
        private readonly Dictionary<string,object> values=new Dictionary<string,object>(StringComparer.Ordinal);

        // keys: every exported key (SkillData.BoardKeys); the board keeps those of its scope.
        public Blackboard(IEnumerable<BoardKey> keys,string scope)
        {
            Scope=scope;
            Keys=(keys??Array.Empty<BoardKey>()).Where(k=>k.Scope==scope).OrderBy(k=>k.Name,StringComparer.Ordinal).ToList().AsReadOnly();
            byName=Keys.ToDictionary(k=>k.Name,StringComparer.Ordinal);
        }

        public bool Has(string key)=>key!=null&&byName.ContainsKey(key);
        public BoardKey Key(string key)=>key!=null&&byName.TryGetValue(key,out var k)?k:null;
        public bool TryGet(string key,out object value)
        {
            value=null;
            if(!Has(key))return false;
            value=values.TryGetValue(key,out var v)?v:byName[key].Default;
            return true;
        }
        public object Get(string key)=>TryGet(key,out var v)?v:throw new KeyNotFoundException("Unknown board key: "+key);
        public double Number(string key){var v=Get(key);return v is bool b?(b?1:0):v is string?0:Convert.ToDouble(v,CultureInfo.InvariantCulture);}
        public int Integer(string key)=>(int)Math.Round(Number(key));
        public bool Flag(string key)=>BoardKey.Normalize("bool",Get(key)) is bool b&&b;
        public string Text(string key)=>Convert.ToString(Get(key),CultureInfo.InvariantCulture);

        // Sets a key (normalized to its type). Changed is raised when the value differs from before.
        public void Set(string key,object value)
        {
            var k=Key(key)??throw new KeyNotFoundException("Unknown board key: "+key);
            Put(k,k.Normalize(value),true);
        }
        // Back to the default.
        public void Clear(string key){var k=Key(key)??throw new KeyNotFoundException("Unknown board key: "+key);Put(k,k.Default,true);}
        public void ClearAll(){foreach(var k in Keys)Put(k,k.Default,true);}

        // Keys whose value differs from the default, in name order: what a game sends to sync this board.
        public IReadOnlyList<KeyValuePair<string,object>> Values()
            =>Keys.Where(k=>values.TryGetValue(k.Name,out var v)&&!Equals(v,k.Default)).Select(k=>new KeyValuePair<string,object>(k.Name,values[k.Name])).ToList();

        // Replaces the whole board: listed keys take their value, the others go back to the default. Unknown keys are skipped
        // (data of another version). notify: raise Changed for each key whose value changed.
        public void Load(IEnumerable<KeyValuePair<string,object>> list,bool notify=true)
        {
            var given=new Dictionary<string,object>(StringComparer.Ordinal);
            foreach(var p in list??Array.Empty<KeyValuePair<string,object>>())if(byName.TryGetValue(p.Key,out var k))given[p.Key]=k.Normalize(p.Value);
            foreach(var k in Keys)Put(k,given.TryGetValue(k.Name,out var v)?v:k.Default,notify);
        }

        private void Put(BoardKey k,object value,bool notify)
        {
            var old=values.TryGetValue(k.Name,out var o)?o:k.Default;
            if(Equals(value,k.Default))values.Remove(k.Name);else values[k.Name]=value;
            if(notify&&!Equals(old,value))Changed?.Invoke(new BoardChange(this,k.Name,old,value));
        }
    }
}
