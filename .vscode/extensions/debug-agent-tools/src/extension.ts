import * as vscode from "vscode";
import { DebugSessionManager } from "./debugSessionManager";
import { StartSessionTool } from "./tools/startSession";
import { SetBreakpointsTool } from "./tools/setBreakpoints";
import { RemoveBreakpointsTool } from "./tools/removeBreakpoints";
import { ContinueTool } from "./tools/continue";
import { StepTool } from "./tools/step";
import { GetSnapshotTool } from "./tools/getSnapshot";
import { EvaluateTool } from "./tools/evaluate";
import { StopSessionTool } from "./tools/stopSession";

export function activate(context: vscode.ExtensionContext): void {
    const manager = new DebugSessionManager();

    // Register event listeners for session tracking
    const eventDisposables = manager.registerEventListeners();
    context.subscriptions.push(...eventDisposables);

    // Register all 8 Language Model Tools
    context.subscriptions.push(
        vscode.lm.registerTool("debug_startSession", new StartSessionTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_setBreakpoints", new SetBreakpointsTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_removeBreakpoints", new RemoveBreakpointsTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_continue", new ContinueTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_step", new StepTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_getSnapshot", new GetSnapshotTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_evaluate", new EvaluateTool(manager))
    );
    context.subscriptions.push(
        vscode.lm.registerTool("debug_stopSession", new StopSessionTool(manager))
    );

    // Clean up manager on deactivation
    context.subscriptions.push({
        dispose: () => manager.dispose(),
    });
}

export function deactivate(): void {
    // Nothing to do — all cleanup is via subscriptions
}
