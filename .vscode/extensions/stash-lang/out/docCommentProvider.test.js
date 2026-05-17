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
/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-require-imports */
const node_test_1 = require("node:test");
const assert = __importStar(require("node:assert/strict"));
const NodeModule = require("module");
const originalRequire = NodeModule.prototype.require;
NodeModule.prototype.require = function (id, ...args) {
    if (id === "vscode") {
        return {};
    }
    return originalRequire.call(this, id, ...args);
};
const mod = require("./docCommentProvider");
const parseFunctionSignature = mod.parseFunctionSignature;
const buildDocCommentSnippet = mod.buildDocCommentSnippet;
const buildDocCommentSnippetForContext = mod.buildDocCommentSnippetForContext;
(0, node_test_1.describe)("parseFunctionSignature", () => {
    (0, node_test_1.it)("parses a plain function declaration", () => {
        assert.deepEqual(parseFunctionSignature("fn add(a, b) {"), {
            parameterNames: ["a", "b"],
            hasExplicitReturn: false,
        });
    });
    (0, node_test_1.it)("parses typed params and explicit return type", () => {
        assert.deepEqual(parseFunctionSignature("fn add(a: int, b: int) -> int {"), {
            parameterNames: ["a", "b"],
            hasExplicitReturn: true,
        });
    });
    (0, node_test_1.it)("parses async exported declarations", () => {
        assert.deepEqual(parseFunctionSignature("export async fn fetch(url: string) -> string {"), {
            parameterNames: ["url"],
            hasExplicitReturn: true,
        });
    });
    (0, node_test_1.it)("parses rest params", () => {
        assert.deepEqual(parseFunctionSignature("fn join(...parts: string[]) -> string {"), {
            parameterNames: ["parts"],
            hasExplicitReturn: true,
        });
    });
    (0, node_test_1.it)("ignores commas inside default values", () => {
        assert.deepEqual(parseFunctionSignature('fn pick(items = ["a", "b"], fallback = "x,y") -> string {'), {
            parameterNames: ["items", "fallback"],
            hasExplicitReturn: true,
        });
    });
    (0, node_test_1.it)("returns null for non-function lines", () => {
        assert.equal(parseFunctionSignature("let fnName = value;"), null);
    });
});
(0, node_test_1.describe)("buildDocCommentSnippet", () => {
    (0, node_test_1.it)("builds summary and parameter tags without returns for implicit return", () => {
        assert.equal(buildDocCommentSnippet("  ", {
            parameterNames: ["path", "recursive"],
            hasExplicitReturn: false,
        }), [
            "  /// ${1:Summary}",
            "  /// @param path ${2}",
            "  /// @param recursive ${3}",
        ].join("\n"));
    });
    (0, node_test_1.it)("adds returns tag only for explicit return types", () => {
        assert.equal(buildDocCommentSnippet("", {
            parameterNames: [],
            hasExplicitReturn: true,
        }), ["/// ${1:Summary}", "/// @returns ${2}"].join("\n"));
    });
});
(0, node_test_1.describe)("buildDocCommentSnippetForContext", () => {
    (0, node_test_1.it)("does not expand when extending an existing doc comment", () => {
        assert.equal(buildDocCommentSnippetForContext("///", "/// @return array of values", "fn testExpansion(val1: int, val2: string, ...rest) -> array {"), null);
    });
    (0, node_test_1.it)("expands when starting a fresh doc comment above a function", () => {
        assert.equal(buildDocCommentSnippetForContext("  ///", null, "fn testExpansion(val1: int, val2: string, ...rest) -> array {"), [
            "  /// ${1:Summary}",
            "  /// @param val1 ${2}",
            "  /// @param val2 ${3}",
            "  /// @param rest ${4}",
            "  /// @returns ${5}",
        ].join("\n"));
    });
});
//# sourceMappingURL=docCommentProvider.test.js.map