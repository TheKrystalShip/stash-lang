; Injection queries for Stash templates
; These are intended for use with a future tree-sitter-stash-tpl grammar.
; When a tree-sitter TPL grammar is available, it would inject the Stash
; language into expression and tag content nodes.
;
; Example (requires tree-sitter-stash-tpl):
;   ((template_expression_content) @injection.content
;    (#set! injection.language "stash"))
;
;   ((template_tag_content) @injection.content
;    (#set! injection.language "stash"))
