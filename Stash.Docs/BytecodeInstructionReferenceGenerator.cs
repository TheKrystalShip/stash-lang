namespace Stash.Docs;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Stash.Bytecode;

/// <summary>
/// Deterministic Markdown generator for the bytecode instruction-set reference.
/// The generator reads opcode names, numeric values, XML summaries, and category
/// comments from <c>Stash.Bytecode/Bytecode/OpCode.cs</c>, then combines them
/// with runtime metadata such as <see cref="OpCodeInfo"/> and the serialized
/// opcode table hash. The C# opcode metadata is the source of truth; generated
/// Markdown is always overwritten, and git is expected to report whether the
/// canonical output changed.
/// </summary>
public static class BytecodeInstructionReferenceGenerator
{
    private const string DocTitle = "Bytecode VM — Instruction Set Reference";

    public const string DefaultRelativePath = "docs/Bytecode VM — Instruction Set Reference.md";
    public const string DefaultSourceRelativePath = "Stash.Bytecode/Bytecode/OpCode.cs";

    private static readonly Regex CategoryRegex = new(@"^\s*//\s*===\s*(.+?)\s*===\s*$", RegexOptions.Compiled);
    private static readonly Regex MemberRegex = new(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>\d+),", RegexOptions.Compiled);

    public static string Generate(string opCodeSourcePath)
    {
        var opcodes = LoadOpcodeDocs(opCodeSourcePath);
        var byCategory = opcodes
            .GroupBy(o => o.Category, StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        EmitFrontMatter(sb, opcodes);
        EmitTableOfContents(sb, byCategory);
        EmitArchitecture(sb, opcodes);
        EmitEncoding(sb);
        EmitRegisterModel(sb);
        EmitNotation(sb);
        EmitInstructionReference(sb, byCategory);
        EmitCompanionWords(sb);
        EmitCompatibility(sb);
        EmitChangeRules(sb);

        return Normalize(sb.ToString());
    }

    public static void WriteTo(string absolutePath, string opCodeSourcePath)
    {
        string output = Generate(opCodeSourcePath);
        // Always overwrite. If opcode metadata has not changed, the bytes are
        // identical and git sees no diff; if it has changed, the source of truth
        // wins without a separate stale-doc comparison path.
        File.WriteAllText(absolutePath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void EmitFrontMatter(StringBuilder sb, IReadOnlyList<OpcodeDoc> opcodes)
    {
        sb.Append("# ").AppendLine(DocTitle);
        sb.AppendLine();
        sb.AppendLine("> Generated reference for the Stash bytecode VM instruction set. Opcode names, numeric values,");
        sb.AppendLine("> categories, and effects are extracted from `Stash.Bytecode/Bytecode/OpCode.cs`; encoding");
        sb.AppendLine("> formats and table hash metadata come from `Stash.Bytecode`. The generator always overwrites");
        sb.AppendLine("> this file from source metadata; do not edit it by hand. Run");
        sb.AppendLine("> `dotnet run --project Stash.Docs/ --bytecode` to regenerate after changing opcodes.");
        sb.AppendLine(">");
        sb.AppendLine("> **Companion documents:**");
        sb.AppendLine(">");
        sb.AppendLine("> - [Bytecode VM — Binary Format (.stashc)](Bytecode%20VM%20—%20Binary%20Format%20%28.stashc%29.md)");
        sb.AppendLine("> - [Language Specification](Stash%20—%20Language%20Specification.md)");
        sb.AppendLine("> - [DAP — Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md)");
        sb.AppendLine();
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("| --- | --- |");
        sb.Append("| Opcode count | `").Append(opcodes.Count).AppendLine("` |");
        sb.Append("| Numeric range | `0..").Append(opcodes.Max(o => o.Value)).AppendLine("` |");
        sb.Append("| Opcode table hash | `0x").Append(BytecodeWriter.ComputeOpCodeTableHash().ToString("X8")).AppendLine("` |");
        sb.AppendLine("| Instruction width | 32 bits |");
        sb.AppendLine("| Register index width | 8 bits |");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void EmitTableOfContents(StringBuilder sb, IReadOnlyList<IGrouping<string, OpcodeDoc>> categories)
    {
        sb.AppendLine("## Table of Contents");
        sb.AppendLine();
        sb.AppendLine("1. [Architecture Overview](#1-architecture-overview)");
        sb.AppendLine("2. [Instruction Encoding](#2-instruction-encoding)");
        sb.AppendLine("3. [Register Model](#3-register-model)");
        sb.AppendLine("4. [Notation Conventions](#4-notation-conventions)");
        sb.AppendLine("5. [Instruction Reference](#5-instruction-reference)");
        for (int i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            string heading = "5." + (i + 1) + " " + category.Key;
            sb.Append("   - [").Append(heading).Append("](#").Append(HeadingAnchor(heading)).AppendLine(")");
        }
        sb.AppendLine("6. [Companion Words](#6-companion-words)");
        sb.AppendLine("7. [Compatibility](#7-compatibility)");
        sb.AppendLine("8. [Change Rules](#8-change-rules)");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void EmitArchitecture(StringBuilder sb, IReadOnlyList<OpcodeDoc> opcodes)
    {
        sb.AppendLine("## 1. Architecture Overview");
        sb.AppendLine();
        sb.AppendLine("The Stash bytecode VM is a register-based virtual machine that executes fixed-width");
        sb.AppendLine("32-bit instruction words. A compiled chunk contains an instruction stream, constant pool,");
        sb.AppendLine("source maps, global metadata, closure metadata, and inline-cache metadata.");
        sb.AppendLine();
        sb.AppendLine("| Physical CPU concept | Stash VM equivalent |");
        sb.AppendLine("| --- | --- |");
        sb.Append("| Instruction set | `").Append(opcodes.Count).AppendLine("` opcodes |");
        sb.AppendLine("| Registers | Virtual registers `r0..rN` in the current call frame |");
        sb.AppendLine("| Machine code | `uint` instruction words |");
        sb.AppendLine("| Program counter | `IP` per call frame |");
        sb.AppendLine("| Call stack | `CallFrame[]` with base slot, instruction pointer, chunk, and closure state |");
        sb.AppendLine("| Constant memory | Per-chunk constant pool `K(i)` |");
        sb.AppendLine("| Inline caches | `ICSlot` entries used by selected field and built-in call opcodes |");
        sb.AppendLine();
        sb.AppendLine("The VM dispatch loop decodes an opcode from the low byte of each instruction word and");
        sb.AppendLine("dispatches to the matching handler. Some instructions consume companion words after the");
        sb.AppendLine("primary instruction; companion words are part of the instruction stream but do not contain");
        sb.AppendLine("an opcode in their low byte.");
        sb.AppendLine();
    }

    private static void EmitEncoding(StringBuilder sb)
    {
        sb.AppendLine("## 2. Instruction Encoding");
        sb.AppendLine();
        sb.AppendLine("All instructions use one 32-bit little-endian word in memory and on disk. The low byte");
        sb.AppendLine("always stores the opcode value.");
        sb.AppendLine();
        sb.AppendLine("| Format | Layout | Operand range | Typical use |");
        sb.AppendLine("| --- | --- | --- | --- |");
        sb.AppendLine("| `ABC` | `[op:8][A:8][B:8][C:8]` | `A`, `B`, `C`: `0..255` | Registers and small immediates |");
        sb.AppendLine("| `ABx` | `[op:8][A:8][Bx:16]` | `A`: `0..255`, `Bx`: `0..65535` | Constant pool and metadata indexes |");
        sb.Append("| `AsBx` | `[op:8][A:8][sBx:16]` | `sBx`: `")
          .Append(Instruction.SBxMin).Append("..").Append(Instruction.SBxMax)
          .AppendLine("` | Relative jumps and signed immediates |");
        sb.Append("| `Ax` | `[op:8][Ax:24]` | `Ax`: `0..").Append(Instruction.AxMax).AppendLine("` | Large payload without register operands |");
        sb.AppendLine();
        sb.Append("Signed `sBx` fields are bias-encoded with `").Append(Instruction.SBxBias).AppendLine("`.");
        sb.AppendLine("Encoding and decoding helpers live in `Instruction`.");
        sb.AppendLine();
    }

    private static void EmitRegisterModel(StringBuilder sb)
    {
        sb.AppendLine("## 3. Register Model");
        sb.AppendLine();
        sb.AppendLine("Each call frame owns a register window over the VM stack. `R(i)` means register `i` in");
        sb.AppendLine("the current frame, implemented as `stack[frame.BaseSlot + i]`.");
        sb.AppendLine();
        sb.AppendLine("- Function parameters and locals occupy low-numbered registers.");
        sb.AppendLine("- Temporary expression values occupy compiler-assigned registers above locals.");
        sb.AppendLine("- Function calls place the callee in `R(A)` and arguments in following registers.");
        sb.AppendLine("- Return values overwrite the caller's callee register.");
        sb.AppendLine();
    }

    private static void EmitNotation(StringBuilder sb)
    {
        sb.AppendLine("## 4. Notation Conventions");
        sb.AppendLine();
        sb.AppendLine("| Notation | Meaning |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine("| `R(A)` | Register `A` in the current frame |");
        sb.AppendLine("| `K(i)` | Constant pool entry `i` |");
        sb.AppendLine("| `G(i)` | Global slot `i` |");
        sb.AppendLine("| `UV(i)` | Upvalue `i` in the current closure |");
        sb.AppendLine("| `IP` | Instruction pointer, measured in instruction words |");
        sb.AppendLine("| companion word | Extra `uint` consumed after an opcode word |");
        sb.AppendLine();
    }

    private static void EmitInstructionReference(StringBuilder sb, IReadOnlyList<IGrouping<string, OpcodeDoc>> categories)
    {
        sb.AppendLine("## 5. Instruction Reference");
        sb.AppendLine();
        sb.AppendLine("This section is generated from the `OpCode` enum. The **Encoding** column is the");
        sb.AppendLine("VM's decoded operand format. The **Operands** column is the operand shape documented");
        sb.AppendLine("on the opcode itself, including companion-word notes where applicable.");
        sb.AppendLine();

        int index = 1;
        foreach (var category in categories)
        {
            sb.Append("### 5.").Append(index++).Append(' ').AppendLine(category.Key);
            sb.AppendLine();
            sb.AppendLine("| Value | Opcode | Encoding | Operands | Effect |");
            sb.AppendLine("| ---: | --- | --- | --- | --- |");
            foreach (var op in category)
            {
                sb.Append("| `").Append(op.Value).Append("` | `").Append(op.Name).Append("` | `")
                  .Append(op.Encoding).Append("` | ")
                  .Append(FormatOperandCell(op.OperandShape)).Append(" | ")
                  .Append(EscapeTableCell(op.Effect)).AppendLine(" |");
            }
            sb.AppendLine();
        }
    }

    private static void EmitCompanionWords(StringBuilder sb)
    {
        sb.AppendLine("## 6. Companion Words");
        sb.AppendLine();
        sb.AppendLine("Most opcodes consume exactly one instruction word. The following opcodes consume");
        sb.AppendLine("additional companion words immediately after the primary opcode word:");
        sb.AppendLine();
        sb.AppendLine("| Opcode | Companion contract |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine("| `Closure` | One companion word per upvalue descriptor in the target function prototype. |");
        sb.AppendLine("| `PipeChain` | `B` companion words, one per pipeline stage. Bits `15..8` store part count; bits `7..0` store stage flags. |");
        sb.AppendLine("| `StreamingPipeline` | `B` companion words, one per pipeline stage. Bits `15..8` store part count; bit `0x01` marks strict mode on the last stage. |");
        sb.AppendLine("| `GetFieldIC` | One companion word storing the inline-cache slot index. |");
        sb.AppendLine("| `CallBuiltIn` | One companion word storing the inline-cache slot index. |");
        sb.AppendLine();
        sb.AppendLine("Companion words are serialized in the code array and count toward bytecode offsets.");
        sb.AppendLine();
    }

    private static void EmitCompatibility(StringBuilder sb)
    {
        sb.AppendLine("## 7. Compatibility");
        sb.AppendLine();
        sb.AppendLine("Serialized `.stashc` files store an opcode table hash in the binary header. The hash");
        sb.AppendLine("is computed from opcode names and numeric values. A reader rejects bytecode when its");
        sb.AppendLine("computed hash does not match the file header.");
        sb.AppendLine();
        sb.AppendLine("Changing an opcode name, numeric value, or order is therefore a bytecode compatibility");
        sb.AppendLine("change. Adding an opcode also changes the hash and requires regenerating this document.");
        sb.AppendLine();
    }

    private static void EmitChangeRules(StringBuilder sb)
    {
        sb.AppendLine("## 8. Change Rules");
        sb.AppendLine();
        sb.AppendLine("When changing the instruction set:");
        sb.AppendLine();
        sb.AppendLine("- Add or update the opcode in `Stash.Bytecode/Bytecode/OpCode.cs`.");
        sb.AppendLine("- Keep the XML summary in the form `FORMAT: effect`, for example `ABC: R(A) = R(B) + R(C)`.");
        sb.AppendLine("- Place the opcode under the correct `// === Category ===` comment.");
        sb.AppendLine("- Update `OpCodeInfo.GetFormat` when the encoded operand format is not the default `ABC`.");
        sb.AppendLine("- Update verifier, disassembler, optimizer, serializer, and VM dispatch behavior as required.");
        sb.AppendLine("- Run `dotnet run --project Stash.Docs/ --bytecode` and commit the regenerated Markdown.");
        sb.AppendLine("- Add or update tests for execution, verification, disassembly, and generated documentation.");
        sb.AppendLine();
    }

    private static IReadOnlyList<OpcodeDoc> LoadOpcodeDocs(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Opcode source file not found.", sourcePath);

        var declared = ParseSource(sourcePath);
        var enumValues = Enum.GetValues<OpCode>();
        var result = new List<OpcodeDoc>(enumValues.Length);

        foreach (var op in enumValues)
        {
            string name = op.ToString();
            if (!declared.TryGetValue(name, out var sourceDoc))
                throw new InvalidOperationException("Missing XML summary metadata for opcode " + name + ".");

            var (operandShape, effect) = SplitSummary(sourceDoc.Summary);
            string encoding = OpCodeInfo.GetFormat(op).ToString();
            ValidateOperandShape(name, encoding, operandShape);
            result.Add(new OpcodeDoc(
                name,
                (byte)op,
                sourceDoc.Category,
                encoding,
                operandShape,
                effect));
        }

        return result.OrderBy(o => o.Value).ToArray();
    }

    private static Dictionary<string, SourceOpcodeDoc> ParseSource(string sourcePath)
    {
        var result = new Dictionary<string, SourceOpcodeDoc>(StringComparer.Ordinal);
        string category = "Uncategorized";
        var summaryLines = new List<string>();
        bool inSummary = false;

        foreach (string rawLine in File.ReadLines(sourcePath))
        {
            var categoryMatch = CategoryRegex.Match(rawLine);
            if (categoryMatch.Success)
            {
                category = categoryMatch.Groups[1].Value.Trim();
                summaryLines.Clear();
                continue;
            }

            string line = rawLine.Trim();
            if (line.StartsWith("///", StringComparison.Ordinal))
            {
                string xml = line.Substring(3).Trim();
                if (xml.Contains("<summary>", StringComparison.Ordinal))
                {
                    inSummary = true;
                    xml = xml.Replace("<summary>", "", StringComparison.Ordinal);
                }

                if (inSummary)
                {
                    bool closes = xml.Contains("</summary>", StringComparison.Ordinal);
                    xml = xml.Replace("</summary>", "", StringComparison.Ordinal);
                    if (!string.IsNullOrWhiteSpace(xml))
                        summaryLines.Add(xml.Trim());
                    if (closes)
                        inSummary = false;
                }
                continue;
            }

            var memberMatch = MemberRegex.Match(rawLine);
            if (!memberMatch.Success)
            {
                if (line.Length > 0)
                    summaryLines.Clear();
                continue;
            }

            string name = memberMatch.Groups["name"].Value;
            string summary = CleanXmlSummary(string.Join(" ", summaryLines));
            if (summary.Length == 0)
                throw new InvalidOperationException("Opcode " + name + " is missing an XML summary.");

            result[name] = new SourceOpcodeDoc(category, summary);
            summaryLines.Clear();
            continue;
        }

        return result;
    }

    private static (string OperandShape, string Effect) SplitSummary(string summary)
    {
        int colon = summary.IndexOf(':');
        if (colon <= 0) return ("`ABC`", summary);

        string prefix = summary.Substring(0, colon).Trim();
        string effect = summary.Substring(colon + 1).Trim();
        return ("`" + prefix + "`", effect);
    }

    private static void ValidateOperandShape(string name, string encoding, string operandShape)
    {
        string token = operandShape.Trim('`');
        int plus = token.IndexOf('+');
        if (plus >= 0) token = token.Substring(0, plus).Trim();
        int space = token.IndexOf(' ');
        if (space >= 0) token = token.Substring(0, space).Trim();

        if (token is "ABC" or "ABx" or "AsBx" or "Ax")
        {
            if (!string.Equals(token, encoding, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Opcode " + name + " documents operand format " + token +
                    " but OpCodeInfo.GetFormat returns " + encoding + ".");
            }
        }
    }

    private static string CleanXmlSummary(string summary)
    {
        string decoded = WebUtility.HtmlDecode(summary);
        decoded = Regex.Replace(decoded, @"<see\s+cref=""[^""]+""\s*/>", "", RegexOptions.CultureInvariant);
        decoded = Regex.Replace(decoded, @"\s+", " ", RegexOptions.CultureInvariant);
        return decoded.Trim();
    }

    private static string FormatOperandCell(string operandShape)
    {
        if (string.IsNullOrEmpty(operandShape)) return "—";
        return EscapeTableCell(operandShape);
    }

    private static string EscapeTableCell(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
    }

    private static string HeadingAnchor(string heading)
    {
        var sb = new StringBuilder(heading.Length);
        foreach (char ch in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == ' ') sb.Append('-');
            else if (ch == '-') sb.Append('-');
        }
        return sb.ToString();
    }

    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i].TrimEnd());
            if (i < lines.Length - 1) sb.Append('\n');
        }
        string result = sb.ToString();
        while (result.EndsWith("\n\n", StringComparison.Ordinal))
            result = result.Substring(0, result.Length - 1);
        if (!result.EndsWith("\n", StringComparison.Ordinal))
            result += "\n";
        return result;
    }

    private sealed record SourceOpcodeDoc(string Category, string Summary);

    private sealed record OpcodeDoc(
        string Name,
        int Value,
        string Category,
        string Encoding,
        string OperandShape,
        string Effect);
}
