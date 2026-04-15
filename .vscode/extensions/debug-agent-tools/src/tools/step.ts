import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { StepInput } from "../types";

export class StepTool implements vscode.LanguageModelTool<StepInput> {
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        options: vscode.LanguageModelToolInvocationPrepareOptions<StepInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        const input = options.input;
        const count = input.count ?? 1;
        const desc =
            count === 1
                ? `Stepping ${input.action}`
                : `Stepping ${input.action} (${count} times)`;
        return { invocationMessage: desc };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<StepInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        const count = Math.min(Math.max(input.count ?? 1, 1), 20);
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
                message: "Debug session is running. Cannot step while running.",
            });
        }

        // Validate step count
        if (input.count !== undefined && (input.count < 1 || input.count > 20)) {
            return this.jsonResult({
                error: "invalid_input",
                message: "Step count must be between 1 and 20.",
            });
        }

        try {
            for (let i = 0; i < count; i++) {
                // Send step request
                await this.manager.step(input.action, input.threadId);

                // Wait for pause (5s timeout per step)
                const result = await this.manager.waitForPause(5000);

                if (result === "terminated") {
                    return this.jsonResult({
                        status: "terminated",
                        exitCode: undefined,
                        output: this.manager.capturedOutput,
                    });
                }

                if (result === "timeout") {
                    return this.jsonResult({
                        error: "timeout",
                        message: `Step operation timed out after 5000ms at step ${i + 1} of ${count}.`,
                    });
                }

                // If this is not the last step, continue looping
                // If it is the last step, we'll build the snapshot below
                if (i === count - 1) {
                    // Build snapshot after final step
                    const builder = this.manager.createSnapshotBuilder();
                    const snapshot = await builder.build({
                        reason: "step",
                        threadId: typeof result !== "string" ? result.threadId : undefined,
                    });
                    return this.jsonResult(snapshot);
                }
            }

            // Should not reach here, but just in case
            const builder = this.manager.createSnapshotBuilder();
            const snapshot = await builder.build({ reason: "step" });
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
                    message: "Debug session is running. Cannot step while running.",
                });
            }
            return this.jsonResult({
                error: "step_failed",
                message: `Step failed: ${message}`,
            });
        }
    }

    private jsonResult(data: unknown): vscode.LanguageModelToolResult {
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(data, null, 2)),
        ]);
    }
}
