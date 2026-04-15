import * as vscode from "vscode";
import { formatSignatureHelp } from "../outputFormatter";
import { resolvePosition } from "../positionResolver";
import { ErrorCode } from "../types";

interface SignatureHelpInput {
    filePath: string;
    symbol: string;
    lineContent?: string;
}

export class SignatureHelpTool implements vscode.LanguageModelTool<SignatureHelpInput> {
    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<SignatureHelpInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;

        let resolved: Awaited<ReturnType<typeof resolvePosition>>;
        try {
            resolved = await resolvePosition(input);
        } catch (err: unknown) {
            const code = (err as NodeJS.ErrnoException).code;
            if (code === ErrorCode.FILE_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`File not found: '${input.filePath}'.`),
                ]);
            }
            if (code === ErrorCode.SYMBOL_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(
                        `Symbol '${input.symbol}' not found in '${input.filePath}'.`
                    ),
                ]);
            }
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Failed to resolve position: ${(err as Error)?.message ?? String(err)}`
                ),
            ]);
        }

        try {
            const lineText = resolved.document.lineAt(resolved.position.line).text;
            const symbolEnd = resolved.position.character + input.symbol.length;
            const parenIndex = lineText.indexOf("(", symbolEnd - 1);

            const adjustedPosition =
                parenIndex !== -1
                    ? new vscode.Position(resolved.position.line, parenIndex + 1)
                    : new vscode.Position(
                          resolved.position.line,
                          resolved.position.character + input.symbol.length
                      );

            const help = await vscode.commands.executeCommand<vscode.SignatureHelp>(
                "vscode.executeSignatureHelpProvider",
                resolved.uri,
                adjustedPosition
            );

            if (!help || help.signatures.length === 0) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(
                        `No signature information available for '${input.symbol}'.`
                    ),
                ]);
            }

            const formatted = formatSignatureHelp(help, input.symbol);
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(formatted),
            ]);
        } catch (err: unknown) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Signature help provider error: ${(err as Error)?.message ?? String(err)}`
                ),
            ]);
        }
    }
}
