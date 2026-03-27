// Stash language definition for Monaco Editor (Monarch tokenizer)
// Ported from stash.tmLanguage.json TextMate grammar

function registerStashLanguage() {
    // Register language
    monaco.languages.register({ id: 'stash' });

    // Monarch tokenizer
    monaco.languages.setMonarchTokensProvider('stash', {
        keywords: [
            'let', 'const', 'fn', 'struct', 'enum', 'if', 'else', 'while', 'do',
            'for', 'in', 'return', 'break', 'continue', 'try', 'catch', 'finally',
            'throw', 'switch', 'case', 'default', 'as', 'import', 'from',
            'async', 'await', 'spawn', 'typeof', 'delete', 'match'
        ],

        builtinConstants: ['true', 'false', 'null'],

        typeKeywords: [
            'int', 'float', 'string', 'bool', 'array', 'dict',
            'function', 'range', 'namespace', 'Error'
        ],

        namespaces: [
            'arr', 'dict', 'str', 'math', 'time', 'json', 'fs', 'path',
            'env', 'sys', 'http', 'crypto', 'io', 'conv', 'process', 'log',
            'term', 'store', 'encoding', 'ini', 'config', 'args', 'tpl',
            'test', 'assert'
        ],

        builtinFunctions: ['println', 'print', 'input', 'sleep', 'exit', 'error'],

        operators: [
            '??=', '&&', '||', '??', '?.', '=>', '..', '|>',
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
                [/\$>?\(/, 'metatag', '@command'],

                // Triple-quoted interpolated strings: $"""..."""
                [/\$"""/, 'string', '@tripleInterpolatedString'],

                // Triple-quoted strings: """..."""
                [/"""/, 'string', '@tripleString'],

                // Interpolated strings: $"..."
                [/\$"/, 'string', '@interpolatedString'],

                // Regular strings: "..."
                [/"/, 'string', '@string'],

                // Numbers (float before int)
                [/\b\d+\.\d+\b/, 'number.float'],
                [/\b\d+\b/, 'number'],

                // self keyword
                [/\bself\b/, 'variable.language'],

                // is TYPE pattern
                [/\b(is\s+)(int|float|string|bool|null|array|dict|struct|enum|function|range|namespace|Error)\b/, ['keyword', 'type']],

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
                [/\{/, 'delimiter.bracket', '@interpolationExpr'],
                [/"/, 'string', '@string'],
                [/[^){"\\]+/, 'metatag'],
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
