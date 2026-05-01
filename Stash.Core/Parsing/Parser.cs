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
    /// <summary>Gets the list of structured diagnostic errors with full <see cref="SourceSpan"/> location information, accumulated during parsing.</summary>
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
            // Soft keyword: 'async fn' — recognized when 'async' identifier precedes 'fn'
            if (Check(TokenType.Identifier) && Peek().Lexeme == "async"
                && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Fn)
            {
                Advance(); // consume 'async'
                Token asyncToken = Previous();
                Consume(TokenType.Fn, "Expected 'fn' after 'async'.");
                return FnDeclaration(isAsync: true, asyncToken: asyncToken);
            }

            switch (Peek().Type)
            {
                case TokenType.Let:
                    Advance();
                    return VarDeclaration();
                case TokenType.Const:
                    Advance();
                    return ConstDeclaration();
                case TokenType.Fn:
                    Advance();
                    return FnDeclaration();
                case TokenType.Struct:
                    Advance();
                    return StructDeclaration();
                case TokenType.Enum:
                    Advance();
                    return EnumDeclaration();
                case TokenType.Interface:
                    Advance();
                    return InterfaceDeclaration();
                case TokenType.Extend:
                    Advance();
                    return ExtendDeclaration();
                case TokenType.Import:
                    Advance();
                    return ImportDeclaration();
                default:
                    return Statement();
            }
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
        TypeHint? typeHint = null;
        if (Match(TokenType.Colon))
        {
            typeHint = ParseTypeHint();
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
        TypeHint? typeHint = null;
        if (Match(TokenType.Colon))
        {
            typeHint = ParseTypeHint();
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
        Token? restName = null;

        if (Match(TokenType.LeftBracket))
        {
            // Array destructuring: let [a, b, ...rest] = ...
            kind = DestructureStmt.PatternKind.Array;
            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    if (Match(TokenType.DotDotDot))
                    {
                        if (restName != null)
                        {
                            Error(Previous(), "Only one rest element is allowed in destructuring pattern.");
                        }
                        restName = Consume(TokenType.Identifier, "Expected variable name after '...' in destructuring pattern.");
                    }
                    else
                    {
                        if (restName != null)
                        {
                            Error(Previous(), "Rest element must be the last element in destructuring pattern.");
                        }
                        names.Add(Consume(TokenType.Identifier, "Expected variable name in destructuring pattern."));
                    }
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBracket, "Expected ']' after destructuring pattern.");
        }
        else
        {
            // Object destructuring: let { x, y, ...rest } = ...
            Match(TokenType.LeftBrace); // consume the '{'
            kind = DestructureStmt.PatternKind.Object;
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    if (Match(TokenType.DotDotDot))
                    {
                        if (restName != null)
                        {
                            Error(Previous(), "Only one rest element is allowed in destructuring pattern.");
                        }
                        restName = Consume(TokenType.Identifier, "Expected variable name after '...' in destructuring pattern.");
                    }
                    else
                    {
                        if (restName != null)
                        {
                            Error(Previous(), "Rest element must be the last element in destructuring pattern.");
                        }
                        names.Add(Consume(TokenType.Identifier, "Expected property name in destructuring pattern."));
                    }
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBrace, "Expected '}' after destructuring pattern.");
        }

        if (names.Count == 0 && restName == null)
        {
            Error(Previous(), "Destructuring pattern must contain at least one name.");
        }

        Consume(TokenType.Equal, "Expected '=' after destructuring pattern.");
        Expr initializer = Expression();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after destructuring declaration.");

        return new DestructureStmt(kind, names, isConst, initializer, MakeSpan(keyword.Span, semi.Span), restName);
    }

    /// <summary>
    /// Parses a function declaration: <c>fn name(params) { body }</c>.
    /// The <c>fn</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="FnDeclStmt"/>.</returns>
    private Stmt FnDeclaration(bool isAsync = false, Token? asyncToken = null)
    {
        Token fnToken = Previous();
        SourceSpan startSpan = asyncToken?.Span ?? fnToken.Span;
        Token name = Consume(TokenType.Identifier, "Expected function name.");
        Consume(TokenType.LeftParen, "Expected '(' after function name.");

        List<Token> parameters = new();
        List<TypeHint?> parameterTypes = new();
        List<Expr?> defaultValues = new();
        bool hasSeenDefault = false;
        bool hasRestParam = false;

        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (Match(TokenType.DotDotDot))
                {
                    if (hasRestParam)
                    {
                        Error(Previous(), "Only one rest parameter is allowed.");
                    }
                    hasRestParam = true;
                    Token restParamName = Consume(TokenType.Identifier, "Expected parameter name.");
                    TypeHint? restParamType = null;
                    if (Match(TokenType.Colon))
                    {
                        restParamType = ParseTypeHint();
                    }
                    if (Check(TokenType.Equal))
                    {
                        Error(Previous(), "Rest parameter cannot have a default value.");
                    }
                    parameters.Add(restParamName);
                    parameterTypes.Add(restParamType);
                    defaultValues.Add(null);
                    if (Check(TokenType.Comma))
                    {
                        Error(Previous(), "Rest parameter must be the last parameter.");
                    }
                    break;
                }

                parameters.Add(Consume(TokenType.Identifier, "Expected parameter name."));
                TypeHint? paramType = null;
                if (Match(TokenType.Colon))
                {
                    paramType = ParseTypeHint();
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
        TypeHint? returnType = null;
        if (Match(TokenType.Arrow))
        {
            returnType = ParseTypeHint();
        }
        BlockStmt body = ParseBlock();
        return new FnDeclStmt(name, parameters, parameterTypes, defaultValues, returnType, body, MakeSpan(startSpan, body.Span), isAsync, asyncToken, hasRestParam);
    }

    /// <summary>Parses a struct declaration: <c>struct Name { field1, field2, fn method() { ... } }</c>.</summary>
    /// <returns>A <see cref="StructDeclStmt"/> node.</returns>
    private Stmt StructDeclaration()
    {
        Token structToken = Previous();
        Token name = Consume(TokenType.Identifier, "Expected struct name.");

        List<Token> interfaces = new();
        if (Match(TokenType.Colon))
        {
            do
            {
                interfaces.Add(Consume(TokenType.Identifier, "Expected interface name."));
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.LeftBrace, "Expected '{' after struct name.");

        List<Token> fields = new();
        List<TypeHint?> fieldTypes = new();
        List<FnDeclStmt> methods = new();

        // Parse fields (comma-separated, stop when we hit fn, async, or })
        if (!Check(TokenType.RightBrace) && !Check(TokenType.Fn) && !(Check(TokenType.Identifier) && Peek().Lexeme == "async"))
        {
            do
            {
                if (Check(TokenType.Fn) || (Check(TokenType.Identifier) && Peek().Lexeme == "async"))
                {
                    break;
                }

                fields.Add(Consume(TokenType.Identifier, "Expected field name."));
                TypeHint? fieldType = null;
                if (Match(TokenType.Colon))
                {
                    fieldType = ParseTypeHint();
                }
                fieldTypes.Add(fieldType);
            } while (Match(TokenType.Comma));
        }

        // Parse methods (including async methods)
        while (true)
        {
            if (Check(TokenType.Identifier) && Peek().Lexeme == "async"
                && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Fn)
            {
                Advance(); // consume 'async'
                Token asyncToken = Previous();
                Consume(TokenType.Fn, "Expected 'fn' after 'async'.");
                methods.Add((FnDeclStmt)FnDeclaration(isAsync: true, asyncToken: asyncToken));
            }
            else if (Match(TokenType.Fn))
            {
                methods.Add((FnDeclStmt)FnDeclaration());
            }
            else
            {
                break;
            }
        }

        Token close = Consume(TokenType.RightBrace, "Expected '}' after struct body.");
        return new StructDeclStmt(name, fields, fieldTypes, methods, interfaces, MakeSpan(structToken.Span, close.Span));
    }

    /// <summary>Parses an enum declaration: <c>enum Name { Member1, Member2, ... }</c>.</summary>
    /// <returns>An <see cref="EnumDeclStmt"/> node.</returns>
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

    /// <summary>Parses an interface declaration: <c>interface Name { field1, method1(params), ... }</c>.</summary>
    /// <returns>An <see cref="InterfaceDeclStmt"/> node.</returns>
    private Stmt InterfaceDeclaration()
    {
        Token interfaceToken = Previous();
        Token name = Consume(TokenType.Identifier, "Expected interface name.");
        Consume(TokenType.LeftBrace, "Expected '{' after interface name.");

        List<Token> fields = new();
        List<TypeHint?> fieldTypes = new();
        List<InterfaceMethodSignature> methods = new();
        HashSet<string> memberNames = new();

        if (!Check(TokenType.RightBrace))
        {
            do
            {
                if (Check(TokenType.RightBrace))
                {
                    break;
                }

                // Optional 'fn' keyword before a method name: "fn describe(self)" or "describe(self)"
                Match(TokenType.Fn);

                Token memberName = Consume(TokenType.Identifier, "Expected member name.");

                if (memberNames.Contains(memberName.Lexeme))
                {
                    Error(memberName, $"Duplicate member '{memberName.Lexeme}' in interface '{name.Lexeme}'.");
                }
                memberNames.Add(memberName.Lexeme);

                if (Match(TokenType.LeftParen))
                {
                    // Method signature: name(param1, param2, ...) -> ReturnType
                    List<Token> parameters = new();
                    List<TypeHint?> parameterTypes = new();

                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            parameters.Add(Consume(TokenType.Identifier, "Expected parameter name."));
                            TypeHint? paramType = null;
                            if (Match(TokenType.Colon))
                            {
                                paramType = ParseTypeHint();
                            }
                            parameterTypes.Add(paramType);
                        } while (Match(TokenType.Comma));
                    }

                    Consume(TokenType.RightParen, "Expected ')' after interface method parameters.");

                    TypeHint? returnType = null;
                    if (Match(TokenType.Arrow))
                    {
                        returnType = ParseTypeHint();
                    }

                    methods.Add(new InterfaceMethodSignature(memberName, parameters, parameterTypes, returnType));
                }
                else
                {
                    // Field requirement: name or name: Type
                    TypeHint? fieldType = null;
                    if (Match(TokenType.Colon))
                    {
                        fieldType = ParseTypeHint();
                    }
                    fields.Add(memberName);
                    fieldTypes.Add(fieldType);
                }
            } while (Match(TokenType.Comma));
        }

        if (fields.Count == 0 && methods.Count == 0)
        {
            Error(name, "An interface must have at least one member.");
        }

        Token close = Consume(TokenType.RightBrace, "Expected '}' after interface body.");
        return new InterfaceDeclStmt(name, fields, fieldTypes, methods, MakeSpan(interfaceToken.Span, close.Span));
    }

    /// <summary>Parses a type extension block: <c>extend TypeName { fn method() { ... } }</c>.</summary>
    /// <returns>An <see cref="ExtendStmt"/> node.</returns>
    private Stmt ExtendDeclaration()
    {
        Token extendToken = Previous();
        Token typeName = Consume(TokenType.Identifier, "Expected type name after 'extend'.");

        Consume(TokenType.LeftBrace, "Expected '{' after type name in extend block.");

        List<FnDeclStmt> methods = new();

        while (!Check(TokenType.RightBrace) && !IsAtEnd)
        {
            if (Check(TokenType.Identifier) && Peek().Lexeme == "async"
                && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Fn)
            {
                Advance(); // consume 'async'
                Token asyncToken = Previous();
                Consume(TokenType.Fn, "Expected 'fn' after 'async'.");
                methods.Add((FnDeclStmt)FnDeclaration(isAsync: true, asyncToken: asyncToken));
            }
            else if (Match(TokenType.Fn))
            {
                methods.Add((FnDeclStmt)FnDeclaration());
            }
            else
            {
                throw Error(Peek(), "Only method declarations (fn) are allowed inside extend blocks.");
            }
        }

        Token close = Consume(TokenType.RightBrace, "Expected '}' after extend block body.");
        return new ExtendStmt(extendToken, typeName, methods, MakeSpan(extendToken.Span, close.Span));
    }

    /// <summary>
    /// Parses an import declaration. The <c>import</c> token has already been consumed.
    /// Supports two forms:
    /// <list type="bullet">
    ///   <item><description><c>import { name1, name2 } from expr;</c></description></item>
    ///   <item><description><c>import expr as name;</c></description></item>
    /// </list>
    /// The path may be any expression, not just a string literal.
    /// </summary>
    private Stmt ImportDeclaration()
    {
        Token importToken = Previous();

        // import { name1, name2 } from expr;
        if (Check(TokenType.LeftBrace))
        {
            Advance(); // consume {

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

            ConsumeIdentifier("from", "Expected 'from' after import names.");
            Expr pathExpr = Expression();
            Token finalSemi = Consume(TokenType.Semicolon, "Expected ';' after import declaration.");

            return new ImportStmt(names, pathExpr, MakeSpan(importToken.Span, finalSemi.Span));
        }

        // import expr as name;
        Expr path = Expression();
        Consume(TokenType.As, "Expected 'as' after module path.");
        Token alias = Consume(TokenType.Identifier, "Expected namespace name after 'as'.");
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after import declaration.");
        return new ImportAsStmt(path, alias, MakeSpan(importToken.Span, semi.Span));
    }

    /// <summary>
    /// Dispatches to the appropriate statement parser based on the current token.
    /// </summary>
    /// <returns>The parsed <see cref="Stmt"/>.</returns>
    private Stmt Statement()
    {
        // Soft keyword dispatch: handled before the hard-keyword switch
        if (Check(TokenType.Identifier))
        {
            string lexeme = Peek().Lexeme;

            if (lexeme == "timeout" && IsTimeoutKeyword())
            {
                Expr expr = Expression();
                Match(TokenType.Semicolon);
                return new ExprStmt(expr, expr.Span);
            }

            if (lexeme == "defer" && IsDeferKeyword())
            {
                Advance(); // consume 'defer'
                return DeferStatement();
            }

            if (lexeme == "lock" && IsLockKeyword())
            {
                Advance(); // consume 'lock'
                return LockStatement();
            }

            if (lexeme == "unset" && IsUnsetKeyword())
            {
                Advance(); // consume 'unset'
                return UnsetStatement();
            }

            if (lexeme == "elevate" && IsElevateKeyword())
            {
                Advance(); // consume 'elevate'
                return ElevateStatement();
            }

            // retry: expression-level block — optional trailing semicolon (no ';' after closing '}')
            if (lexeme == "retry" && IsRetryKeyword())
            {
                Expr expr = Expression();
                Match(TokenType.Semicolon);
                return new ExprStmt(expr, expr.Span);
            }
        }

        switch (Peek().Type)
        {
            case TokenType.If:
                Advance();
                return IfStatement();
            case TokenType.While:
                Advance();
                return WhileStatement();
            case TokenType.Do:
                Advance();
                return DoWhileStatement();
            case TokenType.For:
                Advance();
                return ForStatement();
            case TokenType.Return:
                Advance();
                return ReturnStatement();
            case TokenType.Throw:
                Advance();
                return ThrowStatement();
            case TokenType.Break:
                Advance();
                return BreakStatement();
            case TokenType.Continue:
                Advance();
                return ContinueStatement();
            case TokenType.Switch:
                if (_tokens[_current + 1].Type == TokenType.LeftParen)
                {
                    Advance();
                    return SwitchStatement();
                }
                return ExpressionStatement();
            case TokenType.Try:
                if (_tokens[_current + 1].Type == TokenType.LeftBrace)
                {
                    Advance();
                    return TryCatchStatement();
                }
                return ExpressionStatement();
            case TokenType.LeftBrace:
                return ParseBlock();
            default:
                return ExpressionStatement();
        }
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
    /// Parses an elevate statement: <c>elevate { ... }</c> or <c>elevate("expr") { ... }</c>.
    /// The <c>elevate</c> token has already been consumed.
    /// </summary>
    /// <returns>An <see cref="ElevateStmt"/>.</returns>
    private Stmt ElevateStatement()
    {
        Token elevateToken = Previous();
        Expr? elevator = null;

        if (Match(TokenType.LeftParen))
        {
            elevator = Expression();
            Consume(TokenType.RightParen, "Expected ')' after elevator expression.");
        }

        BlockStmt body = ParseBlock();
        return new ElevateStmt(elevator, body, MakeSpan(elevateToken.Span, body.Span), elevateToken);
    }

    /// <summary>
    /// Parses a lock statement: <c>lock path { ... }</c> or <c>lock path (wait: duration, stale: duration) { ... }</c>.
    /// The <c>lock</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="LockStmt"/>.</returns>
    private Stmt LockStatement()
    {
        Token lockKeyword = Previous();

        // Parse path expression using a disambiguation-aware parser.
        // Standard Expression()/Call() would greedily consume '(' as a function call,
        // preventing the lock options list from being recognized. ParseLockPath()
        // stops before '(' when the look-ahead matches 'IDENTIFIER :' (named options).
        Expr path = ParseLockPath();

        // Parse optional named options: (wait: duration, stale: duration)
        Expr? waitOption = null;
        Expr? staleOption = null;

        if (Check(TokenType.LeftParen)
            && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Identifier
            && _current + 2 < _tokens.Count && _tokens[_current + 2].Type == TokenType.Colon)
        {
            Advance(); // consume '('
            do
            {
                Token optionName = Consume(TokenType.Identifier, "Expected option name in lock options.");
                Consume(TokenType.Colon, "Expected ':' after option name.");
                Expr optionValue = Assignment();

                string name = optionName.Lexeme;
                if (name == "wait")
                    waitOption = optionValue;
                else if (name == "stale")
                    staleOption = optionValue;
                else
                    throw Error(optionName, $"Unknown lock option '{name}'. Expected 'wait' or 'stale'.");

            } while (Match(TokenType.Comma)
                  && Check(TokenType.Identifier)
                  && _current + 1 < _tokens.Count
                  && _tokens[_current + 1].Type == TokenType.Colon);

            Consume(TokenType.RightParen, "Expected ')' after lock options.");
        }

        BlockStmt body = ParseBlock();
        return new LockStmt(lockKeyword, path, waitOption, staleOption, body, MakeSpan(lockKeyword.Span, body.Span));
    }

    /// <summary>
    /// Parses the path expression for a <c>lock</c> statement using Call()-level
    /// precedence, but stops before consuming <c>(</c> when the lookahead is
    /// <c>IDENTIFIER ':'</c> (indicating the start of the named-options list rather
    /// than a function call argument list).
    /// </summary>
    private Expr ParseLockPath()
    {
        Expr expr = Primary();
        while (true)
        {
            // If '(' is followed by 'IDENTIFIER :' it is a lock named-options list — stop.
            if (Check(TokenType.LeftParen)
                && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Identifier
                && _current + 2 < _tokens.Count && _tokens[_current + 2].Type == TokenType.Colon)
            {
                break;
            }

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
            else if (Match(TokenType.Dot) || Match(TokenType.QuestionDot))
            {
                bool isOptional = Previous().Type == TokenType.QuestionDot;
                Token name = ConsumePropertyName();
                expr = new DotExpr(expr, name, MakeSpan(expr.Span, name.Span), isOptional);
            }
            else
            {
                break;
            }
        }
        return expr;
    }

    private Stmt UnsetStatement()
    {
        Token unsetKeyword = Previous();
        var targets = new List<UnsetTarget>();

        do
        {
            Token name = Consume(TokenType.Identifier, "Expected identifier after 'unset'.");
            targets.Add(new UnsetTarget(name.Lexeme, name.Span));
        } while (Match(TokenType.Comma));

        Token semi = Consume(TokenType.Semicolon, "Expected ';' after 'unset' statement.");
        return new UnsetStmt(unsetKeyword, targets, MakeSpan(unsetKeyword.Span, semi.Span));
    }

    /// <returns>A <see cref="DoWhileStmt"/>.</returns>
    private Stmt DoWhileStatement()
    {
        Token doToken = Previous();
        BlockStmt body = ParseBlock();
        Consume(TokenType.While, "Expected 'while' after do block.");
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        Expr condition = Expression();
        Consume(TokenType.RightParen, "Expected ')' after do-while condition.");
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after do-while condition.");
        return new DoWhileStmt(body, condition, MakeSpan(doToken.Span, semi.Span));
    }

    /// <summary>
    /// Dispatches a <c>for</c> statement to either a for-in or C-style for parser.
    /// The <c>for</c> token has already been consumed.
    /// </summary>
    private Stmt ForStatement()
    {
        Token forToken = Previous();
        Consume(TokenType.LeftParen, "Expected '(' after 'for'.");

        // Disambiguate: if we see 'let' followed by identifier(s) and 'in', it's a for-in loop
        if (Check(TokenType.Let) && IsForInLoop())
        {
            return ParseForIn(forToken);
        }

        return ParseForCStyle(forToken);
    }

    /// <summary>
    /// Lookahead check to determine whether the current token stream matches a for-in pattern.
    /// Assumes the opening <c>(</c> has been consumed and the current token is <c>let</c>.
    /// </summary>
    private bool IsForInLoop()
    {
        int saved = _current;
        try
        {
            Advance(); // skip 'let'
            if (!Check(TokenType.Identifier)) return false;
            Advance(); // skip first identifier

            // Optional: comma + second identifier (indexed for-in)
            if (Check(TokenType.Comma))
            {
                Advance(); // skip ','
                if (!Check(TokenType.Identifier)) return false;
                Advance(); // skip second identifier
            }

            // Optional: colon + type hint
            if (Check(TokenType.Colon))
            {
                Advance(); // skip ':'
                if (!Check(TokenType.Identifier)) return false;
                Advance(); // skip type
            }

            return Check(TokenType.In);
        }
        finally
        {
            _current = saved;
        }
    }

    /// <summary>
    /// Parses a for-in statement: <c>for (let name in iterable) { ... }</c>.
    /// The <c>for</c> token and opening <c>(</c> have already been consumed.
    /// </summary>
    /// <returns>A <see cref="ForInStmt"/>.</returns>
    private Stmt ParseForIn(Token forToken)
    {
        Consume(TokenType.Let, "Expected 'let' after '(' in for-in loop.");
        Token varName = Consume(TokenType.Identifier, "Expected variable name in for-in loop.");
        Token? indexName = null;

        // Check for two-variable form: for (let i, item in collection)
        if (Match(TokenType.Comma))
        {
            indexName = varName;  // First variable is the index
            varName = Consume(TokenType.Identifier, "Expected variable name after ',' in for-in loop.");
        }

        TypeHint? typeHint = null;
        if (Match(TokenType.Colon))
        {
            typeHint = ParseTypeHint();
        }
        Consume(TokenType.In, "Expected 'in' after variable name in for-in loop.");
        Expr iterable = Expression();
        Consume(TokenType.RightParen, "Expected ')' after for-in clause.");
        BlockStmt body = ParseBlock();
        return new ForInStmt(indexName, varName, typeHint, iterable, body, MakeSpan(forToken.Span, body.Span));
    }

    /// <summary>
    /// Parses a C-style for statement: <c>for (init; condition; update) { ... }</c>.
    /// The <c>for</c> token and opening <c>(</c> have already been consumed.
    /// </summary>
    /// <returns>A <see cref="ForStmt"/>.</returns>
    private Stmt ParseForCStyle(Token forToken)
    {
        // Parse initializer: let declaration, expression statement, or empty
        Stmt? initializer;
        if (Match(TokenType.Semicolon))
        {
            initializer = null;
        }
        else if (Match(TokenType.Let))
        {
            initializer = VarDeclaration(); // consumes trailing ';' (the first for-clause separator)
        }
        else
        {
            Expr expr = Expression();
            Consume(TokenType.Semicolon, "Expected ';' after for loop initializer.");
            initializer = new ExprStmt(expr, expr.Span);
        }

        // Parse condition (or empty)
        Expr? condition = null;
        if (!Check(TokenType.Semicolon))
        {
            condition = Expression();
        }
        Consume(TokenType.Semicolon, "Expected ';' after for loop condition.");

        // Parse increment (or empty)
        Expr? increment = null;
        if (!Check(TokenType.RightParen))
        {
            increment = Expression();
        }
        Consume(TokenType.RightParen, "Expected ')' after for clauses.");

        BlockStmt body = ParseBlock();
        return new ForStmt(initializer, condition, increment, body, MakeSpan(forToken.Span, body.Span));
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
    /// Parses a throw statement: <c>throw expr;</c> or bare <c>throw;</c> (re-throw).
    /// The <c>throw</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="ThrowStmt"/>.</returns>
    private Stmt ThrowStatement()
    {
        Token keyword = Previous();
        if (Match(TokenType.Semicolon))
        {
            // Bare rethrow: throw;
            return new ThrowStmt(keyword, null, MakeSpan(keyword.Span, Previous().Span));
        }
        Expr value = Expression();
        Token semi = Consume(TokenType.Semicolon, "Expected ';' after throw value.");
        return new ThrowStmt(keyword, value, MakeSpan(keyword.Span, semi.Span));
    }

    /// <summary>
    /// Parses a defer statement: <c>defer expr;</c>, <c>defer await expr;</c>, or <c>defer { block }</c>.
    /// The <c>defer</c> token has already been consumed.
    /// </summary>
    /// <returns>A <see cref="DeferStmt"/>.</returns>
    private Stmt DeferStatement()
    {
        Token deferToken = Previous();

        // Block defer: defer { ... }
        if (Check(TokenType.LeftBrace))
        {
            BlockStmt block = ParseBlock();
            return new DeferStmt(deferToken, block, false, MakeSpan(deferToken.Span, block.Span));
        }

        // Check for await: defer await expr
        bool hasAwait = Check(TokenType.Identifier) && Peek().Lexeme == "await" && IsAwaitKeyword();
        if (hasAwait) Advance(); // consume 'await'

        // Single-statement defer: defer expr; or defer await expr;
        Stmt body = ExpressionStatement();
        return new DeferStmt(deferToken, body, hasAwait, MakeSpan(deferToken.Span, body.Span));
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

    private Stmt SwitchStatement()
    {
        Token switchKeyword = Previous();
        Consume(TokenType.LeftParen, "Expected '(' after 'switch'.");
        Expr subject = Expression();
        Consume(TokenType.RightParen, "Expected ')' after switch subject.");
        Consume(TokenType.LeftBrace, "Expected '{' before switch cases.");
        List<SwitchCase> cases = new();
        while (!Check(TokenType.RightBrace) && !IsAtEnd)
        {
            if (Match(TokenType.Case))
            {
                Token caseKeyword = Previous();
                List<Expr> patterns = new();
                patterns.Add(Expression());
                while (Match(TokenType.Comma))
                {
                    if (Check(TokenType.Colon))
                        break;
                    patterns.Add(Expression());
                }
                Consume(TokenType.Colon, "Expected ':' after case patterns.");
                Stmt body = ParseBlock();
                cases.Add(new SwitchCase(patterns, false, body, MakeSpan(caseKeyword.Span, body.Span)));
            }
            else if (Match(TokenType.Default))
            {
                Token defaultKeyword = Previous();
                Consume(TokenType.Colon, "Expected ':' after 'default'.");
                Stmt body = ParseBlock();
                cases.Add(new SwitchCase(new List<Expr>(), true, body, MakeSpan(defaultKeyword.Span, body.Span)));
            }
            else
            {
                throw Error(Peek(), "Expected 'case' or 'default' in switch statement.");
            }
        }
        Token close = Consume(TokenType.RightBrace, "Expected '}' after switch cases.");
        return new SwitchStmt(subject, cases, MakeSpan(switchKeyword.Span, close.Span));
    }

    /// <summary>
    /// Parses a try/catch/finally statement: <c>try { ... } catch (e) { ... } finally { ... }</c>.
    /// The <c>try</c> token has already been consumed.
    /// A bare <c>try { ... }</c> block (no catch, no finally) is also valid and suppresses errors silently.
    /// </summary>
    /// <returns>A <see cref="TryCatchStmt"/>.</returns>
    private Stmt TryCatchStatement()
    {
        Token tryKeyword = Previous();
        BlockStmt tryBody = ParseBlock();

        var catchClauses = new List<CatchClause>();
        Token? finallyKeyword = null;
        BlockStmt? finallyBody = null;

        while (Match(TokenType.Catch))
        {
            Token catchKeyword = Previous();
            Consume(TokenType.LeftParen, "Expected '(' after 'catch'.");
            // Parse either "Type var" or just "var" (full multi-clause/union parsing)
            Token firstIdent = Consume(TokenType.Identifier, "Expected variable name or type name in 'catch'.");
            var typeTokens = new List<Token>();
            Token variable;
            if (!Check(TokenType.RightParen))
            {
                // First token was a type name; parse optional | type names, then variable
                typeTokens.Add(firstIdent);
                while (Match(TokenType.Pipe))
                {
                    typeTokens.Add(Consume(TokenType.Identifier, "Expected type name after '|' in catch clause."));
                }
                variable = Consume(TokenType.Identifier, "Expected variable name in 'catch'.");
            }
            else
            {
                // Single identifier → untyped catch-all
                variable = firstIdent;
            }
            Consume(TokenType.RightParen, "Expected ')' after catch variable.");
            BlockStmt catchBody = ParseBlock();
            SourceSpan clauseSpan = MakeSpan(catchKeyword.Span, catchBody.Span);
            catchClauses.Add(new CatchClause(catchKeyword, typeTokens, variable, catchBody, clauseSpan));
        }

        if (Match(TokenType.Finally))
        {
            finallyKeyword = Previous();
            finallyBody = ParseBlock();
        }

        Stmt lastPart = (Stmt?)finallyBody ?? (catchClauses.Count > 0 ? catchClauses[^1].Body : tryBody);
        SourceSpan endSpan = lastPart.Span;
        return new TryCatchStmt(tryKeyword, tryBody, catchClauses, finallyKeyword, finallyBody,
            MakeSpan(tryKeyword.Span, endSpan));
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

        if (IsCompoundAssignment())
        {
            Advance();
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

    /// <summary>Desugars a compound assignment operator (e.g. <c>+=</c>, <c>-=</c>) into a binary expression wrapped in an assignment.</summary>
    /// <param name="compoundOp">The compound assignment operator token.</param>
    /// <param name="target">The assignment target expression.</param>
    /// <param name="value">The right-hand side expression.</param>
    /// <returns>An assignment expression node wrapping the desugared binary operation.</returns>
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
            TokenType.AmpersandEqual => TokenType.Ampersand,
            TokenType.PipeEqual => TokenType.Pipe,
            TokenType.CaretEqual => TokenType.Caret,
            TokenType.LessLessEqual => TokenType.LessLess,
            TokenType.GreaterGreaterEqual => TokenType.GreaterGreater,
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
    /// <summary>
    /// Parses a pipe expression: <c>$(cmd1) | $(cmd2)</c>.
    /// Pipe chains process stdout → stdin, left-associative.
    /// Only consumes <c>|</c> as a pipe when the left operand is a command-producing expression;
    /// otherwise <c>|</c> is left for the <see cref="BitwiseOr"/> precedence level.
    /// </summary>
    private Expr Pipe()
    {
        Expr expr = Or();

        while (Check(TokenType.Pipe))
        {
            bool isCommandLike = expr is CommandExpr or PipeExpr or RedirectExpr;
            if (!isCommandLike) break;

            Advance();
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
    /// or the result of <see cref="BitwiseOr"/> if no <c>||</c> operator is present.
    /// </returns>
    private Expr Or()
    {
        Expr expr = BitwiseOr();

        while (Match(TokenType.PipePipe))
        {
            Token op = Previous();
            Expr right = BitwiseOr();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a bitwise OR expression: <c>left | right</c> (integer operands only).
    /// When the left operand is a command-producing expression, <c>|</c> is left for
    /// <see cref="Pipe"/> to consume as a shell pipe operator instead.
    /// </summary>
    private Expr BitwiseOr()
    {
        Expr expr = BitwiseXor();

        while (Check(TokenType.Pipe))
        {
            if (expr is CommandExpr or PipeExpr or RedirectExpr) break;

            Advance();
            Token op = Previous();
            Expr right = BitwiseXor();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a bitwise XOR expression: <c>left ^ right</c> (integer operands only).
    /// </summary>
    private Expr BitwiseXor()
    {
        Expr expr = BitwiseAnd();

        while (Match(TokenType.Caret))
        {
            Token op = Previous();
            Expr right = BitwiseAnd();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a bitwise AND expression: <c>left &amp; right</c> (integer operands only).
    /// </summary>
    private Expr BitwiseAnd()
    {
        Expr expr = And();

        while (Match(TokenType.Ampersand))
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
        Expr expr = Shift();

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
            Expr right = Shift();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        while (Match(TokenType.In))
        {
            Token op = Previous();
            Expr right = Shift();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        if (Match(TokenType.Is))
        {
            Token isKeyword = Previous();
            if (Match(TokenType.Null) || Match(TokenType.Struct) || Match(TokenType.Enum))
            {
                // Built-in type keywords — bare token path
                Token typeName = Previous();
                expr = new IsExpr(expr, isKeyword, typeName, MakeSpan(expr.Span, typeName.Span));
            }
            else if (Check(TokenType.Identifier))
            {
                // Check for typed array pattern: identifier followed by []
                if (_current + 2 < _tokens.Count &&
                    _tokens[_current + 1].Type is TokenType.LeftBracket &&
                    _tokens[_current + 2].Type is TokenType.RightBracket)
                {
                    // T[] pattern: consume identifier, [, ]
                    Token baseType = Advance();
                    Advance(); // [
                    Token closeBracket = Advance(); // ]
                    // Create a synthetic token with combined lexeme "int[]"
                    var syntheticToken = new Token(TokenType.Identifier, $"{baseType.Lexeme}[]", null,
                        new SourceSpan(baseType.Span.File, baseType.Span.StartLine, baseType.Span.StartColumn,
                            closeBracket.Span.EndLine, closeBracket.Span.EndColumn));
                    expr = new IsExpr(expr, isKeyword, syntheticToken, MakeSpan(expr.Span, closeBracket.Span));
                }
                // Peek ahead: if followed by (, ., or [ → complex expression (array index, call, member access)
                else if (_current + 1 < _tokens.Count &&
                    _tokens[_current + 1].Type is TokenType.LeftParen or TokenType.Dot or TokenType.LeftBracket)
                {
                    Expr typeExpr = Call();
                    expr = new IsExpr(expr, isKeyword, typeExpr, MakeSpan(expr.Span, typeExpr.Span));
                }
                else
                {
                    // Bare identifier: int, Point, Printable
                    Token typeName = Advance();
                    expr = new IsExpr(expr, isKeyword, typeName, MakeSpan(expr.Span, typeName.Span));
                }
            }
            else
            {
                // Non-identifier expression start (e.g., parenthesized group)
                Expr typeExpr = Call();
                expr = new IsExpr(expr, isKeyword, typeExpr, MakeSpan(expr.Span, typeExpr.Span));
            }
        }

        return expr;
    }

    /// <summary>
    /// Parses a bit-shift expression: <c>left &lt;&lt; right</c> or <c>left &gt;&gt; right</c> (integer operands only).
    /// In command context, <c>&gt;&gt;</c> is consumed as redirection by <see cref="Redirect"/>, not as right-shift.
    /// </summary>
    private Expr Shift()
    {
        Expr expr = Range();

        while (true)
        {
            if (Match(TokenType.LessLess))
            {
                Token op = Previous();
                Expr right = Range();
                expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
            }
            else if (Check(TokenType.GreaterGreater))
            {
                // In command context, >> is redirection, not right-shift.
                bool isCommandLike = expr is CommandExpr or PipeExpr or RedirectExpr;
                if (isCommandLike) break;

                Advance();
                Token op = Previous();
                Expr right = Range();
                expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
            }
            else
            {
                break;
            }
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
        if (Match(TokenType.Bang, TokenType.Minus, TokenType.Tilde))
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

        if (Check(TokenType.Identifier) && Peek().Lexeme == "await" && IsAwaitKeyword())
        {
            Advance(); // consume 'await'
            Token awaitToken = Previous();
            Expr expression = Unary();
            return new AwaitExpr(awaitToken, expression, MakeSpan(awaitToken.Span, expression.Span));
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
            else if (Match(TokenType.Dot) || Match(TokenType.QuestionDot))
            {
                bool isOptional = Previous().Type == TokenType.QuestionDot;
                Token name = ConsumePropertyName();
                Expr dotExpr = new DotExpr(expr, name, MakeSpan(expr.Span, name.Span), isOptional);

                // Check for namespaced struct init: ns.StructName { field: value, ... }
                // (only for regular dot, not optional chaining)
                if (!isOptional && Check(TokenType.LeftBrace))
                {
                    int savedPosition = _current;
                    Advance(); // consume '{'

                    if (Check(TokenType.Identifier))
                    {
                        int peekAhead = _current;
                        if (peekAhead + 1 < _tokens.Count &&
                            (_tokens[peekAhead + 1].Type == TokenType.Colon ||
                             _tokens[peekAhead + 1].Type == TokenType.Comma ||
                             _tokens[peekAhead + 1].Type == TokenType.RightBrace))
                        {
                            List<(Token Field, Expr Value)> fieldValues = new();
                            do
                            {
                                Token field = Consume(TokenType.Identifier, "Expected field name.");
                                Expr value;
                                if (Match(TokenType.Colon))
                                {
                                    value = Expression();
                                }
                                else
                                {
                                    // Shorthand: { host } is equivalent to { host: host }
                                    value = new IdentifierExpr(field, field.Span);
                                }
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

    /// <summary>
    /// Parses a retry expression: <c>retry (maxAttempts, options...) onRetry (...) { ... } until predicate { body }</c>.
    /// The <c>retry</c> token has already been consumed.
    /// </summary>
    private Expr ParseRetryExpr()
    {
        Token retryKeyword = Previous();

        // Parse (maxAttempts, options...)
        Consume(TokenType.LeftParen, "Expected '(' after 'retry'.");
        Expr maxAttempts = Expression();

        List<(Token Name, Expr Value)>? namedOptions = null;
        Expr? optionsExpr = null;

        if (Match(TokenType.Comma))
        {
            // Disambiguate: named options (Identifier ':') vs struct expression
            if (Check(TokenType.Identifier) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Colon)
            {
                // Named options: delay: 1s, backoff: Backoff.Exponential, ...
                namedOptions = new List<(Token, Expr)>();
                do
                {
                    Token name = Consume(TokenType.Identifier, "Expected option name.");
                    Consume(TokenType.Colon, "Expected ':' after option name.");
                    Expr value = Assignment();
                    namedOptions.Add((name, value));
                } while (Match(TokenType.Comma) && Check(TokenType.Identifier) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Colon);
            }
            else
            {
                // Single expression (RetryOptions struct instance)
                optionsExpr = Assignment();
            }
        }

        Consume(TokenType.RightParen, "Expected ')' after retry arguments.");

        // Parse optional onRetry clause (contextual — check for identifier "onRetry")
        OnRetryNode? onRetryClause = null;
        if (Check(TokenType.Identifier) && Peek().Lexeme == "onRetry")
        {
            Token onRetryToken = Advance();
            SourceSpan onRetryStart = onRetryToken.Span;

            if (Check(TokenType.LeftParen))
            {
                // Inline block: onRetry (n, err) { ... }
                Advance(); // consume '('
                Token paramAttempt = Consume(TokenType.Identifier, "Expected parameter name for attempt number.");
                TypeHint? paramAttemptTypeHint = null;
                if (Match(TokenType.Colon))
                    paramAttemptTypeHint = ParseTypeHint();
                Consume(TokenType.Comma, "Expected ',' between onRetry parameters.");
                Token paramError = Consume(TokenType.Identifier, "Expected parameter name for error.");
                TypeHint? paramErrorTypeHint = null;
                if (Match(TokenType.Colon))
                    paramErrorTypeHint = ParseTypeHint();
                Consume(TokenType.RightParen, "Expected ')' after onRetry parameters.");
                BlockStmt hookBody = ParseBlock();
                onRetryClause = new OnRetryNode(onRetryToken, false, paramAttempt, paramAttemptTypeHint, paramError, paramErrorTypeHint, hookBody, null, MakeSpan(onRetryStart, hookBody.Span));
            }
            else
            {
                // Function reference: onRetry logRetry
                Expr reference = Primary();
                onRetryClause = new OnRetryNode(onRetryToken, true, null, null, null, null, null, reference, MakeSpan(onRetryStart, reference.Span));
            }
        }

        // Parse optional until clause (contextual — check for identifier "until")
        Token? untilKeyword = null;
        Expr? untilClause = null;
        if (Check(TokenType.Identifier) && Peek().Lexeme == "until")
        {
            untilKeyword = Advance(); // consume and store "until"
            untilClause = Assignment();
        }

        // Parse retry body block
        BlockStmt body = ParseBlock();

        return new RetryExpr(
            retryKeyword, maxAttempts, namedOptions, optionsExpr,
            untilKeyword, untilClause, onRetryClause, body,
            MakeSpan(retryKeyword.Span, body.Span));
    }

    private Expr ParseTimeoutExpr()
    {
        Token timeoutKeyword = Previous();

        // Parse duration expression (e.g., 30s, 5m, durationVar, getDuration(), config.timeout).
        // Use Call() to support function calls, member access, and indexing.
        // Binary/ternary expressions require parentheses: timeout (base + extra) { ... }
        Expr duration = Call();

        // Parse body block.
        BlockStmt body = ParseBlock();

        return new TimeoutExpr(timeoutKeyword, duration, body, MakeSpan(timeoutKeyword.Span, body.Span));
    }

    /// <summary>Parses the arms of a <c>switch</c> expression after the subject has been parsed.</summary>
    /// <param name="subject">The switch subject expression.</param>
    /// <returns>A <see cref="SwitchExpr"/> node containing all parsed arms.</returns>
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
                if (Match(TokenType.DotDotDot))
                {
                    Token spread = Previous();
                    Expr inner = Expression();
                    arguments.Add(new SpreadExpr(spread, inner, MakeSpan(spread.Span, inner.Span)));
                }
                else
                {
                    arguments.Add(Expression());
                }
            } while (Match(TokenType.Comma));
        }

        Token paren = Consume(TokenType.RightParen, "Expected ')' after arguments.");
        bool isOptional = callee is DotExpr { IsOptional: true };
        return new CallExpr(callee, paren, arguments, MakeSpan(callee.Span, paren.Span), isOptional);
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
        if (Match(TokenType.IntegerLiteral, TokenType.FloatLiteral, TokenType.StringLiteral, TokenType.IpAddressLiteral, TokenType.DurationLiteral, TokenType.ByteSizeLiteral, TokenType.SemVerLiteral))
        {
            Token token = Previous();
            return new LiteralExpr(token.Literal, token.Span);
        }

        if (Match(TokenType.InterpolatedString))
        {
            Token token = Previous();
            return ParseInterpolatedString(token);
        }

        if (Match(TokenType.CommandLiteral, TokenType.PassthroughCommandLiteral, TokenType.StrictCommandLiteral, TokenType.StrictPassthroughCommandLiteral))
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
                    if (Match(TokenType.DotDotDot))
                    {
                        Token spread = Previous();
                        Expr inner = Expression();
                        elements.Add(new SpreadExpr(spread, inner, MakeSpan(spread.Span, inner.Span)));
                    }
                    else
                    {
                        elements.Add(Expression());
                    }
                } while (Match(TokenType.Comma));
            }
            Token close = Consume(TokenType.RightBracket, "Expected ']' after array elements.");
            return new ArrayExpr(elements, MakeSpan(open.Span, close.Span));
        }

        // Dict literal: { key: value, key2: value2 }
        // Only in expression context — Statement() catches LeftBrace first for blocks.
        if (Check(TokenType.LeftBrace))
        {
            int savedPosition = _current;
            Token open = Advance(); // consume '{'

            // Empty dict: { }
            if (Check(TokenType.RightBrace))
            {
                Token close = Advance();
                return new DictLiteralExpr(new List<(Token?, Expr)>(), MakeSpan(open.Span, close.Span));
            }

            // Check for dict patterns: spread entry or Identifier ':' (key-value pair)
            bool isDict = false;
            if (Check(TokenType.DotDotDot))
            {
                isDict = true;
            }
            else if (IsDictKeyToken(Peek().Type))
            {
                int peekAhead = _current;
                if (peekAhead + 1 < _tokens.Count &&
                    _tokens[peekAhead + 1].Type == TokenType.Colon)
                {
                    isDict = true;
                }
            }

            if (isDict)
            {
                var entries = new List<(Token? Key, Expr Value)>();
                do
                {
                    if (Match(TokenType.DotDotDot))
                    {
                        Token spread = Previous();
                        Expr inner = Expression();
                        entries.Add((null, new SpreadExpr(spread, inner, MakeSpan(spread.Span, inner.Span))));
                    }
                    else
                    {
                        if (!IsDictKeyToken(Peek().Type))
                            throw Error(Peek(), "Expected identifier key in dict literal.");
                        Token key = Advance();
                        Consume(TokenType.Colon, "Expected ':' after dict key.");
                        Expr value = Expression();
                        entries.Add((key, value));
                    }
                } while (Match(TokenType.Comma));

                Token close = Consume(TokenType.RightBrace, "Expected '}' after dict entries.");
                return new DictLiteralExpr(entries, MakeSpan(open.Span, close.Span));
            }

            // Not a dict literal — backtrack
            _current = savedPosition;
        }

        // Soft keywords: must be checked BEFORE Match(Identifier) to intercept the token before it is consumed.
        if (Check(TokenType.Identifier))
        {
            string skLexeme = Peek().Lexeme;
            if (skLexeme == "timeout" && IsTimeoutKeyword())
            {
                Advance(); // consume 'timeout'
                return ParseTimeoutExpr();
            }
            if (skLexeme == "retry" && IsRetryKeyword())
            {
                Advance(); // consume 'retry'
                return ParseRetryExpr();
            }
            if (skLexeme == "async" && IsAsyncLambdaKeyword())
            {
                Advance(); // consume 'async'
                Token asyncToken = Previous();
                Consume(TokenType.LeftParen, "Expected '(' after 'async'.");
                Token open = Previous();
                return ParseLambda(open, isAsync: true, asyncToken: asyncToken);
            }
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

                // It's a struct init if we see: Identifier ':', Identifier ',', or Identifier '}'
                if (Check(TokenType.Identifier))
                {
                    int peekAhead = _current;
                    if (peekAhead + 1 < _tokens.Count &&
                        (_tokens[peekAhead + 1].Type == TokenType.Colon ||
                         _tokens[peekAhead + 1].Type == TokenType.Comma ||
                         _tokens[peekAhead + 1].Type == TokenType.RightBrace))
                    {
                        // This is a struct init: Name { field: value, ... } or shorthand Name { field, ... }
                        List<(Token Field, Expr Value)> fieldValues = new();
                        do
                        {
                            Token field = Consume(TokenType.Identifier, "Expected field name.");
                            Expr value;
                            if (Match(TokenType.Colon))
                            {
                                value = Expression();
                            }
                            else
                            {
                                // Shorthand: { host } is equivalent to { host: host }
                                value = new IdentifierExpr(field, field.Span);
                            }
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

        // Cheap early-out: if next token can't start a parameter list, bail immediately
        TokenType next = Peek().Type;
        if (next is not (TokenType.RightParen or TokenType.Identifier or TokenType.DotDotDot))
        {
            return false;
        }

        try
        {
            // () => ...
            if (Check(TokenType.RightParen))
            {
                Advance(); // skip ')'
                return Check(TokenType.FatArrow);
            }

            // Try to match: [... ] identifier [: type] (, [... ] identifier [: type])* ) =>
            while (true)
            {
                // Allow rest parameter prefix
                if (Check(TokenType.DotDotDot))
                {
                    Advance(); // skip '...'
                }

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
    /// Determines whether the current 'timeout' identifier token should be parsed
    /// as a TimeoutExpr keyword rather than a plain identifier reference.
    /// Called only when Peek().Lexeme == "timeout".
    /// </summary>
    private bool IsTimeoutKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        TokenType peek = _tokens[_current + 1].Type;

        // ── Clear identifier continuations (cannot start a duration expression) ──
        switch (peek)
        {
            case TokenType.Eof:
            case TokenType.Semicolon:
            case TokenType.Comma:
            case TokenType.RightParen:
            case TokenType.RightBracket:
            case TokenType.Colon:
            case TokenType.Equal:
            case TokenType.PlusEqual:
            case TokenType.MinusEqual:
            case TokenType.StarEqual:
            case TokenType.SlashEqual:
            case TokenType.PercentEqual:
            case TokenType.QuestionQuestionEqual:
            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.Percent:
            case TokenType.Pipe:
            case TokenType.Ampersand:
            case TokenType.Caret:
            case TokenType.Tilde:
            case TokenType.PipePipe:
            case TokenType.AmpersandAmpersand:
            case TokenType.Bang:
            case TokenType.EqualEqual:
            case TokenType.BangEqual:
            case TokenType.Less:
            case TokenType.Greater:
            case TokenType.LessEqual:
            case TokenType.GreaterEqual:
            case TokenType.Dot:
            case TokenType.LeftBracket:
            case TokenType.QuestionMark:
            case TokenType.QuestionQuestion:
            case TokenType.In:
            case TokenType.Is:
            case TokenType.As:
                return false;
        }

        // ── Clear TimeoutExpr starters ──
        switch (peek)
        {
            case TokenType.IntegerLiteral:
            case TokenType.FloatLiteral:
            case TokenType.DurationLiteral:
            case TokenType.ByteSizeLiteral:
            case TokenType.StringLiteral:
            case TokenType.Identifier:
                return true;
        }

        // ── Ambiguous: '(' — scan forward to see if '{' follows the balanced parens ──
        if (peek == TokenType.LeftParen)
            return IsFollowedByBlockAfterParens(_current + 1);

        // Conservative fallback — treat as identifier
        return false;
    }

    /// <summary>
    /// Scans forward from <paramref name="parenIndex"/> (the index of a '(' token)
    /// to find the matching ')'. Returns true if the token immediately after the
    /// closing ')' is '{', indicating a TimeoutExpr with a grouped duration.
    /// </summary>
    private bool IsFollowedByBlockAfterParens(int parenIndex)
    {
        int depth = 0;
        int pos = parenIndex;
        while (pos < _tokens.Count)
        {
            TokenType t = _tokens[pos].Type;
            if (t == TokenType.LeftParen) depth++;
            else if (t == TokenType.RightParen)
            {
                depth--;
                if (depth == 0)
                    return pos + 1 < _tokens.Count && _tokens[pos + 1].Type == TokenType.LeftBrace;
            }
            else if (t == TokenType.Eof) break;
            pos++;
        }
        return false;
    }

    /// <summary>Returns true if 'defer' at current position is used as a keyword (not an identifier).</summary>
    private bool IsDeferKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        TokenType peek = _tokens[_current + 1].Type;
        return peek switch
        {
            TokenType.LeftBrace => true,       // defer { block }
            TokenType.Identifier => true,      // defer someCall() or defer await expr
            TokenType.IntegerLiteral or TokenType.FloatLiteral or TokenType.StringLiteral
                or TokenType.DurationLiteral or TokenType.ByteSizeLiteral
                or TokenType.True or TokenType.False or TokenType.Null => true,
            _ => false  // defer(...) = function call; defer = x; etc.
        };
    }

    /// <summary>Returns true if 'lock' at current position is used as a keyword (not an identifier).</summary>
    private bool IsLockKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        TokenType peek = _tokens[_current + 1].Type;
        if (peek == TokenType.Identifier) return true;                // lock myPath { }
        if (IsNonOperatorLiteralType(peek)) return true;              // lock "path/file" { }
        if (peek == TokenType.LeftParen)
            return IsFollowedByLockOptionsOrBlockAfterParens(_current + 1);
        return false;
    }

    /// <summary>Returns true if 'elevate' at current position is used as a keyword (not an identifier).</summary>
    private bool IsElevateKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        TokenType peek = _tokens[_current + 1].Type;
        if (peek == TokenType.LeftBrace) return true;                 // elevate { block }
        if (peek == TokenType.LeftParen)
            return IsFollowedByBlockAfterParens(_current + 1);        // elevate(expr) { block }
        return false;
    }

    /// <summary>Returns true if 'retry' at current position is used as a keyword expression (not a function call on a variable).</summary>
    private bool IsRetryKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        if (_tokens[_current + 1].Type != TokenType.LeftParen) return false;
        return IsFollowedByRetryClauseAfterParens(_current + 1);
    }

    /// <summary>Returns true if 'await' at current position is used as a keyword (prefix operator).</summary>
    private bool IsAwaitKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        return IsExpressionStarter(_tokens[_current + 1].Type);
    }

    /// <summary>Returns true if 'async' at current position precedes a lambda parameter list '(' ... ') =>'.</summary>
    private bool IsAsyncLambdaKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        if (_tokens[_current + 1].Type != TokenType.LeftParen) return false;
        int saved = _current;
        _current += 2; // point to first token inside '(' (past 'async' and '(')
        bool result = IsLambdaStart();
        _current = saved;
        return result;
    }

    /// <summary>Returns true if 'unset' at current position is used as a keyword (not an identifier).</summary>
    private bool IsUnsetKeyword()
    {
        if (_current + 1 >= _tokens.Count) return false;
        return _tokens[_current + 1].Type == TokenType.Identifier;
    }

    /// <summary>Returns true if the token type can start an expression (used for await keyword disambiguation).</summary>
    private static bool IsExpressionStarter(TokenType t) =>
        t is TokenType.Identifier
            or TokenType.IntegerLiteral or TokenType.FloatLiteral or TokenType.StringLiteral
            or TokenType.DurationLiteral or TokenType.ByteSizeLiteral
            or TokenType.True or TokenType.False or TokenType.Null
            or TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace
            or TokenType.Bang or TokenType.Tilde or TokenType.Minus
            or TokenType.PlusPlus or TokenType.MinusMinus;

    /// <summary>Returns true if the token type is a non-operator literal (can start a primary expression but cannot continue a binary expression after an identifier).</summary>
    private static bool IsNonOperatorLiteralType(TokenType type) =>
        type is TokenType.IntegerLiteral or TokenType.FloatLiteral or TokenType.StringLiteral
             or TokenType.DurationLiteral or TokenType.ByteSizeLiteral
             or TokenType.True or TokenType.False or TokenType.Null;

    /// <summary>
    /// Like <see cref="IsFollowedByBlockAfterParens"/> but also returns true when the matching ')'
    /// is followed by a retry clause token: 'onRetry', 'until', or '{'.
    /// </summary>
    private bool IsFollowedByRetryClauseAfterParens(int parenIndex)
    {
        int depth = 0;
        int pos = parenIndex;
        while (pos < _tokens.Count)
        {
            TokenType t = _tokens[pos].Type;
            if (t == TokenType.LeftParen) depth++;
            else if (t == TokenType.RightParen)
            {
                depth--;
                if (depth == 0)
                {
                    if (pos + 1 >= _tokens.Count) return false;
                    Token next = _tokens[pos + 1];
                    if (next.Type == TokenType.LeftBrace) return true;
                    if (next.Type == TokenType.Identifier
                        && (next.Lexeme == "onRetry" || next.Lexeme == "until")) return true;
                    return false;
                }
            }
            else if (t == TokenType.Eof) break;
            pos++;
        }
        return false;
    }

    /// <summary>
    /// Like <see cref="IsFollowedByBlockAfterParens"/> but also returns true when the matching ')'
    /// is followed by a lock body '{' or a lock options list '(' IDENTIFIER ':'.
    /// </summary>
    private bool IsFollowedByLockOptionsOrBlockAfterParens(int parenIndex)
    {
        int depth = 0;
        int pos = parenIndex;
        while (pos < _tokens.Count)
        {
            TokenType t = _tokens[pos].Type;
            if (t == TokenType.LeftParen) depth++;
            else if (t == TokenType.RightParen)
            {
                depth--;
                if (depth == 0)
                {
                    if (pos + 1 >= _tokens.Count) return false;
                    TokenType nextType = _tokens[pos + 1].Type;
                    if (nextType == TokenType.LeftBrace) return true;
                    // lock (path) (wait: ...) { } \u2014 options list follows path
                    if (nextType == TokenType.LeftParen
                        && pos + 2 < _tokens.Count && _tokens[pos + 2].Type == TokenType.Identifier
                        && pos + 3 < _tokens.Count && _tokens[pos + 3].Type == TokenType.Colon)
                        return true;
                    return false;
                }
            }
            else if (t == TokenType.Eof) break;
            pos++;
        }
        return false;
    }

    /// <summary>
    /// Parses a lambda expression after the opening <c>(</c> has been consumed and
    /// <see cref="IsLambdaStart"/> has confirmed this is a lambda.
    /// Supports both expression bodies <c>(x) =&gt; x + 1</c> and block bodies <c>(x) =&gt; { ... }</c>.
    /// </summary>
    private Expr ParseLambda(Token open, bool isAsync = false, Token? asyncToken = null)
    {
        List<Token> parameters = new();
        List<TypeHint?> parameterTypes = new();
        List<Expr?> defaultValues = new();
        bool hasSeenDefault = false;
        bool hasRestParam = false;
        SourceSpan startSpan = asyncToken?.Span ?? open.Span;

        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (Match(TokenType.DotDotDot))
                {
                    if (hasRestParam)
                    {
                        Error(Previous(), "Only one rest parameter is allowed.");
                    }
                    hasRestParam = true;
                    Token restParamName = Consume(TokenType.Identifier, "Expected parameter name.");
                    TypeHint? restParamType = null;
                    if (Match(TokenType.Colon))
                    {
                        restParamType = ParseTypeHint();
                    }
                    if (Check(TokenType.Equal))
                    {
                        Error(Previous(), "Rest parameter cannot have a default value.");
                    }
                    parameters.Add(restParamName);
                    parameterTypes.Add(restParamType);
                    defaultValues.Add(null);
                    if (Check(TokenType.Comma))
                    {
                        Error(Previous(), "Rest parameter must be the last parameter.");
                    }
                    break;
                }

                parameters.Add(Consume(TokenType.Identifier, "Expected parameter name."));
                TypeHint? paramType = null;
                if (Match(TokenType.Colon))
                {
                    paramType = ParseTypeHint();
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
                                  MakeSpan(startSpan, block.Span), isAsync, asyncToken, hasRestParam);
        }

        // Expression body: (params) => expr
        Expr body = Assignment();
        return new LambdaExpr(parameters, parameterTypes, defaultValues, body, null,
                              MakeSpan(startSpan, body.Span), isAsync, asyncToken, hasRestParam);
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
                innerTokens.Add(new Token(TokenType.Eof, "", null, token.Span));

                var innerParser = new Parser(innerTokens);
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
    /// Converts a <see cref="TokenType.CommandLiteral"/> or <see cref="TokenType.PassthroughCommandLiteral"/> token into a
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
                innerTokens.Add(new Token(TokenType.Eof, "", null, token.Span));

                var innerParser = new Parser(innerTokens);
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

        bool isPassthrough = token.Type is TokenType.PassthroughCommandLiteral or TokenType.StrictPassthroughCommandLiteral;
        bool isStrict = token.Type is TokenType.StrictCommandLiteral or TokenType.StrictPassthroughCommandLiteral;
        return new CommandExpr(exprParts, token.Span, isPassthrough, isStrict);
    }

    // ── Helper methods ────────────────────────────────────────────

    /// <summary>
    /// <summary>
    /// Checks whether the current token matches the given type. If so, consumes it.
    /// Non-allocating single-type overload.
    /// </summary>
    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether the current token matches either type. If so, consumes it.
    /// Non-allocating two-type overload.
    /// </summary>
    private bool Match(TokenType type1, TokenType type2)
    {
        if (Check(type1))
        {
            Advance();
            return true;
        }
        if (Check(type2))
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether the current token matches any of the three types. If so, consumes it.
    /// Non-allocating three-type overload.
    /// </summary>
    private bool Match(TokenType type1, TokenType type2, TokenType type3)
    {
        if (Check(type1))
        {
            Advance();
            return true;
        }
        if (Check(type2))
        {
            Advance();
            return true;
        }
        if (Check(type3))
        {
            Advance();
            return true;
        }
        return false;
    }

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

    private bool IsCompoundAssignment()
    {
        return Peek().Type is
            TokenType.PlusEqual or TokenType.MinusEqual or TokenType.StarEqual or
            TokenType.SlashEqual or TokenType.PercentEqual or TokenType.QuestionQuestionEqual or
            TokenType.AmpersandEqual or TokenType.PipeEqual or TokenType.CaretEqual or
            TokenType.LessLessEqual or TokenType.GreaterGreaterEqual;
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

    /// <summary>Parses an optional type hint after a colon: <c>: TypeName</c> or <c>: TypeName[]</c>.</summary>
    private TypeHint ParseTypeHint()
    {
        Token typeName = Consume(TokenType.Identifier, "Expected type name after ':'.");
        if (Match(TokenType.LeftBracket))
        {
            Consume(TokenType.RightBracket, "Expected ']' after '[' in typed array type.");
            var span = new SourceSpan(typeName.Span.File, typeName.Span.StartLine, typeName.Span.StartColumn,
                Previous().Span.EndLine, Previous().Span.EndColumn);
            return new TypeHint(typeName, true, span);
        }
        return new TypeHint(typeName, false, typeName.Span);
    }

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
    private static bool IsDictKeyToken(TokenType type) =>
        type == TokenType.Identifier || (type >= TokenType.Let && type <= TokenType.Timeout);

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
    /// Consumes the current token if it is an identifier matching <paramref name="name"/>,
    /// otherwise throws a parse error with <paramref name="message"/>.
    /// Used for contextual keywords like <c>from</c> in import statements.
    /// </summary>
    private Token ConsumeIdentifier(string name, string message)
    {
        if (CheckIdentifier(name))
        {
            return Advance();
        }

        throw Error(Peek(), message);
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
                case TokenType.Interface:
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
