import * as vscode from "vscode";
import { resolveDocument } from "../positionResolver";
import { formatDocumentSymbols } from "../outputFormatter";
import { ErrorCode, parseKindFilter, toWorkspaceRelativePath } from "../types";

interface DocumentSymbolsInput {
    filePath: string;
    kind?: string;
}

export class DocumentSymbolsTool implements vscode.LanguageModelTool<DocumentSymbolsInput> {
    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<DocumentSymbolsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;

        let resolved: { uri: vscode.Uri; document: vscode.TextDocument };
        try {
            resolved = await resolveDocument(input.filePath);
        } catch (err) {
            const code = (err as NodeJS.ErrnoException).code;
            if (code === ErrorCode.FILE_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`File not found: '${input.filePath}'.`),
                ]);
            }
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Unexpected error resolving file: ${(err as Error).message}`
                ),
            ]);
        }

        const kindFilter = parseKindFilter(input.kind);

        let symbols: vscode.DocumentSymbol[] | undefined;
        try {
            symbols = await vscode.commands.executeCommand<vscode.DocumentSymbol[]>(
                "vscode.executeDocumentSymbolProvider",
                resolved.uri
            );
        } catch (err) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Unexpected error executing document symbol provider: ${(err as Error).message}`
                ),
            ]);
        }

        const formatted = formatDocumentSymbols(
            symbols ?? [],
            toWorkspaceRelativePath(resolved.uri),
            kindFilter
        );

        return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(formatted)]);
    }
}
