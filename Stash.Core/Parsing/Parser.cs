namespace Stash.Parsing;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// A recursive-descent parser that transforms a flat list of <see cref="Token"/>s (produced by
/// the <see cref="Lexer"/>) into an AST of statements and expressions.
/// </summary>
/// <remarks>
/// <para>
/// A program is a list of <see cref="Stmt"/> nodes. The top-level grammar is:
/// <c>program → declaration* EOF</c>. Declarations include variable (<c>let</c>),
/// constant (<c>const</c>), and function (<c>fn</c>) declarations, as well as
/// general statements (expression statements, if, while, for-in, return, break, continue,
/// and blocks).
/// </para>
/// <para>
/// Each expression precedence level is encoded as its own method, with lower-precedence
/// methods calling higher-precedence ones. The full chain (lowest → highest) is:
/// </para>
/// <list type="number">
///   <item><description><see cref="Expression"/> — entry point, delegates to <see cref="Assignment"/></description></item>
///   <item><description><see cref="Assignment"/> — <c>=</c> (right-associative)</description></item>
///   <item><description><see cref="Ternary"/> — <c>? :</c> (right-associative)</description></item>
///   <item><description><see cref="NullCoalesce"/> — <c>??</c> (left-associative)</description></item>
///   <item><description><see cref="Redirect"/> — <c>&gt;</c> <c>&gt;&gt;</c> <c>2&gt;</c> <c>&amp;&gt;</c> (output redirection, command-context only)</description></item>
///   <item><description><see cref="Pipe"/> — <c>|</c> (left-associative, command chaining)</description></item>
///   <item><description><see cref="Or"/> — <c>||</c></description></item>
///   <item><description><see cref="And"/> — <c>&amp;&amp;</c></description></item>
///   <item><description><see cref="Equality"/> — <c>== !=</c></description></item>
///   <item><description><see cref="Comparison"/> — <c>&lt; &gt; &lt;= &gt;=</c></description></item>
///   <item><description><see cref="Term"/> — <c>+ -</c></description></item>
///   <item><description><see cref="Factor"/> — <c>* / %</c></description></item>
///   <item><description><see cref="Unary"/> — prefix <c>! - try</c></description></item>
///   <item><description><see cref="Call"/> — function calls <c>callee(args)</c></description></item>
///   <item><description><see cref="Primary"/> — literals, identifiers, grouping <c>( )</c></description></item>
/// </list>
/// <para>
/// Left-associativity for binary operators is achieved via <c>while</c> loops (e.g.
/// <c>1 + 2 + 3</c> → <c>(1 + 2) + 3</c>). The ternary operator is right-associative
/// because its branches call <see cref="Expression"/> (the lowest precedence), allowing
/// <c>a ? b : c ? d : e</c> → <c>a ? b : (c ? d : e)</c>.
/// </para>
/// <para>
/// Error recovery uses a private <see cref="ParseError"/> exception for control flow.
/// When the parser encounters an unexpected token it records a human-readable message in
/// <see cref="Errors"/> and throws <see cref="ParseError"/>, which is caught in
/// <see cref="Declaration"/> to prevent cascading errors. The <see cref="Synchronize"/>
/// method discards tokens until a likely statement boundary is found.
/// </para>
/// </remarks>
public class Parser
{
    /// <summary>
    /// The complete list of tokens produced by the <see cref="Lexer"/>, including the
    /// trailing <see cref="TokenType.Eof"/> sentinel.
    /// </summary>
    private readonly List<Token> _tokens;

    /// <summary>
    /// Index of the next token to be consumed. Advances monotonically via <see cref="Advance"/>.
    /// </summary>
    private int _current;

    /// <summary>
    /// Gets the list of human-readable error messages accumulated during parsing.
    /// Each entry includes the file name, line, column, and a description of the problem.
    /// </summary>
    public List<string> Errors { get; } = new();
    public List<DiagnosticError> StructuredErrors { get; } = new();

    /// <summary>
    /// Creates a new <see cref="Parser"/> for the given token stream.
    /// </summary>
    /// <param name="tokens">
    /// The list of tokens to parse. Must end with a <see cref="TokenType.Eof"/> token.
    /// </param>
    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _current = 0;
    }

    /// <summary>
    /// Parses the token stream into a list of statements (a full program).
    /// </summary>
    /// <returns>
    /// A list of <see cref="Stmt"/> nodes representing the program.
    /// Statements that fail to parse are skipped (error recovery via <see cref="Synchronize"/>).
    /// </returns>
    /// <remarks>
    /// Any syntax errors encountered are recorded in <see cref="Errors"/>.
    /// The caller should check <see cref="Errors"/> after calling this method to determine
    /// whether parsing succeeded.
    /// </remarks>
    public List<Stmt> ParseProgram()
    {
        List<Stmt> statements = new();
        while (!IsAtEnd)
        {
            Stmt? stmt = Declaration();
            if (stmt is not null)
            {
                statements.Add(stmt);
            }
        }
        return statements;
    }

    /// <summary>
    /// Parses the token stream into a single expression AST.
    /// Retained for backward compatibility with existing expression-level tests.
    /// </summary>
    /// <returns>
    /// The root <see cref="Expr"/> of the parsed AST. On parse failure, returns a
    /// <see cref="LiteralExpr"/> with a <c>null</c> value as a safe fallback.
    /// </returns>
    public Expr Parse()
    {
        try
        {
            return Expression();
        }
        catch (ParseError)
        {
            return new LiteralExpr(null, Peek().Span);
        }
    }

    // ── Declaration / Statement parsing ────────────────────────────

    /// <summary>
    /// Parses a declaration or falls back to a statement.
    /// Catches <see cref="ParseError"/> and synchronizes to recover.
    /// </summary>
    /// <returns>
    /// The parsed <see cref="Stmt"/>, or <c>null</c> if error recovery was triggered.
    /// </returns>
    private Stmt? Declaration()
    {
        try
        {
            if (Match(TokenType.Let))
            {
                return VarDeclaration();
            }

            if (Match(TokenType.Const))
            {
                return ConstDeclaration();
            }

            if (Match(TokenType.Fn))
            {
                return FnDeclaration();
            }

            if (Match(TokenType.Struct))
            {
                return StructDeclaration();
            }

            if (Match(TokenType.Enum))
            {
                return EnumDeclaration();
            }

            if (Match(TokenType.Import))
            {
                return ImportDeclaration();
            }

            return Statement();
        }
        catch (ParseError)
        {
            Synchronize();
            return null;
        }
    }

    /// <summary>
    /// Parses a variable declaration: <c>let name = expr;</c> or <c>let name;</c>.
    /// The <c>let</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="VarDeclStmt"/>.</returns>
    private Stmt VarDeclaration()
    {
        Token letToken = Previous();

        // Check for destructuring pattern
        if (Check(TokenType.LeftBracket))
        {
            return DestructureDeclaration(letToken, isConst: false);
        }
        if (Check(TokenType.LeftBrace))
        {
            return DestructureDeclaration(letToken, isConst: false);
        }

        // Normal variable declaration
        Token name = Consume(TokenType.Identifier, "Expected variable name.");
        Token? typeHint = null;
        if (Match(TokenType.Colon))
        {
            typeHint = Consume(TokenType.Identifier, "Expected type name after ':'.");
        }
        Expr? initializer = null;
        if (Match(TokenType.Equal))
        {
            initializer = Expression();
        }

        Token semi = Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
        return new VarDeclStmt(name, typeHint, initializer, MakeSpan(letToken.Span, semi.Span));
    }

    /// <summary>
    /// Parses a constant declaration: <c>const NAME = expr;</c>.
    /// The <c>const</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="ConstDeclStmt"/>.</returns>
    private Stmt ConstDeclaration()
    {
        Token constToken = Previous();

        // Check for destructuring pattern
        if (Check(TokenType.LeftBracket))
        {
            return DestructureDeclaration(constToken, isConst: true);
        }
        if (Check(TokenType.LeftBrace))
        {
            return DestructureDeclaration(constToken, isConst: true);
        }

        // Normal constant declaration
        Token name = Consume(TokenType.Identifier, "Expected constant name.");
        Token? typeHint = null;
        if (Match(TokenType.Colon))
        {
            typeHint = Consume(TokenType.Identifier, "Expected type name after ':'.");
        }
        Consume(TokenType.Equal, "Expected '=' after constant name (constants must be initialized).");
        Expr initializer = Expression();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after constant declaration.");
        return new ConstDeclStmt(name, typeHint, initializer, MakeSpan(constToken.Span, semi.Span));
    }

    /// <summary>
    /// Parses a destructuring declaration: <c>let [a, b, c] = expr;</c> or <c>let { x, y } = expr;</c>
    /// The <c>let</c>/<c>const</c> token has already been consumed.
    /// </summary>
    private Stmt DestructureDeclaration(Token keyword, bool isConst)
    {
        DestructureStmt.PatternKind kind;
        var names = new List<Token>();

        if (Match(TokenType.LeftBracket))
        {
            // Array destructuring: let [a, b, c] = ...
            kind = DestructureStmt.PatternKind.Array;
            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    names.Add(Consume(TokenType.Identifier, "Expected variable name in destructuring pattern."));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBracket, "Expected ']' after destructuring pattern.");
        }
        else
        {
            // Object destructuring: let { x, y } = ...
            Match(TokenType.LeftBrace); // consume the '{'
            kind = DestructureStmt.PatternKind.Object;
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    names.Add(Consume(TokenType.Identifier, "Expected property name in destructuring pattern."));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBrace, "Expected '}' after destructuring pattern.");
        }

        if (names.Count == 0)
        {
            Error(Previous(), "Destructuring pattern must contain at least one name.");
        }

        Consume(TokenType.Equal, "Expected '=' after destructuring pattern.");
        Expr initializer = Expression();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after destructuring declaration.");

        return new DestructureStmt(kind, names, isConst, initializer, MakeSpan(keyword.Span, semi.Span));
    }

    /// <summary>
    /// Parses a function declaration: <c>fn name(params) { body }</c>.
    /// The <c>fn</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="FnDeclStmt"/>.</returns>
    private Stmt FnDeclaration()
    {
        Token fnToken = Previous();
        Token name = Consume(TokenType.Identifier, "Expected function name.");
        Consume(TokenType.LeftParen, "Expected '(' after function name.");

        List<Token> parameters = new();
        List<Token?> parameterTypes = new();
        List<Expr?> defaultValues = new();
        bool hasSeenDefault = false;

        if (!Check(TokenType.RightParen))
        {
            do
            {
                parameters.Add(Consume(TokenType.Identifier, "Expected parameter name."));
                Token? paramType = null;
                if (Match(TokenType.Colon))
                {
                    paramType = Consume(TokenType.Identifier, "Expected type name after ':'.");
                }
                parameterTypes.Add(paramType);

                Expr? defaultValue = null;
                if (Match(TokenType.Equal))
                {
                    defaultValue = Assignment();
                    hasSeenDefault = true;
                }
                else if (hasSeenDefault)
                {
                    Error(Previous(), "Non-default parameter cannot follow a default parameter.");
                }
                defaultValues.Add(defaultValue);
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after parameters.");
        Token? returnType = null;
        if (Match(TokenType.Arrow))
        {
            returnType = Consume(TokenType.Identifier, "Expected return type after '->'.");
        }
        BlockStmt body = ParseBlock();
        return new FnDeclStmt(name, parameters, parameterTypes, defaultValues, returnType, body, MakeSpan(fnToken.Span, body.Span));
    }

    private Stmt StructDeclaration()
    {
        Token structToken = Previous();
        Token name = Consume(TokenType.Identifier, "Expected struct name.");
        Consume(TokenType.LeftBrace, "Expected '{' after struct name.");

        List<Token> fields = new();
        List<Token?> fieldTypes = new();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                fields.Add(Consume(TokenType.Identifier, "Expected field name."));
                Token? fieldType = null;
                if (Match(TokenType.Colon))
                {
                    fieldType = Consume(TokenType.Identifier, "Expected type name after ':'.");
                }
                fieldTypes.Add(fieldType);
            } while (Match(TokenType.Comma));
        }

        Token close = Consume(TokenType.RightBrace, "Expected '}' after struct fields.");
        return new StructDeclStmt(name, fields, fieldTypes, MakeSpan(structToken.Span, close.Span));
    }

    private Stmt EnumDeclaration()
    {
        Token enumToken = Previous();
        Token name = Consume(TokenType.Identifier, "Expected enum name.");
        Consume(TokenType.LeftBrace, "Expected '{' after enum name.");

        List<Token> members = new();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                members.Add(Consume(TokenType.Identifier, "Expected enum member name."));
            } while (Match(TokenType.Comma));
        }

        Token close = Consume(TokenType.RightBrace, "Expected '}' after enum members.");
        return new EnumDeclStmt(name, members, MakeSpan(enumToken.Span, close.Span));
    }

    /// <summary>
    /// Parses an import declaration: <c>import { name1, name2 } from "path";</c>.
    /// The <c>import</c> token has already been consumed.
    /// </summary>
    private Stmt ImportDeclaration()
    {
        Token importToken = Previous();

        // import "path" as name;
        if (Check(TokenType.StringLiteral))
        {
            Token path = Consume(TokenType.StringLiteral, "Expected module path string.");
            Consume(TokenType.As, "Expected 'as' after module path.");
            Token alias = Consume(TokenType.Identifier, "Expected namespace name after 'as'.");
            Token semi = Consume(TokenType.Semicolon, "Expected ';' after import declaration.");
            return new ImportAsStmt(path, alias, MakeSpan(importToken.Span, semi.Span));
        }

        // import { name1, name2 } from "path";
        Consume(TokenType.LeftBrace, "Expected '{' or module path after 'import'.");

        List<Token> names = new();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                names.Add(Consume(TokenType.Identifier, "Expected name to import."));
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBrace, "Expected '}' after import names.");
        if (names.Count == 0)
        {
            Error(Previous(), "Expected at least one name to import.");
        }

        Consume(TokenType.From, "Expected 'from' after import names.");
        Token fromPath = Consume(TokenType.StringLiteral, "Expected module path string after 'from'.");
        Token finalSemi = Consume(TokenType.Semicolon, "Expected ';' after import declaration.");

        return new ImportStmt(names, fromPath, MakeSpan(importToken.Span, finalSemi.Span));
    }

    /// <summary>
    /// Dispatches to the appropriate statement parser based on the current token.
    /// </summary>
    /// <returns>The parsed <see cref="Stmt"/>.</returns>
    private Stmt Statement()
    {
        if (Match(TokenType.If))
        {
            return IfStatement();
        }

        if (Match(TokenType.While))
        {
            return WhileStatement();
        }

        if (Match(TokenType.For))
        {
            return ForInStatement();
        }

        if (Match(TokenType.Return))
        {
            return ReturnStatement();
        }

        if (Match(TokenType.Break))
        {
            return BreakStatement();
        }

        if (Match(TokenType.Continue))
        {
            return ContinueStatement();
        }

        if (Check(TokenType.LeftBrace))
        {
            return ParseBlock();
        }

        return ExpressionStatement();
    }

    /// <summary>
    /// Parses a block: <c>{ declarations... }</c>.
    /// </summary>
    /// <returns>A <see cref="BlockStmt"/> containing the enclosed declarations.</returns>
    private BlockStmt ParseBlock()
    {
        Token open = Consume(TokenType.LeftBrace, "Expected '{' before block.");
        List<Stmt> statements = new();
        while (!Check(TokenType.RightBrace) && !IsAtEnd)
        {
            Stmt? stmt = Declaration();
            if (stmt is not null)
            {
                statements.Add(stmt);
            }
        }
        Token close = Consume(TokenType.RightBrace, "Expected '}' after block.");
        return new BlockStmt(statements, MakeSpan(open.Span, close.Span));
    }

    /// <summary>
    /// Parses an if statement: <c>if (cond) { ... } else { ... }</c>.
    /// The <c>if</c> token has already been consumed.
    /// </summary>
    /// <returns>An <see cref="IfStmt"/>.</returns>
    private Stmt IfStatement()
    {
        Token ifToken = Previous();
        Consume(TokenType.LeftParen, "Expected '(' after 'if'.");
        Expr condition = Expression();
        Consume(TokenType.RightParen, "Expected ')' after if condition.");
        Stmt thenBranch = ParseBlock();
        Stmt? elseBranch = null;
        if (Match(TokenType.Else))
        {
            if (Check(TokenType.If))
            {
                Match(TokenType.If);
                elseBranch = IfStatement();
            }
            else
            {
                elseBranch = ParseBlock();
            }
        }
        SourceSpan endSpan = elseBranch?.Span ?? thenBranch.Span;
        return new IfStmt(condition, thenBranch, elseBranch, MakeSpan(ifToken.Span, endSpan));
    }

    /// <summary>
    /// Parses a while statement: <c>while (cond) { ... }</c>.
    /// The <c>while</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="WhileStmt"/>.</returns>
    private Stmt WhileStatement()
    {
        Token whileToken = Previous();
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        Expr condition = Expression();
        Consume(TokenType.RightParen, "Expected ')' after while condition.");
        BlockStmt body = ParseBlock();
        return new WhileStmt(condition, body, MakeSpan(whileToken.Span, body.Span));
    }

    /// <summary>
    /// Parses a for-in statement: <c>for (let name in iterable) { ... }</c>.
    /// The <c>for</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="ForInStmt"/>.</returns>
    private Stmt ForInStatement()
    {
        Token forToken = Previous();
        Consume(TokenType.LeftParen, "Expected '(' after 'for'.");
        Consume(TokenType.Let, "Expected 'let' after '(' in for-in loop.");
        Token varName = Consume(TokenType.Identifier, "Expected variable name in for-in loop.");
        Token? typeHint = null;
        if (Match(TokenType.Colon))
        {
            typeHint = Consume(TokenType.Identifier, "Expected type name after ':'.");
        }
        Consume(TokenType.In, "Expected 'in' after variable name in for-in loop.");
        Expr iterable = Expression();
        Consume(TokenType.RightParen, "Expected ')' after for-in clause.");
        BlockStmt body = ParseBlock();
        return new ForInStmt(varName, typeHint, iterable, body, MakeSpan(forToken.Span, body.Span));
    }

    /// <summary>
    /// Parses a return statement: <c>return expr;</c> or <c>return;</c>.
    /// The <c>return</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="ReturnStmt"/>.</returns>
    private Stmt ReturnStatement()
    {
        Token keyword = Previous();
        Expr? value = null;
        if (!Check(TokenType.Semicolon))
        {
            value = Expression();
        }

        Token semi = Consume(TokenType.Semicolon, "Expected ';' after return value.");
        return new ReturnStmt(value, MakeSpan(keyword.Span, semi.Span));
    }

    /// <summary>
    /// Parses a break statement: <c>break;</c>.
    /// The <c>break</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="BreakStmt"/>.</returns>
    private Stmt BreakStatement()
    {
        Token keyword = Previous();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after 'break'.");
        return new BreakStmt(MakeSpan(keyword.Span, semi.Span));
    }

    /// <summary>
    /// Parses a continue statement: <c>continue;</c>.
    /// The <c>continue</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="ContinueStmt"/>.</returns>
    private Stmt ContinueStatement()
    {
        Token keyword = Previous();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after 'continue'.");
        return new ContinueStmt(MakeSpan(keyword.Span, semi.Span));
    }

    /// <summary>
    /// Parses an expression statement: <c>expr;</c>.
    /// </summary>
    /// <returns>An <see cref="ExprStmt"/>.</returns>
    private Stmt ExpressionStatement()
    {
        Expr expr = Expression();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after expression.");
        return new ExprStmt(expr, MakeSpan(expr.Span, semi.Span));
    }

    // ── Precedence levels (lowest → highest) ──────────────────────

    /// <summary>
    /// Parses an expression at the lowest precedence level.
    /// Delegates to <see cref="Assignment"/>.
    /// </summary>
    /// <returns>The parsed expression.</returns>
    private Expr Expression()
    {
        return Assignment();
    }

    /// <summary>
    /// Parses an assignment expression: <c>name = value</c>.
    /// Assignment is right-associative.
    /// </summary>
    /// <returns>
    /// An <see cref="AssignExpr"/> if the left-hand side is an identifier followed by <c>=</c>,
    /// otherwise the result of <see cref="Ternary"/>.
    /// </returns>
    private Expr Assignment()
    {
        Expr expr = Ternary();

        if (Match(TokenType.Equal))
        {
            Token equals = Previous();
            Expr value = Assignment();

            if (expr is IdentifierExpr id)
            {
                return new AssignExpr(id.Name, value, MakeSpan(id.Span, value.Span));
            }
            else if (expr is IndexExpr indexExpr)
            {
                return new IndexAssignExpr(indexExpr.Object, indexExpr.Index, value, indexExpr.BracketSpan, MakeSpan(indexExpr.Span, value.Span));
            }
            else if (expr is DotExpr dotExpr)
            {
                return new DotAssignExpr(dotExpr.Object, dotExpr.Name, value, MakeSpan(dotExpr.Span, value.Span));
            }

            Error(equals, "Invalid assignment target.");
        }

        if (Match(TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual,
                  TokenType.SlashEqual, TokenType.PercentEqual, TokenType.QuestionQuestionEqual))
        {
            Token op = Previous();
            Expr value = Assignment();

            if (expr is IdentifierExpr id)
            {
                Expr compoundValue = DesugarCompoundAssignment(op, new IdentifierExpr(id.Name, id.Span), value);
                return new AssignExpr(id.Name, compoundValue, MakeSpan(id.Span, value.Span));
            }
            else if (expr is IndexExpr indexExpr)
            {
                Expr compoundValue = DesugarCompoundAssignment(op, indexExpr, value);
                return new IndexAssignExpr(indexExpr.Object, indexExpr.Index, compoundValue,
                    indexExpr.BracketSpan, MakeSpan(indexExpr.Span, value.Span));
            }
            else if (expr is DotExpr dotExpr)
            {
                Expr compoundValue = DesugarCompoundAssignment(op, dotExpr, value);
                return new DotAssignExpr(dotExpr.Object, dotExpr.Name, compoundValue,
                    MakeSpan(dotExpr.Span, value.Span));
            }

            Error(op, "Invalid assignment target.");
        }

        return expr;
    }

    private Expr DesugarCompoundAssignment(Token compoundOp, Expr target, Expr value)
    {
        SourceSpan span = MakeSpan(target.Span, value.Span);

        if (compoundOp.Type == TokenType.QuestionQuestionEqual)
        {
            return new NullCoalesceExpr(target, value, span);
        }

        TokenType binaryOp = compoundOp.Type switch
        {
            TokenType.PlusEqual => TokenType.Plus,
            TokenType.MinusEqual => TokenType.Minus,
            TokenType.StarEqual => TokenType.Star,
            TokenType.SlashEqual => TokenType.Slash,
            TokenType.PercentEqual => TokenType.Percent,
            _ => throw new InvalidOperationException($"Unexpected compound operator: {compoundOp.Type}")
        };

        Token syntheticOp = new Token(binaryOp, compoundOp.Lexeme, null, compoundOp.Span);
        return new BinaryExpr(target, syntheticOp, value, span);
    }

    /// <summary>
    /// Parses a ternary conditional expression: <c>condition ? thenBranch : elseBranch</c>.
    /// </summary>
    /// <returns>
    /// A <see cref="TernaryExpr"/> if a <c>?</c> token follows the condition,
    /// otherwise the condition expression itself.
    /// </returns>
    /// <remarks>
    /// Right-associativity is achieved by calling <see cref="Expression"/> (the lowest
    /// precedence) for both the then-branch and else-branch, so nested ternaries like
    /// <c>a ? b : c ? d : e</c> parse as <c>a ? b : (c ? d : e)</c>.
    /// </remarks>
    private Expr Ternary()
    {
        Expr expr = NullCoalesce();

        if (Match(TokenType.QuestionMark))
        {
            Expr thenBranch = Expression();
            Consume(TokenType.Colon, "Expected ':' after then-branch of ternary expression.");
            Expr elseBranch = Expression();
            SourceSpan span = MakeSpan(expr.Span, elseBranch.Span);
            return new TernaryExpr(expr, thenBranch, elseBranch, span);
        }

        return expr;
    }

    /// <summary>
    /// Parses a null-coalescing expression: <c>left ?? right</c>.
    /// Left-associative: <c>a ?? b ?? c</c> → <c>(a ?? b) ?? c</c>.
    /// </summary>
    private Expr NullCoalesce()
    {
        Expr expr = Redirect();

        while (Match(TokenType.QuestionQuestion))
        {
            Expr right = Redirect();
            expr = new NullCoalesceExpr(expr, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses output redirection: <c>$(cmd) &gt; "file"</c>, <c>$(cmd) &gt;&gt; "file"</c>,
    /// <c>$(cmd) 2&gt; "file"</c>, <c>$(cmd) &amp;&gt; "file"</c>, etc.
    /// Redirection is only valid after a <see cref="CommandExpr"/>, <see cref="PipeExpr"/>,
    /// or another <see cref="RedirectExpr"/>. In all other contexts, <c>&gt;</c> remains a
    /// comparison operator.
    /// </summary>
    private Expr Redirect()
    {
        Expr expr = Pipe();

        while (true)
        {
            // Only attempt redirection if the left side is a command-producing expression
            bool isCommandLike = expr is CommandExpr or PipeExpr or RedirectExpr;

            if (isCommandLike && Check(TokenType.Greater))
            {
                Advance();
                Expr target = Pipe();
                expr = new RedirectExpr(expr, RedirectStream.Stdout, false, target,
                    MakeSpan(expr.Span, target.Span));
            }
            else if (isCommandLike && Check(TokenType.GreaterGreater))
            {
                Advance();
                Expr target = Pipe();
                expr = new RedirectExpr(expr, RedirectStream.Stdout, true, target,
                    MakeSpan(expr.Span, target.Span));
            }
            else if (isCommandLike && Check(TokenType.AmpersandGreater))
            {
                Advance();
                Expr target = Pipe();
                expr = new RedirectExpr(expr, RedirectStream.All, false, target,
                    MakeSpan(expr.Span, target.Span));
            }
            else if (isCommandLike && Check(TokenType.AmpersandGreaterGreater))
            {
                Advance();
                Expr target = Pipe();
                expr = new RedirectExpr(expr, RedirectStream.All, true, target,
                    MakeSpan(expr.Span, target.Span));
            }
            else if (isCommandLike && CheckStderrRedirect())
            {
                // 2> or 2>> — integer literal 2 followed by > or >>
                Advance(); // consume the integer 2
                bool append = Check(TokenType.GreaterGreater);
                Advance(); // consume > or >>
                Expr target = Pipe();
                expr = new RedirectExpr(expr, RedirectStream.Stderr, append, target,
                    MakeSpan(expr.Span, target.Span));
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    /// <summary>
    /// Checks if the current position has a stderr redirect pattern: integer literal <c>2</c>
    /// immediately followed by <c>&gt;</c> or <c>&gt;&gt;</c>.
    /// </summary>
    private bool CheckStderrRedirect()
    {
        if (IsAtEnd)
        {
            return false;
        }

        Token current = Peek();
        if (current.Type != TokenType.IntegerLiteral || current.Literal is not long val || val != 2)
        {
            return false;
        }
        // Check the token after the '2'
        if (_current + 1 >= _tokens.Count)
        {
            return false;
        }

        TokenType next = _tokens[_current + 1].Type;
        return next == TokenType.Greater || next == TokenType.GreaterGreater;
    }

    /// <summary>
    /// Parses a pipe expression: <c>$(cmd1) | $(cmd2)</c>.
    /// Pipe chains process stdout → stdin, left-associative.
    /// </summary>
    private Expr Pipe()
    {
        Expr expr = Or();

        while (Match(TokenType.Pipe))
        {
            Token op = Previous();
            Expr right = Or();
            expr = new PipeExpr(expr, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a logical OR expression: <c>left || right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>||</c>,
    /// or the result of <see cref="And"/> if no <c>||</c> operator is present.
    /// </returns>
    private Expr Or()
    {
        Expr expr = And();

        while (Match(TokenType.PipePipe))
        {
            Token op = Previous();
            Expr right = And();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a logical AND expression: <c>left &amp;&amp; right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>&amp;&amp;</c>,
    /// or the result of <see cref="Equality"/> if no <c>&amp;&amp;</c> operator is present.
    /// </returns>
    private Expr And()
    {
        Expr expr = Equality();

        while (Match(TokenType.AmpersandAmpersand))
        {
            Token op = Previous();
            Expr right = Equality();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses an equality expression: <c>left == right</c> or <c>left != right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>==</c> / <c>!=</c>,
    /// or the result of <see cref="Comparison"/> if no equality operator is present.
    /// </returns>
    private Expr Equality()
    {
        Expr expr = Comparison();

        while (Match(TokenType.EqualEqual, TokenType.BangEqual))
        {
            Token op = Previous();
            Expr right = Comparison();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a comparison expression: <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, or <c>&gt;=</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for comparison operators,
    /// or the result of <see cref="Term"/> if no comparison operator is present.
    /// </returns>
    /// <remarks>
    /// When the left operand is a command-producing expression (<see cref="CommandExpr"/>,
    /// <see cref="PipeExpr"/>, or <see cref="RedirectExpr"/>), the <c>&gt;</c> and <c>&gt;&gt;</c>
    /// tokens are NOT consumed as comparison operators. They are left for the
    /// <see cref="Redirect"/> precedence level to handle as output redirection.
    /// </remarks>
    private Expr Comparison()
    {
        Expr expr = Range();

        while (true)
        {
            // When the left side is a command expression, > and >> are redirection, not comparison.
            bool isCommandLike = expr is CommandExpr or PipeExpr or RedirectExpr;
            if (isCommandLike && (Check(TokenType.Greater) || Check(TokenType.GreaterGreater)))
            {
                break;
            }

            if (!Match(TokenType.Less, TokenType.Greater, TokenType.LessEqual, TokenType.GreaterEqual))
            {
                break;
            }

            Token op = Previous();
            Expr right = Range();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        while (Match(TokenType.In))
        {
            Token op = Previous();
            Expr right = Range();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    private Expr Range()
    {
        Expr expr = Term();

        if (Match(TokenType.DotDot))
        {
            Expr end = Term();
            Expr? step = null;
            if (Match(TokenType.DotDot))
            {
                step = Term();
            }
            return new RangeExpr(expr, end, step, MakeSpan(expr.Span, (step ?? end).Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses an additive expression: <c>left + right</c> or <c>left - right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>+</c> / <c>-</c>,
    /// or the result of <see cref="Factor"/> if no additive operator is present.
    /// </returns>
    private Expr Term()
    {
        Expr expr = Factor();

        while (Match(TokenType.Plus, TokenType.Minus))
        {
            Token op = Previous();
            Expr right = Factor();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a multiplicative expression: <c>*</c>, <c>/</c>, or <c>%</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>* / %</c>,
    /// or the result of <see cref="Unary"/> if no multiplicative operator is present.
    /// </returns>
    private Expr Factor()
    {
        Expr expr = Unary();

        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            Token op = Previous();
            Expr right = Unary();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a prefix unary expression: <c>!expr</c> or <c>-expr</c>.
    /// </summary>
    /// <returns>
    /// A <see cref="UnaryExpr"/> if a prefix operator is found; otherwise falls through
    /// to <see cref="Call"/>.
    /// </returns>
    /// <remarks>
    /// Unary is right-recursive — calling itself allows chaining like <c>!!x</c> or <c>--x</c>.
    /// </remarks>
    private Expr Unary()
    {
        if (Match(TokenType.Bang, TokenType.Minus))
        {
            Token op = Previous();
            Expr right = Unary();
            return new UnaryExpr(op, right, MakeSpan(op.Span, right.Span));
        }

        if (Match(TokenType.Try))
        {
            Token tryToken = Previous();
            Expr expression = Unary();
            return new TryExpr(expression, MakeSpan(tryToken.Span, expression.Span));
        }

        if (Match(TokenType.PlusPlus, TokenType.MinusMinus))
        {
            Token op = Previous();
            Expr operand = Unary();
            return new UpdateExpr(op, operand, true, MakeSpan(op.Span, operand.Span));
        }

        return Call();
    }

    /// <summary>
    /// Parses function call expressions: <c>callee(arg1, arg2, ...)</c>.
    /// Handles chained calls like <c>a()()</c>.
    /// </summary>
    /// <returns>
    /// A <see cref="CallExpr"/> if a <c>(</c> follows the primary expression,
    /// otherwise the primary expression itself.
    /// </returns>
    private Expr Call()
    {
        Expr expr = Primary();

        while (true)
        {
            if (Match(TokenType.LeftParen))
            {
                expr = FinishCall(expr);
            }
            else if (Match(TokenType.LeftBracket))
            {
                Token bracket = Previous();
                Expr index = Expression();
                Token close = Consume(TokenType.RightBracket, "Expected ']' after index.");
                expr = new IndexExpr(expr, index, bracket.Span, MakeSpan(expr.Span, close.Span));
            }
            else if (Match(TokenType.Dot))
            {
                Token name = ConsumePropertyName();
                Expr dotExpr = new DotExpr(expr, name, MakeSpan(expr.Span, name.Span));

                // Check for namespaced struct init: ns.StructName { field: value, ... }
                if (Check(TokenType.LeftBrace))
                {
                    int savedPosition = _current;
                    Advance(); // consume '{'

                    if (Check(TokenType.Identifier))
                    {
                        int peekAhead = _current;
                        if (peekAhead + 1 < _tokens.Count && _tokens[peekAhead + 1].Type == TokenType.Colon)
                        {
                            List<(Token Field, Expr Value)> fieldValues = new();
                            do
                            {
                                Token field = Consume(TokenType.Identifier, "Expected field name.");
                                Consume(TokenType.Colon, "Expected ':' after field name.");
                                Expr value = Expression();
                                fieldValues.Add((field, value));
                            } while (Match(TokenType.Comma));

                            Token close = Consume(TokenType.RightBrace, "Expected '}' after struct fields.");
                            expr = new StructInitExpr(name, dotExpr, fieldValues, MakeSpan(expr.Span, close.Span));
                            continue;
                        }
                    }

                    // Also handle empty struct init: ns.StructName { }
                    if (Check(TokenType.RightBrace))
                    {
                        Token close = Advance();
                        expr = new StructInitExpr(name, dotExpr, new List<(Token, Expr)>(), MakeSpan(expr.Span, close.Span));
                        continue;
                    }

                    // Not a struct init — backtrack
                    _current = savedPosition;
                }

                expr = dotExpr;
            }
            else if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            {
                Token op = Advance();
                expr = new UpdateExpr(op, expr, false, MakeSpan(expr.Span, op.Span));
            }
            else if (Match(TokenType.Switch))
            {
                expr = ParseSwitchArms(expr);
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private Expr ParseSwitchArms(Expr subject)
    {
        Consume(TokenType.LeftBrace, "Expected '{' after 'switch'.");
        List<SwitchArm> arms = new();

        while (!Check(TokenType.RightBrace) && !IsAtEnd)
        {
            Token armStart = Peek();
            bool isDiscard = Peek().Type == TokenType.Identifier
                && Peek().Lexeme == "_"
                && _current + 1 < _tokens.Count
                && _tokens[_current + 1].Type == TokenType.FatArrow;

            if (isDiscard)
            {
                Token discard = Advance(); // consume '_'
                Consume(TokenType.FatArrow, "Expected '=>' after '_'.");
                Expr body = Expression();
                arms.Add(new SwitchArm(null, true, body, MakeSpan(discard.Span, body.Span)));
            }
            else
            {
                Expr pattern = Expression();
                Consume(TokenType.FatArrow, "Expected '=>' after switch pattern.");
                Expr body = Expression();
                arms.Add(new SwitchArm(pattern, false, body, MakeSpan(pattern.Span, body.Span)));
            }

            if (!Check(TokenType.RightBrace))
            {
                Consume(TokenType.Comma, "Expected ',' between switch arms.");
            }
            else
            {
                Match(TokenType.Comma); // optional trailing comma
            }
        }

        Token closeBrace = Consume(TokenType.RightBrace, "Expected '}' after switch arms.");
        return new SwitchExpr(subject, arms, MakeSpan(subject.Span, closeBrace.Span));
    }

    /// <summary>
    /// Parses the argument list and closing paren of a function call.
    /// </summary>
    /// <param name="callee">The expression being called.</param>
    /// <returns>A <see cref="CallExpr"/> wrapping the callee and its arguments.</returns>
    private Expr FinishCall(Expr callee)
    {
        List<Expr> arguments = new();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                arguments.Add(Expression());
            } while (Match(TokenType.Comma));
        }

        Token paren = Consume(TokenType.RightParen, "Expected ')' after arguments.");
        return new CallExpr(callee, paren, arguments, MakeSpan(callee.Span, paren.Span));
    }

    /// <summary>
    /// Parses a primary (highest-precedence) expression: literals, identifiers, or
    /// parenthesized groupings.
    /// </summary>
    /// <returns>
    /// A <see cref="LiteralExpr"/>, <see cref="IdentifierExpr"/>, or <see cref="GroupingExpr"/>
    /// depending on the current token.
    /// </returns>
    /// <exception cref="ParseError">
    /// Thrown when the current token does not start a valid expression, triggering
    /// error recovery in <see cref="Parse"/>.
    /// </exception>
    private Expr Primary()
    {
        if (Match(TokenType.IntegerLiteral, TokenType.FloatLiteral, TokenType.StringLiteral))
        {
            Token token = Previous();
            return new LiteralExpr(token.Literal, token.Span);
        }

        if (Match(TokenType.InterpolatedString))
        {
            Token token = Previous();
            return ParseInterpolatedString(token);
        }

        if (Match(TokenType.CommandLiteral))
        {
            Token token = Previous();
            return ParseCommandLiteral(token);
        }

        if (Match(TokenType.True))
        {
            return new LiteralExpr(true, Previous().Span);
        }

        if (Match(TokenType.False))
        {
            return new LiteralExpr(false, Previous().Span);
        }

        if (Match(TokenType.Null))
        {
            return new LiteralExpr(null, Previous().Span);
        }

        if (Match(TokenType.LeftBracket))
        {
            Token open = Previous();
            List<Expr> elements = new();
            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    elements.Add(Expression());
                } while (Match(TokenType.Comma));
            }
            Token close = Consume(TokenType.RightBracket, "Expected ']' after array elements.");
            return new ArrayExpr(elements, MakeSpan(open.Span, close.Span));
        }

        if (Match(TokenType.Identifier))
        {
            Token name = Previous();

            // Check for struct instantiation: Name { field: value, ... }
            if (Check(TokenType.LeftBrace))
            {
                // Save position to backtrack if it's not a struct init
                int savedPosition = _current;

                Advance(); // consume '{'

                // It's a struct init if we see: Identifier ':'
                if (Check(TokenType.Identifier))
                {
                    int peekAhead = _current;
                    if (peekAhead + 1 < _tokens.Count && _tokens[peekAhead + 1].Type == TokenType.Colon)
                    {
                        // This is a struct init: Name { field: value, ... }
                        List<(Token Field, Expr Value)> fieldValues = new();
                        do
                        {
                            Token field = Consume(TokenType.Identifier, "Expected field name.");
                            Consume(TokenType.Colon, "Expected ':' after field name.");
                            Expr value = Expression();
                            fieldValues.Add((field, value));
                        } while (Match(TokenType.Comma));

                        Token close = Consume(TokenType.RightBrace, "Expected '}' after struct fields.");
                        return new StructInitExpr(name, fieldValues, MakeSpan(name.Span, close.Span));
                    }
                }

                // Also handle empty struct init: Name { }
                if (Check(TokenType.RightBrace))
                {
                    Token close = Advance();
                    return new StructInitExpr(name, new List<(Token, Expr)>(), MakeSpan(name.Span, close.Span));
                }

                // Not a struct init — backtrack
                _current = savedPosition;
            }

            return new IdentifierExpr(name, name.Span);
        }

        if (Match(TokenType.LeftParen))
        {
            Token open = Previous();

            // Check if this is a lambda: (params) => ...
            if (IsLambdaStart())
            {
                return ParseLambda(open);
            }

            Expr expr = Expression();
            Token close = Consume(TokenType.RightParen, "Expected ')' after expression.");
            return new GroupingExpr(expr, MakeSpan(open.Span, close.Span));
        }

        throw Error(Peek(), "Expected expression.");
    }

    /// <summary>
    /// Checks whether the current token stream starting after an opening <c>(</c>
    /// matches the pattern for a lambda parameter list followed by <c>=&gt;</c>.
    /// Does not consume any tokens — saves and restores <see cref="_current"/>.
    /// </summary>
    private bool IsLambdaStart()
    {
        int saved = _current;

        try
        {
            // () => ...
            if (Check(TokenType.RightParen))
            {
                Advance(); // skip ')'
                return Check(TokenType.FatArrow);
            }

            // Try to match: identifier [: type] (, identifier [: type])* ) =>
            while (true)
            {
                if (!Check(TokenType.Identifier))
                {
                    return false;
                }

                Advance(); // skip identifier

                // Optional type annotation: : Type
                if (Check(TokenType.Colon))
                {
                    Advance(); // skip ':'
                    if (!Check(TokenType.Identifier))
                    {
                        return false;
                    }

                    Advance(); // skip type name
                }

                // Optional default value: = expr
                if (Check(TokenType.Equal))
                {
                    Advance(); // skip '='
                    // Skip the default value expression: scan until ',' or ')' at depth 0
                    int depth = 0;
                    while (!IsAtEnd)
                    {
                        if (Check(TokenType.LeftParen) || Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace))
                        {
                            depth++;
                        }
                        else if (Check(TokenType.RightBrace) || Check(TokenType.RightBracket))
                        {
                            if (depth == 0)
                            {
                                return false;
                            }

                            depth--;
                        }
                        else if (Check(TokenType.RightParen))
                        {
                            if (depth == 0)
                            {
                                break;
                            }

                            depth--;
                        }
                        else if (Check(TokenType.Comma) && depth == 0)
                        {
                            break;
                        }
                        Advance();
                    }
                }

                if (Check(TokenType.Comma))
                {
                    Advance(); // skip ','
                    continue;
                }

                if (Check(TokenType.RightParen))
                {
                    Advance(); // skip ')'
                    return Check(TokenType.FatArrow);
                }

                return false;
            }
        }
        finally
        {
            _current = saved;
        }
    }

    /// <summary>
    /// Parses a lambda expression after the opening <c>(</c> has been consumed and
    /// <see cref="IsLambdaStart"/> has confirmed this is a lambda.
    /// Supports both expression bodies <c>(x) =&gt; x + 1</c> and block bodies <c>(x) =&gt; { ... }</c>.
    /// </summary>
    private Expr ParseLambda(Token open)
    {
        List<Token> parameters = new();
        List<Token?> parameterTypes = new();
        List<Expr?> defaultValues = new();
        bool hasSeenDefault = false;

        if (!Check(TokenType.RightParen))
        {
            do
            {
                parameters.Add(Consume(TokenType.Identifier, "Expected parameter name."));
                Token? paramType = null;
                if (Match(TokenType.Colon))
                {
                    paramType = Consume(TokenType.Identifier, "Expected type name after ':'.");
                }
                parameterTypes.Add(paramType);

                Expr? defaultValue = null;
                if (Match(TokenType.Equal))
                {
                    defaultValue = Assignment();
                    hasSeenDefault = true;
                }
                else if (hasSeenDefault)
                {
                    Error(Previous(), "Non-default parameter cannot follow a default parameter.");
                }
                defaultValues.Add(defaultValue);
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after lambda parameters.");
        Consume(TokenType.FatArrow, "Expected '=>' after lambda parameters.");

        // Block body: (params) => { ... }
        if (Check(TokenType.LeftBrace))
        {
            BlockStmt block = ParseBlock();
            return new LambdaExpr(parameters, parameterTypes, defaultValues, null, block,
                                  MakeSpan(open.Span, block.Span));
        }

        // Expression body: (params) => expr
        Expr body = Assignment();
        return new LambdaExpr(parameters, parameterTypes, defaultValues, body, null,
                              MakeSpan(open.Span, body.Span));
    }

    // ── Interpolated string parsing ──────────────────────────────

    /// <summary>
    /// Converts an <see cref="TokenType.InterpolatedString"/> token into an
    /// <see cref="InterpolatedStringExpr"/> AST node.
    /// </summary>
    /// <remarks>
    /// The token's <see cref="Token.Literal"/> is a <c>List&lt;object&gt;</c> where each element
    /// is either a <see cref="string"/> (literal text segment) or a <c>List&lt;Token&gt;</c>
    /// (expression tokens). Each expression token list is parsed by a nested <see cref="Parser"/>.
    /// </remarks>
    /// <param name="token">The interpolated string token to parse.</param>
    /// <returns>An <see cref="InterpolatedStringExpr"/> containing the parsed parts.</returns>
    private Expr ParseInterpolatedString(Token token)
    {
        var parts = (List<object>)token.Literal!;
        var exprParts = new List<Expr>();

        foreach (object part in parts)
        {
            if (part is string text)
            {
                exprParts.Add(new LiteralExpr(text, token.Span));
            }
            else if (part is List<Token> innerTokens)
            {
                // Add an EOF token so the nested parser can detect end of input.
                var tokensWithEof = new List<Token>(innerTokens);
                tokensWithEof.Add(new Token(TokenType.Eof, "", null, token.Span));

                var innerParser = new Parser(tokensWithEof);
                Expr expr = innerParser.Parse();

                if (innerParser.Errors.Count > 0)
                {
                    foreach (string error in innerParser.Errors)
                    {
                        Errors.Add(error);
                    }
                }

                exprParts.Add(expr);
            }
        }

        return new InterpolatedStringExpr(exprParts, token.Span);
    }

    // ── Command literal parsing ──────────────────────────────────

    /// <summary>
    /// Converts a <see cref="TokenType.CommandLiteral"/> token into a
    /// <see cref="CommandExpr"/> AST node.
    /// </summary>
    /// <param name="token">The command literal token to parse.</param>
    /// <returns>A <see cref="CommandExpr"/> containing the parsed parts.</returns>
    private Expr ParseCommandLiteral(Token token)
    {
        var parts = (List<object>)token.Literal!;
        var exprParts = new List<Expr>();

        foreach (object part in parts)
        {
            if (part is string text)
            {
                exprParts.Add(new LiteralExpr(text, token.Span));
            }
            else if (part is List<Token> innerTokens)
            {
                var tokensWithEof = new List<Token>(innerTokens);
                tokensWithEof.Add(new Token(TokenType.Eof, "", null, token.Span));

                var innerParser = new Parser(tokensWithEof);
                Expr expr = innerParser.Parse();

                if (innerParser.Errors.Count > 0)
                {
                    foreach (string error in innerParser.Errors)
                    {
                        Errors.Add(error);
                    }
                }

                exprParts.Add(expr);
            }
        }

        return new CommandExpr(exprParts, token.Span);
    }

    // ── Helper methods ────────────────────────────────────────────

    /// <summary>
    /// Checks whether the current token matches any of the given types. If a match is found,
    /// the token is consumed (advanced past).
    /// </summary>
    /// <param name="types">One or more <see cref="TokenType"/> values to check against.</param>
    /// <returns><c>true</c> if the current token matched and was consumed; <c>false</c> otherwise.</returns>
    private bool Match(params TokenType[] types)
    {
        foreach (TokenType type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether the current token is of the given type without consuming it.
    /// </summary>
    /// <param name="type">The token type to check for.</param>
    /// <returns><c>true</c> if the current token has the specified type and we are not at EOF.</returns>
    private bool Check(TokenType type)
    {
        if (IsAtEnd)
        {
            return false;
        }

        return Peek().Type == type;
    }

    /// <summary>
    /// Consumes the current token and returns the previously current token.
    /// </summary>
    /// <returns>The token that was current before advancing (i.e. <see cref="Previous"/>).</returns>
    private Token Advance()
    {
        if (!IsAtEnd)
        {
            _current++;
        }

        return Previous();
    }

    /// <summary>
    /// Returns the current token without consuming it.
    /// </summary>
    /// <returns>The token at position <see cref="_current"/>.</returns>
    private Token Peek()
    {
        return _tokens[_current];
    }

    /// <summary>
    /// Returns the most recently consumed token (one position before <see cref="_current"/>).
    /// </summary>
    /// <returns>The token at position <c>_current - 1</c>.</returns>
    private Token Previous()
    {
        return _tokens[_current - 1];
    }

    /// <summary>
    /// Gets a value indicating whether the parser has reached the end-of-file sentinel token.
    /// </summary>
    private bool IsAtEnd => Peek().Type == TokenType.Eof;

    /// <summary>
    /// Consumes the current token if it matches <paramref name="type"/>; otherwise reports
    /// an error with <paramref name="message"/> and throws <see cref="ParseError"/>.
    /// </summary>
    /// <param name="type">The expected <see cref="TokenType"/>.</param>
    /// <param name="message">The error message to report if the token does not match.</param>
    /// <returns>The consumed token (via <see cref="Advance"/>).</returns>
    /// <exception cref="ParseError">Thrown when the current token does not match <paramref name="type"/>.</exception>
    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }

        throw Error(Peek(), message);
    }

    /// <summary>
    /// Consumes the next token as a property name after a dot. Accepts any identifier or
    /// keyword that may legally appear as a member name (e.g. <c>assert.true</c>, <c>assert.null</c>).
    /// </summary>
    private Token ConsumePropertyName()
    {
        if (Check(TokenType.Identifier) ||
            Check(TokenType.True) ||
            Check(TokenType.False) ||
            Check(TokenType.Null))
        {
            return Advance();
        }

        throw Error(Peek(), "Expected field name after '.'.");
    }

    /// <summary>
    /// Records a parse error with file location and returns a <see cref="ParseError"/>
    /// exception for control-flow unwinding.
    /// </summary>
    /// <param name="token">The token where the error was detected.</param>
    /// <param name="message">A description of what went wrong.</param>
    /// <returns>A <see cref="ParseError"/> instance to be thrown by the caller.</returns>
    private ParseError Error(Token token, string message)
    {
        string location = token.Type == TokenType.Eof
            ? "at end"
            : $"at '{token.Lexeme}'";

        string error = $"[{token.Span.File} {token.Span.StartLine}:{token.Span.StartColumn}] Error {location}: {message}";
        Errors.Add(error);
        StructuredErrors.Add(new DiagnosticError(token.Span, $"Error {location}: {message}"));
        return new ParseError();
    }

    /// <summary>
    /// Creates a <see cref="SourceSpan"/> that stretches from the start position of
    /// <paramref name="start"/> to the end position of <paramref name="end"/>.
    /// </summary>
    /// <param name="start">The span whose start position is used.</param>
    /// <param name="end">The span whose end position is used.</param>
    /// <returns>A new <see cref="SourceSpan"/> covering the combined range.</returns>
    /// <remarks>
    /// Used to compute the source span for compound AST nodes (e.g. a <see cref="BinaryExpr"/>
    /// spanning from its left operand to its right operand).
    /// </remarks>
    private static SourceSpan MakeSpan(SourceSpan start, SourceSpan end)
    {
        return new SourceSpan(start.File, start.StartLine, start.StartColumn, end.EndLine, end.EndColumn);
    }

    /// <summary>
    /// Checks whether the current token is an identifier with the given lexeme, without consuming it.
    /// Used for contextual keywords like <c>args</c>, <c>flag</c>, <c>option</c>, etc.
    /// </summary>
    private bool CheckIdentifier(string name)
    {
        if (IsAtEnd)
        {
            return false;
        }

        return Peek().Type == TokenType.Identifier && Peek().Lexeme == name;
    }

    /// <summary>
    /// If the current token is an identifier matching <paramref name="name"/>, consumes it and returns true.
    /// Used for contextual keywords inside <c>args</c> blocks.
    /// </summary>
    private bool MatchIdentifier(string name)
    {
        if (CheckIdentifier(name))
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Discards tokens until the parser reaches a likely statement boundary,
    /// enabling recovery after a syntax error.
    /// </summary>
    private void Synchronize()
    {
        Advance();
        while (!IsAtEnd)
        {
            if (Previous().Type == TokenType.Semicolon)
            {
                return;
            }

            switch (Peek().Type)
            {
                case TokenType.Let:
                case TokenType.Const:
                case TokenType.Fn:
                case TokenType.Struct:
                case TokenType.Enum:
                case TokenType.Import:
                case TokenType.If:
                case TokenType.While:
                case TokenType.For:
                case TokenType.Return:
                case TokenType.Break:
                case TokenType.Continue:
                    return;
            }
            Advance();
        }
    }

    /// <summary>
    /// A private sentinel exception used purely for control flow during error recovery.
    /// </summary>
    /// <remarks>
    /// When the parser encounters an unexpected token, it throws <see cref="ParseError"/>
    /// to unwind the recursive-descent call stack back to <see cref="Declaration"/>, which
    /// catches it and calls <see cref="Synchronize"/> to recover. This approach
    /// avoids cascading errors from a single syntax mistake.
    /// </remarks>
    private class ParseError : Exception { }
}
