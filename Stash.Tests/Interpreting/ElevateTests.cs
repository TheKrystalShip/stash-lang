using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Analysis;

namespace Stash.Tests.Interpreting;

public class ElevateTests
{
    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    private static List<Stmt> ParseProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseProgram();
    }

    private static List<SemanticDiagnostic> Validate(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }

    private static string Format(string source) =>
        new StashFormatter(2, useTabs: false).Format(source);

    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);
        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // ===== Lexer Tests =====

    [Fact]
    public void Lexer_Elevate_ProducesKeywordToken()
    {
        var tokens = Scan("elevate");
        var elevateToken = tokens.First(t => t.Type != TokenType.Eof);
        Assert.Equal(TokenType.Elevate, elevateToken.Type);
        Assert.Equal("elevate", elevateToken.Lexeme);
    }

    [Fact]
    public void Lexer_ElevateInExpression_NotConfusedWithIdentifier()
    {
        var tokens = Scan("let elevate_mode = true;");
        var identToken = tokens.First(t => t.Lexeme == "elevate_mode");
        Assert.Equal(TokenType.Identifier, identToken.Type);
    }

    // ===== Parser Tests =====

    [Fact]
    public void Parser_ElevateBlock_ParsesCorrectly()
    {
        var stmts = ParseProgram("elevate { let x = 1; }");
        var elevate = Assert.IsType<ElevateStmt>(Assert.Single(stmts));
        Assert.Null(elevate.Elevator);
        Assert.Single(elevate.Body.Statements);
    }

    [Fact]
    public void Parser_ElevateWithElevator_ParsesExpression()
    {
        var stmts = ParseProgram("elevate(\"doas\") { let x = 1; }");
        var elevate = Assert.IsType<ElevateStmt>(Assert.Single(stmts));
        var literal = Assert.IsType<LiteralExpr>(elevate.Elevator);
        Assert.Equal("doas", literal.Value);
        Assert.NotNull(elevate.Body);
    }

    [Fact]
    public void Parser_ElevateEmptyBlock_ParsesCorrectly()
    {
        var stmts = ParseProgram("elevate { }");
        var elevate = Assert.IsType<ElevateStmt>(Assert.Single(stmts));
        Assert.Empty(elevate.Body.Statements);
    }

    [Fact]
    public void Parser_ElevateNestedBlock_ParsesCorrectly()
    {
        var stmts = ParseProgram("elevate { elevate { let x = 1; } }");
        var outer = Assert.IsType<ElevateStmt>(Assert.Single(stmts));
        var inner = Assert.IsType<ElevateStmt>(Assert.Single(outer.Body.Statements));
        Assert.Single(inner.Body.Statements);
    }

    // ===== Semantic Validator Tests =====

    [Fact]
    public void Validator_SingleElevate_NoElevateWarning()
    {
        var diagnostics = Validate("elevate { let x = 1; }");
        var elevateWarnings = diagnostics
            .Where(d => d.Message.Contains("elevate") && d.Level == DiagnosticLevel.Warning)
            .ToList();
        Assert.Empty(elevateWarnings);
    }

    [Fact]
    public void Validator_NestedElevate_ReportsWarning()
    {
        var diagnostics = Validate("elevate { elevate { let x = 1; } }");
        var warning = Assert.Single(diagnostics,
            d => d.Message.Contains("Nested") && d.Message.Contains("elevate"));
        Assert.Equal(DiagnosticLevel.Warning, warning.Level);
    }

    [Fact]
    public void Validator_DeeplyNestedElevate_ReportsWarning()
    {
        var diagnostics = Validate("elevate { elevate { elevate { let x = 1; } } }");
        var warnings = diagnostics
            .Where(d => d.Message.Contains("Nested") && d.Message.Contains("elevate"))
            .ToList();
        Assert.True(warnings.Count >= 2);
    }

    // ===== Formatter Tests =====

    [Fact]
    public void Formatter_ElevateBlock_BasicFormatting()
    {
        var result = Format("elevate{let x=1;}");
        Assert.Equal("elevate {\n  let x = 1;\n}\n", result);
    }

    [Fact]
    public void Formatter_ElevateWithElevator_IncludesExpression()
    {
        var result = Format("elevate(\"doas\"){let x=1;}");
        Assert.Equal("elevate(\"doas\") {\n  let x = 1;\n}\n", result);
    }

    // ===== Interpreter Tests =====

    [Fact]
    public void Interpreter_ElevateBlock_EmbeddedModeThrows()
    {
        var lexer = new Lexer("elevate { let x = 1; }");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.EmbeddedMode = true;
        var ex = Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        Assert.Contains("embedded mode", ex.Message);
    }

    [Fact]
    public void Interpreter_ElevateBlock_AlreadyPrivileged_ExecutesBody()
    {
        if (!System.Environment.IsPrivilegedProcess)
        {
            return; // Skip on non-privileged test environments
        }

        Assert.Equal(42L, Run(@"
            let result = 0;
            elevate {
                result = 42;
            }
        "));
    }
}
