import * as vscode from "vscode";
import { symbolKindToString, toWorkspaceRelativePath } from "./types";

const MAX_SYMBOLS = 100;
const MAX_CALL_HIERARCHY = 50;

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

export function extractMarkdownContent(
    content: vscode.MarkdownString | string | { language: string; value: string }
): string {
    if (typeof content === "string") {
        return content;
    }
    if (content instanceof vscode.MarkdownString) {
        return content.value;
    }
    // { language, value } code-fenced object
    return `\`\`\`${content.language}\n${content.value}\n\`\`\``;
}

// ---------------------------------------------------------------------------
// 1. Hover
// ---------------------------------------------------------------------------

export function formatHover(hovers: vscode.Hover[], symbol: string): string {
    if (!hovers || hovers.length === 0) {
        return `No hover information available for '${symbol}'.`;
    }

    const parts: string[] = [];
    for (const hover of hovers) {
        for (const content of hover.contents) {
            const text = extractMarkdownContent(
                content as vscode.MarkdownString | string | { language: string; value: string }
            );
            if (text.trim()) {
                parts.push(text.trim());
            }
        }
    }

    if (parts.length === 0) {
        return `No hover information available for '${symbol}'.`;
    }

    return `## Hover: \`${symbol}\`\n\n${parts.join("\n\n")}`;
}

// ---------------------------------------------------------------------------
// 2. Document Symbols
// ---------------------------------------------------------------------------

function renderDocumentSymbol(
    symbol: vscode.DocumentSymbol,
    kindFilter: Set<vscode.SymbolKind> | undefined,
    indent: string,
    collected: string[],
    count: { value: number }
): void {
    const include = !kindFilter || kindFilter.has(symbol.kind);
    if (include) {
        if (count.value >= MAX_SYMBOLS) {
            return;
        }
        const kind = symbolKindToString(symbol.kind);
        const detail = symbol.detail ? `: ${symbol.detail}` : "";
        const startLine = symbol.range.start.line + 1;
        const endLine = symbol.range.end.line + 1;
        const lineRange = startLine === endLine ? `line ${startLine}` : `line ${startLine}-${endLine}`;
        collected.push(`${indent}${kind} ${symbol.name}${detail} — ${lineRange}`);
        count.value++;
    }

    for (const child of symbol.children) {
        renderDocumentSymbol(child, kindFilter, indent + "  ", collected, count);
    }
}

function countSymbols(syms: vscode.DocumentSymbol[], kindFilter?: Set<vscode.SymbolKind>): number {
    let count = 0;
    for (const s of syms) {
        if (!kindFilter || kindFilter.has(s.kind)) {
            count++;
        }
        count += countSymbols(s.children, kindFilter);
    }
    return count;
}

export function formatDocumentSymbols(
    symbols: vscode.DocumentSymbol[],
    filePath: string,
    kindFilter?: Set<vscode.SymbolKind>
): string {
    if (!symbols || symbols.length === 0) {
        return `No symbols found in '${filePath}'.`;
    }

    const filename = filePath.split("/").pop() ?? filePath;
    const totalCount = countSymbols(symbols, kindFilter);
    const lines: string[] = [];
    const count = { value: 0 };

    for (const sym of symbols) {
        renderDocumentSymbol(sym, kindFilter, "  ", lines, count);
        if (count.value >= MAX_SYMBOLS) {
            break;
        }
    }

    const header = `## Symbols in ${filename} (${totalCount} symbols)`;

    if (totalCount > MAX_SYMBOLS) {
        lines.push(`  ...(+${totalCount - MAX_SYMBOLS} more)`);
    }

    return `${header}\n\n${lines.join("\n")}`;
}

// ---------------------------------------------------------------------------
// 3. Call Hierarchy
// ---------------------------------------------------------------------------

export function formatCallHierarchy(
    items: {
        item: vscode.CallHierarchyItem;
        incoming?: vscode.CallHierarchyIncomingCall[];
        outgoing?: vscode.CallHierarchyOutgoingCall[];
    }[],
    symbol: string
): string {
    if (!items || items.length === 0) {
        return `No call hierarchy information available for '${symbol}'.`;
    }

    const lines: string[] = [`## Call Hierarchy: \`${symbol}\``];

    for (const entry of items) {
        const incomingCalls = entry.incoming ?? [];
        const outgoingCalls = entry.outgoing ?? [];

        lines.push(`\n### Incoming (who calls ${symbol}):`);
        if (incomingCalls.length === 0) {
            lines.push("  (none)");
        } else {
            const slice = incomingCalls.slice(0, MAX_CALL_HIERARCHY);
            for (const call of slice) {
                const file = toWorkspaceRelativePath(call.from.uri);
                const line = call.fromRanges[0]?.start.line != null
                    ? call.fromRanges[0].start.line + 1
                    : call.from.range.start.line + 1;
                lines.push(`  ${call.from.name}() — ${file}:${line}`);
            }
            if (incomingCalls.length > MAX_CALL_HIERARCHY) {
                lines.push(`  ...(+${incomingCalls.length - MAX_CALL_HIERARCHY} more)`);
            }
        }

        lines.push(`\n### Outgoing (what ${symbol} calls):`);
        if (outgoingCalls.length === 0) {
            lines.push("  (none)");
        } else {
            const slice = outgoingCalls.slice(0, MAX_CALL_HIERARCHY);
            for (const call of slice) {
                const file = toWorkspaceRelativePath(call.to.uri);
                const line = call.to.range.start.line + 1;
                lines.push(`  ${call.to.name}() — ${file}:${line}`);
            }
            if (outgoingCalls.length > MAX_CALL_HIERARCHY) {
                lines.push(`  ...(+${outgoingCalls.length - MAX_CALL_HIERARCHY} more)`);
            }
        }
    }

    return lines.join("\n");
}

// ---------------------------------------------------------------------------
// 4. Code Actions
// ---------------------------------------------------------------------------

function codeActionGroupLabel(kind: vscode.CodeActionKind | undefined): string {
    if (!kind) {
        return "Other";
    }
    const value = kind.value;
    if (value.startsWith("quickfix")) {
        return "Quick Fixes";
    }
    if (value.startsWith("refactor")) {
        return "Refactorings";
    }
    if (value.startsWith("source")) {
        return "Source Actions";
    }
    return "Other";
}

export function formatCodeActions(
    actions: vscode.CodeAction[],
    filePath: string,
    line: number
): string {
    const filename = filePath.split("/").pop() ?? filePath;

    if (!actions || actions.length === 0) {
        return `No code actions available at ${filename}:${line}.`;
    }

    const groups = new Map<string, { action: vscode.CodeAction; index: number }[]>();
    const order: string[] = [];

    let index = 1;
    for (const action of actions) {
        const label = codeActionGroupLabel(action.kind);
        if (!groups.has(label)) {
            groups.set(label, []);
            order.push(label);
        }
        groups.get(label)!.push({ action, index: index++ });
    }

    const lines: string[] = [`## Code Actions at ${filename}:${line}`];

    for (const groupLabel of order) {
        lines.push(`\n### ${groupLabel}:`);
        for (const { action, index: i } of groups.get(groupLabel)!) {
            const kind = action.kind?.value ?? "unknown";
            lines.push(`  ${i}. "${action.title}" — ${kind}`);
        }
    }

    return lines.join("\n");
}

// ---------------------------------------------------------------------------
// 5. Workspace Symbols
// ---------------------------------------------------------------------------

function scoreMatch(name: string, query: string): number {
    const lower = name.toLowerCase();
    const q = query.toLowerCase();
    if (lower === q) {
        return 2;
    }
    if (lower.startsWith(q)) {
        return 1;
    }
    return 0;
}

export function formatWorkspaceSymbols(
    symbols: vscode.SymbolInformation[],
    query: string,
    maxResults: number
): string {
    if (!symbols || symbols.length === 0) {
        return `No symbols found matching '${query}'.`;
    }

    const sorted = [...symbols].sort((a, b) => scoreMatch(b.name, query) - scoreMatch(a.name, query));
    const slice = sorted.slice(0, maxResults);
    const total = symbols.length;

    const lines: string[] = [`## Workspace Symbols matching "${query}" (${total} results)`];
    lines.push("");

    for (const sym of slice) {
        const kind = symbolKindToString(sym.kind);
        const file = toWorkspaceRelativePath(sym.location.uri);
        const line = sym.location.range.start.line + 1;
        const container = sym.containerName ? ` (in ${sym.containerName})` : "";
        lines.push(`  ${kind} ${sym.name} — ${file}:${line}${container}`);
    }

    if (total > maxResults) {
        lines.push(`  ...(+${total - maxResults} more)`);
    }

    return lines.join("\n");
}

// ---------------------------------------------------------------------------
// 6. Signature Help
// ---------------------------------------------------------------------------

export function formatSignatureHelp(help: vscode.SignatureHelp, symbol: string): string {
    if (!help || help.signatures.length === 0) {
        return `No signature information available for '${symbol}'.`;
    }

    const sig = help.signatures[help.activeSignature ?? 0] ?? help.signatures[0];
    const lines: string[] = [`## Signature: \`${symbol}\``, "", `  ${sig.label}`];

    if (sig.parameters && sig.parameters.length > 0) {
        lines.push("");
        lines.push("  Parameters:");
        sig.parameters.forEach((param, i) => {
            const name = typeof param.label === "string"
                ? param.label
                : sig.label.substring(param.label[0], param.label[1]);
            const doc = param.documentation
                ? " — " + (typeof param.documentation === "string"
                    ? param.documentation
                    : param.documentation.value)
                : "";
            lines.push(`    ${i + 1}. ${name}${doc}`);
        });
    }

    if (sig.documentation) {
        const doc = typeof sig.documentation === "string"
            ? sig.documentation
            : sig.documentation.value;
        // Extract return type hint from documentation if present (heuristic)
        const returnMatch = doc.match(/[Rr]eturns?:?\s*([^\n.]+)/);
        if (returnMatch) {
            lines.push("");
            lines.push(`  Returns: ${returnMatch[1].trim()}`);
        }
    }

    return lines.join("\n");
}

// ---------------------------------------------------------------------------
// 7. Type Definition
// ---------------------------------------------------------------------------

function locationUri(loc: vscode.Location | vscode.LocationLink): vscode.Uri {
    return "uri" in loc ? loc.uri : loc.targetUri;
}

function locationRange(loc: vscode.Location | vscode.LocationLink): vscode.Range {
    return "range" in loc ? loc.range : loc.targetRange;
}

export function formatTypeDefinition(
    locations: (vscode.Location | vscode.LocationLink)[],
    symbol: string,
    documentMap: Map<string, vscode.TextDocument>
): string {
    if (!locations || locations.length === 0) {
        return `No type definition found for '${symbol}'.`;
    }

    const lines: string[] = [`## Type Definition of \`${symbol}\``];

    for (const loc of locations) {
        const uri = locationUri(loc);
        const range = locationRange(loc);
        const file = toWorkspaceRelativePath(uri);
        const startLine = range.start.line + 1;
        const endLine = range.end.line + 1;

        lines.push("");
        lines.push(`  Defined at: ${file}:${startLine}-${endLine}`);

        // Provide source context from the target document
        const targetDoc = documentMap.get(uri.toString());
        if (targetDoc) {
            const contextStart = range.start.line;
            const contextEnd = Math.min(range.end.line, contextStart + 5);
            const contextLines: string[] = [];
            for (let i = contextStart; i <= contextEnd; i++) {
                contextLines.push(targetDoc.lineAt(i).text);
            }
            if (contextLines.length > 0) {
                lines.push("");
                lines.push("  ```");
                for (const cl of contextLines) {
                    lines.push(`  ${cl}`);
                }
                lines.push("  ```");
            }
        }
    }

    return lines.join("\n");
}

// ---------------------------------------------------------------------------
// 8. Applied Action
// ---------------------------------------------------------------------------

export function formatAppliedAction(action: vscode.CodeAction, filePath: string): string {
    const lines: string[] = [`## Applied: "${action.title}"`, "", "Modified files:"];

    const modifiedFiles = new Set<string>();
    modifiedFiles.add(filePath);

    if (action.edit) {
        for (const [uri] of action.edit.entries()) {
            modifiedFiles.add(toWorkspaceRelativePath(uri));
        }
    }

    for (const file of modifiedFiles) {
        lines.push(`  - ${file}`);
    }

    return lines.join("\n");
}
