import * as vscode from "vscode";
import { formatCodeActions, formatAppliedAction } from "../outputFormatter";
import { resolveDocument } from "../positionResolver";
import { ErrorCode, toWorkspaceRelativePath } from "../types";

interface CodeActionsInput {
    filePath: string;
    line: number;
    diagnosticMessage?: string;
    kind?: string;
    apply?: string;
}

function resolveKind(kind: string | undefined): vscode.CodeActionKind | undefined {
    switch (kind) {
        case "quickfix": return vscode.CodeActionKind.QuickFix;
        case "refactor": return vscode.CodeActionKind.Refactor;
        case "source":   return vscode.CodeActionKind.Source;
        default:         return undefined;
    }
}

async function queryActions(
    uri: vscode.Uri,
    document: vscode.TextDocument,
    input: CodeActionsInput
): Promise<vscode.CodeAction[]> {
    const lineIndex = input.line - 1;
    let range: vscode.Range;

    if (input.diagnosticMessage) {
        const diags = vscode.languages.getDiagnostics(uri);
        const needle = input.diagnosticMessage.toLowerCase();
        const match = diags.find(d => d.message.toLowerCase().includes(needle));
        range = match ? match.range : document.lineAt(lineIndex).range;
    } else {
        range = document.lineAt(lineIndex).range;
    }

    const codeActionKind = resolveKind(input.kind);

    // Don't pass kind to the command — do client-side filtering only
    // Server-side kind filtering is inconsistent across LSPs
    const actions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
        "vscode.executeCodeActionProvider",
        uri,
        range
    );

    const results = actions ?? [];

    if (codeActionKind) {
        // codeActionKind.contains(a.kind) — "is this action a sub-kind of the filter?"
        return results.filter(a => a.kind && codeActionKind.contains(a.kind));
    }

    return results;
}

export class CodeActionsTool implements vscode.LanguageModelTool<CodeActionsInput> {
    async prepareInvocation(
        options: vscode.LanguageModelToolInvocationPrepareOptions<CodeActionsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        if (options.input.apply) {
            return {
                invocationMessage: `Apply code action: "${options.input.apply}"`,
                confirmationMessages: {
                    title: "Apply Code Action",
                    message: new vscode.MarkdownString(
                        `Apply code action **"${options.input.apply}"** in \`${options.input.filePath}\`?`
                    ),
                },
            };
        }
        return {
            invocationMessage: `Querying code actions at line ${options.input.line}`,
        };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<CodeActionsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;

        let uri: vscode.Uri;
        let document: vscode.TextDocument;

        try {
            ({ uri, document } = await resolveDocument(input.filePath));
        } catch (err: any) {
            if (err?.code === ErrorCode.FILE_NOT_FOUND) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`File not found: '${input.filePath}'.`),
                ]);
            }
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Failed to open file: ${err?.message ?? String(err)}`
                ),
            ]);
        }

        const relPath = toWorkspaceRelativePath(uri);
        const lineIndex = input.line - 1;
        if (lineIndex < 0 || lineIndex >= document.lineCount) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Line ${input.line} is out of range for '${relPath}' (${document.lineCount} lines).`
                ),
            ]);
        }

        try {
            const actions = await queryActions(uri, document, input);


            if (!input.apply) {
                const formatted = formatCodeActions(actions, relPath, input.line);
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(formatted),
                ]);
            }

            const needle = input.apply.toLowerCase();
            const match = actions.find(a => a.title.toLowerCase().includes(needle));

            if (!match) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(
                        `No code action matching '${input.apply}' found at ${relPath}:${input.line}.`
                    ),
                ]);
            }

            if (match.edit) {
                await vscode.workspace.applyEdit(match.edit);
            }
            if (match.command) {
                await vscode.commands.executeCommand(
                    match.command.command,
                    ...(match.command.arguments ?? [])
                );
            }

            const formatted = formatAppliedAction(match, relPath);
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(formatted),
            ]);
        } catch (err: any) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Code action error: ${err?.message ?? String(err)}`
                ),
            ]);
        }
    }
}
