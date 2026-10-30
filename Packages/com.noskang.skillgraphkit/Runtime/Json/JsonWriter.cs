using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Noskang.SkillGraph
{
    // An object with ordered keys. Values: null, bool, string, int, long, double, LocalText, JsonObject, or an IEnumerable of those.
    public sealed class JsonObject : IEnumerable<KeyValuePair<string,object>>
    {
        private readonly List<KeyValuePair<string,object>> items=new List<KeyValuePair<string,object>>();
        public void Add(string key,object value)=>items.Add(new KeyValuePair<string,object>(key,value));
        public JsonObject With(string key,object value){Add(key,value);return this;}
        public int Count=>items.Count;
        public IEnumerator<KeyValuePair<string,object>> GetEnumerator()=>items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>items.GetEnumerator();
    }

    // Writes the same bytes as Python's json.dumps(value, ensure_ascii=False, indent=1) plus a final newline,
    // so files exported from C# declarations and files written by the Python tools can be compared byte for byte.
    public static class JsonWriter
    {
        public static string Write(object value)
        {
            var b=new StringBuilder();
            Value(b,value,0);
            return b.Append('\n').ToString();
        }

        public static byte[] Utf8(string text)=>new UTF8Encoding(false).GetBytes(text);

        public static string Sha256(string text)
        {
            using var sha=SHA256.Create();
            var hash=sha.ComputeHash(Utf8(text));
            var b=new StringBuilder(64);
            foreach(var x in hash)b.Append(x.ToString("x2",CultureInfo.InvariantCulture));
            return b.ToString();
        }

        private static void Indent(StringBuilder b,int depth){b.Append('\n');b.Append(' ',depth);}

        private static void Value(StringBuilder b,object value,int depth)
        {
            switch(value)
            {
                case null:b.Append("null");return;
                case bool flag:b.Append(flag?"true":"false");return;
                case string text:Quote(b,text);return;
                case LocalText local:Value(b,local.ToJson(),depth);return;
                case int i:b.Append(i.ToString(CultureInfo.InvariantCulture));return;
                case long l:b.Append(l.ToString(CultureInfo.InvariantCulture));return;
                case float f:b.Append(Number(f));return;
                case double d:b.Append(Number(d));return;
                case JsonObject obj:
                    if(obj.Count==0){b.Append("{}");return;}
                    b.Append('{');
                    bool first=true;
                    foreach(var kv in obj)
                    {
                        if(!first)b.Append(',');
                        first=false;
                        Indent(b,depth+1);Quote(b,kv.Key);b.Append(": ");Value(b,kv.Value,depth+1);
                    }
                    Indent(b,depth);b.Append('}');
                    return;
                case IEnumerable list:
                    var items=new List<object>();
                    foreach(var x in list)items.Add(x);
                    if(items.Count==0){b.Append("[]");return;}
                    b.Append('[');
                    for(int k=0;k<items.Count;k++)
                    {
                        if(k>0)b.Append(',');
                        Indent(b,depth+1);Value(b,items[k],depth+1);
                    }
                    Indent(b,depth);b.Append(']');
                    return;
                default:throw new ArgumentException("Cannot write "+value.GetType().Name+" as JSON");
            }
        }

        // Python float repr: shortest round-trip digits, always a '.' or an exponent, exponent as e-05 / e+16.
        public static string Number(double d)
        {
            if(double.IsNaN(d)||double.IsInfinity(d))throw new ArgumentException("JSON numbers must be finite");
            string sign=d<0||(d==0&&1/d<0)?"-":"";
            string r=Math.Abs(d).ToString("R",CultureInfo.InvariantCulture);
            int e=r.IndexOfAny(new[]{'E','e'});
            string mantissa=e<0?r:r.Substring(0,e);
            int exponent=e<0?0:int.Parse(r.Substring(e+1),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture);
            int dot=mantissa.IndexOf('.');
            string all=mantissa.Replace(".","");
            // value = 0.<digits> x 10^point
            int point=(dot<0?mantissa.Length:dot)+exponent;
            string digits=all.TrimStart('0');
            point-=all.Length-digits.Length;
            digits=digits.TrimEnd('0');
            if(digits.Length==0)return sign+"0.0";
            int sci=point-1;
            if(sci<-4||sci>=16)
            {
                string m=digits.Length==1?digits:digits[0]+"."+digits.Substring(1);
                return sign+m+"e"+(sci<0?"-":"+")+Math.Abs(sci).ToString("00",CultureInfo.InvariantCulture);
            }
            if(point<=0)return sign+"0."+new string('0',-point)+digits;
            if(point>=digits.Length)return sign+digits+new string('0',point-digits.Length)+".0";
            return sign+digits.Substring(0,point)+"."+digits.Substring(point);
        }

        public static void Quote(StringBuilder b,string text)
        {
            b.Append('"');
            foreach(char c in text??"")
            {
                switch(c)
                {
                    case '"':b.Append("\\\"");break;
                    case '\\':b.Append("\\\\");break;
                    case '\n':b.Append("\\n");break;
                    case '\r':b.Append("\\r");break;
                    case '\t':b.Append("\\t");break;
                    case '\b':b.Append("\\b");break;
                    case '\f':b.Append("\\f");break;
                    default:
                        if(c<' ')b.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
    }
}
