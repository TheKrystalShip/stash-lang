import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { GetSnapshotInput } from "../types";

export class GetSnapshotTool
    implements vscode.LanguageModelTool<GetSnapshotInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        _options: vscode.LanguageModelToolInvocationPrepareOptions<GetSnapshotInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        return { invocationMessage: "Getting debug snapshot" };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<GetSnapshotInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        this.manager.resetAutoTimeout();

        // Validate state
        if (!this.manager.session || this.manager.state === "terminated") {
            return this.jsonResult({
                error: "no_session",
                message: "No active debug session. Call debug_startSession first.",
            });
        }

        if (this.manager.state !== "paused") {
            return this.jsonResult({
                error: "not_paused",
                message:
                    "Debug session is running. Cannot inspect state while running. Use debug_continue to wait for the next pause, or debug_stopSession to terminate.",
            });
        }

        try {
            const builder = this.manager.createSnapshotBuilder();
            const snapshot = await builder.build({
                variableDepth: input.variableDepth,
                stackDepth: input.stackDepth,
                includeGlobals: input.includeGlobals,
            });
            return this.jsonResult(snapshot);
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            return this.jsonResult({
                error: "snapshot_failed",
                message: `Failed to get snapshot: ${message}`,
            });
        }
    }

    private jsonResult(data: unknown): vscode.LanguageModelToolResult {
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(data, null, 2)),
        ]);
    }
}
