using System.Text;
using System.Text.Json;

namespace LicoreTestApp.Services;

/// <summary>
/// Produces a canonical JSON string (RFC 8785 subset) over a <see cref="JsonElement"/>.
/// Rules: UTF-8 encoding, object keys in lexicographic (ordinal) order applied recursively,
/// no whitespace between tokens, standard JSON string escaping.
/// This is the byte-exact representation that licore.dll signs and verifies.
/// </summary>
internal static class CanonicalJson
{
    public static string Serialize(JsonElement element)
    {
        var sb = new StringBuilder(256);
        Write(element, sb);
        return sb.ToString();
    }

    private static void Write(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
            {
                sb.Append('{');
                var props = e.EnumerateObject()
                             .OrderBy(p => p.Name, StringComparer.Ordinal)
                             .ToList();
                for (int i = 0; i < props.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteString(props[i].Name, sb);
                    sb.Append(':');
                    Write(props[i].Value, sb);
                }
                sb.Append('}');
                break;
            }
            case JsonValueKind.Array:
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in e.EnumerateArray())
                {
                    if (!first) sb.Append(',');
                    Write(item, sb);
                    first = false;
                }
                sb.Append(']');
                break;
            }
            case JsonValueKind.String:
                WriteString(e.GetString()!, sb);
                break;
            case JsonValueKind.Number:
                sb.Append(e.GetRawText());
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            default: // Null
                sb.Append("null");
                break;
        }
    }

    private static void WriteString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
