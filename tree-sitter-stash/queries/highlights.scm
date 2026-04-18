; Keywords
"fn" @keyword.function
"let" @keyword.modifier
"const" @keyword.modifier
"struct" @keyword.type
"enum" @keyword.type
"interface" @keyword.type
"extend" @keyword.type
"async" @keyword.coroutine
"await" @keyword.coroutine
"if" @keyword.conditional
"else" @keyword.conditional
"switch" @keyword.conditional
"case" @keyword.conditional
"default" @keyword.conditional
"for" @keyword.repeat
"while" @keyword.repeat
"do" @keyword.repeat
"break" @keyword.repeat
"continue" @keyword.repeat
"try" @keyword.exception
"catch" @keyword.exception
"finally" @keyword.exception
"throw" @keyword.exception
"defer" @keyword
"return" @keyword.return
"import" @keyword.import
"from" @keyword.import
"as" @keyword
"and" @keyword.operator
"or" @keyword.operator
"is" @keyword.operator
"in" @keyword.operator

; Literals
(boolean) @boolean
(null) @constant.builtin

(number) @number
(hex_number) @number
(binary_number) @number
(octal_number) @number
(float) @number.float
(duration_literal) @number
(byte_size_literal) @number
(semver_literal) @string.special
(ip_address_literal) @string.special

; Strings
(string) @string
(interpolated_string) @string
(triple_string) @string
(escape_sequence) @string.escape
(string_interpolation
  "${" @punctuation.special
  "}" @punctuation.special)
(interpolation
  "{" @punctuation.special
  "}" @punctuation.special)

; Comments
(line_comment) @comment
(doc_comment) @comment.documentation
(block_comment) @comment

; Commands
(command_expression) @string.special

; Function definitions
(function_declaration
  name: (identifier) @function)
(struct_method
  name: (identifier) @function.method)

; Function calls
(call_expression
  function: (identifier) @function.call)
(call_expression
  function: (member_expression
    property: (identifier) @function.call))

; Types
(struct_declaration
  name: (identifier) @type)
(enum_declaration
  name: (identifier) @type)
(interface_declaration
  name: (identifier) @type)
(struct_init_expression
  name: (identifier) @type)
(extend_statement
  type: (identifier) @type)

; Variables and parameters
(parameter
  name: (identifier) @variable.parameter)
(rest_parameter
  name: (identifier) @variable.parameter)

(variable_declaration
  name: (identifier) @variable)
(constant_declaration
  name: (identifier) @constant)

; Struct fields and enum members
(struct_field
  name: (identifier) @property)
(struct_init_field
  name: (identifier) @property)
(interface_field
  name: (identifier) @property)
(enum_member
  name: (identifier) @constant)

; Interface methods
(interface_method
  name: (identifier) @function)

; Built-in variables
((identifier) @variable.builtin
  (#match? @variable.builtin "^(self|attempt)$"))

; Member access
(member_expression
  property: (identifier) @property)
(optional_member_expression
  property: (identifier) @property)

; Named arguments
(named_argument
  name: (identifier) @property)

; Operators
[
  "+"
  "-"
  "*"
  "/"
  "%"
  "**"
  "=="
  "!="
  "<"
  ">"
  "<="
  ">="
  "&&"
  "||"
  "|"
  "^"
  "&"
  "~"
  "<<"
  ">>"
  "!"
  "="
  "+="
  "-="
  "*="
  "/="
  "%="
  "??="
  "&="
  "|="
  "^="
  "<<="
  ">>="
  "??"
  "?."
  ".."
  "..."
  "|>"
  "=>"
  "->"
  "++"
  "--"
] @operator

; Punctuation
["(" ")" "[" "]" "{" "}"] @punctuation.bracket
["," "." ";" ":"] @punctuation.delimiter
