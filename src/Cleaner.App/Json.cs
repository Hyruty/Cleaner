using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cleaner
{
    /// <summary>
    /// Tiny dependency-free JSON reader/writer. Only the shapes this app needs
    /// are supported: objects, arrays, strings, numbers, booleans and null.
    /// Keeps settings human readable and removes the need for extra assemblies.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            int index = 0;
            object value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index < text.Length)
            {
                throw new FormatException("Unexpected trailing characters at position " + index + ".");
            }

            return value;
        }

        public static string Serialize(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, 0, true);
            return sb.ToString();
        }

        public static string SerializeCompact(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, 0, false);
            return sb.ToString();
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            Dictionary<string, object> map = value as Dictionary<string, object>;
            return map;
        }

        public static string GetString(object node, string key, string fallback)
        {
            Dictionary<string, object> map = AsObject(node);
            if (map == null)
            {
                return fallback;
            }

            object raw;
            if (!map.TryGetValue(key, out raw) || raw == null)
            {
                return fallback;
            }

            string s = raw as string;
            return s ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        public static bool GetBool(object node, string key, bool fallback)
        {
            Dictionary<string, object> map = AsObject(node);
            if (map == null)
            {
                return fallback;
            }

            object raw;
            if (!map.TryGetValue(key, out raw) || raw == null)
            {
                return fallback;
            }

            if (raw is bool)
            {
                return (bool)raw;
            }

            bool parsed;
            if (bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public static int GetInt(object node, string key, int fallback)
        {
            Dictionary<string, object> map = AsObject(node);
            if (map == null)
            {
                return fallback;
            }

            object raw;
            if (!map.TryGetValue(key, out raw) || raw == null)
            {
                return fallback;
            }

            double d;
            if (raw is double)
            {
                return (int)(double)raw;
            }

            if (double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                return (int)d;
            }

            return fallback;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF')
                {
                    index++;
                    continue;
                }

                break;
            }
        }

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new FormatException("Unexpected end of JSON input.");
            }

            char c = text[index];
            switch (c)
            {
                case '{':
                    return ParseObject(text, ref index);
                case '[':
                    return ParseArray(text, ref index);
                case '"':
                    return ParseString(text, ref index);
                case 't':
                    Expect(text, ref index, "true");
                    return true;
                case 'f':
                    Expect(text, ref index, "false");
                    return false;
                case 'n':
                    Expect(text, ref index, "null");
                    return null;
                default:
                    return ParseNumber(text, ref index);
            }
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length ||
                string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Invalid JSON literal at position " + index + ".");
            }

            index += literal.Length;
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            index++; // {
            SkipWhitespace(text, ref index);

            if (index < text.Length && text[index] == '}')
            {
                index++;
                return map;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new FormatException("Expected ':' at position " + index + ".");
                }

                index++;
                map[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new FormatException("Unterminated JSON object.");
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return map;
                }

                throw new FormatException("Expected ',' or '}' at position " + index + ".");
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            List<object> list = new List<object>();
            index++; // [
            SkipWhitespace(text, ref index);

            if (index < text.Length && text[index] == ']')
            {
                index++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new FormatException("Unterminated JSON array.");
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return list;
                }

                throw new FormatException("Expected ',' or ']' at position " + index + ".");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            if (index >= text.Length || text[index] != '"')
            {
                throw new FormatException("Expected a string at position " + index + ".");
            }

            index++;
            StringBuilder sb = new StringBuilder();

            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    break;
                }

                char esc = text[index++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            throw new FormatException("Invalid unicode escape.");
                        }

                        sb.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                    default:
                        throw new FormatException("Invalid escape sequence \\" + esc + ".");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private static object ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    index++;
                    continue;
                }

                break;
            }

            string raw = text.Substring(start, index - start);
            double value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new FormatException("Invalid JSON number '" + raw + "'.");
            }

            return value;
        }

        private static void WriteValue(StringBuilder sb, object value, int depth, bool indented)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            string s = value as string;
            if (s != null)
            {
                WriteString(sb, s);
                return;
            }

            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }

            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null)
            {
                WriteMap(sb, map, depth, indented);
                return;
            }

            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                Dictionary<string, object> converted = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in dictionary)
                {
                    converted[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = entry.Value;
                }

                WriteMap(sb, converted, depth, indented);
                return;
            }

            IEnumerable sequence = value as IEnumerable;
            if (sequence != null)
            {
                WriteArray(sb, sequence, depth, indented);
                return;
            }

            if (value is IFormattable)
            {
                sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteMap(StringBuilder sb, IDictionary<string, object> map, int depth, bool indented)
        {
            if (map.Count == 0)
            {
                sb.Append("{ }");
                return;
            }

            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> pair in map)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                NewLine(sb, depth + 1, indented);
                WriteString(sb, pair.Key);
                sb.Append(indented ? ": " : ":");
                WriteValue(sb, pair.Value, depth + 1, indented);
            }

            NewLine(sb, depth, indented);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable sequence, int depth, bool indented)
        {
            List<object> items = new List<object>();
            foreach (object item in sequence)
            {
                items.Add(item);
            }

            if (items.Count == 0)
            {
                sb.Append("[ ]");
                return;
            }

            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                NewLine(sb, depth + 1, indented);
                WriteValue(sb, items[i], depth + 1, indented);
            }

            NewLine(sb, depth, indented);
            sb.Append(']');
        }

        private static void NewLine(StringBuilder sb, int depth, bool indented)
        {
            if (!indented)
            {
                return;
            }

            sb.Append("\r\n");
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
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
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
