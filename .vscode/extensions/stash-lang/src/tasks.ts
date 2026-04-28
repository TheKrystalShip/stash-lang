import * as vscode from "vscode";
import { resolveBinary } from "./resolveBinary";

// ── Task definition ────────────────────────────────────────────────────────────

export interface StashTaskDefinition extends vscode.TaskDefinition {
    /** Must be "stash" */
    type: "stash";
    /** The operation to run */
    command: "test" | "check" | "format" | "compile";
    /**
     * Path to the target .stash file. Accepts VS Code variables (e.g. `${file}`).
     * Defaults to `${file}` (the currently active editor) when omitted.
     * Only used by the `test` and `compile` commands.
     */
    file?: string;
}

// ── Task provider ──────────────────────────────────────────────────────────────

/**
 * Provides VS Code tasks for the four Stash toolchain operations:
 *
 * - **test**    — Run a Stash test file through the TAP harness (`stash <file> --test`)
 * - **check**   — Save all documents and surface the Problems panel (diagnostics
 *                 are provided by the running Stash LSP in real time)
 * - **format**  — Format the active document via the Stash LSP (`editor.action.formatDocument`)
 * - **compile** — Compile a script to `.stashc` bytecode via `stash --compile`
 *
 * `check` and `format` delegate to VS Code commands backed by the Stash LSP server
 * rather than launching external binaries. The LSP's `FormattingHandler` produces
 * proper in-editor edits (with undo history), and its `SemanticValidator` keeps the
 * Problems panel updated on every save — no separate `stash-check` process needed.
 *
 * `test` and `compile` still launch the `stash` interpreter binary. Binary resolution:
 * 1. The `stash.interpreterPath` extension setting, if non-empty
 * 2. `resolveBinary()` — checks system PATH then `~/.local/bin/` (Unix)
 */
export class StashTaskProvider implements vscode.TaskProvider<vscode.Task> {
    public static readonly taskType = "stash";

    // ── TaskProvider API ───────────────────────────────────────────────────────

    provideTasks(): vscode.Task[] {
        return [
            this.buildTask({ type: "stash", command: "test" },    vscode.TaskScope.Workspace),
            this.buildTask({ type: "stash", command: "check" },   vscode.TaskScope.Workspace),
            this.buildTask({ type: "stash", command: "format" },  vscode.TaskScope.Workspace),
            this.buildTask({ type: "stash", command: "compile" }, vscode.TaskScope.Workspace),
        ];
    }

    resolveTask(task: vscode.Task): vscode.Task | undefined {
        const def = task.definition as StashTaskDefinition;
        if (def.type !== StashTaskProvider.taskType || !def.command) {
            return undefined;
        }
        return this.buildTask(def, task.scope ?? vscode.TaskScope.Workspace);
    }

    // ── Internal builders ──────────────────────────────────────────────────────

    private buildTask(
        def: StashTaskDefinition,
        scope: vscode.TaskScope | vscode.WorkspaceFolder,
    ): vscode.Task {
        const config = vscode.workspace.getConfiguration("stash");

        switch (def.command) {

            // ── Shell tasks (invoke the stash binary) ──────────────────────────

            case "test": {
                const file = def.file ?? "${file}";
                const bin = config.get<string>("interpreterPath", "") || resolveBinary("stash");
                const task = new vscode.Task(
                    def, scope, "test", "stash",
                    new vscode.ShellExecution(`"${bin}" "${file}" --test`),
                    [],
                );
                task.group = vscode.TaskGroup.Test;
                return task;
            }

            case "compile": {
                const file = def.file ?? "${file}";
                const bin = config.get<string>("interpreterPath", "") || resolveBinary("stash");
                const task = new vscode.Task(
                    def, scope, "compile", "stash",
                    new vscode.ShellExecution(`"${bin}" --compile "${file}"`),
                    [],
                );
                task.group = vscode.TaskGroup.Build;
                return task;
            }

            // ── LSP-backed tasks (delegate to VS Code commands) ────────────────

            case "check": {
                // Save all dirty documents so the LSP re-analyses them, then
                // bring the Problems panel into view. The LSP's SemanticValidator
                // already keeps diagnostics up to date on every change/save.
                const task = new vscode.Task(
                    def, scope, "check", "stash",
                    new vscode.CustomExecution(async (): Promise<vscode.Pseudoterminal> => {
                        const writeEmitter = new vscode.EventEmitter<string>();
                        const closeEmitter = new vscode.EventEmitter<number>();
                        return {
                            onDidWrite: writeEmitter.event,
                            onDidClose: closeEmitter.event,
                            open(_dims) {
                                void vscode.commands
                                    .executeCommand("workbench.action.files.saveAll")
                                    .then(() => vscode.commands.executeCommand(
                                        "workbench.panel.markers.view.focus"))
                                    .then(() => closeEmitter.fire(0));
                            },
                            close() { /* no-op */ },
                        };
                    }),
                    [],
                );
                task.group = vscode.TaskGroup.Build;
                return task;
            }

            case "format": {
                // Invoke the LSP's FormattingHandler via VS Code's built-in
                // format command. This produces in-editor edits with full undo
                // history, unlike running stash-format which writes to stdout
                // (or disk with -w) outside the editor's knowledge.
                const task = new vscode.Task(
                    def, scope, "format", "stash",
                    new vscode.CustomExecution(async (): Promise<vscode.Pseudoterminal> => {
                        const writeEmitter = new vscode.EventEmitter<string>();
                        const closeEmitter = new vscode.EventEmitter<number>();
                        return {
                            onDidWrite: writeEmitter.event,
                            onDidClose: closeEmitter.event,
                            open(_dims) {
                                void vscode.commands
                                    .executeCommand("editor.action.formatDocument")
                                    .then(
                                        () => closeEmitter.fire(0),
                                        (err: unknown) => {
                                            writeEmitter.fire(`Stash LSP format failed: ${err}\r\n`);
                                            closeEmitter.fire(1);
                                        },
                                    );
                            },
                            close() { /* no-op */ },
                        };
                    }),
                    [],
                );
                task.group = vscode.TaskGroup.Build;
                return task;
            }

            default:
                throw new Error(`Unknown stash task command: ${(def as StashTaskDefinition).command}`);
        }
    }
}
