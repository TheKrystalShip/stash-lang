import * as vscode from "vscode";

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
export function stripStringsAndComments(line: string): string {
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
        } else {
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

export class StashInlineValuesProvider implements vscode.InlineValuesProvider {
  provideInlineValues(
    document: vscode.TextDocument,
    viewPort: vscode.Range,
    context: vscode.InlineValueContext,
    _token: vscode.CancellationToken,
  ): vscode.InlineValue[] {
    const result: vscode.InlineValue[] = [];
    const seen = new Set<string>(); // de-duplicate per line

    // Only show values from the viewport start up to (and including)
    // the line where execution stopped — lines after the stop point
    // haven't executed yet, so their values would be stale/misleading
    const endLine = Math.min(
      context.stoppedLocation.start.line,
      viewPort.end.line,
    );
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

      let match: RegExpExecArray | null;
      IDENTIFIER_RE.lastIndex = 0;
      while ((match = IDENTIFIER_RE.exec(cleaned)) !== null) {
        const name = match[1];
        if (KEYWORDS.has(name) || seen.has(name)) continue;

        // Skip identifiers that look like function calls (followed by '(')
        const afterIdx = match.index + name.length;
        if (afterIdx < cleaned.length && cleaned[afterIdx] === "(") continue;

        // Skip identifiers that look like property access (preceded by '.')
        if (match.index > 0 && cleaned[match.index - 1] === ".") continue;

        // Skip identifiers immediately after 'fn ' (function declarations)
        if (
          match.index >= 3 &&
          cleaned.substring(match.index - 3, match.index) === "fn "
        ) continue;

        seen.add(name);

        const range = new vscode.Range(
          line,
          match.index,
          line,
          match.index + name.length,
        );
        result.push(new vscode.InlineValueVariableLookup(range, name, true));
      }
    }

    return result;
  }
}
