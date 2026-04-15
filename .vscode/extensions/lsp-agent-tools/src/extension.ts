import * as vscode from "vscode";
import { HoverTool } from "./tools/hover";
import { DocumentSymbolsTool } from "./tools/documentSymbols";
import { TypeDefinitionTool } from "./tools/typeDefinition";
import { CallHierarchyTool } from "./tools/callHierarchy";
import { WorkspaceSymbolsTool } from "./tools/workspaceSymbols";
import { CodeActionsTool } from "./tools/codeActions";
import { SignatureHelpTool } from "./tools/signatureHelp";

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(
        vscode.lm.registerTool("lsp_hover", new HoverTool()),
        vscode.lm.registerTool("lsp_documentSymbols", new DocumentSymbolsTool()),
        vscode.lm.registerTool("lsp_typeDefinition", new TypeDefinitionTool()),
        vscode.lm.registerTool("lsp_callHierarchy", new CallHierarchyTool()),
        vscode.lm.registerTool("lsp_workspaceSymbols", new WorkspaceSymbolsTool()),
        vscode.lm.registerTool("lsp_codeActions", new CodeActionsTool()),
        vscode.lm.registerTool("lsp_signatureHelp", new SignatureHelpTool()),
    );
}

export function deactivate() {}
