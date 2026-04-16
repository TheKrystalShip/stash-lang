import * as vscode from "vscode";
import { formatWorkspaceSymbols } from "../outputFormatter";
import { parseKindFilter } from "../types";

const NON_CODE_KINDS = new Set([
    vscode.SymbolKind.String,
    vscode.SymbolKind.Number,
    vscode.SymbolKind.Boolean,
    vscode.SymbolKind.Null,
]);

interface WorkspaceSymbolsInput {
    query: string;
    kind?: string;
    maxResults?: number;
}

export class WorkspaceSymbolsTool implements vscode.LanguageModelTool<WorkspaceSymbolsInput> {
    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<WorkspaceSymbolsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        const maxResults = Math.min(input.maxResults ?? 20, 50);

        let symbols: vscode.SymbolInformation[] | undefined;
        try {
            symbols = await vscode.commands.executeCommand<vscode.SymbolInformation[]>(
                "vscode.executeWorkspaceSymbolProvider",
                input.query
            );
        } catch (err) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Unexpected error executing workspace symbol provider: ${(err as Error).message}`
                ),
            ]);
        }

        let filtered = symbols ?? [];
        if (input.kind !== undefined && input.kind !== "all") {
            const kindFilter = parseKindFilter(input.kind);
            if (kindFilter !== undefined) {
                filtered = filtered.filter((s) => kindFilter.has(s.kind));
            }
        } else if (input.kind === undefined) {
            // Default: exclude non-code symbol kinds (markdown headings, JSON values, etc.)
            filtered = filtered.filter((s) => !NON_CODE_KINDS.has(s.kind));
        }
        // When kind === "all", return everything unfiltered

        const formatted = formatWorkspaceSymbols(filtered, input.query, maxResults);

        return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(formatted)]);
    }
}
