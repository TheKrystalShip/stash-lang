import * as vscode from "vscode";
import { BreakpointTracker } from "./breakpointTracker";
import { CapabilityTracker } from "./adapterCapabilities";
import { SnapshotBuilder } from "./snapshotBuilder";
import { withTimeout } from "./timeoutController";
import {
    SessionState,
    AdapterCapabilities,
    DebugSnapshot,
    SNAPSHOT_LIMITS,
} from "./types";

interface StoppedEvent {
    threadId?: number;
    reason: string;
    description?: string;
    allThreadsStopped?: boolean;
}

/**
 * Manages the lifecycle of agent-initiated debug sessions.
 * Enforces single-session constraint.
 */
export class DebugSessionManager {
    private _session: vscode.DebugSession | undefined;
    private _state: SessionState = "terminated";
    private _capabilityTracker: CapabilityTracker | undefined;
    private _disposables: vscode.Disposable[] = [];
    private _outputBuffer: string[] = [];
    private _totalOutput = 0;
    private _breakpointsHit = 0;
    private _sessionStartTime: number | undefined;
    private _autoTimeoutTimer: ReturnType<typeof setTimeout> | undefined;
    private _lastToolInteraction: number = Date.now();

    /** Resolves when the session reaches a stopped state */
    private _stoppedPromiseResolve: ((event: StoppedEvent) => void) | undefined;
    /** Resolves when the session terminates */
    private _terminatedPromiseResolve: ((value: "terminated") => void) | undefined;

    readonly breakpointTracker = new BreakpointTracker();

    /** Auto-timeout duration (5 minutes of no tool interaction) */
    private readonly AUTO_TIMEOUT_MS = 5 * 60 * 1000;

    get session(): vscode.DebugSession | undefined {
        return this._session;
    }

    get state(): SessionState {
        return this._state;
    }

    get capabilities(): AdapterCapabilities | undefined {
        return this._capabilityTracker?.capabilities;
    }

    get breakpointsHit(): number {
        return this._breakpointsHit;
    }

    get sessionDuration(): string {
        if (!this._sessionStartTime) {
            return "0s";
        }
        const ms = Date.now() - this._sessionStartTime;
        return `${(ms / 1000).toFixed(1)}s`;
    }

    get capturedOutput(): string {
        return this._outputBuffer.join("");
    }

    /**
     * Register VS Code event listeners. Call once during activation.
     */
    registerEventListeners(): vscode.Disposable[] {
        const disposables: vscode.Disposable[] = [];

        // Track session termination
        disposables.push(
            vscode.debug.onDidTerminateDebugSession((session) => {
                if (session === this._session) {
                    this._state = "terminated";
                    this._terminatedPromiseResolve?.("terminated");
                    this._terminatedPromiseResolve = undefined;
                    // Also resolve stopped promise so continue/step don't hang
                    this._stoppedPromiseResolve?.({
                        reason: "terminated",
                    });
                    this._stoppedPromiseResolve = undefined;
                    this.clearAutoTimeout();
                }
            })
        );

        // Track debug adapter messages via tracker factory
        disposables.push(
            vscode.debug.registerDebugAdapterTrackerFactory("*", {
                createDebugAdapterTracker: (session) => {
                    if (session !== this._session) {
                        // If this is our session being created, the _session might not
                        // be set yet. Use the capability tracker if available.
                        if (this._capabilityTracker && this._state === "starting") {
                            return this.createTracker(this._capabilityTracker);
                        }
                        return undefined;
                    }
                    return this.createTracker(this._capabilityTracker);
                },
            })
        );

        this._disposables = disposables;
        return disposables;
    }

    private createTracker(
        capTracker: CapabilityTracker | undefined
    ): vscode.DebugAdapterTracker {
        const self = this;
        return {
            onDidSendMessage(message: unknown) {
                const msg = message as {
                    type?: string;
                    event?: string;
                    body?: Record<string, unknown>;
                    command?: string;
                };

                // Forward to capability tracker
                capTracker?.onDidSendMessage(message);

                // Handle stopped events
                if (msg.type === "event" && msg.event === "stopped") {
                    const body = msg.body as StoppedEvent | undefined;
                    self._state = "paused";
                    self._breakpointsHit +=
                        body?.reason === "breakpoint" ? 1 : 0;
                    self._stoppedPromiseResolve?.(
                        body ?? { reason: "unknown" }
                    );
                    self._stoppedPromiseResolve = undefined;
                }

                // Handle output events
                if (msg.type === "event" && msg.event === "output") {
                    const body = msg.body as {
                        category?: string;
                        output?: string;
                    } | undefined;
                    if (body?.output) {
                        const cat = body.category ?? "console";
                        if (
                            cat === "stdout" ||
                            cat === "stderr" ||
                            cat === "console"
                        ) {
                            self._totalOutput += body.output.length;
                            if (self._totalOutput <= SNAPSHOT_LIMITS.MAX_SESSION_OUTPUT) {
                                self._outputBuffer.push(body.output);
                            }
                        }
                    }
                }

                // Handle terminated events from DAP
                if (msg.type === "event" && msg.event === "terminated") {
                    self._state = "terminated";
                    self._terminatedPromiseResolve?.("terminated");
                    self._terminatedPromiseResolve = undefined;
                    self._stoppedPromiseResolve?.({
                        reason: "terminated",
                    });
                    self._stoppedPromiseResolve = undefined;
                }
            },
        };
    }

    /**
     * Start a new debug session.
     * If a session is already active, terminates it first.
     */
    async startSession(
        config: vscode.DebugConfiguration,
        workspaceFolder?: vscode.WorkspaceFolder
    ): Promise<void> {
        // Terminate existing session if any
        if (this._session && this._state !== "terminated") {
            await this.stopSession();
        }

        // Reset state
        this._outputBuffer = [];
        this._totalOutput = 0;
        this._breakpointsHit = 0;
        this._sessionStartTime = Date.now();
        this._state = "starting";

        // Create capability tracker for this session
        this._capabilityTracker = new CapabilityTracker();

        // Wait for session to start
        const sessionStarted = new Promise<vscode.DebugSession>((resolve) => {
            const disposable = vscode.debug.onDidStartDebugSession((session) => {
                if (session.name === config.name && session.type === config.type) {
                    disposable.dispose();
                    resolve(session);
                }
            });
            // Timeout cleanup
            setTimeout(() => disposable.dispose(), 30000);
        });

        const started = await vscode.debug.startDebugging(
            workspaceFolder,
            config
        );

        if (!started) {
            this._state = "terminated";
            throw new Error("Failed to start debug session");
        }

        // Wait for the session object
        const session = await withTimeout(sessionStarted, 30000, undefined as unknown as vscode.DebugSession);
        if (!session) {
            this._state = "terminated";
            throw new Error("Debug session did not start within 30 seconds");
        }

        this._session = session;
        this._state = "running";

        // Start auto-timeout tracking
        this.resetAutoTimeout();
    }

    /**
     * Wait for the program to pause (stopped event).
     * Returns the stop reason, or undefined if terminated/timed out.
     */
    async waitForPause(
        timeoutMs: number
    ): Promise<StoppedEvent | "terminated" | "timeout"> {
        if (this._state === "paused") {
            return { reason: "already_paused" };
        }
        if (this._state === "terminated") {
            return "terminated";
        }

        const stoppedPromise = new Promise<StoppedEvent>((resolve) => {
            this._stoppedPromiseResolve = resolve;
        });

        const terminatedPromise = new Promise<"terminated">((resolve) => {
            this._terminatedPromiseResolve = resolve;
        });

        const result = await Promise.race([
            stoppedPromise.then((e) => {
                if (e.reason === "terminated") {
                    return "terminated" as const;
                }
                return e;
            }),
            terminatedPromise,
            new Promise<"timeout">((resolve) =>
                setTimeout(() => resolve("timeout"), timeoutMs)
            ),
        ]);

        // Clean up unresolved promises
        this._stoppedPromiseResolve = undefined;
        this._terminatedPromiseResolve = undefined;

        return result;
    }

    /**
     * Send a continue request to the debug adapter.
     */
    async continue(threadId?: number): Promise<void> {
        this.assertPaused();
        const tid = threadId ?? await this.getMainThreadId();
        this._state = "running";
        await this._session!.customRequest("continue", { threadId: tid });
    }

    /**
     * Send a step request to the debug adapter.
     */
    async step(
        action: "over" | "in" | "out",
        threadId?: number
    ): Promise<void> {
        this.assertPaused();
        const tid = threadId ?? await this.getMainThreadId();
        this._state = "running";

        const command =
            action === "over"
                ? "next"
                : action === "in"
                ? "stepIn"
                : "stepOut";
        await this._session!.customRequest(command, { threadId: tid });
    }

    /**
     * Create a SnapshotBuilder for the current session.
     */
    createSnapshotBuilder(): SnapshotBuilder {
        this.assertSession();
        return new SnapshotBuilder(this._session!, this._outputBuffer);
    }

    /**
     * Evaluate an expression in the given frame.
     */
    async evaluate(
        expression: string,
        frameId: number,
        context: string
    ): Promise<{ result: string; type: string; variablesReference: number }> {
        this.assertPaused();
        const response = await this._session!.customRequest("evaluate", {
            expression,
            frameId,
            context,
        });
        return {
            result: response.result ?? "",
            type: response.type ?? "unknown",
            variablesReference: response.variablesReference ?? 0,
        };
    }

    /**
     * Get the frame ID for a given frame index.
     */
    async getFrameId(
        frameIndex: number,
        threadId?: number
    ): Promise<number> {
        this.assertPaused();
        const tid = threadId ?? await this.getMainThreadId();
        const response = await this._session!.customRequest("stackTrace", {
            threadId: tid,
            startFrame: 0,
            levels: frameIndex + 1,
        });
        const frames: Array<{ id: number }> = response.stackFrames ?? [];
        if (frameIndex >= frames.length) {
            throw new Error(
                `Frame index ${frameIndex} out of range (${frames.length} frames available)`
            );
        }
        return frames[frameIndex].id;
    }

    /**
     * Stop the current debug session.
     */
    async stopSession(): Promise<void> {
        this.clearAutoTimeout();

        if (!this._session || this._state === "terminated") {
            return;
        }

        const terminated = new Promise<"terminated">((resolve) => {
            this._terminatedPromiseResolve = resolve;
        });

        try {
            await vscode.debug.stopDebugging(this._session);
        } catch {
            // Session may already be terminated
        }

        // Wait up to 5s for termination
        await withTimeout(terminated, 5000, "terminated" as const);

        this._state = "terminated";
        this._session = undefined;
        this._terminatedPromiseResolve = undefined;
        this._stoppedPromiseResolve = undefined;
    }

    /**
     * Reset the auto-timeout timer. Called on every tool interaction.
     */
    resetAutoTimeout(): void {
        this._lastToolInteraction = Date.now();
        this.clearAutoTimeout();

        this._autoTimeoutTimer = setTimeout(async () => {
            if (
                this._session &&
                this._state !== "terminated" &&
                Date.now() - this._lastToolInteraction >= this.AUTO_TIMEOUT_MS
            ) {
                await this.stopSession();
                this.breakpointTracker.removeAll();
            }
        }, this.AUTO_TIMEOUT_MS);
    }

    private clearAutoTimeout(): void {
        if (this._autoTimeoutTimer) {
            clearTimeout(this._autoTimeoutTimer);
            this._autoTimeoutTimer = undefined;
        }
    }

    private async getMainThreadId(): Promise<number> {
        this.assertSession();
        const response = await this._session!.customRequest("threads");
        const threads: Array<{ id: number }> = response.threads ?? [];
        if (threads.length === 0) {
            throw new Error("No threads available");
        }
        return threads[0].id;
    }

    private assertSession(): void {
        if (!this._session || this._state === "terminated") {
            throw new Error("no_session");
        }
    }

    private assertPaused(): void {
        this.assertSession();
        if (this._state !== "paused") {
            throw new Error("not_paused");
        }
    }

    /**
     * Dispose all resources.
     */
    dispose(): void {
        this.clearAutoTimeout();
        for (const d of this._disposables) {
            d.dispose();
        }
        this._disposables = [];
    }
}
