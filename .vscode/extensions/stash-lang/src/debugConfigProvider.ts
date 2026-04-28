import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";

// Preferred entry-point names, checked in order.
const ENTRY_POINT_CANDIDATES = ["index.stash"];

// Max number of .stash files to surface as separate launch configs.
const MAX_CONFIGS = 5;

/**
 * Provides smart debug launch configurations for Stash workspaces.
 *
 * **Initial configurations** (triggered when the user clicks
 * "create a launch.json file"):
 * - Looks for a preferred entry point (`main.stash`, `index.stash`, `build.stash`)
 *   at the workspace root. If found, generates a launch config targeting it.
 * - Falls back to scanning for `.stash` files (excluding `*.test.stash`) in the
 *   workspace root. Returns one config per file, up to five.
 * - If nothing is found, returns a generic config pointing to `${file}`.
 *
 * **Configuration resolution** (triggered on every debug session launch):
 * - If the user launches a debug session with no launch.json, fills in
 *   reasonable defaults targeting the active editor.
 * - Fills in a missing `program` field with the active editor's file path.
 */
export class StashDebugConfigurationProvider implements vscode.DebugConfigurationProvider {

    // ── Initial configurations ─────────────────────────────────────────────────

    async provideDebugConfigurations(
        folder: vscode.WorkspaceFolder | undefined,
    ): Promise<vscode.DebugConfiguration[]> {
        if (!folder) {
            return [this.makeConfig("${file}", "${workspaceFolder}")];
        }

        const root = folder.uri.fsPath;

        // 1. Preferred entry points
        for (const candidate of ENTRY_POINT_CANDIDATES) {
            const fullPath = path.join(root, candidate);
            if (fs.existsSync(fullPath)) {
                return [this.makeConfig(fullPath, root)];
            }
        }

        // 2. Any .stash file at the workspace root (not test files)
        const found = await vscode.workspace.findFiles(
            new vscode.RelativePattern(folder, "*.stash"),
            new vscode.RelativePattern(folder, "**/*.test.stash"),
            MAX_CONFIGS,
        );

        if (found.length > 0) {
            return found.map(uri => this.makeConfig(uri.fsPath, root));
        }

        // 3. Generic fallback — active file
        return [this.makeConfig("${file}", root)];
    }

    // ── Resolution at launch time ──────────────────────────────────────────────

    resolveDebugConfiguration(
        folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration,
    ): vscode.DebugConfiguration {
        // F5 with no launch.json at all: all fields are empty.
        if (!config.type && !config.request && !config.name) {
            const activeFile = vscode.window.activeTextEditor?.document.fileName;
            if (!activeFile?.endsWith(".stash")) {
                // Not a Stash file — don't intercept.
                return config;
            }
            return {
                type: "stash",
                request: "launch",
                name: `Debug ${path.basename(activeFile, ".stash")}`,
                program: activeFile,
                stopOnEntry: false,
                cwd: folder?.uri.fsPath ?? "${workspaceFolder}",
                args: [],
            };
        }

        // launch.json exists but program is missing: fill in the active file.
        if (!config.program) {
            config.program =
                vscode.window.activeTextEditor?.document.fileName ?? "${file}";
        }

        return config;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private makeConfig(
        program: string,
        cwd: string,
    ): vscode.DebugConfiguration {
        const scriptName = program === "${file}"
            ? "${fileBasenameNoExtension}"
            : path.basename(program, ".stash");

        return {
            type: "stash",
            request: "launch",
            name: `Debug ${scriptName}`,
            program,
            stopOnEntry: false,
            cwd,
            args: [],
        };
    }
}
