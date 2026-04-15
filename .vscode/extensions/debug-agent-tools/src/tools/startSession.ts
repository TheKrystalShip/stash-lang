import * as vscode from "vscode";
import { DebugSessionManager } from "../debugSessionManager";
import {
    StartSessionInput,
    ErrorResult,
    BLOCKED_ENV_VARS,
} from "../types";
import { resolveDebugType } from "../debugTypeResolver";

export class StartSessionTool
    implements vscode.LanguageModelTool<StartSessionInput>
{
    constructor(private readonly manager: DebugSessionManager) {}

    async prepareInvocation(
        options: vscode.LanguageModelToolInvocationPrepareOptions<StartSessionInput>,
        _token: vscode.CancellationToken
    ): Promise<vscode.PreparedToolInvocation> {
        const input = options.input;
        const args = input.args?.length ? ` with args ${JSON.stringify(input.args)}` : "";
        return {
            invocationMessage: `Starting debug session for ${input.program}${args}`,
            confirmationMessages: {
                title: "Start Debug Session",
                message: new vscode.MarkdownString(
                    `Start debugging \`${input.program}\`${args}?`
                ),
            },
        };
    }

    async invoke(
        options: vscode.LanguageModelToolInvocationOptions<StartSessionInput>,
        token: vscode.CancellationToken
    ): Promise<vscode.LanguageModelToolResult> {
        const input = options.input;
        this.manager.resetAutoTimeout();

        // Validate environment variables
        if (input.env) {
            for (const key of Object.keys(input.env)) {
                if (BLOCKED_ENV_VARS.has(key.toUpperCase())) {
                    return this.errorResult({
                        error: "blocked_env_var",
                        message: `Environment variable '${key}' is blocked for security reasons.`,
                    });
                }
            }
        }

        // Resolve debug type
        let debugType = input.debugType;
        if (!debugType) {
            const result = await resolveDebugType(input.program);
            if ("error" in result) {
                return this.errorResult({
                    error: "no_adapter",
                    message: `${result.error} Available adapters: ${result.availableAdapters.join(", ")}`,
                });
            }
            debugType = result.debugType;
        }

        // Handle named launch configuration
        if (input.configuration) {
            const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
            try {
                await this.manager.startSession(
                    { type: debugType, request: "launch", name: input.configuration },
                    workspaceFolder
                );

                if (input.stopOnEntry) {
                    const result = await this.manager.waitForPause(30000);
                    if (result === "terminated") {
                        return this.jsonResult({
                            status: "terminated",
                            output: this.manager.capturedOutput,
                        });
                    }
                    if (result === "timeout") {
                        return this.jsonResult({
                            status: "running",
                            sessionId: this.manager.session?.id ?? "",
                        });
                    }
                    const builder = this.manager.createSnapshotBuilder();
                    const snapshot = await builder.build({ reason: "entry" });
                    return this.jsonResult(snapshot);
                }

                return this.jsonResult({
                    status: "running",
                    sessionId: this.manager.session?.id ?? "",
                });
            } catch (err) {
                return this.errorResult({
                    error: "start_failed",
                    message: `Failed to start debug session: ${err instanceof Error ? err.message : String(err)}`,
                });
            }
        }

        // Build debug configuration
        const config: vscode.DebugConfiguration = {
            type: debugType,
            request: "launch",
            name: "Agent Debug",
            program: input.program,
            stopOnEntry: input.stopOnEntry ?? false,
            noDebug: input.noDebug ?? false,
        };

        if (input.args) {
            config.args = input.args;
        }
        if (input.cwd) {
            config.cwd = input.cwd;
        } else {
            config.cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        }
        if (input.env) {
            config.env = input.env;
        }

        const exceptionBreakpoints = input.exceptionBreakpoints ?? "uncaught";

        // Set exception breakpoints
        if (exceptionBreakpoints !== "none") {
            // This is adapter-specific; most adapters handle it via setExceptionBreakpoints
            // We store it in config and let the session manager handle it
            config.__exceptionBreakpoints = exceptionBreakpoints;
        }

        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];

        try {
            await this.manager.startSession(config, workspaceFolder);

            // Handle exception breakpoints after session starts
            if (exceptionBreakpoints !== "none" && this.manager.session) {
                try {
                    const filters: string[] = [];
                    if (exceptionBreakpoints === "uncaught") {
                        filters.push("uncaught");
                    } else if (exceptionBreakpoints === "all") {
                        filters.push("all", "uncaught");
                    }
                    await this.manager.session.customRequest("setExceptionBreakpoints", {
                        filters,
                    });
                } catch {
                    // Adapter may not support exception breakpoints — ignore
                }
            }

            if (input.stopOnEntry) {
                const result = await this.manager.waitForPause(30000);
                if (result === "terminated") {
                    return this.jsonResult({
                        status: "terminated",
                        output: this.manager.capturedOutput,
                    });
                }
                if (result === "timeout") {
                    return this.jsonResult({
                        status: "running",
                        sessionId: this.manager.session?.id ?? "",
                    });
                }
                const builder = this.manager.createSnapshotBuilder();
                const snapshot = await builder.build({
                    reason: "entry",
                    threadId: typeof result !== "string" ? result.threadId : undefined,
                });
                return this.jsonResult(snapshot);
            }

            return this.jsonResult({
                status: "running",
                sessionId: this.manager.session?.id ?? "",
            });
        } catch (err) {
            return this.errorResult({
                error: "start_failed",
                message: `Failed to start debug session: ${err instanceof Error ? err.message : String(err)}`,
            });
        }
    }

    private jsonResult(data: unknown): vscode.LanguageModelToolResult {
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(data, null, 2)),
        ]);
    }

    private errorResult(error: ErrorResult): vscode.LanguageModelToolResult {
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(JSON.stringify(error, null, 2)),
        ]);
    }
}
