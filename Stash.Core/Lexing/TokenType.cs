namespace Stash.Lexing;

/// <summary>
/// Classifies every possible token type recognized by the Stash <see cref="Lexer"/>.
/// </summary>
/// <remarks>
/// Token types are grouped into four categories: single-character tokens, two-character
/// (compound) tokens, literal values, keywords, and special tokens. The parser uses these
/// to drive its recursive-descent logic without inspecting raw lexeme strings.
/// </remarks>
public enum TokenType
{
    // ── Single-character tokens ──────────────────────────────────────

    /// <summary>The <c>(</c> character. Opens a grouped expression, function parameter list, or call argument list.</summary>
    LeftParen,

    /// <summary>The <c>)</c> character. Closes a grouped expression, function parameter list, or call argument list.</summary>
    RightParen,

    /// <summary>The <c>{</c> character. Opens a block body (function, if/else, loop, struct).</summary>
    LeftBrace,

    /// <summary>The <c>}</c> character. Closes a block body.</summary>
    RightBrace,

    /// <summary>The <c>[</c> character. Opens an array literal or index/subscript expression.</summary>
    LeftBracket,

    /// <summary>The <c>]</c> character. Closes an array literal or index/subscript expression.</summary>
    RightBracket,

    /// <summary>The <c>,</c> character. Separates items in argument lists, parameter lists, and array literals.</summary>
    Comma,

    /// <summary>The <c>.</c> character. Used for member access (e.g. <c>obj.field</c>).</summary>
    Dot,

    /// <summary>The <c>..</c> operator. Used to construct range expressions (e.g. <c>0..10</c> or <c>0..20..2</c>).</summary>
    DotDot,

    /// <summary>The <c>;</c> character. Terminates statements.</summary>
    Semicolon,

    /// <summary>The <c>+</c> character. Used for numeric addition and string concatenation.</summary>
    Plus,

    /// <summary>The <c>-</c> character. Used for numeric subtraction and unary negation.</summary>
    Minus,

    /// <summary>The <c>*</c> character. Used for numeric multiplication.</summary>
    Star,

    /// <summary>The <c>/</c> character. Used for numeric division. Also begins comment sequences (<c>//</c> and <c>/*</c>).</summary>
    Slash,

    /// <summary>The <c>%</c> character. Used for the modulo (remainder) operator.</summary>
    Percent,

    /// <summary>The <c>!</c> character. Used as the logical NOT unary operator.</summary>
    Bang,

    /// <summary>The <c>&lt;</c> character. Used as the less-than comparison operator.</summary>
    Less,

    /// <summary>The <c>&gt;</c> character. Used as the greater-than comparison operator.</summary>
    Greater,

    /// <summary>The <c>:</c> character. Used as a separator in ternary expressions and other constructs.</summary>
    Colon,

    /// <summary>The <c>?</c> character. Used in ternary conditional expressions (<c>cond ? a : b</c>).</summary>
    QuestionMark,

    /// <summary>The <c>|</c> character. Reserved for future use (e.g. pipeline or bitwise OR).</summary>
    Pipe,

    // ── Two-character tokens ─────────────────────────────────────────

    /// <summary>The <c>==</c> operator. Tests structural equality between two values.</summary>
    EqualEqual,

    /// <summary>The <c>!=</c> operator. Tests structural inequality between two values.</summary>
    BangEqual,

    /// <summary>The <c>&lt;=</c> operator. Tests whether the left operand is less than or equal to the right.</summary>
    LessEqual,

    /// <summary>The <c>&gt;=</c> operator. Tests whether the left operand is greater than or equal to the right.</summary>
    GreaterEqual,

    /// <summary>The <c>&amp;&amp;</c> operator. Short-circuit logical AND.</summary>
    AmpersandAmpersand,

    /// <summary>The <c>||</c> operator. Short-circuit logical OR.</summary>
    PipePipe,

    /// <summary>The <c>??</c> operator. Null-coalescing — returns the left operand if non-null, otherwise the right.</summary>
    QuestionQuestion,

    /// <summary>The <c>?.</c> operator. Optional chaining — short-circuits to null if the left operand is null.</summary>
    QuestionDot,

    /// <summary>The <c>++</c> operator. Increment operator for numeric values.</summary>
    PlusPlus,

    /// <summary>The <c>--</c> operator. Decrement operator for numeric values.</summary>
    MinusMinus,

    /// <summary>The <c>-&gt;</c> operator. Used for function return type annotations.</summary>
    Arrow,

    /// <summary>The <c>=&gt;</c> operator. Used for lambda/arrow function expressions.</summary>
    FatArrow,

    /// <summary>The <c>&gt;&gt;</c> operator. Output redirection — appends stdout to a file.</summary>
    GreaterGreater,

    /// <summary>The <c>2&gt;</c> operator. Output redirection — writes stderr to a file (overwrite).</summary>
    TwoGreater,

    /// <summary>The <c>2&gt;&gt;</c> operator. Output redirection — appends stderr to a file.</summary>
    TwoGreaterGreater,

    /// <summary>The <c>&amp;&gt;</c> operator. Output redirection — writes both stdout and stderr to a file (overwrite).</summary>
    AmpersandGreater,

    /// <summary>The <c>&amp;&gt;&gt;</c> operator. Output redirection — appends both stdout and stderr to a file.</summary>
    AmpersandGreaterGreater,

    /// <summary>The <c>=</c> operator. Assignment — binds a value to a variable.</summary>
    Equal,

    /// <summary>The <c>+=</c> operator. Compound addition assignment.</summary>
    PlusEqual,

    /// <summary>The <c>-=</c> operator. Compound subtraction assignment.</summary>
    MinusEqual,

    /// <summary>The <c>*=</c> operator. Compound multiplication assignment.</summary>
    StarEqual,

    /// <summary>The <c>/=</c> operator. Compound division assignment.</summary>
    SlashEqual,

    /// <summary>The <c>%=</c> operator. Compound modulo assignment.</summary>
    PercentEqual,

    /// <summary>The <c>??=</c> operator. Null-coalescing assignment — assigns only if the target is null.</summary>
    QuestionQuestionEqual,

    // ── Literals ─────────────────────────────────────────────────────

    /// <summary>An integer literal (e.g. <c>42</c>, <c>0</c>). The <see cref="Token.Literal"/> value is a <see cref="long"/>.</summary>
    IntegerLiteral,

    /// <summary>A floating-point literal (e.g. <c>3.14</c>). The <see cref="Token.Literal"/> value is a <see cref="double"/>.</summary>
    FloatLiteral,

    /// <summary>A double-quoted string literal (e.g. <c>"hello"</c>). The <see cref="Token.Literal"/> value is the unescaped <see cref="string"/> content.</summary>
    StringLiteral,

    /// <summary>A string literal containing interpolated expressions. Reserved for future implementation.</summary>
    InterpolatedString,

    /// <summary>A command literal for shell execution. Reserved for future implementation.</summary>
    CommandLiteral,

    // ── Keywords ─────────────────────────────────────────────────────

    /// <summary>The <c>let</c> keyword. Declares a mutable variable binding.</summary>
    Let,

    /// <summary>The <c>const</c> keyword. Declares an immutable variable binding.</summary>
    Const,

    /// <summary>The <c>fn</c> keyword. Declares a function.</summary>
    Fn,

    /// <summary>The <c>struct</c> keyword. Declares a struct type.</summary>
    Struct,

    /// <summary>The <c>enum</c> keyword. Declares an enum type.</summary>
    Enum,

    /// <summary>The <c>if</c> keyword. Begins a conditional branch.</summary>
    If,

    /// <summary>The <c>else</c> keyword. Begins the alternate branch of an <c>if</c> statement.</summary>
    Else,

    /// <summary>The <c>for</c> keyword. Begins a for-in loop over an iterable.</summary>
    For,

    /// <summary>The <c>in</c> keyword. Separates the loop variable from the iterable in a <c>for</c> loop.</summary>
    In,

    /// <summary>The <c>while</c> keyword. Begins a while loop that repeats as long as its condition is truthy.</summary>
    While,

    /// <summary>The <c>return</c> keyword. Exits a function and optionally yields a value to the caller.</summary>
    Return,

    /// <summary>The <c>break</c> keyword. Exits the innermost enclosing loop.</summary>
    Break,

    /// <summary>The <c>continue</c> keyword. Skips the rest of the current loop iteration and proceeds to the next.</summary>
    Continue,

    /// <summary>The <c>true</c> keyword. Boolean literal whose <see cref="Token.Literal"/> value is <see langword="true"/>.</summary>
    True,

    /// <summary>The <c>false</c> keyword. Boolean literal whose <see cref="Token.Literal"/> value is <see langword="false"/>.</summary>
    False,

    /// <summary>The <c>null</c> keyword. Represents the absence of a value.</summary>
    Null,

    /// <summary>The <c>try</c> keyword. Begins a try/catch error-handling block.</summary>
    Try,

    /// <summary>The <c>import</c> keyword. Imports bindings from another module.</summary>
    Import,

    /// <summary>The <c>from</c> keyword. Specifies the module path in an import statement.</summary>
    From,

    /// <summary>The <c>as</c> keyword. Aliases an import to a namespace name.</summary>
    As,

    /// <summary>The <c>switch</c> keyword. Begins a switch expression (<c>subject switch { pattern => result }</c>).</summary>
    Switch,

    // ── Special ──────────────────────────────────────────────────────

    /// <summary>A user-defined identifier (variable name, function name, type name, etc.).</summary>
    Identifier,

    /// <summary>A synthetic token appended at the end of the token stream to signal that all input has been consumed.</summary>
    Eof,

    /// <summary>The <c>$</c> character. Used as a prefix for string interpolation expressions.</summary>
    Dollar,

    // ── Trivia (preserved only when requested) ───────────────────────

    /// <summary>A single-line comment starting with <c>//</c>. Only emitted when trivia preservation is enabled.</summary>
    SingleLineComment,

    /// <summary>A block comment delimited by <c>/* ... */</c>. Only emitted when trivia preservation is enabled.</summary>
    BlockComment,

    /// <summary>A Unix shebang line (<c>#!/usr/bin/env stash</c>). Only emitted when trivia preservation is enabled.</summary>
    Shebang,
}
