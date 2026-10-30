using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Noskang.SkillGraph
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    // Small strict JSON reader. Exported skill data mixes types inside arrays ([slot, "ref", index]),
    // so it is read as a tree instead of through a serializer. Duplicate keys, trailing text and non-finite numbers are rejected.
    public sealed class JsonNode
    {
        public JsonKind Kind { get; }
        public double Number { get; }
        public string Text { get; }
        public bool Bool { get; }
        public IReadOnlyList<JsonNode> Items { get; }
        public IReadOnlyDictionary<string,JsonNode> Fields { get; }
        // Object keys in file order (Fields is a dictionary and does not keep order).
        public IReadOnlyList<string> Keys { get; }
        private JsonNode(JsonKind kind,double number=0,string text=null,bool flag=false,List<JsonNode> items=null,Dictionary<string,JsonNode> fields=null,List<string> keys=null)
        {Kind=kind;Number=number;Text=text;Bool=flag;Items=items;Fields=fields;Keys=keys;}
        public bool IsNull=>Kind==JsonKind.Null;
        public bool Has(string name)=>Kind==JsonKind.Object&&Fields.ContainsKey(name);
        public JsonNode this[string name]=>Kind==JsonKind.Object&&Fields.TryGetValue(name,out var value)?value:throw new FormatException("Missing field: "+name);
        public JsonNode this[int index]=>Kind==JsonKind.Array&&index>=0&&index<Items.Count?Items[index]:throw new FormatException("Missing array index: "+index);
        public JsonNode Get(string name)=>Kind==JsonKind.Object&&Fields.TryGetValue(name,out var value)?value:null;
        public double AsNumber()=>Kind==JsonKind.Number?Number:throw new FormatException("A number is required");
        public int AsInt()
        {double v=AsNumber();if(v!=Math.Floor(v)||v<int.MinValue||v>int.MaxValue)throw new FormatException("An integer is required: "+v.ToString(CultureInfo.InvariantCulture));return (int)v;}
        public string AsText()=>Kind==JsonKind.String?Text:throw new FormatException("A string is required");
        public bool AsBool()=>Kind==JsonKind.Bool?Bool:throw new FormatException("A boolean is required");
        public IReadOnlyList<JsonNode> AsArray()=>Kind==JsonKind.Array?Items:throw new FormatException("An array is required");
        public string TextOr(string fallback)=>Kind==JsonKind.String?Text:fallback;

        public static JsonNode Parse(string text)=>Parse(new UTF8Encoding(false).GetBytes(text??""));
        public static JsonNode Parse(byte[] bytes)
        {
            if(bytes==null||bytes.Length>32*1024*1024)throw new FormatException("JSON is too large");
            string text=new UTF8Encoding(false,true).GetString(bytes);
            int i=0;
            if(text.Length>0&&text[0]=='﻿')i=1;
            var node=Value(text,ref i,0);Space(text,ref i);
            if(i!=text.Length)throw new FormatException("Text after the JSON value");
            return node;
        }
        private static void Space(string s,ref int i){while(i<s.Length&&(s[i]==' '||s[i]=='\n'||s[i]=='\r'||s[i]=='\t'))i++;}
        private static JsonNode Value(string s,ref int i,int depth)
        {
            if(depth>64)throw new FormatException("JSON is nested too deep");
            Space(s,ref i);if(i>=s.Length)throw new FormatException("JSON ended early");
            char c=s[i];
            if(c=='{')
            {
                i++;var fields=new Dictionary<string,JsonNode>(StringComparer.Ordinal);var keys=new List<string>();Space(s,ref i);
                if(i<s.Length&&s[i]=='}'){i++;return new JsonNode(JsonKind.Object,fields:fields,keys:keys);}
                while(true)
                {
                    Space(s,ref i);if(i>=s.Length||s[i]!='"')throw new FormatException("A field name is required");
                    string key=Str(s,ref i);Space(s,ref i);
                    if(i>=s.Length||s[i]!=':')throw new FormatException("':' is required");i++;
                    if(fields.ContainsKey(key))throw new FormatException("Duplicate field: "+key);
                    fields.Add(key,Value(s,ref i,depth+1));keys.Add(key);Space(s,ref i);
                    if(i<s.Length&&s[i]==','){i++;continue;}
                    if(i<s.Length&&s[i]=='}'){i++;return new JsonNode(JsonKind.Object,fields:fields,keys:keys);}
                    throw new FormatException("',' or '}' is required");
                }
            }
            if(c=='[')
            {
                i++;var items=new List<JsonNode>();Space(s,ref i);
                if(i<s.Length&&s[i]==']'){i++;return new JsonNode(JsonKind.Array,items:items);}
                while(true)
                {
                    items.Add(Value(s,ref i,depth+1));Space(s,ref i);
                    if(i<s.Length&&s[i]==','){i++;continue;}
                    if(i<s.Length&&s[i]==']'){i++;return new JsonNode(JsonKind.Array,items:items);}
                    throw new FormatException("',' or ']' is required");
                }
            }
            if(c=='"')return new JsonNode(JsonKind.String,text:Str(s,ref i));
            if(Word(s,ref i,"true"))return new JsonNode(JsonKind.Bool,flag:true);
            if(Word(s,ref i,"false"))return new JsonNode(JsonKind.Bool,flag:false);
            if(Word(s,ref i,"null"))return new JsonNode(JsonKind.Null);
            int start=i;
            if(i<s.Length&&s[i]=='-')i++;
            while(i<s.Length&&(char.IsDigit(s[i])||s[i]=='.'||s[i]=='e'||s[i]=='E'||s[i]=='+'||s[i]=='-'))i++;
            if(i==start||!double.TryParse(s.Substring(start,i-start),NumberStyles.Float,CultureInfo.InvariantCulture,out double number)||double.IsNaN(number)||double.IsInfinity(number))
                throw new FormatException("Cannot read a value at "+start);
            return new JsonNode(JsonKind.Number,number:number);
        }
        private static bool Word(string s,ref int i,string word)
        {if(i+word.Length>s.Length||string.CompareOrdinal(s,i,word,0,word.Length)!=0)return false;i+=word.Length;return true;}
        private static string Str(string s,ref int i)
        {
            i++;var b=new StringBuilder();
            while(true)
            {
                if(i>=s.Length)throw new FormatException("Unterminated string");
                char c=s[i++];
                if(c=='"')return b.ToString();
                if(c<' ')throw new FormatException("Control character inside a string");
                if(c!='\\'){b.Append(c);continue;}
                if(i>=s.Length)throw new FormatException("Unterminated escape");
                char e=s[i++];
                switch(e)
                {
                    case '"':b.Append('"');break;case '\\':b.Append('\\');break;case '/':b.Append('/');break;
                    case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;
                    case 'u':
                        if(i+4>s.Length||!int.TryParse(s.Substring(i,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int code))throw new FormatException("Bad \\u escape");
                        b.Append((char)code);i+=4;break;
                    default:throw new FormatException("Unknown escape");
                }
            }
        }
    }
}
