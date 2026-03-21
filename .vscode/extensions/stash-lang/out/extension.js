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
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = __importStar(require("vscode"));
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
const node_1 = require("vscode-languageclient/node");
const testing_1 = require("./testing");
const resolveBinary_1 = require("./resolveBinary");
let client;
let debugOutput;
function activate(context) {
    debugOutput = vscode.window.createOutputChannel("Stash Debug");
    context.subscriptions.push(debugOutput);
    debugOutput.appendLine("Stash extension activated");
    const config = vscode.workspace.getConfiguration("stash");
    const customPath = config.get("lspPath", "");
    const serverCommand = customPath || (0, resolveBinary_1.resolveBinary)("stash-lsp");
    const serverOptions = {
        run: { command: serverCommand, transport: 0 },
        debug: { command: serverCommand, transport: 0 },
    };
    const clientOptions = {
        documentSelector: [{ scheme: "file", language: "stash" }],
        synchronize: {
            configurationSection: "stash",
        },
    };
    client = new node_1.LanguageClient("stashLanguageServer", "Stash Language Server", serverOptions, clientOptions);
    const showRefsDisposable = vscode.commands.registerCommand("stash.showReferences", (uriStr, positionObj, locations) => {
        const uri = vscode.Uri.parse(uriStr);
        const position = new vscode.Position(positionObj.line, positionObj.character);
        const locs = locations.map((loc) => new vscode.Location(vscode.Uri.parse(loc.uri), new vscode.Range(new vscode.Position(loc.range.start.line, loc.range.start.character), new vscode.Position(loc.range.end.line, loc.range.end.character))));
        vscode.commands.executeCommand("editor.action.showReferences", uri, position, locs);
    });
    context.subscriptions.push(showRefsDisposable);
    client.start();
    // ── Test Explorer ─────────────────────────────────────────────────────────
    try {
        (0, testing_1.activateTesting)(context);
    }
    catch (err) {
        debugOutput?.appendLine(`Failed to activate testing: ${err}`);
    }
    // ── DAP Debug Adapter ─────────────────────────────────────────────────────
    debugOutput.appendLine("Registering debug adapter descriptor factory for type 'stash'");
    const factory = new StashDebugAdapterFactory(debugOutput);
    const registration = vscode.debug.registerDebugAdapterDescriptorFactory("stash", factory);
    context.subscriptions.push(registration);
}
function deactivate() {
    return client?.stop();
}
class StashDebugAdapterFactory {
    constructor(output) {
        this.output = output;
    }
    createDebugAdapterDescriptor(session, _executable) {
        try {
            this.output.appendLine(`createDebugAdapterDescriptor called — session: "${session.name}", type: "${session.type}"`);
            const config = vscode.workspace.getConfiguration("stash");
            const customPath = config.get("dapPath", "");
            this.output.appendLine(`dapPath config value: "${customPath}"`);
            const command = customPath || (0, resolveBinary_1.resolveBinary)("stash-dap");
            this.output.appendLine(`Resolved command: "${command}"`);
            if (path.isAbsolute(command)) {
                let resolvedPath;
                try {
                    resolvedPath = fs.realpathSync(command);
                    this.output.appendLine(`Resolved symlink to: "${resolvedPath}"`);
                }
                catch (e) {
                    const msg = `Failed to resolve symlink for "${command}": ${e}`;
                    this.output.appendLine(msg);
                    vscode.window.showErrorMessage(`Stash DAP: ${msg}`);
                    return undefined;
                }
                const exists = fs.existsSync(resolvedPath);
                this.output.appendLine(`File exists at resolved path: ${exists}`);
                if (!exists) {
                    const msg = `DAP binary not found at resolved path: "${resolvedPath}"`;
                    this.output.appendLine(msg);
                    vscode.window.showErrorMessage(`Stash DAP: ${msg}`);
                    return undefined;
                }
                let executable = true;
                try {
                    fs.accessSync(resolvedPath, fs.constants.X_OK);
                    this.output.appendLine(`File is executable: true`);
                }
                catch {
                    executable = false;
                    this.output.appendLine(`File is executable: false`);
                    const msg = `DAP binary is not executable: "${resolvedPath}"`;
                    vscode.window.showErrorMessage(`Stash DAP: ${msg}`);
                    return undefined;
                }
                this.output.appendLine(`Launching DAP with resolved path: "${resolvedPath}"`);
                return new vscode.DebugAdapterExecutable(resolvedPath);
            }
            this.output.appendLine(`Launching DAP with command: "${command}"`);
            return new vscode.DebugAdapterExecutable(command);
        }
        catch (err) {
            const msg = `Unexpected error in createDebugAdapterDescriptor: ${err}`;
            this.output.appendLine(msg);
            vscode.window.showErrorMessage(`Stash DAP: ${msg}`);
            return undefined;
        }
    }
}
//# sourceMappingURL=extension.js.map