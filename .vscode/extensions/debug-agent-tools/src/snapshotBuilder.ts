import * as vscode from "vscode";
import * as fs from "fs";
import {
    DebugSnapshot,
    SourceContext,
    StackFrame,
    VariableMap,
    VariableInfo,
    ExceptionInfo,
    SNAPSHOT_LIMITS,
} from "./types";

/**
 * Assembles a DebugSnapshot from DAP requests.
 */
export class SnapshotBuilder {
    constructor(
        private readonly session: vscode.DebugSession,
        private readonly outputBuffer: string[]
    ) {}

    /**
     * Build a complete debug snapshot.
     */
    async build(options?: {
        reason?: DebugSnapshot["reason"];
        threadId?: number;
        variableDepth?: number;
        stackDepth?: number;
        includeGlobals?: boolean;
        exceptionInfo?: { description?: string; breakMode?: string };
    }): Promise<DebugSnapshot> {
        const reason = options?.reason ?? "breakpoint";
        const variableDepth = Math.min(options?.variableDepth ?? SNAPSHOT_LIMITS.MAX_NESTED_DEPTH, 3);
        const stackDepth = Math.min(options?.stackDepth ?? SNAPSHOT_LIMITS.MAX_CALL_STACK_FRAMES, 20);
        const includeGlobals = options?.includeGlobals ?? false;

        // Get thread ID
        const threadId = options?.threadId ?? await this.getStoppedThreadId();
        if (threadId === undefined) {
            throw new Error("No stopped thread found");
        }

        // Get stack trace
        const stackResponse = await this.session.customRequest("stackTrace", {
            threadId,
            startFrame: 0,
            levels: stackDepth,
        });

        const frames: Array<{
            id: number;
            name: string;
            source?: { path?: string; name?: string };
            line: number;
            column: number;
        }> = stackResponse.stackFrames ?? [];

        if (frames.length === 0) {
            throw new Error("No stack frames available");
        }

        const topFrame = frames[0];
        const filePath = topFrame.source?.path ?? topFrame.source?.name ?? "<unknown>";
        const line = topFrame.line;
        const column = topFrame.column;

        // Build call stack
        const callStack: StackFrame[] = frames.map((f) => ({
            name: f.name,
            file: this.toWorkspaceRelative(f.source?.path ?? f.source?.name ?? "<unknown>"),
            line: f.line,
        }));

        // Get source context
        const sourceContext = this.getSourceContext(filePath, line);

        // Get variables for the top frame
        const scopesResponse = await this.session.customRequest("scopes", {
            frameId: topFrame.id,
        });

        const scopes: Array<{
            name: string;
            variablesReference: number;
            expensive: boolean;
        }> = scopesResponse.scopes ?? [];

        const locals: VariableMap = {};
        const closures: VariableMap = {};

        for (const scope of scopes) {
            if (scope.expensive && !includeGlobals) {
                continue;
            }

            const scopeName = scope.name.toLowerCase();
            const isGlobal = scopeName.includes("global");
            const isClosure = scopeName.includes("closure") || scopeName.includes("captured");

            if (isGlobal && !includeGlobals) {
                continue;
            }

            const variables = await this.getVariables(
                scope.variablesReference,
                variableDepth,
                0
            );

            const target = isClosure ? closures : locals;
            for (const [name, info] of Object.entries(variables)) {
                target[name] = info;
            }
        }

        // Get exception info if paused on exception
        let exception: ExceptionInfo | undefined;
        if (reason === "exception" || options?.exceptionInfo) {
            exception = await this.getExceptionInfo(threadId, options?.exceptionInfo);
        }

        // Get output
        const output = this.getOutputDelta();

        return {
            status: "paused",
            reason,
            file: this.toWorkspaceRelative(filePath),
            line,
            column,
            sourceContext,
            callStack,
            locals,
            closures,
            exception,
            output,
        };
    }

    private async getStoppedThreadId(): Promise<number | undefined> {
        try {
            const threadsResponse = await this.session.customRequest("threads");
            const threads: Array<{ id: number; name: string }> = threadsResponse.threads ?? [];
            // Return the first thread (most DAP adapters have a single main thread)
            return threads.length > 0 ? threads[0].id : undefined;
        } catch {
            return undefined;
        }
    }

    private getSourceContext(filePath: string, line: number): SourceContext {
        try {
            const content = fs.readFileSync(filePath, "utf-8");
            const lines = content.split("\n");
            const zeroLine = line - 1; // convert to 0-based

            const before: string[] = [];
            for (
                let i = Math.max(0, zeroLine - SNAPSHOT_LIMITS.SOURCE_CONTEXT_LINES_BEFORE);
                i < zeroLine;
                i++
            ) {
                before.push(`${i + 1}: ${lines[i]}`);
            }

            const current =
                zeroLine >= 0 && zeroLine < lines.length
                    ? `${line}: ${lines[zeroLine]}`
                    : `${line}: <unavailable>`;

            const after: string[] = [];
            for (
                let i = zeroLine + 1;
                i <= Math.min(lines.length - 1, zeroLine + SNAPSHOT_LIMITS.SOURCE_CONTEXT_LINES_AFTER);
                i++
            ) {
                after.push(`${i + 1}: ${lines[i]}`);
            }

            return { before, current, after };
        } catch {
            return {
                before: [],
                current: `${line}: <source unavailable>`,
                after: [],
            };
        }
    }

    private async getVariables(
        variablesReference: number,
        maxDepth: number,
        currentDepth: number
    ): Promise<VariableMap> {
        if (variablesReference === 0) {
            return {};
        }

        const result: VariableMap = {};

        try {
            const response = await this.session.customRequest("variables", {
                variablesReference,
            });

            const variables: Array<{
                name: string;
                value: string;
                type?: string;
                variablesReference: number;
            }> = response.variables ?? [];

            let count = 0;
            for (const v of variables) {
                if (count >= SNAPSHOT_LIMITS.MAX_LOCAL_VARIABLES) {
                    break;
                }

                let value = v.value;
                if (value.length > SNAPSHOT_LIMITS.MAX_VARIABLE_VALUE_LENGTH) {
                    value =
                        value.substring(0, SNAPSHOT_LIMITS.MAX_VARIABLE_VALUE_LENGTH) +
                        "...(truncated)";
                }

                const info: VariableInfo = {
                    value,
                    type: v.type ?? "unknown",
                };

                // Expand nested if within depth budget
                if (v.variablesReference > 0 && currentDepth < maxDepth) {
                    const nested = await this.getVariables(
                        v.variablesReference,
                        maxDepth,
                        currentDepth + 1
                    );
                    // Represent nested as JSON-like value
                    const entries = Object.entries(nested);
                    if (entries.length > SNAPSHOT_LIMITS.MAX_ARRAY_DICT_ELEMENTS) {
                        const shown = entries.slice(0, SNAPSHOT_LIMITS.MAX_ARRAY_DICT_ELEMENTS);
                        const remaining = entries.length - SNAPSHOT_LIMITS.MAX_ARRAY_DICT_ELEMENTS;
                        const parts = shown.map(([k, vi]) => `${k}: ${vi.value}`);
                        info.value = `{ ${parts.join(", ")}, ...(+${remaining} more) }`;
                    } else if (entries.length > 0) {
                        const parts = entries.map(([k, vi]) => `${k}: ${vi.value}`);
                        info.value = `{ ${parts.join(", ")} }`;
                    }
                }

                result[v.name] = info;
                count++;
            }
        } catch {
            // Silently handle variable fetch failures
        }

        return result;
    }

    private async getExceptionInfo(
        threadId: number,
        provided?: { description?: string; breakMode?: string }
    ): Promise<ExceptionInfo | undefined> {
        try {
            const response = await this.session.customRequest("exceptionInfo", {
                threadId,
            });
            return {
                type: response.exceptionId ?? "Exception",
                message: response.description ?? provided?.description ?? "Unknown exception",
                breakMode: response.breakMode === "always" ? "always" : "uncaught",
            };
        } catch {
            if (provided) {
                return {
                    type: "Exception",
                    message: provided.description ?? "Unknown exception",
                    breakMode: provided.breakMode === "always" ? "always" : "uncaught",
                };
            }
            return undefined;
        }
    }

    private getOutputDelta(): string {
        const joined = this.outputBuffer.join("");
        // Return last N characters
        if (joined.length > SNAPSHOT_LIMITS.MAX_OUTPUT_LENGTH) {
            return joined.substring(joined.length - SNAPSHOT_LIMITS.MAX_OUTPUT_LENGTH);
        }
        return joined;
    }

    private toWorkspaceRelative(filePath: string): string {
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (!workspaceFolders || workspaceFolders.length === 0) {
            return filePath;
        }

        for (const folder of workspaceFolders) {
            const folderPath = folder.uri.fsPath;
            if (filePath.startsWith(folderPath)) {
                const relative = filePath.substring(folderPath.length);
                // Normalize to forward slashes and remove leading separator
                return relative.replace(/^[\\/]/, "").replace(/\\/g, "/");
            }
        }

        return filePath;
    }
}
