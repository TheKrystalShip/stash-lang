; Scopes
(function_declaration) @local.scope
(struct_method) @local.scope
(block) @local.scope
(for_in_statement) @local.scope
(for_statement) @local.scope
(lambda_expression) @local.scope

; Definitions
(function_declaration
  name: (identifier) @local.definition)
(variable_declaration
  name: (identifier) @local.definition)
(constant_declaration
  name: (identifier) @local.definition)
(parameter
  name: (identifier) @local.definition)
(rest_parameter
  name: (identifier) @local.definition)
(for_in_statement
  value: (identifier) @local.definition)
(for_in_statement
  index: (identifier) @local.definition)

; References
(identifier) @local.reference
