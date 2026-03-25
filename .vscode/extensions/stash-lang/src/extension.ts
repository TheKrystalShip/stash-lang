import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import {
  LanguageClient,
  LanguageClientOptions,
  RevealOutputChannelOn,
  ServerOptions,
} from "vscode-languageclient/node";
import { activateTesting } from "./testing";
import { resolveBinary } from "./resolveBinary";

let client: LanguageClient | undefined;
let debugOutput: vscode.OutputChannel | undefined;
let lspTrace: vscode.OutputChannel | undefined;

export function activate(context: vscode.ExtensionContext) {
  debugOutput = vscode.window.createOutputChannel("Stash Debug");
  context.subscriptions.push(debugOutput);
  debugOutput.appendLine("Stash extension activated");

  const lspOutput = vscode.window.createOutputChannel("Stash Language Server");
  context.subscriptions.push(lspOutput);
  lspTrace = vscode.window.createOutputChannel("Stash Language Server (Trace)");
  context.subscriptions.push(lspTrace);

  const config = vscode.workspace.getConfiguration("stash");
  const customPath: string = config.get<string>("lspPath", "");

  const serverCommand = customPath || resolveBinary("stash-lsp");

  const serverOptions: ServerOptions = {
    run: { command: serverCommand, transport: 0 },
    debug: { command: serverCommand, transport: 0 },
  };

  const indexingEnabled = config.get<boolean>("workspaceIndexing.enabled", false);

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "stash" }],
    outputChannel: lspOutput,
    traceOutputChannel: lspTrace,
    revealOutputChannelOn: RevealOutputChannelOn.Never,
    synchronize: {
      configurationSection: "stash",
      fileEvents: indexingEnabled
        ? vscode.workspace.createFileSystemWatcher("**/*.stash")
        : undefined,
    },
  };

  client = new LanguageClient(
    "stashLanguageServer",
    "Stash Language Server",
    serverOptions,
    clientOptions
  );

  const showRefsDisposable = vscode.commands.registerCommand(
    "stash.showReferences",
    (uriStr: string, positionObj: { line: number; character: number }, locations: Array<{ uri: string; range: { start: { line: number; character: number }; end: { line: number; character: number } } }>) => {
      const uri = vscode.Uri.parse(uriStr);
      const position = new vscode.Position(positionObj.line, positionObj.character);
      const locs = locations.map(
        (loc) =>
          new vscode.Location(
            vscode.Uri.parse(loc.uri),
            new vscode.Range(
              new vscode.Position(loc.range.start.line, loc.range.start.character),
              new vscode.Position(loc.range.end.line, loc.range.end.character)
            )
          )
      );
      vscode.commands.executeCommand("editor.action.showReferences", uri, position, locs);
    }
  );
  context.subscriptions.push(showRefsDisposable);

  client.start();

  // ── Test Explorer ─────────────────────────────────────────────────────────

  try {
    activateTesting(context);
  } catch (err) {
    debugOutput?.appendLine(`Failed to activate testing: ${err}`);
  }

  // ── DAP Debug Adapter ─────────────────────────────────────────────────────

  debugOutput.appendLine("Registering debug adapter descriptor factory for type 'stash'");
  const factory = new StashDebugAdapterFactory(debugOutput);
  const registration = vscode.debug.registerDebugAdapterDescriptorFactory("stash", factory);
  context.subscriptions.push(registration);
}

export function deactivate(): Thenable<void> | undefined {
  lspTrace?.dispose();
  return client?.stop();
}

class StashDebugAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
  private output: vscode.OutputChannel;

  constructor(output: vscode.OutputChannel) {
    this.output = output;
  }

  createDebugAdapterDescriptor(
    session: vscode.DebugSession,
    _executable: vscode.DebugAdapterExecutable | undefined
  ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
    try {
      this.output.appendLine(`createDebugAdapterDescriptor called — session: "${session.name}", type: "${session.type}"`);

      const config = vscode.workspace.getConfiguration("stash");
      const customPath: string = config.get<string>("dapPath", "");
      this.output.appendLine(`dapPath config value: "${customPath}"`);

      const command = customPath || resolveBinary("stash-dap");
      this.output.appendLine(`Resolved command: "${command}"`);

      if (path.isAbsolute(command)) {
        let resolvedPath: string;
        try {
          resolvedPath = fs.realpathSync(command);
          this.output.appendLine(`Resolved symlink to: "${resolvedPath}"`);
        } catch (e) {
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
        } catch {
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
    } catch (err) {
      const msg = `Unexpected error in createDebugAdapterDescriptor: ${err}`;
      this.output.appendLine(msg);
      vscode.window.showErrorMessage(`Stash DAP: ${msg}`);
      return undefined;
    }
  }
}
