/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-require-imports */
import { describe, it } from "node:test";
import * as assert from "node:assert/strict";

const NodeModule: any = require("module");
const originalRequire: (this: any, id: string, ...args: any[]) => any =
  NodeModule.prototype.require;

NodeModule.prototype.require = function (
  this: any,
  id: string,
  ...args: any[]
): any {
  if (id === "vscode") {
    return {};
  }
  return originalRequire.call(this, id, ...args);
};

const mod: any = require("./docCommentProvider");

const parseFunctionSignature: (line: string) => {
  parameterNames: string[];
  hasExplicitReturn: boolean;
} | null = mod.parseFunctionSignature;

const buildDocCommentSnippet: (
  indent: string,
  signature: { parameterNames: string[]; hasExplicitReturn: boolean },
) => string = mod.buildDocCommentSnippet;

const buildDocCommentSnippetForContext: (
  lineText: string,
  previousLineText: string | null,
  nextLineText: string,
) => string | null = mod.buildDocCommentSnippetForContext;

describe("parseFunctionSignature", () => {
  it("parses a plain function declaration", () => {
    assert.deepEqual(parseFunctionSignature("fn add(a, b) {"), {
      parameterNames: ["a", "b"],
      hasExplicitReturn: false,
    });
  });

  it("parses typed params and explicit return type", () => {
    assert.deepEqual(parseFunctionSignature("fn add(a: int, b: int) -> int {"), {
      parameterNames: ["a", "b"],
      hasExplicitReturn: true,
    });
  });

  it("parses async exported declarations", () => {
    assert.deepEqual(parseFunctionSignature("export async fn fetch(url: string) -> string {"), {
      parameterNames: ["url"],
      hasExplicitReturn: true,
    });
  });

  it("parses rest params", () => {
    assert.deepEqual(parseFunctionSignature("fn join(...parts: string[]) -> string {"), {
      parameterNames: ["parts"],
      hasExplicitReturn: true,
    });
  });

  it("ignores commas inside default values", () => {
    assert.deepEqual(
      parseFunctionSignature('fn pick(items = ["a", "b"], fallback = "x,y") -> string {'),
      {
        parameterNames: ["items", "fallback"],
        hasExplicitReturn: true,
      },
    );
  });

  it("returns null for non-function lines", () => {
    assert.equal(parseFunctionSignature("let fnName = value;"), null);
  });
});

describe("buildDocCommentSnippet", () => {
  it("builds summary and parameter tags without returns for implicit return", () => {
    assert.equal(
      buildDocCommentSnippet("  ", {
        parameterNames: ["path", "recursive"],
        hasExplicitReturn: false,
      }),
      [
        "  /// ${1:Summary}",
        "  /// @param path ${2}",
        "  /// @param recursive ${3}",
      ].join("\n"),
    );
  });

  it("adds returns tag only for explicit return types", () => {
    assert.equal(
      buildDocCommentSnippet("", {
        parameterNames: [],
        hasExplicitReturn: true,
      }),
      ["/// ${1:Summary}", "/// @returns ${2}"].join("\n"),
    );
  });
});

describe("buildDocCommentSnippetForContext", () => {
  it("does not expand when extending an existing doc comment", () => {
    assert.equal(
      buildDocCommentSnippetForContext(
        "///",
        "/// @return array of values",
        "fn testExpansion(val1: int, val2: string, ...rest) -> array {",
      ),
      null,
    );
  });

  it("expands when starting a fresh doc comment above a function", () => {
    assert.equal(
      buildDocCommentSnippetForContext(
        "  ///",
        null,
        "fn testExpansion(val1: int, val2: string, ...rest) -> array {",
      ),
      [
        "  /// ${1:Summary}",
        "  /// @param val1 ${2}",
        "  /// @param val2 ${3}",
        "  /// @param rest ${4}",
        "  /// @returns ${5}",
      ].join("\n"),
    );
  });
});
