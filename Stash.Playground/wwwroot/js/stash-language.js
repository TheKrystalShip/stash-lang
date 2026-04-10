// Stash language definition for Monaco Editor (Monarch tokenizer)
// Ported from stash.tmLanguage.json TextMate grammar

function registerStashLanguage() {
    // Register language
    monaco.languages.register({ id: 'stash' });

    // Monarch tokenizer
    monaco.languages.setMonarchTokensProvider('stash', {
        keywords: [
            'let', 'const', 'fn', 'struct', 'enum', 'interface', 'extend', 'if', 'else', 'while', 'do',
            'for', 'in', 'return', 'break', 'continue', 'try', 'catch', 'finally', 'retry', 'onRetry', 'until',
            'throw', 'switch', 'case', 'default', 'as', 'import',
            'async', 'await', 'spawn', 'typeof', 'delete', 'match'
        ],

        builtinConstants: ['true', 'false', 'null'],

        typeKeywords: [
            'int', 'float', 'string', 'bool', 'array', 'dict',
            'function', 'range', 'namespace', 'Error'
        ],

        namespaces: [
            'arr', 'dict', 'str', 'math', 'time', 'json', 'fs', 'path',
            'env', 'sys', 'http', 'crypto', 'io', 'conv', 'process',
            'term', 'encoding', 'ini', 'config', 'args', 'tpl',
            'test', 'assert'
        ],

        builtinFunctions: ['println', 'print', 'input', 'sleep', 'exit', 'error'],

        operators: [
            '??=', '&&', '||', '??', '?.', '=>', '...', '..', '|>',
            '==', '!=', '<=', '>=', '+=', '-=', '*=', '/=', '%=',
            '++', '--', '->', '&>>', '&>', '>>', '2>>', '2>',
            '+', '-', '*', '/', '%', '<', '>', '!', '=', '|', '?'
        ],

        symbols: /[=><!~?:&|+\-*\/\^%]+/,

        escapes: /\\[\\\"ntr0]/,

        tokenizer: {
            root: [
                // Shebang
                [/^#!.*$/, 'comment'],

                // Block comments (before line comments)
                [/\/\*/, 'comment', '@comment'],

                // Line comments
                [/\/\/.*$/, 'comment'],

                // Command literals: $(...) and $>(...)
                [/\$!?>?\(/, 'metatag', '@command'],

                // Triple-quoted interpolated strings: $"""..."""
                [/\$"""/, 'string', '@tripleInterpolatedString'],

                // Triple-quoted strings: """..."""
                [/"""/, 'string', '@tripleString'],

                // Interpolated strings: $"..."
                [/\$"/, 'string', '@interpolatedString'],

                // Regular strings: "..."
                [/"/, 'string', '@string'],

                // Semver literal (@v1.2.3, @v1.0.0-beta.2)
                [/[@]v[0-9]+\.(?:[xX*]|[0-9]+(?:\.(?:[xX*]|[0-9]+))?(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)/, 'number'],

                // IP address literals (@192.168.1.1, @::1)
                [/[@](?:[0-9]{1,3}\.){3}[0-9]{1,3}(?:\/[0-9]{1,2})?/, 'number'],
                [/[@](?:[0-9a-fA-F]*:[0-9a-fA-F:]+(?:%[a-zA-Z0-9]+)?(?:\/[0-9]{1,3})?)/, 'number'],

                // Duration literals (5s, 2h30m, 500ms)
                [/\b[0-9][0-9_]*(?:\.[0-9][0-9_]*)?(?:ms|[smhd])(?:[0-9]+(?:ms|[smhd]))*\b/, 'number'],

                // Byte-size literals (100B, 1.5MB, 2GB)
                [/\b[0-9][0-9_]*(?:\.[0-9][0-9_]*)?(?:TB|GB|MB|KB|B)\b/, 'number'],

                // Numbers (hex, octal, binary before float before int)
                [/\b0[xX][0-9a-fA-F][0-9a-fA-F_]*\b/, 'number.hex'],
                [/\b0[oO][0-7][0-7_]*\b/, 'number.octal'],
                [/\b0[bB][01][01_]*\b/, 'number.binary'],
                [/\b\d[\d_]*\.\d[\d_]*\b/, 'number.float'],
                [/\b\d[\d_]*\b/, 'number'],

                // self keyword
                [/\bself\b/, 'variable.language'],
                // attempt keyword (retry block implicit variable)
                [/\battempt\b/, 'variable.language'],

                // is TYPE pattern
                [/\b(is\s+)(int|float|string|bool|null|array|dict|struct|enum|function|range|namespace|Error|duration|bytes|ip|semver|Future)\b/, ['keyword', 'type']],

                // Identifiers and keywords
                [/\b[a-zA-Z_]\w*(?=\s*\()/, {
                    cases: {
                        '@keywords': 'keyword',
                        '@builtinFunctions': 'keyword',
                        '@namespaces': 'type',
                        '@default': 'entity'
                    }
                }],
                [/\b[a-zA-Z_]\w*\b/, {
                    cases: {
                        '@keywords': 'keyword',
                        '@builtinConstants': 'constant',
                        '@typeKeywords': 'type',
                        '@namespaces': 'type',
                        '@default': 'identifier'
                    }
                }],

                // Brackets
                [/[{}()\[\]]/, '@brackets'],

                // Operators
                [/@symbols/, {
                    cases: {
                        '@operators': 'operator',
                        '@default': ''
                    }
                }],

                // Delimiters
                [/[;,]/, 'delimiter'],
                [/\./, 'delimiter'],
            ],

            comment: [
                [/\/\*/, 'comment', '@push'],  // nested
                [/\*\//, 'comment', '@pop'],
                [/./, 'comment']
            ],

            string: [
                [/@escapes/, 'string.escape'],
                [/[^"\\]+/, 'string'],
                [/"/, 'string', '@pop']
            ],

            interpolatedString: [
                [/@escapes/, 'string.escape'],
                [/\{/, 'delimiter.bracket', '@interpolationExpr'],
                [/[^"\\{]+/, 'string'],
                [/"/, 'string', '@pop']
            ],

            tripleString: [
                [/"""/, 'string', '@pop'],
                [/@escapes/, 'string.escape'],
                [/[^"\\]+/, 'string'],
                [/"(?!"")/, 'string']
            ],

            tripleInterpolatedString: [
                [/"""/, 'string', '@pop'],
                [/@escapes/, 'string.escape'],
                [/\{/, 'delimiter.bracket', '@interpolationExpr'],
                [/[^"\\{]+/, 'string'],
                [/"(?!"")/, 'string']
            ],

            interpolationExpr: [
                [/\}/, 'delimiter.bracket', '@pop'],
                { include: 'root' }
            ],

            command: [
                [/\)/, 'metatag', '@pop'],
                [/\$\{/, 'delimiter.bracket', '@interpolationExpr'],
                [/\|\|/, 'metatag'],
                [/'[^']*'/, 'string'],
                [/\|/, 'operator'],
                [/"/, 'string', '@string'],
                [/[^)$"'\\|]+/, 'metatag'],
                [/\$(?!\{)/, 'metatag'],
                [/\\./, 'metatag']
            ]
        }
    });

    // Catppuccin Mocha (dark) theme
    monaco.editor.defineTheme('stash-dark', {
        base: 'vs-dark',
        inherit: true,
        rules: [
            { token: 'keyword',           foreground: 'cba6f7' },  // Mauve
            { token: 'constant',          foreground: 'fab387' },  // Peach
            { token: 'type',              foreground: '89b4fa' },  // Blue
            { token: 'entity',            foreground: '89dceb' },  // Sky (function calls)
            { token: 'string',            foreground: 'a6e3a1' },  // Green
            { token: 'string.escape',     foreground: 'f5c2e7' },  // Pink
            { token: 'number',            foreground: 'fab387' },  // Peach
            { token: 'number.float',      foreground: 'fab387' },  // Peach
            { token: 'comment',           foreground: '6c7086' },  // Overlay0
            { token: 'operator',          foreground: '89dceb' },  // Sky
            { token: 'delimiter',         foreground: '9399b2' },  // Overlay2
            { token: 'delimiter.bracket', foreground: 'f5c2e7' },  // Pink
            { token: 'identifier',        foreground: 'cdd6f4' },  // Text
            { token: 'variable.language', foreground: 'f38ba8' },  // Red (self)
            { token: 'metatag',           foreground: 'fab387' },  // Peach (commands)
        ],
        colors: {
            'editor.background':                 '#1e1e2e',
            'editor.foreground':                 '#cdd6f4',
            'editorLineNumber.foreground':       '#6c7086',
            'editorLineNumber.activeForeground': '#cdd6f4',
            'editorCursor.foreground':           '#f5e0dc',
            'editor.selectionBackground':        '#44446a',
            'editor.lineHighlightBackground':    '#282840',
            'editorWidget.background':           '#1e1e2e',
            'editorWidget.border':               '#44446a',
            'input.background':                  '#282840',
        }
    });

    // Catppuccin Latte (light) theme
    monaco.editor.defineTheme('stash-light', {
        base: 'vs',
        inherit: true,
        rules: [
            { token: 'keyword',           foreground: '8839ef' },  // Mauve
            { token: 'constant',          foreground: 'fe640b' },  // Peach
            { token: 'type',              foreground: '1e66f5' },  // Blue
            { token: 'entity',            foreground: '04a5e5' },  // Sky
            { token: 'string',            foreground: '40a02b' },  // Green
            { token: 'string.escape',     foreground: 'ea76cb' },  // Pink
            { token: 'number',            foreground: 'fe640b' },  // Peach
            { token: 'number.float',      foreground: 'fe640b' },  // Peach
            { token: 'comment',           foreground: '9ca0b0' },  // Overlay0
            { token: 'operator',          foreground: '04a5e5' },  // Sky
            { token: 'delimiter',         foreground: '7c7f93' },  // Overlay2
            { token: 'delimiter.bracket', foreground: 'ea76cb' },  // Pink
            { token: 'identifier',        foreground: '4c4f69' },  // Text
            { token: 'variable.language', foreground: 'd20f39' },  // Red
            { token: 'metatag',           foreground: 'fe640b' },  // Peach
        ],
        colors: {
            'editor.background':                 '#eff1f5',
            'editor.foreground':                 '#4c4f69',
            'editorLineNumber.foreground':       '#9ca0b0',
            'editorLineNumber.activeForeground': '#4c4f69',
            'editorCursor.foreground':           '#dc8a78',
            'editor.selectionBackground':        '#acb0be',
            'editor.lineHighlightBackground':    '#e6e9ef',
            'editorWidget.background':           '#eff1f5',
            'editorWidget.border':               '#acb0be',
            'input.background':                  '#e6e9ef',
        }
    });
}

function setPlaygroundTheme(isDark) {
    document.body.className = isDark ? 'theme-dark' : 'theme-light';
}

function addRunCommand(editorId, dotnetHelper) {
    var editorInstance = window.blazorMonaco.editor.getEditor(editorId);
    if (editorInstance) {
        editorInstance.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
            function() {
                dotnetHelper.invokeMethodAsync('RunFromKeyboard');
            }
        );
    } else {
        console.warn('addRunCommand: editor not found for id:', editorId);
    }
}

function registerLanguageProviders() {
    monaco.languages.registerCompletionItemProvider('stash', {
        triggerCharacters: ['.'],
        provideCompletionItems: async function(model, position) {
            try {
                var code = model.getValue();
                // Monaco positions are 1-based; convert to 0-based for C#
                var items = await DotNet.invokeMethodAsync('Stash.Playground', 'GetCompletions', code, position.lineNumber - 1, position.column - 1);
                if (!items || items.length === 0) return { suggestions: [] };

                var word = model.getWordUntilPosition(position);
                var range = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: word.startColumn,
                    endColumn: word.endColumn
                };

                return {
                    suggestions: items.map(function(item) {
                        return {
                            label: item.label,
                            kind: item.kind,
                            detail: item.detail || undefined,
                            documentation: item.documentation ? { value: item.documentation, isTrusted: true } : undefined,
                            insertText: item.label,
                            range: range
                        };
                    })
                };
            } catch (e) {
                console.warn('Completion error:', e);
                return { suggestions: [] };
            }
        }
    });

    monaco.languages.registerHoverProvider('stash', {
        provideHover: async function(model, position) {
            try {
                var code = model.getValue();
                // Monaco positions are 1-based; convert to 0-based for C#
                var result = await DotNet.invokeMethodAsync('Stash.Playground', 'GetHover', code, position.lineNumber - 1, position.column - 1);
                if (!result) return null;

                var word = model.getWordAtPosition(position);
                return {
                    contents: [{ value: result.content, isTrusted: true }],
                    range: word ? new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn) : undefined
                };
            } catch (e) {
                console.warn('Hover error:', e);
                return null;
            }
        }
    });

    monaco.languages.registerSignatureHelpProvider('stash', {
        signatureHelpTriggerCharacters: ['(', ','],
        signatureHelpRetriggerCharacters: [','],
        provideSignatureHelp: async function(model, position) {
            try {
                var code = model.getValue();
                var result = await DotNet.invokeMethodAsync('Stash.Playground', 'GetSignatureHelp', code, position.lineNumber - 1, position.column - 1);
                if (!result) return null;

                return {
                    value: {
                        signatures: [{
                            label: result.label,
                            documentation: result.documentation ? { value: result.documentation, isTrusted: true } : undefined,
                            parameters: (result.parameters || []).map(function(p) {
                                return {
                                    label: p.label,
                                    documentation: p.documentation ? { value: p.documentation, isTrusted: true } : undefined
                                };
                            })
                        }],
                        activeSignature: 0,
                        activeParameter: result.activeParameter
                    },
                    dispose: function() {}
                };
            } catch (e) {
                console.warn('SignatureHelp error:', e);
                return null;
            }
        }
    });
}

window.registerDiagnosticsListener = function(editorId) {
    var editor = window.blazorMonaco.editor.getEditor(editorId);
    if (!editor) { console.warn('registerDiagnosticsListener: editor not found:', editorId); return; }

    var debounceTimer = null;
    editor.onDidChangeModelContent(function() {
        if (debounceTimer) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(async function() {
            try {
                var code = editor.getValue();
                var diagnostics = await DotNet.invokeMethodAsync('Stash.Playground', 'GetDiagnostics', code);
                var model = editor.getModel();
                if (!model) return;

                var markers = (diagnostics || []).map(function(d) {
                    return {
                        severity: d.severity,
                        startLineNumber: d.startLine,
                        startColumn: d.startColumn,
                        endLineNumber: d.endLine,
                        endColumn: d.endColumn + 1, // Monaco markers are exclusive on end
                        message: d.message
                    };
                });

                monaco.editor.setModelMarkers(model, 'stash', markers);
                window.saveToLocalStorage(code);
            } catch (e) {
                console.warn('Diagnostics error:', e);
            }
        }, 300);
    });

    // Run initial diagnostics
    setTimeout(async function() {
        try {
            var code = editor.getValue();
            var diagnostics = await DotNet.invokeMethodAsync('Stash.Playground', 'GetDiagnostics', code);
            var model = editor.getModel();
            if (!model) return;

            var markers = (diagnostics || []).map(function(d) {
                return {
                    severity: d.severity,
                    startLineNumber: d.startLine,
                    startColumn: d.startColumn,
                    endLineNumber: d.endLine,
                    endColumn: d.endColumn + 1,
                    message: d.message
                };
            });

            monaco.editor.setModelMarkers(model, 'stash', markers);
        } catch (e) {
            console.warn('Initial diagnostics error:', e);
        }
    }, 500);
};

// ================================================================
// Playground QoL: Share, Autosave, Format, Copy, Download
// ================================================================

// Share: encode editor code to URL hash (base64)
window.getShareUrl = function(code) {
    var encoded = btoa(unescape(encodeURIComponent(code)));
    return window.location.origin + window.location.pathname + '#code=' + encoded;
};

// Share: decode code from URL hash
window.getCodeFromHash = function() {
    var hash = window.location.hash;
    if (hash && hash.startsWith('#code=')) {
        try {
            var encoded = hash.substring(6);
            return decodeURIComponent(escape(atob(encoded)));
        } catch (e) {
            console.warn('Failed to decode shared code:', e);
            return null;
        }
    }
    return null;
};

// Autosave: save to localStorage (debounced, called from diagnostics listener)
window.saveToLocalStorage = function(code) {
    try {
        localStorage.setItem('stash-playground-code', code);
    } catch (e) { /* quota exceeded — ignore */ }
};

// Autosave: load from localStorage
window.loadFromLocalStorage = function() {
    try {
        return localStorage.getItem('stash-playground-code');
    } catch (e) {
        return null;
    }
};

// Format: call C# formatter and update editor
window.formatEditorCode = async function(editorId) {
    var editor = window.blazorMonaco.editor.getEditor(editorId);
    if (!editor) return;
    var code = editor.getValue();
    try {
        var formatted = await DotNet.invokeMethodAsync('Stash.Playground', 'FormatCode', code);
        var model = editor.getModel();
        if (formatted && formatted !== code && model) {
            editor.executeEdits('format', [{
                range: model.getFullModelRange(),
                text: formatted
            }]);
        }
    } catch (e) {
        console.warn('Format error:', e);
    }
};

// Copy: copy text to clipboard
window.copyToClipboard = async function(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (e) {
        console.warn('Copy failed:', e);
        return false;
    }
};

// Download: trigger file download
window.downloadFile = function(filename, content) {
    var blob = new Blob([content], { type: 'text/plain' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
