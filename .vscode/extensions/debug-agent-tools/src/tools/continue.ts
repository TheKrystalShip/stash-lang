import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { ContinueInput, ErrorResult } from "../types";

export class ContinueTool
    implements vscode.LanguageModelTool<ContinueInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        _options: vscode.LanguageModelToolInvocationPrepareOptions<ContinueInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        return { invocationMessage: "Continuing program execution" };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<ContinueInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        const timeout = input.timeout ?? 10000;
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
                    "Debug session is running. Cannot continue while already running. Use debug_continue to wait for the next pause, or debug_stopSession to terminate.",
            });
        }

        try {
            // Send continue
            await this.manager.continue(input.threadId);

            // Wait for pause
            const result = await this.manager.waitForPause(timeout);

            if (result === "terminated") {
                return this.jsonResult({
                    status: "terminated",
                    exitCode: undefined,
                    output: this.manager.capturedOutput,
                });
            }

            if (result === "timeout") {
                return this.jsonResult({
                    status: "running",
                    message: `Program did not pause within ${timeout}ms. It may be in a long-running operation or waiting for input. Use debug_stopSession to terminate, or debug_continue with a longer timeout.`,
                });
            }

            // Build snapshot
            const builder = this.manager.createSnapshotBuilder();
            const reason = result.reason === "breakpoint" ? "breakpoint" as const :
                           result.reason === "exception" ? "exception" as const :
                           result.reason === "step" ? "step" as const :
                           "breakpoint" as const;

            const snapshot = await builder.build({
                reason,
                threadId: result.threadId,
                exceptionInfo: reason === "exception" ? {
                    description: result.description,
                } : undefined,
            });

            return this.jsonResult(snapshot);
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            if (message === "no_session") {
                return this.jsonResult({
                    error: "no_session",
                    message: "No active debug session. Call debug_startSession first.",
                });
            }
            if (message === "not_paused") {
                return this.jsonResult({
                    error: "not_paused",
                    message:
                        "Debug session is running. Cannot inspect state while running.",
                });
            }
            return this.jsonResult({
                error: "continue_failed",
                message: `Continue failed: ${message}`,
            });
        }
    }

    private jsonResult(data: unknown): vscode.LanguageModelToolResult {
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(data, null, 2)),
        ]);
    }
}
