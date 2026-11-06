using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Handover.Core
{
    /// <summary>
    /// A small, strict JSON reader and writer for the save file, so the core needs no
    /// engine or third-party serializer. Objects become Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers double, plus string, bool and null.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 32;

        public static bool TryParse(string text, out object value, out string error)
        {
            value = null;
            error = null;
            if (text == null)
            {
                error = "No text.";
                return false;
            }

            var parser = new Parser(text);
            try
            {
                value = parser.ParseValue(0);
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                {
                    error = $"Unexpected text at {parser.Position}.";
                    value = null;
                    return false;
                }

                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                value = null;
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
            }

            public int Position => _i;
            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                {
                    _i++;
                }
            }

            private FormatException Error(string message) => new FormatException($"{message} at {_i}.");

            public object ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error("Nesting too deep");
                }

                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("Unexpected end");
                }

                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }

                        throw Error($"Unexpected '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw Error($"Expected {word}");
                }

                _i += word.Length;
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}')
                {
                    _i++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"')
                    {
                        throw Error("Expected a key");
                    }

                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':')
                    {
                        throw Error("Expected ':'");
                    }

                    _i++;
                    if (result.ContainsKey(key))
                    {
                        throw Error($"Duplicate key '{key}'");
                    }

                    result[key] = ParseValue(depth + 1);
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("Unterminated object");
                    }

                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (_s[_i] == '}')
                    {
                        _i++;
                        return result;
                    }

                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ParseArray(int depth)
            {
                var result = new List<object>();
                _i++;
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']')
                {
                    _i++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("Unterminated array");
                    }

                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (_s[_i] == ']')
                    {
                        _i++;
                        return result;
                    }

                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("Unterminated string");
                    }

                    char c = _s[_i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c == '\\')
                    {
                        if (AtEnd)
                        {
                            throw Error("Bad escape");
                        }

                        char e = _s[_i++];
                        switch (e)
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
                                if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    throw Error("Bad unicode escape");
                                }

                                sb.Append((char)code);
                                _i += 4;
                                break;
                            default:
                                throw Error("Bad escape");
                        }
                    }
                    else if (c < ' ')
                    {
                        throw Error("Control character in string");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                if (_s[_i] == '-')
                {
                    _i++;
                }

                while (!AtEnd && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '+' || _s[_i] == '-'))
                {
                    _i++;
                }

                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                {
                    throw Error($"Bad number '{token}'");
                }

                return d;
            }
        }

        // ---- writer ---------------------------------------------------------------

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, int indent)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> obj:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in obj)
                    {
                        sb.Append(first ? "\n" : ",\n");
                        first = false;
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        Write(sb, pair.Value, indent + 1);
                    }

                    if (!first)
                    {
                        sb.Append('\n').Append(' ', indent * 2);
                    }

                    sb.Append('}');
                    break;
                }

                case IList<object> list:
                {
                    sb.Append('[');
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (k > 0)
                        {
                            sb.Append(", ");
                        }

                        Write(sb, list[k], indent + 1);
                    }

                    sb.Append(']');
                    break;
                }

                default:
                    throw new ArgumentException($"Cannot serialize {value.GetType().Name}.");
            }
        }

        private static void WriteString(StringBuilder sb, string s)
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
