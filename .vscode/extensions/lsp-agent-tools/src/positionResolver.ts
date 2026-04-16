import * as path from "path";
import * as vscode from "vscode";
import { ErrorCode, ResolvedPosition, SymbolLocationInput } from "./types";

/**
 * Resolves a file path (absolute or workspace-relative) to a vscode.Uri.
 * Throws with ErrorCode.FILE_NOT_FOUND if the file cannot be located.
 */
async function resolveUri(filePath: string): Promise<vscode.Uri> {
    if (path.isAbsolute(filePath)) {
        return vscode.Uri.file(filePath);
    }

    const folders = vscode.workspace.workspaceFolders;
    if (folders) {
        for (const folder of folders) {
            const candidate = vscode.Uri.joinPath(folder.uri, filePath);
            try {
                await vscode.workspace.fs.stat(candidate);
                return candidate;
            } catch {
                // not found in this folder, try next
            }
        }
    }

    const error = new Error(`File not found: ${filePath}`);
    (error as NodeJS.ErrnoException).code = ErrorCode.FILE_NOT_FOUND;
    throw error;
}

/**
 * Finds the column of `symbol` within `lineText` using word-boundary matching.
 * Returns -1 if not found.
 */
function findSymbolInLine(lineText: string, symbol: string): number {
    const pattern = new RegExp(`\\b${escapeRegex(symbol)}\\b`);
    const match = pattern.exec(lineText);
    if (!match) {
        return -1;
    }
    // For dotted symbols (e.g., "arr.push"), return the column of the member
    // part (after the last dot) so the LSP hover/signature lands on the member,
    // not the namespace prefix.
    const lastDot = symbol.lastIndexOf(".");
    if (lastDot !== -1) {
        return match.index + lastDot + 1;
    }
    return match.index;
}

function escapeRegex(s: string): string {
    return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Resolves a file path to a Uri and opens the TextDocument.
 * Suitable for tools that only need a document (e.g. documentSymbols) and no position.
 */
export async function resolveDocument(
    filePath: string
): Promise<{ uri: vscode.Uri; document: vscode.TextDocument }> {
    const uri = await resolveUri(filePath);
    const document = await vscode.workspace.openTextDocument(uri);
    return { uri, document };
}

/**
 * Resolves agent-friendly inputs (filePath, symbol, optional lineContent / line)
 * into a ResolvedPosition for use with vscode.execute*Provider commands.
 *
 * Resolution priority:
 *   1. lineContent (scan for matching line, then word-boundary match symbol within it)
 *   2. line (1-based; convert to 0-based, then word-boundary match symbol within it)
 *   3. fallback (scan entire document for first word-boundary match of symbol)
 */
export async function resolvePosition(
    input: SymbolLocationInput
): Promise<ResolvedPosition> {
    const { filePath, symbol, lineContent, line } = input;

    const uri = await resolveUri(filePath);
    const document = await vscode.workspace.openTextDocument(uri);

    const lineCount = document.lineCount;

    // Strategy 1: lineContent hint
    if (lineContent !== undefined) {
        for (let i = 0; i < lineCount; i++) {
            const text = document.lineAt(i).text;
            if (text.includes(lineContent)) {
                const col = findSymbolInLine(text, symbol);
                if (col !== -1) {
                    return {
                        uri,
                        position: new vscode.Position(i, col),
                        document,
                    };
                }
            }
        }
        // lineContent matched but symbol wasn't found within it — fall through to full scan
    }

    // Strategy 2: explicit line (1-based)
    if (line !== undefined) {
        const zeroBasedLine = line - 1;
        if (zeroBasedLine >= 0 && zeroBasedLine < lineCount) {
            const text = document.lineAt(zeroBasedLine).text;
            const col = findSymbolInLine(text, symbol);
            if (col !== -1) {
                return {
                    uri,
                    position: new vscode.Position(zeroBasedLine, col),
                    document,
                };
            }
        }
        // line provided but symbol not on that line — fall through to full scan
    }

    // Strategy 3: full document scan
    for (let i = 0; i < lineCount; i++) {
        const text = document.lineAt(i).text;
        const col = findSymbolInLine(text, symbol);
        if (col !== -1) {
            return {
                uri,
                position: new vscode.Position(i, col),
                document,
            };
        }
    }

    const error = new Error(`Symbol not found: "${symbol}" in ${filePath}`);
    (error as NodeJS.ErrnoException).code = ErrorCode.SYMBOL_NOT_FOUND;
    throw error;
}
