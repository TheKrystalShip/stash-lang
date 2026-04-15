import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { EvaluateInput, EvaluateResult } from "../types";

export class EvaluateTool
    implements vscode.LanguageModelTool<EvaluateInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        options: vscode.LanguageModelToolInvocationPrepareOptions<EvaluateInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        const input = options.input;
        const context = input.context ?? "watch";

        if (context === "repl") {
            return {
                invocationMessage: `Evaluating expression: ${input.expression}`,
                confirmationMessages: {
                    title: "Evaluate Expression (REPL)",
                    message: new vscode.MarkdownString(
                        `Evaluate \`${input.expression}\` in the running program? This may have side effects.`
                    ),
                },
            };
        }

        return {
            invocationMessage: `Evaluating expression: ${input.expression}`,
        };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<EvaluateInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        const context = input.context ?? "watch";
        const frameIndex = input.frameIndex ?? 0;
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
                    "Debug session is running. Cannot evaluate while running.",
            });
        }

        try {
            const frameId = await this.manager.getFrameId(frameIndex);
            const evalResult = await this.manager.evaluate(
                input.expression,
                frameId,
                context
            );

            const result: EvaluateResult = {
                expression: input.expression,
                result: evalResult.result,
                type: evalResult.type,
                variablesReference: evalResult.variablesReference,
            };

            return this.jsonResult(result);
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            return this.jsonResult({
                error: "eval_failed",
                message: `Evaluation failed: ${message}`,
                expression: input.expression,
            });
        }
    }

    private jsonResult(data: unknown): vscode.LanguageModelToolResult {
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(data, null, 2)),
        ]);
    }
}
