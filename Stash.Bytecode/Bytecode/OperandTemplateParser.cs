using System;
using System.Collections.Generic;

namespace Stash.Bytecode;

// ─── Token kinds ─────────────────────────────────────────────────────────────

/// <summary>Kinds of operand tokens recognised by the template DSL.</summary>
internal enum OperandTokenKind
{
    /// <summary>Register operand — rendered as <c>r{N}</c>.</summary>
    RegA,
    RegB,
    RegC,

    /// <summary>Constant-pool reference rendered as <c>k{N}</c> with a <c>FormatConstant</c> comment.</summary>
    KBx,
    KC,
    KB,

    /// <summary>Constant-pool reference rendered as <c>k{N}</c> with NO comment (metadata constants).</summary>
    KNBx,
    KNC,

    /// <summary>Global-slot reference rendered as <c>[g{N}]</c> with name annotation.</summary>
    GBx,
    GAx,

    /// <summary>Upvalue reference rendered as <c>[uv{N}]</c> with name annotation.</summary>
    UVB,

    /// <summary>Field constant rendered as <c>k{N}</c> with <c>.fieldName</c> comment.</summary>
    FC,
    FB,

    /// <summary>Label reference resolved via <c>CollectLabels</c> for the <c>sBx</c> offset.</summary>
    LSBx,

    /// <summary>Raw integer operand — no comment side-effect.</summary>
    IC,
    IB,
    ISBx,

    /// <summary>Literal <c>", "</c> separator between rendered operands.</summary>
    Separator,
}

/// <summary>A single parsed token from an operand-shape template string.</summary>
internal readonly struct OperandToken
{
    public OperandTokenKind Kind { get; }

    public OperandToken(OperandTokenKind kind) => Kind = kind;
}

// ─── Parser ──────────────────────────────────────────────────────────────────

/// <summary>
/// Parses operand-shape template strings (from <see cref="OpCodeAttribute.Operands"/>) into
/// an array of <see cref="OperandToken"/> values that <see cref="OperandTemplateRenderer"/>
/// evaluates at disassembly time.
///
/// <para>Grammar (EBNF):</para>
/// <code>
/// template  ::= item ( ", " item )*
/// item      ::= R(A) | R(B) | R(C)
///             | K(Bx) | K(C) | K(B)
///             | KN(Bx) | KN(C)
///             | G(Bx) | G(Ax)
///             | UV(B)
///             | F(C) | F(B)
///             | L(sBx)
///             | I(C) | I(B) | I(sBx)
/// </code>
///
/// Parsing is done once per opcode at type-init time; errors throw immediately
/// rather than silently producing wrong output.
/// </summary>
internal static class OperandTemplateParser
{
    // Sigil constants — never inline these strings.
    private const string SigilR    = "R";
    private const string SigilK    = "K";
    private const string SigilKN   = "KN";
    private const string SigilG    = "G";
    private const string SigilUV   = "UV";
    private const string SigilF    = "F";
    private const string SigilL    = "L";
    private const string SigilI    = "I";

    private const string ArgA    = "A";
    private const string ArgB    = "B";
    private const string ArgC    = "C";
    private const string ArgBx   = "Bx";
    private const string ArgAx   = "Ax";
    private const string ArgsBx  = "sBx";

    /// <summary>
    /// Parse a template string into an immutable token array.
    /// Returns an empty array for <see cref="OperandTemplate.Empty"/>.
    /// Throws for <see cref="OperandTemplate.Bespoke"/> — callers must not attempt to parse it.
    /// </summary>
    public static OperandToken[] Parse(string template)
    {
        if (template.Length == 0)
            return Array.Empty<OperandToken>();

        var tokens = new List<OperandToken>();
        ReadOnlySpan<char> span = template.AsSpan();
        bool first = true;

        while (span.Length > 0)
        {
            if (!first)
            {
                // Expect ", " separator
                if (span.Length < 2 || span[0] != ',' || span[1] != ' ')
                    throw new FormatException($"Expected ', ' separator in operand template '{template}' at position {template.Length - span.Length}");
                tokens.Add(new OperandToken(OperandTokenKind.Separator));
                span = span[2..];
            }
            first = false;

            OperandToken tok = ParseItem(ref span, template);
            tokens.Add(tok);
        }

        return tokens.ToArray();
    }

    private static OperandToken ParseItem(ref ReadOnlySpan<char> span, string fullTemplate)
    {
        // Consume sigil
        if (TryConsume(ref span, SigilKN))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgBx  => new OperandToken(OperandTokenKind.KNBx),
                ArgC   => new OperandToken(OperandTokenKind.KNC),
                _      => throw new FormatException($"Unknown KN arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilUV))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgB   => new OperandToken(OperandTokenKind.UVB),
                _      => throw new FormatException($"Unknown UV arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilR))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgA   => new OperandToken(OperandTokenKind.RegA),
                ArgB   => new OperandToken(OperandTokenKind.RegB),
                ArgC   => new OperandToken(OperandTokenKind.RegC),
                _      => throw new FormatException($"Unknown R arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilK))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgBx  => new OperandToken(OperandTokenKind.KBx),
                ArgC   => new OperandToken(OperandTokenKind.KC),
                ArgB   => new OperandToken(OperandTokenKind.KB),
                _      => throw new FormatException($"Unknown K arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilG))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgBx  => new OperandToken(OperandTokenKind.GBx),
                ArgAx  => new OperandToken(OperandTokenKind.GAx),
                _      => throw new FormatException($"Unknown G arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilF))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgC   => new OperandToken(OperandTokenKind.FC),
                ArgB   => new OperandToken(OperandTokenKind.FB),
                _      => throw new FormatException($"Unknown F arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilL))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgsBx => new OperandToken(OperandTokenKind.LSBx),
                _      => throw new FormatException($"Unknown L arg '{arg}' in '{fullTemplate}'"),
            };
        }
        if (TryConsume(ref span, SigilI))
        {
            string arg = ConsumeArg(ref span, fullTemplate);
            return arg switch
            {
                ArgC   => new OperandToken(OperandTokenKind.IC),
                ArgB   => new OperandToken(OperandTokenKind.IB),
                ArgsBx => new OperandToken(OperandTokenKind.ISBx),
                _      => throw new FormatException($"Unknown I arg '{arg}' in '{fullTemplate}'"),
            };
        }

        throw new FormatException($"Unrecognised token in operand template '{fullTemplate}' at: '{span}'");
    }

    private static bool TryConsume(ref ReadOnlySpan<char> span, string prefix)
    {
        if (span.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
        {
            span = span[prefix.Length..];
            return true;
        }
        return false;
    }

    /// <summary>Consume the <c>(arg)</c> parenthesised argument portion.</summary>
    private static string ConsumeArg(ref ReadOnlySpan<char> span, string fullTemplate)
    {
        if (span.IsEmpty || span[0] != '(')
            throw new FormatException($"Expected '(' in operand template '{fullTemplate}'");
        span = span[1..];

        int close = span.IndexOf(')');
        if (close < 0)
            throw new FormatException($"Missing ')' in operand template '{fullTemplate}'");

        string arg = span[..close].ToString();
        span = span[(close + 1)..];
        return arg;
    }
}

// ─── Renderer ────────────────────────────────────────────────────────────────

/// <summary>
/// Evaluates a parsed token array against the current instruction word and chunk,
/// producing an <c>(operands, comment?)</c> pair matching the legacy
/// <c>FormatInstruction</c> contract.
/// </summary>
internal static class OperandTemplateRenderer
{
    /// <summary>
    /// Render a pre-parsed token sequence into the <c>(operands, comment?)</c> pair
    /// used by <see cref="Disassembler"/>.
    /// </summary>
    public static (string operands, string? comment) Render(
        OperandToken[] tokens,
        Chunk chunk,
        Dictionary<int, string> labels,
        int idx,
        uint word)
    {
        byte   a   = Instruction.GetA(word);
        byte   b   = Instruction.GetB(word);
        byte   c   = Instruction.GetC(word);
        ushort bx  = Instruction.GetBx(word);
        int    sbx = Instruction.GetSBx(word);

        var opBuilder = new System.Text.StringBuilder();
        var comments  = new List<string>();

        foreach (OperandToken tok in tokens)
        {
            switch (tok.Kind)
            {
                case OperandTokenKind.Separator:
                    opBuilder.Append(", ");
                    break;

                case OperandTokenKind.RegA:  opBuilder.Append('r'); opBuilder.Append(a);   break;
                case OperandTokenKind.RegB:  opBuilder.Append('r'); opBuilder.Append(b);   break;
                case OperandTokenKind.RegC:  opBuilder.Append('r'); opBuilder.Append(c);   break;

                case OperandTokenKind.KBx:
                    opBuilder.Append('k'); opBuilder.Append(bx);
                    if (bx < chunk.Constants.Length)
                        comments.Add(Disassembler.FormatConstantPublic(chunk.Constants[bx]));
                    break;

                case OperandTokenKind.KC:
                    opBuilder.Append('k'); opBuilder.Append(c);
                    if (c < chunk.Constants.Length)
                        comments.Add(Disassembler.FormatConstantPublic(chunk.Constants[c]));
                    break;

                case OperandTokenKind.KB:
                    opBuilder.Append('k'); opBuilder.Append(b);
                    if (b < chunk.Constants.Length)
                        comments.Add(Disassembler.FormatConstantPublic(chunk.Constants[b]));
                    break;

                // KN variants — render constant index but emit NO comment.
                case OperandTokenKind.KNBx:
                    opBuilder.Append('k'); opBuilder.Append(bx);
                    break;

                case OperandTokenKind.KNC:
                    opBuilder.Append('k'); opBuilder.Append(c);
                    break;

                case OperandTokenKind.GBx:
                    opBuilder.Append("[g"); opBuilder.Append(bx); opBuilder.Append(']');
                    comments.Add(Disassembler.FormatGlobalPublic(chunk, bx));
                    break;

                case OperandTokenKind.GAx:
                {
                    uint ax = Instruction.GetAx(word);
                    opBuilder.Append("[g"); opBuilder.Append(ax); opBuilder.Append(']');
                    comments.Add(Disassembler.FormatGlobalPublic(chunk, (ushort)ax));
                    break;
                }

                case OperandTokenKind.UVB:
                    opBuilder.Append("[uv"); opBuilder.Append(b); opBuilder.Append(']');
                    comments.Add(Disassembler.GetUpvalueNamePublic(chunk, b));
                    break;

                case OperandTokenKind.FC:
                    opBuilder.Append('k'); opBuilder.Append(c);
                    comments.Add('.' + Disassembler.FormatFieldNamePublic(chunk, c));
                    break;

                case OperandTokenKind.FB:
                    opBuilder.Append('k'); opBuilder.Append(b);
                    comments.Add('.' + Disassembler.FormatFieldNamePublic(chunk, b));
                    break;

                case OperandTokenKind.LSBx:
                    opBuilder.Append(Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx));
                    comments.Add($"{sbx:+0;-0}");
                    break;

                case OperandTokenKind.IC:  opBuilder.Append(c);   break;
                case OperandTokenKind.IB:  opBuilder.Append(b);   break;
                case OperandTokenKind.ISBx: opBuilder.Append(sbx); break;

                default:
                    throw new InvalidOperationException($"Unhandled OperandTokenKind {tok.Kind}");
            }
        }

        string? comment = comments.Count == 0 ? null : string.Join("; ", comments);
        return (opBuilder.ToString(), comment);
    }
}

// ─── Parsed-template cache ────────────────────────────────────────────────────

/// <summary>
/// Per-opcode cache of parsed <see cref="OperandToken"/> arrays, built once at type-init.
/// </summary>
internal static class OperandTemplateCache
{
    private static readonly OperandToken[]?[] _cache;

    static OperandTemplateCache()
    {
        _cache = new OperandToken[]?[256];
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            string template = OpCodeMetadata.GetOperandTemplate(op);
            if (template == OperandTemplate.Bespoke || template == OperandTemplate.Empty)
                continue;
            _cache[(byte)op] = OperandTemplateParser.Parse(template);
        }
    }

    /// <summary>
    /// Returns the parsed tokens for the given opcode, or <c>null</c> if the opcode
    /// is bespoke / empty (caller must dispatch differently).
    /// </summary>
    public static OperandToken[]? Get(OpCode op)
        => _cache[(byte)op];
}
