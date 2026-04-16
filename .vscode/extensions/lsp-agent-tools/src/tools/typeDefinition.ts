import * as vscode from "vscode";
import { resolvePosition } from "../positionResolver";
import { formatHover, formatTypeDefinition } from "../outputFormatter";
import { ErrorCode } from "../types";

interface TypeDefinitionInput {
    filePath: string;
    symbol: string;
    lineContent?: string;
}

export class TypeDefinitionTool implements vscode.LanguageModelTool<TypeDefinitionInput> {
    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<TypeDefinitionInput>,
        token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;

        let resolved: Awaited<ReturnType<typeof resolvePosition>>;
        try {
            resolved = await resolvePosition(input);
        } catch (err: unknown) {
            const code = (err as NodeJS.ErrnoException).code;
            if (code === ErrorCode.FILE_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`File not found: ${input.filePath}`),
                ]);
            }
            if (code === ErrorCode.SYMBOL_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(
                        `Symbol '${input.symbol}' not found in ${input.filePath}.`
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
            const locations = await vscode.commands.executeCommand<
                (vscode.Location | vscode.LocationLink)[]
            >("vscode.executeTypeDefinitionProvider", resolved.uri, resolved.position);

            if (locations && locations.length > 0) {
                const documentMap = new Map<string, vscode.TextDocument>();
                for (const loc of locations) {
                    const locUri = 'targetUri' in loc ? loc.targetUri : loc.uri;
                    if (!documentMap.has(locUri.toString())) {
                        try {
                            const doc = await vscode.workspace.openTextDocument(locUri);
                            documentMap.set(locUri.toString(), doc);
                        } catch {
                            // Skip if can't open
                        }
                    }
                }
                const text = formatTypeDefinition(locations, input.symbol, documentMap);
                return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(text)]);
            }

            // Fallback: hover usually includes type information
            const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                "vscode.executeHoverProvider",
                resolved.uri,
                resolved.position
            );

            if (hovers && hovers.length > 0) {
                const hoverText = formatHover(hovers, input.symbol);
                // Replace the "## Hover:" header with "## Type Definition of" for consistency
                const reframed = hoverText.replace(
                    `## Hover: \`${input.symbol}\``,
                    `## Type Definition of \`${input.symbol}\` (from hover)`
                );
                return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(reframed)]);
            }

            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(`No type definition found for '${input.symbol}'.`),
            ]);
        } catch (err: unknown) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Failed to retrieve type definition: ${(err as Error)?.message ?? String(err)}`
                ),
            ]);
        }
    }
}
