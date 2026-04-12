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
exports.StashInlineValuesProvider = void 0;
exports.stripStringsAndComments = stripStringsAndComments;
const vscode = __importStar(require("vscode"));
// Stash keywords that should never be treated as variable names
const KEYWORDS = new Set([
    "let",
    "const",
    "fn",
    "return",
    "if",
    "else",
    "for",
    "in",
    "while",
    "do",
    "break",
    "continue",
    "true",
    "false",
    "null",
    "struct",
    "enum",
    "interface",
    "import",
    "from",
    "as",
    "try",
    "catch",
    "finally",
    "throw",
    "switch",
    "case",
    "default",
    "match",
    "typeof",
    "is",
    "delete",
    "spawn",
    "async",
    "await",
    "elevate",
    "extend",
    "and",
    "or",
    "not",
    "retry",
    "new",
]);
// Regex to find identifiers in a line of Stash code
// Matches word-boundary identifiers that start with a letter or underscore
const IDENTIFIER_RE = /\b([a-zA-Z_]\w*)\b/g;
/**
 * Replaces string literal contents and comments with spaces,
 * preserving character offsets for correct Range mapping.
 */
function stripStringsAndComments(line) {
    let result = "";
    let i = 0;
    while (i < line.length) {
        // Single-line comment
        if (line[i] === "/" && i + 1 < line.length && line[i + 1] === "/") {
            result += " ".repeat(line.length - i);
            break;
        }
        // Hash comment
        if (line[i] === "#") {
            result += " ".repeat(line.length - i);
            break;
        }
        // String literal (double or single quote)
        if (line[i] === '"' || line[i] === "'") {
            const quote = line[i];
            result += " "; // replace opening quote
            i++;
            while (i < line.length && line[i] !== quote) {
                if (line[i] === "\\" && i + 1 < line.length) {
                    result += "  "; // escaped char
                    i += 2;
                }
                else {
                    result += " ";
                    i++;
                }
            }
            if (i < line.length) {
                result += " "; // closing quote
                i++;
            }
            continue;
        }
        result += line[i];
        i++;
    }
    return result;
}
class StashInlineValuesProvider {
    provideInlineValues(document, viewPort, context, _token) {
        const result = [];
        const seen = new Set(); // de-duplicate per line
        // Only show values from the viewport start up to (and including)
        // the line where execution stopped — lines after the stop point
        // haven't executed yet, so their values would be stale/misleading
        const endLine = Math.min(context.stoppedLocation.start.line, viewPort.end.line);
        const startLine = viewPort.start.line;
        for (let line = startLine; line <= endLine; line++) {
            const text = document.lineAt(line).text;
            seen.clear();
            // Skip comment-only lines
            const trimmed = text.trimStart();
            if (trimmed.startsWith("//") || trimmed.startsWith("#")) {
                continue;
            }
            // Strip inline comments and string literals to avoid false matches
            const cleaned = stripStringsAndComments(text);
            let match;
            IDENTIFIER_RE.lastIndex = 0;
            while ((match = IDENTIFIER_RE.exec(cleaned)) !== null) {
                const name = match[1];
                if (KEYWORDS.has(name) || seen.has(name))
                    continue;
                // Skip identifiers that look like function calls (followed by '(')
                const afterIdx = match.index + name.length;
                if (afterIdx < cleaned.length && cleaned[afterIdx] === "(")
                    continue;
                // Skip identifiers that look like property access (preceded by '.')
                if (match.index > 0 && cleaned[match.index - 1] === ".")
                    continue;
                // Skip identifiers immediately after 'fn ' (function declarations)
                if (match.index >= 3 &&
                    cleaned.substring(match.index - 3, match.index) === "fn ")
                    continue;
                seen.add(name);
                const range = new vscode.Range(line, match.index, line, match.index + name.length);
                result.push(new vscode.InlineValueVariableLookup(range, name, true));
            }
        }
        return result;
    }
}
exports.StashInlineValuesProvider = StashInlineValuesProvider;
//# sourceMappingURL=inlineValues.js.map