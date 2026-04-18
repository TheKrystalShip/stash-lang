#include "tree_sitter/parser.h"

enum TokenType {
    BLOCK_COMMENT,
    COMMAND_CONTENT,
    ERROR_SENTINEL,
};

static void advance(TSLexer *lexer) {
    lexer->advance(lexer, false);
}

static void skip_ws(TSLexer *lexer) {
    lexer->advance(lexer, true);
}

static bool scan_block_comment(TSLexer *lexer) {
    if (lexer->lookahead != '/') return false;
    advance(lexer);
    if (lexer->lookahead != '*') return false;
    advance(lexer);

    int depth = 1;
    while (depth > 0 && !lexer->eof(lexer)) {
        if (lexer->lookahead == '/') {
            advance(lexer);
            if (!lexer->eof(lexer) && lexer->lookahead == '*') {
                advance(lexer);
                depth++;
            }
        } else if (lexer->lookahead == '*') {
            advance(lexer);
            if (!lexer->eof(lexer) && lexer->lookahead == '/') {
                advance(lexer);
                depth--;
            }
        } else {
            advance(lexer);
        }
    }

    lexer->result_symbol = BLOCK_COMMENT;
    return depth == 0;
}

static bool scan_command_content(TSLexer *lexer) {
    int paren_depth = 0;
    bool has_content = false;

    while (!lexer->eof(lexer)) {
        if (lexer->lookahead == ')' && paren_depth == 0) {
            break;
        }

        has_content = true;

        if (lexer->lookahead == '(') {
            paren_depth++;
            advance(lexer);
        } else if (lexer->lookahead == ')') {
            paren_depth--;
            advance(lexer);
        } else if (lexer->lookahead == '"') {
            advance(lexer);
            while (!lexer->eof(lexer) && lexer->lookahead != '"') {
                if (lexer->lookahead == '\\') {
                    advance(lexer);
                    if (!lexer->eof(lexer)) advance(lexer);
                } else {
                    advance(lexer);
                }
            }
            if (!lexer->eof(lexer)) advance(lexer);
        } else if (lexer->lookahead == '\'') {
            advance(lexer);
            while (!lexer->eof(lexer) && lexer->lookahead != '\'') {
                if (lexer->lookahead == '\\') {
                    advance(lexer);
                    if (!lexer->eof(lexer)) advance(lexer);
                } else {
                    advance(lexer);
                }
            }
            if (!lexer->eof(lexer)) advance(lexer);
        } else {
            advance(lexer);
        }
    }

    if (has_content) {
        lexer->result_symbol = COMMAND_CONTENT;
        return true;
    }
    return false;
}

void *tree_sitter_stash_external_scanner_create(void) {
    return NULL;
}

void tree_sitter_stash_external_scanner_destroy(void *payload) {
}

unsigned tree_sitter_stash_external_scanner_serialize(void *payload, char *buffer) {
    return 0;
}

void tree_sitter_stash_external_scanner_deserialize(void *payload, const char *buffer, unsigned length) {
}

bool tree_sitter_stash_external_scanner_scan(void *payload, TSLexer *lexer, const bool *valid_symbols) {
    /* Error recovery: when the error sentinel is valid, all externals are valid.
       Only attempt block_comment which has an unambiguous marker. */
    if (valid_symbols[ERROR_SENTINEL]) {
        while (lexer->lookahead == ' ' || lexer->lookahead == '\t' ||
               lexer->lookahead == '\n' || lexer->lookahead == '\r') {
            skip_ws(lexer);
        }
        lexer->mark_end(lexer);
        if (lexer->lookahead == '/') {
            return scan_block_comment(lexer);
        }
        return false;
    }

    if (valid_symbols[COMMAND_CONTENT]) {
        return scan_command_content(lexer);
    }

    if (valid_symbols[BLOCK_COMMENT]) {
        while (lexer->lookahead == ' ' || lexer->lookahead == '\t' ||
               lexer->lookahead == '\n' || lexer->lookahead == '\r') {
            skip_ws(lexer);
        }
        lexer->mark_end(lexer);
        return scan_block_comment(lexer);
    }

    return false;
}
