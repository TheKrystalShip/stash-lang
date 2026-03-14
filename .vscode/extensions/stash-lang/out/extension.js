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
const node_1 = require("vscode-languageclient/node");
let client;
function activate(context) {
    const config = vscode.workspace.getConfiguration("stash");
    const customPath = config.get("lspPath", "");
    const serverCommand = customPath || "stash-lsp";
    const serverOptions = {
        run: { command: serverCommand, transport: 0 },
        debug: { command: serverCommand, transport: 0 },
    };
    const clientOptions = {
        documentSelector: [{ scheme: "file", language: "stash" }],
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
}
function deactivate() {
    return client?.stop();
}
//# sourceMappingURL=extension.js.map