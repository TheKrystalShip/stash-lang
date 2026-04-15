import * as vscode from "vscode";
import { formatHover } from "../outputFormatter";
import { resolvePosition } from "../positionResolver";
import { ErrorCode, SymbolLocationInput } from "../types";

interface HoverInput extends SymbolLocationInput {}

export class HoverTool implements vscode.LanguageModelTool<HoverInput> {
    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<HoverInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;

        let resolved;
        try {
            resolved = await resolvePosition(input);
        } catch (err: any) {
            if (err?.code === ErrorCode.FILE_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`File not found: '${input.filePath}'.`),
                ]);
            }
            if (err?.code === ErrorCode.SYMBOL_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(
                        `Symbol '${input.symbol}' not found in '${input.filePath}'.`
                    ),
                ]);
            }
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Failed to resolve position: ${err?.message ?? String(err)}`
                ),
            ]);
        }

        try {
            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                "vscode.executeHoverProvider",
                resolved.uri,
                resolved.position
            );

            const formatted = formatHover(hovers ?? [], input.symbol);
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(formatted),
            ]);
        } catch (err: any) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Hover provider error: ${err?.message ?? String(err)}`
                ),
            ]);
        }
    }
}
