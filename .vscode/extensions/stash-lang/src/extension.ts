import * as vscode from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

export function activate(context: vscode.ExtensionContext) {
  const config = vscode.workspace.getConfiguration("stash");
  const customPath: string = config.get<string>("lspPath", "");

  const serverCommand = customPath || "stash-lsp";

  const serverOptions: ServerOptions = {
    run: { command: serverCommand, transport: 0 },
    debug: { command: serverCommand, transport: 0 },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "stash" }],
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
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}
