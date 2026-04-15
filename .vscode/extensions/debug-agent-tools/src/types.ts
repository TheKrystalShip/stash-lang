// ── Debug Snapshot ──────────────────────────────────────────────────────────

export interface DebugSnapshot {
    status: "paused";
    reason: "breakpoint" | "step" | "exception" | "entry" | "pause";
    file: string;
    line: number;
    column: number;
    sourceContext: SourceContext;
    callStack: StackFrame[];
    locals: VariableMap;
    closures: VariableMap;
    exception?: ExceptionInfo;
    output: string;
}

export interface SourceContext {
    before: string[];
    current: string;
    after: string[];
}

export interface StackFrame {
    name: string;
    file: string;
    line: number;
}

export interface VariableInfo {
    value: string;
    type: string;
}

export type VariableMap = Record<string, VariableInfo>;

export interface ExceptionInfo {
    type: string;
    message: string;
    breakMode: "uncaught" | "always";
}

// ── Tool Results ────────────────────────────────────────────────────────────

export interface RunningResult {
    status: "running";
    sessionId: string;
}

export interface TerminatedResult {
    status: "terminated";
    exitCode?: number;
    output: string;
}

export interface TimeoutResult {
    status: "running";
    message: string;
}

export interface SessionStoppedResult {
    status: "terminated";
    duration: string;
    output: string;
    breakpointsHit: number;
}

export interface NoSessionResult {
    status: "no_session";
}

export interface BreakpointResult {
    line: number;
    verified: boolean;
    condition?: string;
    hitCondition?: string;
    logMessage?: string;
    message?: string;
}

export interface SetBreakpointsResult {
    file: string;
    breakpoints: BreakpointResult[];
}

export interface RemoveBreakpointsResult {
    removed: number;
}

export interface EvaluateResult {
    expression: string;
    result: string;
    type: string;
    variablesReference: number;
}

export interface ErrorResult {
    error: string;
    message: string;
    expression?: string;
    output?: string;
}

export type DebugToolResult =
    | DebugSnapshot
    | RunningResult
    | TerminatedResult
    | TimeoutResult
    | SessionStoppedResult
    | NoSessionResult
    | SetBreakpointsResult
    | RemoveBreakpointsResult
    | EvaluateResult
    | ErrorResult;

// ── Tool Inputs ─────────────────────────────────────────────────────────────

export interface StartSessionInput {
    program: string;
    debugType?: string;
    args?: string[];
    cwd?: string;
    env?: Record<string, string>;
    stopOnEntry?: boolean;
    exceptionBreakpoints?: "none" | "uncaught" | "all";
    noDebug?: boolean;
    configuration?: string;
}

export interface SetBreakpointsInput {
    file: string;
    breakpoints: {
        line: number;
        condition?: string;
        hitCondition?: string;
        logMessage?: string;
    }[];
}

export interface RemoveBreakpointsInput {
    file?: string;
}

export interface ContinueInput {
    threadId?: number;
    timeout?: number;
}

export interface StepInput {
    action: "over" | "in" | "out";
    threadId?: number;
    count?: number;
}

export interface GetSnapshotInput {
    variableDepth?: number;
    stackDepth?: number;
    includeGlobals?: boolean;
}

export interface EvaluateInput {
    expression: string;
    frameIndex?: number;
    context?: "watch" | "repl" | "hover";
}

export interface StopSessionInput {
    captureOutput?: boolean;
}

// ── Session State ───────────────────────────────────────────────────────────

export type SessionState = "starting" | "running" | "paused" | "terminated";

// ── Adapter Capabilities ────────────────────────────────────────────────────

export interface AdapterCapabilities {
    supportsConditionalBreakpoints: boolean;
    supportsHitConditionalBreakpoints: boolean;
    supportsLogPoints: boolean;
    supportsEvaluateForHovers: boolean;
    supportsStepBack: boolean;
    supportsSetVariable: boolean;
    supportsExceptionOptions: boolean;
    exceptionBreakpointFilters: ExceptionBreakpointFilter[];
}

export interface ExceptionBreakpointFilter {
    filter: string;
    label: string;
    default?: boolean;
    supportsCondition?: boolean;
}

// ── Size Limits ─────────────────────────────────────────────────────────────

export const SNAPSHOT_LIMITS = {
    SOURCE_CONTEXT_LINES_BEFORE: 2,
    SOURCE_CONTEXT_LINES_AFTER: 2,
    MAX_CALL_STACK_FRAMES: 5,
    MAX_LOCAL_VARIABLES: 20,
    MAX_VARIABLE_VALUE_LENGTH: 200,
    MAX_NESTED_DEPTH: 1,
    MAX_ARRAY_DICT_ELEMENTS: 10,
    MAX_OUTPUT_LENGTH: 500,
    MAX_SESSION_OUTPUT: 10 * 1024, // 10KB
} as const;

// ── Environment Safety ──────────────────────────────────────────────────────

export const BLOCKED_ENV_VARS = new Set([
    "AWS_SECRET_ACCESS_KEY",
    "AWS_SESSION_TOKEN",
    "GITHUB_TOKEN",
    "GH_TOKEN",
    "GITLAB_TOKEN",
    "DATABASE_PASSWORD",
    "DB_PASSWORD",
    "SECRET_KEY",
    "PRIVATE_KEY",
    "API_SECRET",
    "CLIENT_SECRET",
    "ENCRYPTION_KEY",
    "JWT_SECRET",
    "SSH_PRIVATE_KEY",
    "NPM_TOKEN",
    "NUGET_API_KEY",
    "PYPI_TOKEN",
]);


