import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { RemoveBreakpointsInput, RemoveBreakpointsResult } from "../types";

export class RemoveBreakpointsTool
    implements vscode.LanguageModelTool<RemoveBreakpointsInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        options: vscode.LanguageModelToolInvocationPrepareOptions<RemoveBreakpointsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        const input = options.input;
        const desc = input.file
            ? `Removing breakpoints from ${input.file}`
            : "Removing all agent-managed breakpoints";
        return { invocationMessage: desc };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<RemoveBreakpointsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        this.manager.resetAutoTimeout();

        let removed: number;
        if (input.file) {
            const fileUri = vscode.Uri.file(input.file);
            removed = this.manager.breakpointTracker.removeForFile(fileUri);
        } else {
            removed = this.manager.breakpointTracker.removeAll();
        }

        const result: RemoveBreakpointsResult = { removed };

        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(result, null, 2)),
        ]);
    }
}
