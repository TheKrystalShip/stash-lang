import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import { SetBreakpointsInput, BreakpointResult, SetBreakpointsResult } from "../types";

export class SetBreakpointsTool
    implements vscode.LanguageModelTool<SetBreakpointsInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        options: vscode.LanguageModelToolInvocationPrepareOptions<SetBreakpointsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        const input = options.input;
        const count = input.breakpoints.length;
        const desc =
            count === 0
                ? `Clearing breakpoints in ${input.file}`
                : `Setting ${count} breakpoint${count > 1 ? "s" : ""} in ${input.file}`;
        return { invocationMessage: desc };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<SetBreakpointsInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        this.manager.resetAutoTimeout();

        const fileUri = vscode.Uri.file(input.file);
        const caps = this.manager.capabilities;

        // Build SourceBreakpoint objects and result entries together,
        // stripping unsupported features when capabilities are known.
        const warnings: string[] = [];
        const sourceBreakpoints: vscode.SourceBreakpoint[] = [];
        const breakpointResults: BreakpointResult[] = input.breakpoints.map((bp) => {
            // When caps is undefined (no active session), keep features optimistically.
            const condition = (caps && !caps.supportsConditionalBreakpoints) ? undefined : bp.condition;
            const hitCondition = (caps && !caps.supportsHitConditionalBreakpoints) ? undefined : bp.hitCondition;
            const logMessage = (caps && !caps.supportsLogPoints) ? undefined : bp.logMessage;

            if (bp.condition && condition === undefined) {
                warnings.push(
                    `Conditional breakpoints not supported by adapter — condition on line ${bp.line} was ignored`
                );
            }
            if (bp.hitCondition && hitCondition === undefined) {
                warnings.push(
                    `Hit conditional breakpoints not supported by adapter — hitCondition on line ${bp.line} was ignored`
                );
            }
            if (bp.logMessage && logMessage === undefined) {
                warnings.push(
                    `Logpoints not supported by adapter — logMessage on line ${bp.line} was ignored`
                );
            }

            const location = new vscode.Location(
                fileUri,
                new vscode.Position(bp.line - 1, 0) // convert 1-based to 0-based
            );
            sourceBreakpoints.push(new vscode.SourceBreakpoint(
                location,
                true, // enabled
                condition,
                hitCondition,
                logMessage
            ));

            const result: BreakpointResult = {
                line: bp.line,
                verified: true, // Assume verified; DAP may update this
            };
            if (condition) { result.condition = condition; }
            if (hitCondition) { result.hitCondition = hitCondition; }
            if (logMessage) { result.logMessage = logMessage; }

            return result;
        });

        // Replace agent breakpoints for this file
        this.manager.breakpointTracker.replaceBreakpoints(
            fileUri,
            sourceBreakpoints
        );

        const response: SetBreakpointsResult & { warnings?: string[] } = {
            file: input.file,
            breakpoints: breakpointResults,
        };

        if (warnings.length > 0) {
            response.warnings = warnings;
        }

        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(response, null, 2)),
        ]);
    }
}
