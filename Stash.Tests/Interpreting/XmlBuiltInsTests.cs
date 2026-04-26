namespace Stash.Tests.Interpreting;

public class XmlBuiltInsTests : StashTestBase
{
    // ── xml.valid ─────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_WellFormedElement_ReturnsTrue()
    {
        var result = Run("let result = xml.valid(\"<root/>\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_WellFormedWithChildren_ReturnsTrue()
    {
        var result = Run("let result = xml.valid(\"<a><b/></a>\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_WellFormedWithAttributes_ReturnsTrue()
    {
        var result = Run("let result = xml.valid(\"<item id=\\\"1\\\" name=\\\"foo\\\"/>\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_Malformed_UnclosedTag_ReturnsFalse()
    {
        var result = Run("let result = xml.valid(\"<root>\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_PlainText_ReturnsFalse()
    {
        var result = Run("let result = xml.valid(\"not xml at all\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_EmptyString_ReturnsFalse()
    {
        var result = Run("let result = xml.valid(\"\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_NonStringThrows()
    {
        RunExpectingError("xml.valid(42);");
    }

    // ── xml.parse — basic structure ───────────────────────────────────────────

    [Fact]
    public void Parse_SimpleElement_TagIsCorrect()
    {
        var result = Run("let root = xml.parse(\"<server/>\"); let result = root.tag;");
        Assert.Equal("server", result);
    }

    [Fact]
    public void Parse_SimpleElement_AttrsIsEmptyDict()
    {
        var result = Run("let root = xml.parse(\"<root/>\"); let result = len(root.attrs);");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Parse_ElementWithAttributes_AttrsAccessible()
    {
        var result = Run("let root = xml.parse(\"<item id=\\\"42\\\" name=\\\"foo\\\"/>\"); let result = root.attrs[\"id\"];");
        Assert.Equal("42", result);
    }

    [Fact]
    public void Parse_ElementWithAttributes_MultipleAttrs()
    {
        var result = Run("let root = xml.parse(\"<item id=\\\"42\\\" name=\\\"foo\\\"/>\"); let result = root.attrs[\"name\"];");
        Assert.Equal("foo", result);
    }

    [Fact]
    public void Parse_NestedElements_ChildrenAccessible()
    {
        var result = Run("let root = xml.parse(\"<parent><child/></parent>\"); let result = len(root.children);");
        // children includes element nodes (and possibly whitespace text nodes — the child element is present)
        Assert.True((long)result! >= 1);
    }

    [Fact]
    public void Parse_NestedElementChildTag_Correct()
    {
        var result = Run("let root = xml.parse(\"<parent><child/></parent>\"); let c = root.children[0]; let result = c.tag;");
        Assert.Equal("child", result);
    }

    [Fact]
    public void Parse_TextContent_TextFieldPopulated()
    {
        var result = Run("let root = xml.parse(\"<msg>hello</msg>\"); let result = root.text;");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Parse_EmptyElement_TextFieldEmpty()
    {
        var result = Run("let root = xml.parse(\"<root/>\"); let result = root.text;");
        Assert.Equal("", result);
    }

    [Fact]
    public void Parse_UnicodeContent_Preserved()
    {
        var result = Run("let root = xml.parse(\"<msg>héllo wörld</msg>\"); let result = root.text;");
        Assert.Equal("héllo wörld", result);
    }

    [Fact]
    public void Parse_MalformedXml_ThrowsError()
    {
        RunExpectingError("xml.parse(\"<unclosed>\");");
    }

    [Fact]
    public void Parse_NonStringArgThrows()
    {
        RunExpectingError("xml.parse(42);");
    }

    // ── xml.parse — options ───────────────────────────────────────────────────

    [Fact]
    public void Parse_PreserveWhitespaceFalse_WhitespaceOnlyTextNodeSkipped()
    {
        // Without preserveWhitespace, purely-whitespace text between elements is dropped
        var result = Run("""
            let src = "<root>\n    <a/>\n</root>";
            let opts = xml.XmlParseOptions { preserveWhitespace: false };
            let root = xml.parse(src, opts);
            // All children should be element nodes, not whitespace text nodes
            let result = root.children[0].tag;
        """);
        Assert.Equal("a", result);
    }

    [Fact]
    public void Parse_PreserveWhitespaceTrue_WhitespaceIncluded()
    {
        // With preserveWhitespace, whitespace-only text nodes are preserved
        var result = Run("""
            let src = "<root>\n    <a/>\n</root>";
            let opts = xml.XmlParseOptions { preserveWhitespace: true };
            let root = xml.parse(src, opts);
            let result = len(root.children);
        """);
        // With whitespace preserved there should be at least 3 nodes: text, a, text
        Assert.True((long)result! >= 3);
    }

    // ── xml.stringify ─────────────────────────────────────────────────────────

    [Fact]
    public void Stringify_SimpleNode_ProducesXmlString()
    {
        var result = Run("let root = xml.parse(\"<root/>\"); let result = xml.stringify(root);");
        var xml = Assert.IsType<string>(result);
        Assert.Contains("root", xml);
    }

    [Fact]
    public void Stringify_WithIndentZero_NoIndentation()
    {
        var result = Run("let root = xml.parse(\"<a><b/></a>\"); let opts = xml.XmlStringifyOptions { indent: 0 }; let result = xml.stringify(root, opts);");
        var xml = Assert.IsType<string>(result);
        Assert.DoesNotContain("\n", xml);
    }

    [Fact]
    public void Stringify_WithIndent2_ContainsNewlines()
    {
        var result = Run("let root = xml.parse(\"<a><b/></a>\"); let opts = xml.XmlStringifyOptions { indent: 2 }; let result = xml.stringify(root, opts);");
        var xml = Assert.IsType<string>(result);
        Assert.Contains("\n", xml);
    }

    [Fact]
    public void Stringify_WithDeclarationTrue_ContainsXmlDeclaration()
    {
        var result = Run("let root = xml.parse(\"<root/>\"); let opts = xml.XmlStringifyOptions { declaration: true }; let result = xml.stringify(root, opts);");
        var xml = Assert.IsType<string>(result);
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void Stringify_WithDeclarationFalse_NoDeclaration()
    {
        var result = Run("let root = xml.parse(\"<root/>\"); let opts = xml.XmlStringifyOptions { declaration: false }; let result = xml.stringify(root, opts);");
        var xml = Assert.IsType<string>(result);
        Assert.DoesNotContain("<?xml", xml);
    }

    [Fact]
    public void Stringify_NonNodeThrows()
    {
        RunExpectingError("xml.stringify(\"not a node\");");
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ParseStringifyParse_TagPreserved()
    {
        var result = Run("""
            let src = "<config version=\"2\"><entry key=\"host\">localhost</entry></config>";
            let root1 = xml.parse(src);
            let xmlStr = xml.stringify(root1);
            let root2 = xml.parse(xmlStr);
            let result = root2.tag;
        """);
        Assert.Equal("config", result);
    }

    [Fact]
    public void RoundTrip_ParseStringifyParse_AttributePreserved()
    {
        var result = Run("""
            let src = "<config version=\"2\"><entry key=\"host\">localhost</entry></config>";
            let root1 = xml.parse(src);
            let xmlStr = xml.stringify(root1);
            let root2 = xml.parse(xmlStr);
            let result = root2.attrs["version"];
        """);
        Assert.Equal("2", result);
    }

    // ── xml.query ─────────────────────────────────────────────────────────────

    [Fact]
    public void Query_SelectChildElements_ReturnsNodes()
    {
        var result = Run("""
            let src = "<root><item id=\"1\"/><item id=\"2\"/></root>";
            let root = xml.parse(src);
            let result = len(xml.query(root, "item"));
        """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Query_DeepPath_ReturnsMatchingNode()
    {
        var result = Run("""
            let src = "<root><a><b><c/></b></a></root>";
            let root = xml.parse(src);
            let nodes = xml.query(root, "a/b/c");
            let result = nodes[0].tag;
        """);
        Assert.Equal("c", result);
    }

    [Fact]
    public void Query_AttributeXpath_ReturnsStringValues()
    {
        var result = Run("""
            let src = "<root><item id=\"42\"/></root>";
            let root = xml.parse(src);
            let vals = xml.query(root, "item/@id");
            let result = vals[0];
        """);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Query_NoMatch_ReturnsEmptyArray()
    {
        var result = Run("""
            let src = "<root/>";
            let root = xml.parse(src);
            let result = len(xml.query(root, "nonexistent"));
        """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Query_InvalidXpath_ThrowsError()
    {
        RunExpectingError("""
            let root = xml.parse("<root/>");
            xml.query(root, "***invalid***");
        """);
    }
}
