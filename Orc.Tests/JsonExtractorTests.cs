using Orc.Core.Orchitect;
using Xunit;

namespace Orc.Tests;

public class JsonExtractorTests
{
    [Fact]
    public void Returns_null_for_empty()
    {
        Assert.Null(JsonExtractor.ExtractFirstObjectOrArray(""));
        Assert.Null(JsonExtractor.ExtractFirstObjectOrArray("no json here"));
    }

    [Fact]
    public void Extracts_simple_object()
    {
        var s = JsonExtractor.ExtractFirstObjectOrArray("prefix {\"a\":1} suffix");
        Assert.Equal("{\"a\":1}", s);
    }

    [Fact]
    public void Extracts_array_first_when_seen_first()
    {
        var s = JsonExtractor.ExtractFirstObjectOrArray("noise [{\"a\":1},{\"b\":2}] then {\"c\":3}");
        Assert.Equal("[{\"a\":1},{\"b\":2}]", s);
    }

    [Fact]
    public void Handles_nested_objects()
    {
        var s = JsonExtractor.ExtractFirstObjectOrArray("{\"a\":{\"b\":{\"c\":[1,2,3]}}}");
        Assert.Equal("{\"a\":{\"b\":{\"c\":[1,2,3]}}}", s);
    }

    [Fact]
    public void Ignores_braces_inside_strings()
    {
        var s = JsonExtractor.ExtractFirstObjectOrArray("{\"a\":\"} } }\"}");
        Assert.Equal("{\"a\":\"} } }\"}", s);
    }

    [Fact]
    public void Handles_escaped_quotes_inside_strings()
    {
        var s = JsonExtractor.ExtractFirstObjectOrArray("{\"a\":\"x \\\" } y\"}");
        Assert.Equal("{\"a\":\"x \\\" } y\"}", s);
    }

    [Fact]
    public void Skips_fenced_text_before_json()
    {
        var s = JsonExtractor.ExtractFirstObjectOrArray("```json\n[{\"k\":\"v\"}]\n```");
        Assert.Equal("[{\"k\":\"v\"}]", s);
    }
}
