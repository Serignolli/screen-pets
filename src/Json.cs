using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ScreenPets
{
    // Leitura com JavaScriptSerializer e escrita "bonita" (indentada, sem escapar acentos),
    // para não bagunçar o ~/.claude/settings.json de ninguém.
    public static class Json
    {
        public static object Parse(string text)
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.DeserializeObject(text);
        }

        public static Dictionary<string, object> LoadObject(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Trim().Length == 0) return new Dictionary<string, object>();
            var d = Parse(text) as Dictionary<string, object>;
            if (d == null) throw new InvalidDataException(path + " não contém um objeto JSON");
            return d;
        }

        public static void Save(string path, object value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Stringify(value) + "\n", new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        public static string Stringify(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int ind)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string || v is char) { WriteString(sb, v.ToString()); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            var dict = v as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (DictionaryEntry e in dict)
                {
                    Pad(sb, ind + 1);
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(": ");
                    Write(sb, e.Value, ind + 1);
                    if (++i < dict.Count) sb.Append(",");
                    sb.Append("\n");
                }
                Pad(sb, ind);
                sb.Append("}");
                return;
            }
            var seq = v as IEnumerable;
            if (seq != null)
            {
                var items = new List<object>();
                foreach (object o in seq) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    Pad(sb, ind + 1);
                    Write(sb, items[i], ind + 1);
                    if (i < items.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }
                Pad(sb, ind);
                sb.Append("]");
                return;
            }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is float) { sb.Append(((float)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            var f = v as IFormattable;
            if (f != null) { sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); return; }
            WriteString(sb, v.ToString());
        }

        static void Pad(StringBuilder sb, int ind) { sb.Append(' ', ind * 2); }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- helpers de leitura ----

        public static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v;
            return null;
        }

        public static string Str(IDictionary<string, object> d, string key, string def)
        {
            object v = Get(d, key);
            return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double Num(IDictionary<string, object> d, string key, double def)
        {
            object v = Get(d, key);
            if (v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static bool Bool(IDictionary<string, object> d, string key, bool def)
        {
            object v = Get(d, key);
            return v is bool ? (bool)v : def;
        }

        public static Dictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            return Get(d, key) as Dictionary<string, object>;
        }

        public static List<object> Arr(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null || v is string || v is IDictionary) return null;
            var seq = v as IEnumerable;
            if (seq == null) return null;
            var list = new List<object>();
            foreach (object o in seq) list.Add(o);
            return list;
        }

        public static List<string> Strs(IDictionary<string, object> d, string key)
        {
            var arr = Arr(d, key);
            if (arr == null) return null;
            var list = new List<string>();
            foreach (object o in arr) if (o != null) list.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
            return list;
        }
    }
}
