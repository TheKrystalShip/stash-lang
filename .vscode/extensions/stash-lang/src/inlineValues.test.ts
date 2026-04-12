/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-require-imports */
import { describe, it } from "node:test";
import * as assert from "node:assert/strict";

// ---------------------------------------------------------------------------
// Mock vscode types
// These mirror the vscode API shapes used by inlineValues.ts
// ---------------------------------------------------------------------------

class MockRange {
    public start: { line: number; character: number };
    public end: { line: number; character: number };
    constructor(
        startLine: number,
        startChar: number,
        endLine: number,
        endChar: number,
    ) {
        this.start = { line: startLine, character: startChar };
        this.end = { line: endLine, character: endChar };
    }
}

class MockInlineValueVariableLookup {
    constructor(
        public range: MockRange,
        public variableName: string,
        public caseSensitive: boolean,
    ) {}
}

// ---------------------------------------------------------------------------
// Intercept require("vscode") before loading the module under test.
// Module.prototype.require is called for every require() inside any loaded
// module, so patching it here ensures inlineValues.js gets our mock even
// though it was not imported yet.
// ---------------------------------------------------------------------------

const NodeModule: any = require("module");
const originalRequire: (this: any, id: string, ...args: any[]) => any =
    NodeModule.prototype.require;

NodeModule.prototype.require = function (
    this: any,
    id: string,
    ...args: any[]
): any {
    if (id === "vscode") {
        return {
            Range: MockRange,
            InlineValueVariableLookup: MockInlineValueVariableLookup,
        };
    }
    return originalRequire.call(this, id, ...args);
};

// ---------------------------------------------------------------------------
// Load the module under test AFTER the mock is registered
// ---------------------------------------------------------------------------

const mod: any = require("./inlineValues");

const stripStringsAndComments: (line: string) => string =
    mod.stripStringsAndComments;

const StashInlineValuesProvider: new () => {
    provideInlineValues(doc: any, viewport: any, ctx: any, token: any): any[];
} = mod.StashInlineValuesProvider;

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function mockDocument(lines: string[]): { lineAt(line: number): { text: string } } {
    return {
        lineAt(line: number) {
            return { text: lines[line] };
        },
    };
}

function mockContext(stoppedLine: number): {
    frameId: number;
    stoppedLocation: MockRange;
} {
    return {
        frameId: 1,
        stoppedLocation: new MockRange(stoppedLine, 0, stoppedLine, 0),
    };
}

// ---------------------------------------------------------------------------
// stripStringsAndComments
// ---------------------------------------------------------------------------

describe("stripStringsAndComments", () => {
    it("preserves plain code", () => {
        const input = "let x = 5";
        assert.equal(stripStringsAndComments(input), input);
    });

    it("blanks double-quoted strings", () => {
        // "hello" (7 chars) becomes 7 spaces; code prefix is preserved
        const input = 'let x = "hello"';
        const result = stripStringsAndComments(input);
        assert.equal(result.length, input.length);
        assert.ok(result.startsWith("let x = "));
        assert.equal(result.slice(8), "       ");
    });

    it("blanks single-quoted strings", () => {
        // 'world' (7 chars) becomes 7 spaces; code prefix is preserved
        const input = "let x = 'world'";
        const result = stripStringsAndComments(input);
        assert.equal(result.length, input.length);
        assert.ok(result.startsWith("let x = "));
        assert.equal(result.slice(8), "       ");
    });

    it("handles escaped quotes inside strings", () => {
        // The actual string value is: let x = "say \"hi\""
        const input = 'let x = "say \\"hi\\""';
        const result = stripStringsAndComments(input);
        assert.equal(result.length, input.length);
        assert.ok(result.startsWith("let x = "));
        // Everything after the code prefix must be spaces
        assert.ok(
            result.slice(8).split("").every((c) => c === " "),
            "expected all spaces after 'let x = '",
        );
    });

    it("blanks // comments", () => {
        // "let x = 5 " is 10 chars kept; "// comment" becomes 10 spaces
        const input = "let x = 5 // comment";
        const result = stripStringsAndComments(input);
        assert.equal(result.length, input.length);
        assert.ok(result.startsWith("let x = 5 "));
        assert.ok(
            result.slice(10).split("").every((c) => c === " "),
            "expected all spaces from // onwards",
        );
    });

    it("blanks # comments", () => {
        const input = "# full line comment";
        const result = stripStringsAndComments(input);
        assert.equal(result.length, input.length);
        assert.ok(
            result.split("").every((c) => c === " "),
            "expected entire line to be spaces",
        );
    });

    it("preserves length across various inputs", () => {
        const inputs = [
            "let x = 5",
            'let x = "hello"',
            "# comment",
            "let x = 5 // comment",
            '""',
        ];
        for (const input of inputs) {
            const result = stripStringsAndComments(input);
            assert.equal(
                result.length,
                input.length,
                `length mismatch for: ${input}`,
            );
        }
    });

    it("handles empty string literal", () => {
        // "" is 2 chars: both the opening and closing quote become spaces
        const input = '""';
        const result = stripStringsAndComments(input);
        assert.equal(result.length, 2);
        assert.equal(result, "  ");
    });

    it("handles mixed strings and comments", () => {
        // "hi" (4 chars blanked) then space kept, then // done (7 chars blanked)
        const input = 'let x = "hi" // done';
        const result = stripStringsAndComments(input);
        assert.equal(result.length, input.length);
        assert.ok(result.startsWith("let x = "));
        assert.ok(
            result.slice(8).split("").every((c) => c === " "),
            "expected all spaces after 'let x = '",
        );
    });
});

// ---------------------------------------------------------------------------
// StashInlineValuesProvider
// ---------------------------------------------------------------------------

describe("StashInlineValuesProvider", () => {
    const provider = new StashInlineValuesProvider();
    const token = {};

    /**
     * Convenience wrapper: run provideInlineValues on a set of lines,
     * with execution stopped at `stoppedLine` and the viewport covering
     * [viewportStart, viewportEnd] (inclusive).  Returns variable names.
     */
    function getNames(
        lines: string[],
        stoppedLine: number,
        viewportStart = 0,
        viewportEnd?: number,
    ): string[] {
        const end = viewportEnd ?? lines.length - 1;
        const doc = mockDocument(lines);
        const viewport = new MockRange(viewportStart, 0, end, 0);
        const ctx = mockContext(stoppedLine);
        const result = provider.provideInlineValues(doc, viewport, ctx, token);
        return result.map((v: any) => v.variableName as string);
    }

    it("extracts variable names from assignment", () => {
        const names = getNames(["let x = y + 1"], 0);
        assert.ok(names.includes("x"), "should include x");
        assert.ok(names.includes("y"), "should include y");
    });

    it("filters keywords", () => {
        const names = getNames(["let x = 5"], 0);
        assert.ok(names.includes("x"), "should include x");
        assert.ok(!names.includes("let"), "should not include keyword 'let'");
    });

    it("filters all language keywords including do, is, async", () => {
        const names = getNames(["do { } while (is async)"], 0);
        assert.ok(!names.includes("do"), "should not include keyword 'do'");
        assert.ok(!names.includes("is"), "should not include keyword 'is'");
        assert.ok(!names.includes("async"), "should not include keyword 'async'");
    });

    it("filters function declarations", () => {
        // "greet" is skipped: it is preceded by "fn " in the cleaned text
        // "name" is kept: it is a parameter, not a declaration or call target
        const names = getNames(["fn greet(name) {"], 0);
        assert.ok(names.includes("name"), "should include parameter 'name'");
        assert.ok(!names.includes("greet"), "should not include function name 'greet'");
        assert.ok(!names.includes("fn"), "should not include keyword 'fn'");
    });

    it("filters property access targets", () => {
        // "field" is preceded by "." so it is skipped
        const names = getNames(["x = obj.field"], 0);
        assert.ok(names.includes("x"), "should include x");
        assert.ok(names.includes("obj"), "should include obj");
        assert.ok(!names.includes("field"), "should not include property name 'field'");
    });

    it("filters function calls", () => {
        // "foo" is followed by "(" so it is treated as a call target and skipped
        const names = getNames(["foo(x)"], 0);
        assert.ok(names.includes("x"), "should include argument 'x'");
        assert.ok(!names.includes("foo"), "should not include callee 'foo'");
    });

    it("skips comment-only lines", () => {
        const names = getNames(["// let x = 5"], 0);
        assert.equal(names.length, 0);
    });

    it("skips variables inside string literals", () => {
        // "hello" and "world" live inside the string; only "msg" is real code
        const names = getNames(['let msg = "hello world"'], 0);
        assert.ok(names.includes("msg"), "should include 'msg'");
        assert.ok(!names.includes("hello"), "should not include 'hello' (inside string)");
        assert.ok(!names.includes("world"), "should not include 'world' (inside string)");
    });

    it("respects stopped location — excludes lines after stop", () => {
        // Viewport covers all 3 lines; execution stopped at line 1.
        // Only lines 0 and 1 should be inspected.
        const lines = ["let a = 1", "let b = 2", "let c = 3"];
        const names = getNames(lines, 1, 0, 2);
        assert.ok(names.includes("a"), "should include 'a' from line 0");
        assert.ok(names.includes("b"), "should include 'b' from line 1");
        assert.ok(!names.includes("c"), "should not include 'c' from line 2 (not yet executed)");
    });

    it("deduplicates variable names within a single line", () => {
        // 'x' appears twice on the same line; seen set should deduplicate it
        const names = getNames(["x = x + 1"], 0);
        assert.equal(
            names.filter((n) => n === "x").length,
            1,
            "'x' should appear only once",
        );
    });

    it("handles empty lines gracefully", () => {
        const names = getNames([""], 0);
        assert.equal(names.length, 0);
    });
});
