import * as vscode from "vscode";

/**
 * Common input fields shared by tools that need to locate a symbol.
 */
export interface SymbolLocationInput {
    filePath: string;
    symbol: string;
    lineContent?: string;
    line?: number;
}

/**
 * Resolved position from PositionResolver.
 */
export interface ResolvedPosition {
    uri: vscode.Uri;
    position: vscode.Position;
    document: vscode.TextDocument;
}

/**
 * Error codes returned by tools.
 */
export const ErrorCode = {
    SYMBOL_NOT_FOUND: "symbol_not_found",
    NO_PROVIDER: "no_provider",
    SERVER_STARTING: "server_starting",
    FILE_NOT_FOUND: "file_not_found",
    PROVIDER_ERROR: "provider_error",
} as const;

/**
 * Maps vscode.SymbolKind enum values to human-readable strings.
 */
export function symbolKindToString(kind: vscode.SymbolKind): string {
    const map: Record<number, string> = {
        [vscode.SymbolKind.File]: "file",
        [vscode.SymbolKind.Module]: "module",
        [vscode.SymbolKind.Namespace]: "namespace",
        [vscode.SymbolKind.Package]: "package",
        [vscode.SymbolKind.Class]: "class",
        [vscode.SymbolKind.Method]: "method",
        [vscode.SymbolKind.Property]: "property",
        [vscode.SymbolKind.Field]: "field",
        [vscode.SymbolKind.Constructor]: "constructor",
        [vscode.SymbolKind.Enum]: "enum",
        [vscode.SymbolKind.Interface]: "interface",
        [vscode.SymbolKind.Function]: "function",
        [vscode.SymbolKind.Variable]: "variable",
        [vscode.SymbolKind.Constant]: "constant",
        [vscode.SymbolKind.String]: "string",
        [vscode.SymbolKind.Number]: "number",
        [vscode.SymbolKind.Boolean]: "boolean",
        [vscode.SymbolKind.Array]: "array",
        [vscode.SymbolKind.Object]: "object",
        [vscode.SymbolKind.Key]: "key",
        [vscode.SymbolKind.Null]: "null",
        [vscode.SymbolKind.EnumMember]: "enumMember",
        [vscode.SymbolKind.Struct]: "struct",
        [vscode.SymbolKind.Event]: "event",
        [vscode.SymbolKind.Operator]: "operator",
        [vscode.SymbolKind.TypeParameter]: "typeParameter",
    };
    return map[kind] ?? "unknown";
}

/**
 * Parses a kind filter string to a set of matching SymbolKind values.
 * Returns undefined if kind is "all" or not provided.
 */
export function parseKindFilter(kind?: string): Set<vscode.SymbolKind> | undefined {
    if (!kind || kind === "all") {
        return undefined;
    }

    const kindMap: Record<string, vscode.SymbolKind[]> = {
        function: [vscode.SymbolKind.Function],
        class: [vscode.SymbolKind.Class],
        method: [vscode.SymbolKind.Method],
        variable: [vscode.SymbolKind.Variable],
        constant: [vscode.SymbolKind.Constant],
        struct: [vscode.SymbolKind.Struct],
        enum: [vscode.SymbolKind.Enum],
        interface: [vscode.SymbolKind.Interface],
        namespace: [vscode.SymbolKind.Namespace, vscode.SymbolKind.Module],
        property: [vscode.SymbolKind.Property, vscode.SymbolKind.Field],
    };

    const kinds = kindMap[kind.toLowerCase()];
    return kinds ? new Set(kinds) : undefined;
}

/**
 * Converts a file path to a workspace-relative path with forward slashes.
 */
export function toWorkspaceRelativePath(uri: vscode.Uri): string {
    const wsFolder = vscode.workspace.getWorkspaceFolder(uri);
    if (wsFolder) {
        const relative = uri.fsPath.substring(wsFolder.uri.fsPath.length);
        return relative.replace(/\\/g, "/").replace(/^\//, "");
    }
    // Fall back to filename
    const parts = uri.fsPath.replace(/\\/g, "/").split("/");
    return parts[parts.length - 1];
}
