using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Tpl;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

public class TemplateTests
{
    // ── Helper ──────────────────────────────────────────────────────

    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    // ── Variable Interpolation ──────────────────────────────────────

    [Fact]
    public void Render_SimpleVariable()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "Alice";
            let result = tpl.render("Hello {{ name }}!", d);
        """);
        Assert.Equal("Hello Alice!", result);
    }

    [Fact]
    public void Render_MultipleVariables()
    {
        var result = Run("""
            let d = dict.new();
            d["first"] = "John";
            d["last"] = "Doe";
            let result = tpl.render("{{ first }} {{ last }}", d);
        """);
        Assert.Equal("John Doe", result);
    }

    [Fact]
    public void Render_IntegerVariable()
    {
        var result = Run("""
            let d = dict.new();
            d["count"] = 42;
            let result = tpl.render("Count: {{ count }}", d);
        """);
        Assert.Equal("Count: 42", result);
    }

    [Fact]
    public void Render_NullVariable_RendersEmpty()
    {
        var result = Run("""
            let d = dict.new();
            d["value"] = null;
            let result = tpl.render("Result: {{ value }}.", d);
        """);
        Assert.Equal("Result: .", result);
    }

    [Fact]
    public void Render_BooleanVariable()
    {
        var result = Run("""
            let d = dict.new();
            d["active"] = true;
            let result = tpl.render("Active: {{ active }}", d);
        """);
        Assert.Equal("Active: true", result);
    }

    [Fact]
    public void Render_Expression_Arithmetic()
    {
        var result = Run("""
            let d = dict.new();
            d["x"] = 10;
            d["y"] = 3;
            let result = tpl.render("Sum: {{ x + y }}", d);
        """);
        Assert.Equal("Sum: 13", result);
    }

    [Fact]
    public void Render_Expression_StringConcat()
    {
        var result = Run("""
            let d = dict.new();
            d["first"] = "Hello";
            d["last"] = "World";
            let result = tpl.render("{{ first + \" \" + last }}", d);
        """);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Render_Expression_Ternary()
    {
        var result = Run("""
            let d = dict.new();
            d["active"] = true;
            let result = tpl.render("{{ active ? \"yes\" : \"no\" }}", d);
        """);
        Assert.Equal("yes", result);
    }

    [Fact]
    public void Render_Expression_NullCoalescing()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = null;
            let result = tpl.render("{{ name ?? \"default\" }}", d);
        """);
        Assert.Equal("default", result);
    }

    [Fact]
    public void Render_PlainText_NoDelimiters()
    {
        var result = Run("""
            let d = dict.new();
            let result = tpl.render("Hello World", d);
        """);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Render_EmptyTemplate()
    {
        var result = Run("""
            let d = dict.new();
            let result = tpl.render("", d);
        """);
        Assert.Equal("", result);
    }

    // ── Dot Access ──────────────────────────────────────────────────

    [Fact]
    public void Render_DotAccess_StructField()
    {
        var result = Run("""
            struct Server { host, port }
            let srv = Server { host: "10.0.0.1", port: 22 };
            let d = dict.new();
            d["srv"] = srv;
            let result = tpl.render("{{ srv.host }}:{{ srv.port }}", d);
        """);
        Assert.Equal("10.0.0.1:22", result);
    }

    [Fact]
    public void Render_IndexAccess_Array()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["a", "b", "c"];
            let result = tpl.render("{{ items[0] }}-{{ items[2] }}", d);
        """);
        Assert.Equal("a-c", result);
    }

    // ── Filters ──────────────────────────────────────────────────────

    [Fact]
    public void Filter_Upper()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "alice";
            let result = tpl.render("{{ name | upper }}", d);
        """);
        Assert.Equal("ALICE", result);
    }

    [Fact]
    public void Filter_Lower()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "ALICE";
            let result = tpl.render("{{ name | lower }}", d);
        """);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void Filter_Trim()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "  hello  ";
            let result = tpl.render("[{{ name | trim }}]", d);
        """);
        Assert.Equal("[hello]", result);
    }

    [Fact]
    public void Filter_Length_String()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "hello";
            let result = tpl.render("{{ name | length }}", d);
        """);
        Assert.Equal("5", result);
    }

    [Fact]
    public void Filter_Length_Array()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [1, 2, 3, 4];
            let result = tpl.render("{{ items | length }}", d);
        """);
        Assert.Equal("4", result);
    }

    [Fact]
    public void Filter_Default_WithNull()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = null;
            let result = tpl.render("{{ name | default(\"Anonymous\") }}", d);
        """);
        Assert.Equal("Anonymous", result);
    }

    [Fact]
    public void Filter_Default_WithValue()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "Alice";
            let result = tpl.render("{{ name | default(\"Anonymous\") }}", d);
        """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Filter_Join()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["a", "b", "c"];
            let result = tpl.render("{{ items | join(\"-\") }}", d);
        """);
        Assert.Equal("a-b-c", result);
    }

    [Fact]
    public void Filter_Reverse_String()
    {
        var result = Run("""
            let d = dict.new();
            d["word"] = "hello";
            let result = tpl.render("{{ word | reverse }}", d);
        """);
        Assert.Equal("olleh", result);
    }

    [Fact]
    public void Filter_Replace()
    {
        var result = Run("""
            let d = dict.new();
            d["text"] = "hello world";
            let result = tpl.render("{{ text | replace(\"world\", \"stash\") }}", d);
        """);
        Assert.Equal("hello stash", result);
    }

    [Fact]
    public void Filter_Capitalize()
    {
        var result = Run("""
            let d = dict.new();
            d["word"] = "hello";
            let result = tpl.render("{{ word | capitalize }}", d);
        """);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Filter_First()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [10, 20, 30];
            let result = tpl.render("{{ items | first }}", d);
        """);
        Assert.Equal("10", result);
    }

    [Fact]
    public void Filter_Last()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [10, 20, 30];
            let result = tpl.render("{{ items | last }}", d);
        """);
        Assert.Equal("30", result);
    }

    [Fact]
    public void Filter_Chain()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "  hello  ";
            let result = tpl.render("{{ name | trim | upper }}", d);
        """);
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void Filter_Abs()
    {
        var result = Run("""
            let d = dict.new();
            d["val"] = -42;
            let result = tpl.render("{{ val | abs }}", d);
        """);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Filter_Round()
    {
        var result = Run("""
            let d = dict.new();
            d["val"] = 3.7;
            let result = tpl.render("{{ val | round }}", d);
        """);
        Assert.Equal("4", result);
    }

    // ── Conditionals ──────────────────────────────────────────────────

    [Fact]
    public void If_TrueCondition()
    {
        var result = Run("""
            let d = dict.new();
            d["active"] = true;
            let result = tpl.render("{% if active %}yes{% endif %}", d);
        """);
        Assert.Equal("yes", result);
    }

    [Fact]
    public void If_FalseCondition()
    {
        var result = Run("""
            let d = dict.new();
            d["active"] = false;
            let result = tpl.render("{% if active %}yes{% endif %}", d);
        """);
        Assert.Equal("", result);
    }

    [Fact]
    public void If_Else()
    {
        var result = Run("""
            let d = dict.new();
            d["active"] = false;
            let result = tpl.render("{% if active %}yes{% else %}no{% endif %}", d);
        """);
        Assert.Equal("no", result);
    }

    [Fact]
    public void If_Elif()
    {
        var result = Run("""
            let d = dict.new();
            d["level"] = 2;
            let result = tpl.render("{% if level == 1 %}one{% elif level == 2 %}two{% elif level == 3 %}three{% else %}other{% endif %}", d);
        """);
        Assert.Equal("two", result);
    }

    [Fact]
    public void If_Elif_Else_Fallthrough()
    {
        var result = Run("""
            let d = dict.new();
            d["level"] = 99;
            let result = tpl.render("{% if level == 1 %}one{% elif level == 2 %}two{% else %}other{% endif %}", d);
        """);
        Assert.Equal("other", result);
    }

    [Fact]
    public void If_WithExpression()
    {
        var result = Run("""
            let d = dict.new();
            d["count"] = 5;
            let result = tpl.render("{% if count > 0 %}positive{% else %}zero{% endif %}", d);
        """);
        Assert.Equal("positive", result);
    }

    [Fact]
    public void If_WithLogicalOperators()
    {
        var result = Run("""
            let d = dict.new();
            d["x"] = 5;
            d["y"] = 10;
            let result = tpl.render("{% if x > 0 && y > 0 %}both positive{% endif %}", d);
        """);
        Assert.Equal("both positive", result);
    }

    [Fact]
    public void If_NestedOutputExpression()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "Alice";
            d["isAdmin"] = true;
            let result = tpl.render("{% if isAdmin %}Admin: {{ name }}{% else %}User: {{ name }}{% endif %}", d);
        """);
        Assert.Equal("Admin: Alice", result);
    }

    // ── Loops ──────────────────────────────────────────────────────

    [Fact]
    public void For_SimpleArray()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["a", "b", "c"];
            let result = tpl.render("{% for item in items %}{{ item }}{% endfor %}", d);
        """);
        Assert.Equal("abc", result);
    }

    [Fact]
    public void For_WithSeparator()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [1, 2, 3];
            let result = tpl.render("{% for item in items %}{{ item }},{% endfor %}", d);
        """);
        Assert.Equal("1,2,3,", result);
    }

    [Fact]
    public void For_LoopIndex()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["a", "b", "c"];
            let result = tpl.render("{% for item in items %}{{ loop.index }}.{{ item }} {% endfor %}", d);
        """);
        Assert.Equal("1.a 2.b 3.c ", result);
    }

    [Fact]
    public void For_LoopIndex0()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["x", "y"];
            let result = tpl.render("{% for item in items %}{{ loop.index0 }}{% endfor %}", d);
        """);
        Assert.Equal("01", result);
    }

    [Fact]
    public void For_LoopFirst()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["a", "b", "c"];
            let result = tpl.render("{% for item in items %}{% if loop.first %}[{% endif %}{{ item }}{% if loop.last %}]{% endif %}{% endfor %}", d);
        """);
        Assert.Equal("[abc]", result);
    }

    [Fact]
    public void For_LoopLast_Separator()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [1, 2, 3];
            let result = tpl.render("{% for item in items %}{{ item }}{% if !loop.last %}, {% endif %}{% endfor %}", d);
        """);
        Assert.Equal("1, 2, 3", result);
    }

    [Fact]
    public void For_LoopLength()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = ["a", "b", "c"];
            let result = tpl.render("{% for item in items %}{{ loop.length }}{% endfor %}", d);
        """);
        Assert.Equal("333", result);
    }

    [Fact]
    public void For_NestedLoops()
    {
        var result = Run("""
            let d = dict.new();
            d["rows"] = [[1, 2], [3, 4]];
            let result = tpl.render("{% for row in rows %}{% for item in row %}{{ item }}{% endfor %};{% endfor %}", d);
        """);
        Assert.Equal("12;34;", result);
    }

    [Fact]
    public void For_WithStructs()
    {
        var result = Run("""
            struct Item { name, value }
            let d = dict.new();
            d["items"] = [Item { name: "a", value: 1 }, Item { name: "b", value: 2 }];
            let result = tpl.render("{% for item in items %}{{ item.name }}={{ item.value }} {% endfor %}", d);
        """);
        Assert.Equal("a=1 b=2 ", result);
    }

    [Fact]
    public void For_EmptyArray()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [];
            let result = tpl.render("{% for item in items %}{{ item }}{% endfor %}", d);
        """);
        Assert.Equal("", result);
    }

    // ── Comments ──────────────────────────────────────────────────

    [Fact]
    public void Comment_IsStripped()
    {
        var result = Run("""
            let d = dict.new();
            let result = tpl.render("Hello{# this is a comment #} World", d);
        """);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Comment_MultiLine()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "test";
            let tplStr = "Start{# multi\nline\ncomment #}End";
            let result = tpl.render(tplStr, d);
        """);
        Assert.Equal("StartEnd", result);
    }

    // ── Raw Blocks ──────────────────────────────────────────────────

    [Fact]
    public void Raw_OutputsLiteralDelimiters()
    {
        var result = Run("""
            let d = dict.new();
            let result = tpl.render("{% raw %}{{ not parsed }}{% endraw %}", d);
        """);
        // The template lexer trims content inside delimiters, so spaces adjacent to {{ }} are stripped
        Assert.Equal("{{not parsed}}", result);
    }

    // ── Compile + Render ────────────────────────────────────────────

    [Fact]
    public void Compile_ThenRender()
    {
        var result = Run("""
            let compiled = tpl.compile("Hello {{ name }}!");
            let d = dict.new();
            d["name"] = "World";
            let result = tpl.render(compiled, d);
        """);
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Compile_ReuseMultipleTimes()
    {
        var result = Run("""
            let compiled = tpl.compile("Hi {{ name }}");
            let d1 = dict.new();
            d1["name"] = "Alice";
            let d2 = dict.new();
            d2["name"] = "Bob";
            let r1 = tpl.render(compiled, d1);
            let r2 = tpl.render(compiled, d2);
            let result = r1 + "|" + r2;
        """);
        Assert.Equal("Hi Alice|Hi Bob", result);
    }

    // ── Access to Built-in Functions ────────────────────────────────

    [Fact]
    public void Render_CanCallBuiltinFunctions()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [1, 2, 3];
            let result = tpl.render("Length: {{ len(items) }}", d);
        """);
        Assert.Equal("Length: 3", result);
    }

    [Fact]
    public void Render_CanCallNamespaceFunctions()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "hello";
            let result = tpl.render("{{ str.upper(name) }}", d);
        """);
        Assert.Equal("HELLO", result);
    }

    // ── Complex Templates ───────────────────────────────────────────

    [Fact]
    public void Render_ComplexTemplate_Report()
    {
        var result = Run("""
            struct Server { host, status }
            let d = dict.new();
            d["title"] = "Status Report";
            d["servers"] = [
                Server { host: "web1", status: "up" },
                Server { host: "web2", status: "down" }
            ];
            let tplStr = "{{ title }}\n{% for srv in servers %}{{ loop.index }}. {{ srv.host }}: {{ srv.status }}\n{% endfor %}";
            let result = tpl.render(tplStr, d);
        """);
        Assert.Equal("Status Report\n1. web1: up\n2. web2: down\n", result);
    }

    [Fact]
    public void Render_ConditionalInsideLoop()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [1, 2, 3, 4, 5];
            let tplStr = "{% for item in items %}{% if item > 3 %}{{ item }} {% endif %}{% endfor %}";
            let result = tpl.render(tplStr, d);
        """);
        Assert.Equal("4 5 ", result);
    }

    [Fact]
    public void Render_LogicalOrInCondition_NotConfusedWithFilter()
    {
        var result = Run("""
            let d = dict.new();
            d["a"] = false;
            d["b"] = true;
            let result = tpl.render("{% if a || b %}yes{% else %}no{% endif %}", d);
        """);
        Assert.Equal("yes", result);
    }

    // ── Error Cases ─────────────────────────────────────────────────

    [Fact]
    public void Render_UnknownFilter_Throws()
    {
        Assert.Throws<TemplateException>(() => Run("""
            let d = dict.new();
            d["x"] = "test";
            let result = tpl.render("{{ x | nonexistent }}", d);
        """));
    }

    [Fact]
    public void Render_UnterminatedExpression_Throws()
    {
        Assert.Throws<TemplateException>(() => Run("""
            let d = dict.new();
            let result = tpl.render("Hello {{ name", d);
        """));
    }

    [Fact]
    public void Render_UnterminatedIf_Throws()
    {
        Assert.Throws<TemplateException>(() => Run("""
            let d = dict.new();
            d["x"] = true;
            let result = tpl.render("{% if x %}hello", d);
        """));
    }

    [Fact]
    public void Render_UnterminatedFor_Throws()
    {
        Assert.Throws<TemplateException>(() => Run("""
            let d = dict.new();
            d["items"] = [1, 2];
            let result = tpl.render("{% for x in items %}{{ x }}", d);
        """));
    }

    [Fact]
    public void Render_SecondArgMustBeDict()
    {
        Assert.Throws<RuntimeError>(() => Run("""
            let result = tpl.render("hello", "not a dict");
        """));
    }

    // ── Template Lexer Unit Tests ────────────────────────────────────

    [Fact]
    public void TemplateLexer_PlainText()
    {
        var lexer = new TemplateLexer("Hello World");
        var tokens = lexer.Scan();
        Assert.Equal(TemplateTokenType.Text, tokens[0].Type);
        Assert.Equal("Hello World", tokens[0].Value);
        Assert.Equal(TemplateTokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void TemplateLexer_ExpressionBlock()
    {
        var lexer = new TemplateLexer("{{ name }}");
        var tokens = lexer.Scan();
        Assert.Equal(TemplateTokenType.ExprStart, tokens[0].Type);
        Assert.Equal(TemplateTokenType.Text, tokens[1].Type);
        Assert.Equal("name", tokens[1].Value);
        Assert.Equal(TemplateTokenType.ExprEnd, tokens[2].Type);
    }

    [Fact]
    public void TemplateLexer_TagBlock()
    {
        var lexer = new TemplateLexer("{% if active %}");
        var tokens = lexer.Scan();
        Assert.Equal(TemplateTokenType.TagStart, tokens[0].Type);
        Assert.Equal(TemplateTokenType.Text, tokens[1].Type);
        Assert.Equal("if active", tokens[1].Value);
        Assert.Equal(TemplateTokenType.TagEnd, tokens[2].Type);
    }

    [Fact]
    public void TemplateLexer_CommentBlock()
    {
        var lexer = new TemplateLexer("{# comment #}");
        var tokens = lexer.Scan();
        Assert.Equal(TemplateTokenType.CommentStart, tokens[0].Type);
        Assert.Equal(TemplateTokenType.Text, tokens[1].Type);
        Assert.Equal("comment", tokens[1].Value);
        Assert.Equal(TemplateTokenType.CommentEnd, tokens[2].Type);
    }

    [Fact]
    public void TemplateLexer_TrimLeft()
    {
        var lexer = new TemplateLexer("{{- name }}");
        var tokens = lexer.Scan();
        Assert.True(tokens[0].TrimLeft, "ExprStart should have TrimLeft set");
    }

    [Fact]
    public void TemplateLexer_TrimRight()
    {
        var lexer = new TemplateLexer("{{ name -}}");
        var tokens = lexer.Scan();
        Assert.True(tokens[2].TrimRight, "ExprEnd should have TrimRight set");
    }

    // ── Template Parser Unit Tests ──────────────────────────────────

    [Fact]
    public void TemplateParser_ParsesSimpleText()
    {
        var lexer = new TemplateLexer("Hello World");
        var tokens = lexer.Scan();
        var parser = new TemplateParser(tokens);
        var nodes = parser.Parse();

        Assert.Single(nodes);
        Assert.IsType<TextNode>(nodes[0]);
        Assert.Equal("Hello World", ((TextNode)nodes[0]).Text);
    }

    [Fact]
    public void TemplateParser_ParsesOutputExpression()
    {
        var lexer = new TemplateLexer("{{ name | upper }}");
        var tokens = lexer.Scan();
        var parser = new TemplateParser(tokens);
        var nodes = parser.Parse();

        Assert.Single(nodes);
        var output = Assert.IsType<OutputNode>(nodes[0]);
        Assert.Equal("name", output.Expression);
        Assert.Single(output.Filters);
        Assert.Equal("upper", output.Filters[0].Name);
    }

    [Fact]
    public void TemplateParser_ParsesIfBlock()
    {
        var lexer = new TemplateLexer("{% if x %}yes{% else %}no{% endif %}");
        var tokens = lexer.Scan();
        var parser = new TemplateParser(tokens);
        var nodes = parser.Parse();

        Assert.Single(nodes);
        var ifNode = Assert.IsType<IfNode>(nodes[0]);
        Assert.Single(ifNode.Branches);
        Assert.Equal("x", ifNode.Branches[0].Condition);
        Assert.NotNull(ifNode.ElseBody);
    }

    [Fact]
    public void TemplateParser_ParsesForBlock()
    {
        var lexer = new TemplateLexer("{% for item in items %}{{ item }}{% endfor %}");
        var tokens = lexer.Scan();
        var parser = new TemplateParser(tokens);
        var nodes = parser.Parse();

        Assert.Single(nodes);
        var forNode = Assert.IsType<ForNode>(nodes[0]);
        Assert.Equal("item", forNode.Variable);
        Assert.Equal("items", forNode.Iterable);
        Assert.Single(forNode.Body);
    }

    // ── Template Filter Unit Tests ──────────────────────────────────

    [Fact]
    public void TemplateFilter_Sort()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [3, 1, 2];
            let result = tpl.render("{{ items | sort | join(\",\") }}", d);
        """);
        Assert.Equal("1,2,3", result);
    }

    [Fact]
    public void TemplateFilter_Title()
    {
        var result = Run("""
            let d = dict.new();
            d["text"] = "hello world";
            let result = tpl.render("{{ text | title }}", d);
        """);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void TemplateFilter_Split()
    {
        var result = Run("""
            let d = dict.new();
            d["csv"] = "a,b,c";
            let result = tpl.render("{{ csv | split(\",\") | join(\"-\") }}", d);
        """);
        Assert.Equal("a-b-c", result);
    }

    [Fact]
    public void TemplateFilter_Keys()
    {
        var result = Run("""
            let inner = dict.new();
            inner["x"] = 1;
            inner["y"] = 2;
            let d = dict.new();
            d["data"] = inner;
            let result = tpl.render("{{ data | keys | join(\",\") }}", d);
        """);
        // Keys order may vary, but both should be present
        var r = (string)result!;
        Assert.Contains("x", r);
        Assert.Contains("y", r);
    }

    // ── Regression Tests — Code Review Findings ──────────────────────────

    [Fact]
    public void RawBlock_PreservesExpressionTrimMarkers()
    {
        var result = Run("""
            let data = dict.new();
            let result = tpl.render("{% raw %}{{- x -}}{% endraw %}", data);
        """);
        Assert.Equal("{{- x -}}", result);
    }

    [Fact]
    public void RawBlock_PreservesTagTrimMarkers()
    {
        var result = Run("""
            let data = dict.new();
            let result = tpl.render("{% raw %}{%- if x -%}{%- endif -%}{% endraw %}", data);
        """);
        Assert.Equal("{%- if x -%}{%- endif -%}", result);
    }

    [Fact]
    public void RawBlock_PreservesCommentTrimMarkers()
    {
        var result = Run("""
            let data = dict.new();
            let result = tpl.render("{% raw %}{#- comment -#}{% endraw %}", data);
        """);
        Assert.Equal("{#- comment -#}", result);
    }

    [Fact]
    public void DefaultFilter_UndefinedFallbackExpression_ReturnsRawString()
    {
        var result = Run("""
            let data = dict.new();
            let result = tpl.render("{{ missing | default(fallback_val) }}", data);
        """);
        Assert.Equal("fallback_val", result);
    }

    [Fact]
    public void DefaultFilter_ValidStringExpression_EvaluatesCorrectly()
    {
        var result = Run("""
            let data = dict.new();
            let result = tpl.render("{{ missing | default(\"Anonymous\") }}", data);
        """);
        Assert.Equal("Anonymous", result);
    }

    [Fact]
    public void PipeVsLogicalOr_NotConfused()
    {
        var result = Run("""
            let data = dict.new();
            data["x"] = null;
            data["y"] = "backup";
            let result = tpl.render("{{ x || y }}", data);
        """);
        Assert.Equal("backup", result);
    }
}
