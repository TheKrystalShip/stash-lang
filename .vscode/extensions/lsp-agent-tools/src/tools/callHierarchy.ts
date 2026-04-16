import * as vscode from "vscode";
import { formatCallHierarchy } from "../outputFormatter";
import { resolvePosition } from "../positionResolver";
import { ErrorCode } from "../types";

type HierarchyResult = {
    item: vscode.CallHierarchyItem;
    incoming?: vscode.CallHierarchyIncomingCall[];
    outgoing?: vscode.CallHierarchyOutgoingCall[];
};

async function collectCalls(
    item: vscode.CallHierarchyItem,
    direction: "incoming" | "outgoing" | "both",
    currentDepth: number,
    maxDepth: number,
    results: HierarchyResult[]
): Promise<void> {
    let incoming: vscode.CallHierarchyIncomingCall[] | undefined;
    let outgoing: vscode.CallHierarchyOutgoingCall[] | undefined;

    if (direction === "incoming" || direction === "both") {
        incoming = await vscode.commands.executeCommand<vscode.CallHierarchyIncomingCall[]>(
            "vscode.provideIncomingCalls", item
        ) ?? [];
    }
    if (direction === "outgoing" || direction === "both") {
        outgoing = await vscode.commands.executeCommand<vscode.CallHierarchyOutgoingCall[]>(
            "vscode.provideOutgoingCalls", item
        ) ?? [];
    }

    results.push({ item, incoming, outgoing });

    if (currentDepth < maxDepth) {
        if (direction === "incoming" || direction === "both") {
            for (const call of incoming ?? []) {
                const nestedItems = await vscode.commands.executeCommand<vscode.CallHierarchyItem[]>(
                    "vscode.prepareCallHierarchy", call.from.uri, call.from.selectionRange.start
                );
                for (const nestedItem of nestedItems ?? []) {
                    await collectCalls(nestedItem, direction, currentDepth + 1, maxDepth, results);
                }
            }
        }
        if (direction === "outgoing" || direction === "both") {
            for (const call of outgoing ?? []) {
                const nestedItems = await vscode.commands.executeCommand<vscode.CallHierarchyItem[]>(
                    "vscode.prepareCallHierarchy", call.to.uri, call.to.selectionRange.start
                );
                for (const nestedItem of nestedItems ?? []) {
                    await collectCalls(nestedItem, direction, currentDepth + 1, maxDepth, results);
                }
            }
        }
    }
}

interface CallHierarchyInput {
    filePath: string;
    symbol: string;
    lineContent?: string;
    direction?: "incoming" | "outgoing" | "both";
    depth?: number;
}

export class CallHierarchyTool implements vscode.LanguageModelTool<CallHierarchyInput> {
    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<CallHierarchyInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        const direction = input.direction ?? "both";
        const depth = Math.min(input.depth ?? 1, 3);

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
            // Ensure the document is visible — some LSPs need this for call hierarchy
            await vscode.window.showTextDocument(resolved.document, {
                preview: true,
                preserveFocus: true,
            });

            const rootItems = await vscode.commands.executeCommand<vscode.CallHierarchyItem[]>(
                "vscode.prepareCallHierarchy",
                resolved.document.uri,
                resolved.position
            );

            if (!rootItems || rootItems.length === 0) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(
                        `No call hierarchy information available for '${input.symbol}'. The language server may not support call hierarchy for this file type.`
                    ),
                ]);
            }

            const results: HierarchyResult[] = [];

            for (const item of rootItems) {
                await collectCalls(item, direction, 1, depth, results);
            }

            const formatted = formatCallHierarchy(results, input.symbol);
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(formatted),
            ]);
        } catch (err: unknown) {
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(
                    `Call hierarchy error: ${(err as Error)?.message ?? String(err)}`
                ),
            ]);
        }
    }
}
