import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { StopSessionInput, SessionStoppedResult, NoSessionResult } from "../types";

export class StopSessionTool
    implements vscode.LanguageModelTool<StopSessionInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        _options: vscode.LanguageModelToolInvocationPrepareOptions<StopSessionInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        return { invocationMessage: "Stopping debug session" };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<StopSessionInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        const captureOutput = input.captureOutput ?? true;
        this.manager.resetAutoTimeout();

        if (!this.manager.session || this.manager.state === "terminated") {
            const result: NoSessionResult = { status: "no_session" };
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(JSON.stringify(result, null, 2)),
            ]);
        }

        const duration = this.manager.sessionDuration;
        const output = captureOutput ? this.manager.capturedOutput : "";
        const breakpointsHit = this.manager.breakpointsHit;

        // Stop the session
        await this.manager.stopSession();

        // Clean up agent breakpoints
        this.manager.breakpointTracker.removeAll();

        const result: SessionStoppedResult = {
            status: "terminated",
            duration,
            output,
            breakpointsHit,
        };

        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(result, null, 2)),
        ]);
    }
}
