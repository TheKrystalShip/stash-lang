import { spawn } from 'child_process';
import { TapParser } from './tapParser';

export interface DiscoveredTest {
    /** Fully qualified name: "file.test.stash > describe > test" */
    fullName: string;
    /** Just the test name (e.g., "addition") */
    label: string;
    /** Nesting path: ["file.test.stash", "describe block name"] */
    ancestors: string[];
    /** Source file URI */
    uri: string;
    /** 0-based line number in the source file */
    line: number;
    /** 0-based column number */
    column: number;
    /** Entry kind — describes are containers, it/skip are leaf tests */
    kind: 'describe' | 'it' | 'skip';
}

// ---------------------------------------------------------------------------
// Static discovery
// ---------------------------------------------------------------------------

/**
 * Matches test.describe("name", ...), test.it("name", ...), or test.skip("name", ...) calls.
 * Group 1: "describe" | "it" | "skip"
 * Group 2: the string delimiter (' " `)
 * Group 3: the literal name
 */
const CALL_RE = /\btest\.(describe|it|skip)\s*\(\s*(['"`])((?:[^\\]|\\.)*?)\2/g;

/**
 * Scan `text` for describe/test patterns and return discovered tests.
 * Line numbers are 0-based (VS Code convention).
 */
export function parseTestsFromText(text: string, fileName: string): DiscoveredTest[] {
    const results: DiscoveredTest[] = [];

    // Pre-compute line start offsets for fast offset→line lookup.
    const lineOffsets: number[] = [0];
    for (let i = 0; i < text.length; i++) {
        if (text[i] === '\n') {
            lineOffsets.push(i + 1);
        }
    }

    function offsetToPosition(offset: number): { line: number; column: number } {
        let lo = 0;
        let hi = lineOffsets.length - 1;
        while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (lineOffsets[mid] <= offset) {
                lo = mid;
            } else {
                hi = mid - 1;
            }
        }
        return { line: lo, column: offset - lineOffsets[lo] };
    }

    // Stack of { name, braceDepth } — braceDepth is the depth *before* the
    // opening brace of the describe callback was counted.
    const describeStack: Array<{ name: string; entryDepth: number }> = [];

    // Current brace depth considering the entire file processed so far.
    let braceDepth = 0;

    // Cursor into the text — we advance it as we process tokens.
    let cursor = 0;

    /**
     * Advance `cursor` past the next "token" (string literal, comment, or
     * single character) and update `braceDepth` when a bare `{` or `}` is
     * encountered.  Returns false when the end of the text is reached.
     */
    function advanceCursor(): boolean {
        if (cursor >= text.length) {
            return false;
        }

        const ch = text[cursor];

        // Single-line comment
        if (ch === '/' && text[cursor + 1] === '/') {
            const end = text.indexOf('\n', cursor);
            cursor = end === -1 ? text.length : end + 1;
            return true;
        }

        // Multi-line comment
        if (ch === '/' && text[cursor + 1] === '*') {
            const end = text.indexOf('*/', cursor + 2);
            cursor = end === -1 ? text.length : end + 2;
            return true;
        }

        // String literal — skip over escaped chars and the closing quote.
        if (ch === '"' || ch === "'" || ch === '`') {
            cursor++;
            while (cursor < text.length && text[cursor] !== ch) {
                if (text[cursor] === '\\') {
                    cursor++; // skip escaped char
                }
                cursor++;
            }
            cursor++; // skip closing quote
            return true;
        }

        if (ch === '{') {
            braceDepth++;
            cursor++;
            return true;
        }

        if (ch === '}') {
            braceDepth--;
            // Pop describe blocks that ended at this depth.
            while (
                describeStack.length > 0 &&
                braceDepth <= describeStack[describeStack.length - 1].entryDepth
            ) {
                describeStack.pop();
            }
            cursor++;
            return true;
        }

        cursor++;
        return true;
    }

    CALL_RE.lastIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = CALL_RE.exec(text)) !== null) {
        const matchStart = match.index;
        const kind = match[1] as 'describe' | 'it' | 'skip';
        const name = match[3].replace(/\\(['"`\\])/g, '$1');

        // Advance the brace-depth cursor up to (but not including) this match.
        while (cursor < matchStart) {
            advanceCursor();
        }

        const pos = offsetToPosition(matchStart);

        if (kind === 'describe') {
            // After the match, find the opening `{` of the callback body.
            // We need to skip the `)` pattern — typically: describe("name", () => {
            // We just scan forward from the end of the match for the `{`.
            let scanPos = match.index + match[0].length;
            let parenDepth = 1; // we're inside the describe( call
            let foundBrace = false;

            while (scanPos < text.length && !foundBrace) {
                const c = text[scanPos];
                if (c === '/' && text[scanPos + 1] === '/') {
                    const end = text.indexOf('\n', scanPos);
                    scanPos = end === -1 ? text.length : end + 1;
                    continue;
                }
                if (c === '/' && text[scanPos + 1] === '*') {
                    const end = text.indexOf('*/', scanPos + 2);
                    scanPos = end === -1 ? text.length : end + 2;
                    continue;
                }
                if (c === '"' || c === "'" || c === '`') {
                    scanPos++;
                    while (scanPos < text.length && text[scanPos] !== c) {
                        if (text[scanPos] === '\\') scanPos++;
                        scanPos++;
                    }
                    scanPos++;
                    continue;
                }
                if (c === '(') { parenDepth++; scanPos++; continue; }
                if (c === ')') {
                    parenDepth--;
                    scanPos++;
                    continue;
                }
                if (c === '{') {
                    if (parenDepth === 1) {
                        // This is the callback opening brace — record depth *before* it.
                        foundBrace = true;
                        // Don't advance scanPos; let the main cursor handle it.
                    }
                    break;
                }
                scanPos++;
            }

            // Bring the brace-depth cursor up to (but not past) this `{`.
            while (cursor < scanPos) {
                advanceCursor();
            }

            // Now push the describe onto the stack. After advancing past `{`,
            // braceDepth will be incremented — record the depth *before* that.
            describeStack.push({ name, entryDepth: braceDepth });
            // Emit a describe entry so buildTestItems can set its range
            results.push({
                fullName: [fileName, ...describeStack.map(d => d.name)].join(' > '),
                label: name,
                ancestors: [
                    fileName,
                    ...describeStack.slice(0, -1).map(d => d.name),
                ],
                uri: fileName,
                line: pos.line,
                column: pos.column,
                kind: 'describe',
            });
            // CALL_RE continues from after the match; cursor will catch up on the
            // next iteration.

        } else {
            // kind === 'it' or 'skip'
            const ancestors: string[] = [
                fileName,
                ...describeStack.map(d => d.name),
            ];
            const fullName = [...ancestors, name].join(' > ');

            results.push({
                fullName,
                label: name,
                ancestors,
                uri: fileName,
                line: pos.line,
                column: pos.column,
                kind,
            });
        }
    }

    return results;
}

// ---------------------------------------------------------------------------
// Dynamic discovery
// ---------------------------------------------------------------------------

const DISCOVERY_TIMEOUT_MS = 10_000;

/**
 * Run the Stash interpreter in `--test --test-list` mode and collect
 * discovered tests via TAP discovery comments.
 */
export async function discoverTestsDynamic(
    filePath: string,
    interpreterPath: string,
): Promise<DiscoveredTest[]> {
    return new Promise<DiscoveredTest[]>((resolve) => {
        const discovered: DiscoveredTest[] = [];

        let process_: ReturnType<typeof spawn>;
        try {
            process_ = spawn(interpreterPath, ['--test', '--test-list', filePath], {
                stdio: ['ignore', 'pipe', 'pipe'],
            });
        } catch {
            resolve([]);
            return;
        }

        const parser = new TapParser({
            onTestDiscovered(name, file, line, column) {
                const segments = name.split(' > ');
                const label = segments[segments.length - 1];
                const ancestors = segments.slice(0, -1);

                discovered.push({
                    fullName: name,
                    label,
                    ancestors,
                    uri: file,
                    line: line - 1,     // convert 1-based → 0-based
                    column: column - 1, // convert 1-based → 0-based
                    kind: 'it',
                });
            },
        });

        const timer = setTimeout(() => {
            process_.kill();
            resolve(discovered);
        }, DISCOVERY_TIMEOUT_MS);

        process_.stdout?.on('data', (chunk: Buffer) => {
            parser.feed(chunk.toString());
        });

        process_.on('close', () => {
            clearTimeout(timer);
            parser.flush();
            resolve(discovered);
        });

        process_.on('error', () => {
            clearTimeout(timer);
            resolve([]);
        });
    });
}
