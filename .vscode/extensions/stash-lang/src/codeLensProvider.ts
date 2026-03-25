import * as vscode from 'vscode';

const TEST_CALL_RE = /\btest\.(describe|it|skip)\s*\(\s*(['"`])((?:[^\\]|\\.)*?)\2/g;

export class StashTestCodeLensProvider implements vscode.CodeLensProvider {
    private _onDidChange = new vscode.EventEmitter<void>();
    readonly onDidChangeCodeLenses = this._onDidChange.event;

    refresh(): void {
        this._onDidChange.fire();
    }

    provideCodeLenses(document: vscode.TextDocument): vscode.CodeLens[] {
        if (!document.fileName.endsWith('.test.stash')) {
            return [];
        }

        const text = document.getText();
        const codeLenses: vscode.CodeLens[] = [];

        TEST_CALL_RE.lastIndex = 0;
        let match: RegExpExecArray | null;

        while ((match = TEST_CALL_RE.exec(text)) !== null) {
            const kind = match[1];
            const name = match[3].replace(/\\(['"`\\])/g, '$1');
            const pos = document.positionAt(match.index);

            // Skip matches inside single-line comments
            if (document.lineAt(pos.line).text.trimStart().startsWith('//')) {
                continue;
            }

            const range = new vscode.Range(pos, pos);

            const runTitle = kind === 'describe'
                ? '$(play) Run'
                : '$(play) Run';
            const debugTitle = kind === 'describe'
                ? '$(debug) Debug'
                : '$(debug) Debug';

            codeLenses.push(new vscode.CodeLens(range, {
                title: runTitle,
                command: 'stash.runTestByName',
                arguments: [document.uri.fsPath, name, pos.line],
                tooltip: `Run "${name}"`,
            }));

            codeLenses.push(new vscode.CodeLens(range, {
                title: debugTitle,
                command: 'stash.debugTestByName',
                arguments: [document.uri.fsPath, name, pos.line],
                tooltip: `Debug "${name}"`,
            }));
        }

        return codeLenses;
    }
}
