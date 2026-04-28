"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.StashTaskProvider = void 0;
const vscode = __importStar(require("vscode"));
const resolveBinary_1 = require("./resolveBinary");
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
class StashTaskProvider {
    // ── TaskProvider API ───────────────────────────────────────────────────────
    provideTasks() {
        return [
            this.buildTask({ type: "stash", command: "test" }, vscode.TaskScope.Workspace),
            this.buildTask({ type: "stash", command: "check" }, vscode.TaskScope.Workspace),
            this.buildTask({ type: "stash", command: "format" }, vscode.TaskScope.Workspace),
            this.buildTask({ type: "stash", command: "compile" }, vscode.TaskScope.Workspace),
        ];
    }
    resolveTask(task) {
        const def = task.definition;
        if (def.type !== StashTaskProvider.taskType || !def.command) {
            return undefined;
        }
        return this.buildTask(def, task.scope ?? vscode.TaskScope.Workspace);
    }
    // ── Internal builders ──────────────────────────────────────────────────────
    buildTask(def, scope) {
        const config = vscode.workspace.getConfiguration("stash");
        switch (def.command) {
            // ── Shell tasks (invoke the stash binary) ──────────────────────────
            case "test": {
                const file = def.file ?? "${file}";
                const bin = config.get("interpreterPath", "") || (0, resolveBinary_1.resolveBinary)("stash");
                const task = new vscode.Task(def, scope, "test", "stash", new vscode.ShellExecution(`"${bin}" "${file}" --test`), []);
                task.group = vscode.TaskGroup.Test;
                return task;
            }
            case "compile": {
                const file = def.file ?? "${file}";
                const bin = config.get("interpreterPath", "") || (0, resolveBinary_1.resolveBinary)("stash");
                const task = new vscode.Task(def, scope, "compile", "stash", new vscode.ShellExecution(`"${bin}" --compile "${file}"`), []);
                task.group = vscode.TaskGroup.Build;
                return task;
            }
            // ── LSP-backed tasks (delegate to VS Code commands) ────────────────
            case "check": {
                // Save all dirty documents so the LSP re-analyses them, then
                // bring the Problems panel into view. The LSP's SemanticValidator
                // already keeps diagnostics up to date on every change/save.
                const task = new vscode.Task(def, scope, "check", "stash", new vscode.CustomExecution(async () => {
                    const writeEmitter = new vscode.EventEmitter();
                    const closeEmitter = new vscode.EventEmitter();
                    return {
                        onDidWrite: writeEmitter.event,
                        onDidClose: closeEmitter.event,
                        open(_dims) {
                            void vscode.commands
                                .executeCommand("workbench.action.files.saveAll")
                                .then(() => vscode.commands.executeCommand("workbench.panel.markers.view.focus"))
                                .then(() => closeEmitter.fire(0));
                        },
                        close() { },
                    };
                }), []);
                task.group = vscode.TaskGroup.Build;
                return task;
            }
            case "format": {
                // Invoke the LSP's FormattingHandler via VS Code's built-in
                // format command. This produces in-editor edits with full undo
                // history, unlike running stash-format which writes to stdout
                // (or disk with -w) outside the editor's knowledge.
                const task = new vscode.Task(def, scope, "format", "stash", new vscode.CustomExecution(async () => {
                    const writeEmitter = new vscode.EventEmitter();
                    const closeEmitter = new vscode.EventEmitter();
                    return {
                        onDidWrite: writeEmitter.event,
                        onDidClose: closeEmitter.event,
                        open(_dims) {
                            void vscode.commands
                                .executeCommand("editor.action.formatDocument")
                                .then(() => closeEmitter.fire(0), (err) => {
                                writeEmitter.fire(`Stash LSP format failed: ${err}\r\n`);
                                closeEmitter.fire(1);
                            });
                        },
                        close() { },
                    };
                }), []);
                task.group = vscode.TaskGroup.Build;
                return task;
            }
            default:
                throw new Error(`Unknown stash task command: ${def.command}`);
        }
    }
}
exports.StashTaskProvider = StashTaskProvider;
StashTaskProvider.taskType = "stash";
//# sourceMappingURL=tasks.js.map