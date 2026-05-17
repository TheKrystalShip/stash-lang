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
exports.registerDocCommentExpansionProvider = registerDocCommentExpansionProvider;
exports.buildDocCommentSnippetForContext = buildDocCommentSnippetForContext;
exports.parseFunctionSignature = parseFunctionSignature;
exports.buildDocCommentSnippet = buildDocCommentSnippet;
const vscode = __importStar(require("vscode"));
const STASH_LANGUAGE_ID = "stash";
const DOC_COMMENT_PREFIX = "///";
const FUNCTION_KEYWORD = "fn";
const EXPORT_KEYWORD = "export";
const ASYNC_KEYWORD = "async";
const RETURN_ARROW = "->";
const REST_PARAMETER_PREFIX = "...";
const SUMMARY_PLACEHOLDER = "Summary";
const DOC_PARAM_TAG = "@param";
const DOC_RETURNS_TAG = "@return";
const SLASH = "/";
const OPEN_PAREN = "(";
const CLOSE_PAREN = ")";
const OPEN_BRACKET = "[";
const CLOSE_BRACKET = "]";
const OPEN_BRACE = "{";
const CLOSE_BRACE = "}";
const DOUBLE_QUOTE = '"';
const SINGLE_QUOTE = "'";
const BACKSLASH = "\\";
const COMMA = ",";
const LINE_BREAK = "\n";
const IDENTIFIER_RE = /^[a-zA-Z_]\w*/;
const FUNCTION_DECLARATION_RE = /^(?:export\s+)?(?:async\s+)?fn\s+[a-zA-Z_]\w*\s*\(/;
function registerDocCommentExpansionProvider() {
    return vscode.workspace.onDidChangeTextDocument(async (event) => {
        if (event.document.languageId !== STASH_LANGUAGE_ID) {
            return;
        }
        if (event.contentChanges.length !== 1) {
            return;
        }
        const change = event.contentChanges[0];
        if (!change.text.includes(SLASH)) {
            return;
        }
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document.uri.toString() !== event.document.uri.toString()) {
            return;
        }
        const lineNumber = change.range.start.line;
        if (lineNumber + 1 >= event.document.lineCount) {
            return;
        }
        const previousLine = lineNumber > 0 ? event.document.lineAt(lineNumber - 1).text : null;
        const lineText = event.document.lineAt(lineNumber).text;
        const nextLine = event.document.lineAt(lineNumber + 1).text;
        const snippetText = buildDocCommentSnippetForContext(lineText, previousLine, nextLine);
        if (snippetText == null) {
            return;
        }
        const snippet = new vscode.SnippetString(snippetText);
        const range = new vscode.Range(lineNumber, 0, lineNumber, lineText.length);
        await editor.insertSnippet(snippet, range);
    });
}
function buildDocCommentSnippetForContext(lineText, previousLineText, nextLineText) {
    if (lineText.trim() !== DOC_COMMENT_PREFIX) {
        return null;
    }
    if (previousLineText != null && isDocCommentLine(previousLineText)) {
        return null;
    }
    const signature = parseFunctionSignature(nextLineText);
    if (!signature) {
        return null;
    }
    const indent = lineText.slice(0, lineText.length - lineText.trimStart().length);
    return buildDocCommentSnippet(indent, signature);
}
function parseFunctionSignature(line) {
    const trimmed = line.trimStart();
    if (!FUNCTION_DECLARATION_RE.test(trimmed)) {
        return null;
    }
    const openParen = trimmed.indexOf(OPEN_PAREN);
    if (openParen < 0 || !isFunctionDeclarationPrefix(trimmed.slice(0, openParen))) {
        return null;
    }
    const closeParen = findMatchingParen(trimmed, openParen);
    if (closeParen < 0) {
        return null;
    }
    const parameterSource = trimmed.slice(openParen + 1, closeParen);
    const parameterNames = splitTopLevel(parameterSource, COMMA)
        .map(extractParameterName)
        .filter((name) => name != null);
    const afterParameters = trimmed.slice(closeParen + 1).trimStart();
    return {
        parameterNames,
        hasExplicitReturn: afterParameters.startsWith(RETURN_ARROW),
    };
}
function buildDocCommentSnippet(indent, signature) {
    let tabStop = 1;
    const lines = [`${indent}${DOC_COMMENT_PREFIX} \${${tabStop++}:${SUMMARY_PLACEHOLDER}}`];
    for (const parameterName of signature.parameterNames) {
        lines.push(`${indent}${DOC_COMMENT_PREFIX} ${DOC_PARAM_TAG} ${parameterName} \${${tabStop++}}`);
    }
    if (signature.hasExplicitReturn) {
        lines.push(`${indent}${DOC_COMMENT_PREFIX} ${DOC_RETURNS_TAG} \${${tabStop++}}`);
    }
    return lines.join(LINE_BREAK);
}
function isFunctionDeclarationPrefix(prefix) {
    const words = prefix.trim().split(/\s+/);
    if (words.length < 2) {
        return false;
    }
    const fnIndex = words.indexOf(FUNCTION_KEYWORD);
    if (fnIndex < 0 || fnIndex !== words.length - 2) {
        return false;
    }
    const modifiers = words.slice(0, fnIndex);
    return modifiers.every((word) => word === EXPORT_KEYWORD || word === ASYNC_KEYWORD);
}
function isDocCommentLine(lineText) {
    return lineText.trimStart().startsWith(DOC_COMMENT_PREFIX);
}
function findMatchingParen(text, openParen) {
    let depth = 0;
    let quote = null;
    let escaped = false;
    for (let i = openParen; i < text.length; i++) {
        const char = text[i];
        if (quote != null) {
            if (escaped) {
                escaped = false;
            }
            else if (char === BACKSLASH) {
                escaped = true;
            }
            else if (char === quote) {
                quote = null;
            }
            continue;
        }
        if (char === DOUBLE_QUOTE || char === SINGLE_QUOTE) {
            quote = char;
            continue;
        }
        if (char === OPEN_PAREN) {
            depth++;
        }
        else if (char === CLOSE_PAREN) {
            depth--;
            if (depth === 0) {
                return i;
            }
        }
    }
    return -1;
}
function splitTopLevel(text, separator) {
    const parts = [];
    let partStart = 0;
    let quote = null;
    let escaped = false;
    const depth = { paren: 0, bracket: 0, brace: 0 };
    for (let i = 0; i < text.length; i++) {
        const char = text[i];
        if (quote != null) {
            if (escaped) {
                escaped = false;
            }
            else if (char === BACKSLASH) {
                escaped = true;
            }
            else if (char === quote) {
                quote = null;
            }
            continue;
        }
        if (char === DOUBLE_QUOTE || char === SINGLE_QUOTE) {
            quote = char;
            continue;
        }
        updateNestingDepth(depth, char);
        if (char === separator &&
            depth.paren === 0 &&
            depth.bracket === 0 &&
            depth.brace === 0) {
            parts.push(text.slice(partStart, i));
            partStart = i + 1;
        }
    }
    parts.push(text.slice(partStart));
    return parts;
}
function updateNestingDepth(depth, char) {
    if (char === OPEN_PAREN) {
        depth.paren++;
    }
    else if (char === CLOSE_PAREN) {
        depth.paren = Math.max(0, depth.paren - 1);
    }
    else if (char === OPEN_BRACKET) {
        depth.bracket++;
    }
    else if (char === CLOSE_BRACKET) {
        depth.bracket = Math.max(0, depth.bracket - 1);
    }
    else if (char === OPEN_BRACE) {
        depth.brace++;
    }
    else if (char === CLOSE_BRACE) {
        depth.brace = Math.max(0, depth.brace - 1);
    }
}
function extractParameterName(parameter) {
    let trimmed = parameter.trim();
    if (trimmed.length === 0) {
        return null;
    }
    if (trimmed.startsWith(REST_PARAMETER_PREFIX)) {
        trimmed = trimmed.slice(REST_PARAMETER_PREFIX.length).trimStart();
    }
    const match = IDENTIFIER_RE.exec(trimmed);
    return match?.[0] ?? null;
}
//# sourceMappingURL=docCommentProvider.js.map
