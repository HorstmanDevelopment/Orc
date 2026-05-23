namespace Orc.Core.Orchitect;

public static class JsonExtractor
{
    public static string? ExtractFirstObjectOrArray(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var start = -1;
        var open = '{';
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' || text[i] == '[')
            {
                start = i;
                open = text[i];
                break;
            }
        }
        if (start < 0) return null;

        var close = open == '{' ? '}' : ']';
        var depth = 0;
        var inStr = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (inStr)
            {
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inStr = false; }
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return text.Substring(start, i - start + 1);
            }
        }
        return null;
    }
}
