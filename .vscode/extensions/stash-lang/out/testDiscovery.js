"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.parseTestsFromText = parseTestsFromText;
exports.discoverTestsDynamic = discoverTestsDynamic;
const child_process_1 = require("child_process");
const tapParser_1 = require("./tapParser");
// ---------------------------------------------------------------------------
// Static discovery
// ---------------------------------------------------------------------------
/**
 * Matches describe("name", ...) or test("name", ...) calls.
 * Group 1: "describe" | "test"
 * Group 2: the string delimiter (' " `)
 * Group 3: the literal name
 */
const CALL_RE = /\b(describe|test)\s*\(\s*(['"`])((?:[^\\]|\\.)*?)\2/g;
/**
 * Scan `text` for describe/test patterns and return discovered tests.
 * Line numbers are 0-based (VS Code convention).
 */
function parseTestsFromText(text, fileName) {
    const results = [];
    // Pre-compute line start offsets for fast offset→line lookup.
    const lineOffsets = [0];
    for (let i = 0; i < text.length; i++) {
        if (text[i] === '\n') {
            lineOffsets.push(i + 1);
        }
    }
    function offsetToPosition(offset) {
        let lo = 0;
        let hi = lineOffsets.length - 1;
        while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (lineOffsets[mid] <= offset) {
                lo = mid;
            }
            else {
                hi = mid - 1;
            }
        }
        return { line: lo, column: offset - lineOffsets[lo] };
    }
    // Stack of { name, braceDepth } — braceDepth is the depth *before* the
    // opening brace of the describe callback was counted.
    const describeStack = [];
    // Current brace depth considering the entire file processed so far.
    let braceDepth = 0;
    // Cursor into the text — we advance it as we process tokens.
    let cursor = 0;
    /**
     * Advance `cursor` past the next "token" (string literal, comment, or
     * single character) and update `braceDepth` when a bare `{` or `}` is
     * encountered.  Returns false when the end of the text is reached.
     */
    function advanceCursor() {
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
            while (describeStack.length > 0 &&
                braceDepth <= describeStack[describeStack.length - 1].entryDepth) {
                describeStack.pop();
            }
            cursor++;
            return true;
        }
        cursor++;
        return true;
    }
    CALL_RE.lastIndex = 0;
    let match;
    while ((match = CALL_RE.exec(text)) !== null) {
        const matchStart = match.index;
        const kind = match[1];
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
                        if (text[scanPos] === '\\')
                            scanPos++;
                        scanPos++;
                    }
                    scanPos++;
                    continue;
                }
                if (c === '(') {
                    parenDepth++;
                    scanPos++;
                    continue;
                }
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
            // CALL_RE continues from after the match; cursor will catch up on the
            // next iteration.
        }
        else {
            // kind === 'test'
            const ancestors = [
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
            });
        }
    }
    return results;
}
// ---------------------------------------------------------------------------
// Dynamic discovery
// ---------------------------------------------------------------------------
const DISCOVERY_TIMEOUT_MS = 10000;
/**
 * Run the Stash interpreter in `--test --test-list` mode and collect
 * discovered tests via TAP discovery comments.
 */
async function discoverTestsDynamic(filePath, interpreterPath) {
    return new Promise((resolve) => {
        const discovered = [];
        let process_;
        try {
            process_ = (0, child_process_1.spawn)(interpreterPath, ['--test', '--test-list', filePath], {
                stdio: ['ignore', 'pipe', 'pipe'],
            });
        }
        catch {
            resolve([]);
            return;
        }
        const parser = new tapParser_1.TapParser({
            onTestDiscovered(name, file, line, column) {
                const segments = name.split(' > ');
                const label = segments[segments.length - 1];
                const ancestors = segments.slice(0, -1);
                discovered.push({
                    fullName: name,
                    label,
                    ancestors,
                    uri: file,
                    line: line - 1, // convert 1-based → 0-based
                    column: column - 1, // convert 1-based → 0-based
                });
            },
        });
        const timer = setTimeout(() => {
            process_.kill();
            resolve(discovered);
        }, DISCOVERY_TIMEOUT_MS);
        process_.stdout?.on('data', (chunk) => {
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
//# sourceMappingURL=testDiscovery.js.map