import * as vscode from "vscode";

export type ResolveResult =
    | { debugType: string }
    | { error: string; availableAdapters: string[] };

export function getAvailableDebugAdapters(): string[] {
    const types = new Set<string>();
    for (const ext of vscode.extensions.all) {
        const debuggers: { type?: string }[] | undefined =
            ext.packageJSON?.contributes?.debuggers;
        if (Array.isArray(debuggers)) {
            for (const dbg of debuggers) {
                if (typeof dbg.type === "string") {
                    types.add(dbg.type);
                }
            }
        }
    }
    return [...types].sort();
}

export async function resolveDebugType(programPath: string): Promise<ResolveResult> {
    let languageId: string;
    try {
        const uri = vscode.Uri.file(programPath);
        const doc = await vscode.workspace.openTextDocument(uri);
        languageId = doc.languageId;
    } catch {
        return {
            error: `Cannot open file '${programPath}'.`,
            availableAdapters: getAvailableDebugAdapters(),
        };
    }

    if (languageId === "plaintext") {
        return {
            error: `VS Code could not identify the language of '${programPath}'.`,
            availableAdapters: getAvailableDebugAdapters(),
        };
    }

    for (const ext of vscode.extensions.all) {
        const debuggers: { type?: string; languages?: string[] }[] | undefined =
            ext.packageJSON?.contributes?.debuggers;
        if (!Array.isArray(debuggers)) {
            continue;
        }
        for (const dbg of debuggers) {
            if (Array.isArray(dbg.languages) && dbg.languages.includes(languageId)) {
                return { debugType: dbg.type as string };
            }
        }
    }

    return {
        error: `No debug adapter found for language '${languageId}'.`,
        availableAdapters: getAvailableDebugAdapters(),
    };
}
