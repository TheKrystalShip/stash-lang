import * as vscode from "vscode";
import * as path from "path";
import { execFile } from "child_process";
import { resolveBinary } from "./resolveBinary";

function getNonce(): string {
  let text = "";
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  for (let i = 0; i < 32; i++) {
    text += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return text;
}

export class StashBytecodeDocument implements vscode.CustomDocument {
  readonly uri: vscode.Uri;

  constructor(uri: vscode.Uri) {
    this.uri = uri;
  }

  dispose(): void {
    // no-op
  }
}

export class StashBytecodeViewerProvider
  implements vscode.CustomReadonlyEditorProvider<StashBytecodeDocument>
{
  static readonly viewType = "stash.bytecodeViewer";

  openCustomDocument(uri: vscode.Uri): StashBytecodeDocument {
    return new StashBytecodeDocument(uri);
  }

  async resolveCustomEditor(
    document: StashBytecodeDocument,
    webviewPanel: vscode.WebviewPanel
  ): Promise<void> {
    webviewPanel.webview.options = { enableScripts: true };

    const update = async () => {
      try {
        const disassembly = await this.getDisassembly(document.uri.fsPath);
        webviewPanel.webview.html = this.renderHtml(
          webviewPanel.webview,
          disassembly,
          document.uri
        );
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        webviewPanel.webview.html = this.renderError(
          webviewPanel.webview,
          message,
          document.uri
        );
      }
    };

    await update();

    const dir = path.dirname(document.uri.fsPath);
    const basename = path.basename(document.uri.fsPath);
    const watcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(vscode.Uri.file(dir), basename)
    );
    watcher.onDidChange(() => update());

    webviewPanel.webview.onDidReceiveMessage(async (msg) => {
      if (msg.type === "refresh") {
        await update();
      }
    });

    webviewPanel.onDidDispose(() => watcher.dispose());
  }

  private getDisassembly(filePath: string): Promise<string> {
    const config = vscode.workspace.getConfiguration("stash");
    const customPath = config.get<string>("interpreterPath", "");
    const stashBin = customPath || resolveBinary("stash");

    return new Promise<string>((resolve, reject) => {
      execFile(
        stashBin,
        [filePath, "--disassemble"],
        { timeout: 10000 },
        (error, stdout, stderr) => {
          if (error) {
            reject(new Error(stderr || error.message));
            return;
          }
          resolve(stdout);
        }
      );
    });
  }

  private highlightLine(line: string): string {
    const escaped = line
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");

    if (escaped.startsWith(";")) {
      return `<span class="comment">${escaped}</span>`;
    }

    if (/^\.(const|globals|code):/.test(escaped)) {
      return `<span class="section">${escaped}</span>`;
    }

    const labelMatch = /^(\.[A-Za-z_][A-Za-z0-9_]*:)(.*)/.exec(escaped);
    if (labelMatch) {
      return `<span class="label">${labelMatch[1]}</span>${labelMatch[2]}`;
    }

    const codeMatch = /^(\s+)([0-9a-f]{4})(\s+)([0-9a-f]{2}(?:\s[0-9a-f]{2})*)(\s+)(\S+)(.*)/.exec(escaped);
    if (codeMatch) {
      const [, indent, offset, sp1, hex, sp2, opcode, rest] = codeMatch;
      return (
        `${indent}<span class="offset">${offset}</span>${sp1}` +
        `<span class="hex">${hex}</span>${sp2}` +
        `<span class="opcode">${opcode}</span>` +
        `<span class="operand">${rest}</span>`
      );
    }

    return escaped;
  }

  private renderHtml(
    webview: vscode.Webview,
    disassembly: string,
    uri: vscode.Uri
  ): string {
    const nonce = getNonce();
    const filename = uri.path.split("/").pop() ?? uri.fsPath;
    const lines = disassembly.split("\n");
    const highlightedLines = lines.map((l) => this.highlightLine(l)).join("\n");

    return `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
  <title>Bytecode: ${filename}</title>
  <style>
    body {
      margin: 0;
      padding: 0;
      background: var(--vscode-editor-background);
      color: var(--vscode-editor-foreground);
      font-family: var(--vscode-editor-font-family, 'Consolas', 'Courier New', monospace);
      font-size: var(--vscode-editor-font-size, 14px);
      line-height: var(--vscode-editor-lineHeight, 1.5);
    }
    .toolbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 6px 16px;
      background: var(--vscode-editorGroupHeader-tabsBackground);
      border-bottom: 1px solid var(--vscode-editorGroupHeader-tabsBorder, transparent);
      position: sticky;
      top: 0;
      z-index: 10;
    }
    .toolbar .title {
      font-weight: 600;
      font-size: 13px;
      opacity: 0.9;
    }
    .toolbar button {
      background: var(--vscode-button-secondaryBackground);
      color: var(--vscode-button-secondaryForeground);
      border: none;
      padding: 4px 10px;
      border-radius: 3px;
      cursor: pointer;
      font-size: 12px;
    }
    .toolbar button:hover {
      background: var(--vscode-button-secondaryHoverBackground);
    }
    .disassembly {
      margin: 0;
      padding: 12px 16px;
      white-space: pre;
      tab-size: 8;
      overflow-x: auto;
    }
    .comment { color: var(--vscode-editorLineNumber-foreground); }
    .section { color: var(--vscode-symbolIcon-namespaceForeground, #4ec9b0); font-weight: bold; }
    .offset { color: var(--vscode-editorLineNumber-foreground); }
    .hex { color: var(--vscode-editorLineNumber-foreground); opacity: 0.6; }
    .opcode { color: var(--vscode-symbolIcon-functionForeground, #dcdcaa); }
    .operand { color: var(--vscode-symbolIcon-variableForeground, #9cdcfe); }
    .label { color: var(--vscode-symbolIcon-enumeratorForeground, #b5cea8); font-weight: bold; }
    .source-line { color: var(--vscode-editorLineNumber-foreground); font-style: italic; }
    .error { color: var(--vscode-errorForeground); padding: 16px; }
  </style>
</head>
<body>
  <div class="toolbar">
    <span class="title">${filename}</span>
    <button id="refreshBtn" title="Refresh disassembly">&#x21BB; Refresh</button>
  </div>
  <pre class="disassembly">${highlightedLines}</pre>
  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    document.getElementById('refreshBtn').addEventListener('click', () => {
      vscode.postMessage({ type: 'refresh' });
    });
  </script>
</body>
</html>`;
  }

  private renderError(
    webview: vscode.Webview,
    message: string,
    uri: vscode.Uri
  ): string {
    const nonce = getNonce();
    const filename = uri.path.split("/").pop() ?? uri.fsPath;
    const escaped = message
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");

    return `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
  <title>Bytecode: ${filename}</title>
  <style>
    body {
      margin: 0;
      padding: 0;
      background: var(--vscode-editor-background);
      color: var(--vscode-editor-foreground);
      font-family: var(--vscode-editor-font-family, 'Consolas', 'Courier New', monospace);
      font-size: var(--vscode-editor-font-size, 14px);
    }
    .error { color: var(--vscode-errorForeground); padding: 16px; }
  </style>
</head>
<body>
  <div class="error">Failed to disassemble: ${escaped}</div>
  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
  </script>
</body>
</html>`;
  }
}
