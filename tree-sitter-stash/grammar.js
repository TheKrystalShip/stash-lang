/// <reference types="tree-sitter-cli/dsl" />

const PREC = {
  ASSIGN: 1,
  TERNARY: 2,
  NULL_COALESCE: 3,
  OR: 4,
  AND: 5,
  BITOR: 6,
  BITXOR: 7,
  BITAND: 8,
  EQUALITY: 9,
  COMPARISON: 10,
  TYPE_TEST: 11,
  SHIFT: 12,
  RANGE: 13,
  ADD: 14,
  MULT: 15,
  POWER: 16,
  UNARY: 17,
  POSTFIX: 18,
  CALL: 19,
  PIPE: 20,
};

module.exports = grammar({
  name: 'stash',

  externals: $ => [
    $.block_comment,
    $.command_content,
    $._error_sentinel,
  ],

  extras: $ => [
    /\s/,
    $.line_comment,
    $.block_comment,
    $.doc_comment,
  ],

  word: $ => $.identifier,

  supertypes: $ => [
    $._expression,
    $._declaration,
  ],

  inline: $ => [
    $._statement_or_declaration,
  ],

  conflicts: $ => [
    [$.struct_init_expression, $._expression],
    [$.parameter, $._expression],
    [$.range_expression],
  ],

  rules: {
    source_file: $ => repeat($._statement_or_declaration),

    _statement_or_declaration: $ => choice(
      $._declaration,
      $._statement,
    ),

    // ═══════════════════════════════════════
    // DECLARATIONS
    // ═══════════════════════════════════════

    _declaration: $ => choice(
      $.variable_declaration,
      $.constant_declaration,
      $.function_declaration,
      $.struct_declaration,
      $.enum_declaration,
      $.interface_declaration,
      $.import_statement,
    ),

    variable_declaration: $ => seq(
      'let',
      choice(
        $.destructure_pattern,
        seq(field('name', $.identifier), optional($.type_annotation)),
      ),
      optional(seq('=', $._expression)),
      ';',
    ),

    constant_declaration: $ => seq(
      'const',
      field('name', $.identifier),
      optional($.type_annotation),
      '=',
      $._expression,
      ';',
    ),

    function_declaration: $ => seq(
      optional('async'),
      'fn',
      field('name', $.identifier),
      $.parameter_list,
      optional($.return_type),
      $.block,
    ),

    struct_declaration: $ => seq(
      'struct',
      field('name', $.identifier),
      optional(seq(':', $.interface_list)),
      $.struct_body,
    ),

    interface_list: $ => seq(
      $.identifier,
      repeat(seq(',', $.identifier)),
    ),

    struct_body: $ => seq(
      '{',
      optional(commaSep1($._struct_member)),
      optional(','),
      '}',
    ),

    _struct_member: $ => choice(
      $.struct_method,
      $.struct_field,
    ),

    struct_field: $ => seq(
      field('name', $.identifier),
      optional($.type_annotation),
      optional(seq('=', $._expression)),
    ),

    struct_method: $ => seq(
      optional('async'),
      'fn',
      field('name', $.identifier),
      $.parameter_list,
      optional($.return_type),
      $.block,
    ),

    enum_declaration: $ => seq(
      'enum',
      field('name', $.identifier),
      '{',
      optional(commaSep1($.enum_member)),
      optional(','),
      '}',
    ),

    enum_member: $ => field('name', $.identifier),

    interface_declaration: $ => seq(
      'interface',
      field('name', $.identifier),
      '{',
      optional(commaSep1($._interface_member)),
      optional(','),
      '}',
    ),

    _interface_member: $ => choice(
      $.interface_method,
      $.interface_field,
    ),

    interface_method: $ => seq(
      optional('fn'),
      field('name', $.identifier),
      $.parameter_list,
      optional($.return_type),
    ),

    interface_field: $ => seq(
      field('name', $.identifier),
      $.type_annotation,
    ),

    import_statement: $ => choice(
      seq('import', '{', $.import_specifier_list, '}', 'from', $._expression, ';'),
      seq('import', $._expression, 'as', field('alias', $.identifier), ';'),
    ),

    import_specifier_list: $ => commaSep1($.identifier),

    // ═══════════════════════════════════════
    // PARAMETERS & TYPES
    // ═══════════════════════════════════════

    parameter_list: $ => seq(
      '(',
      optional(commaSep1($.parameter)),
      optional(','),
      ')',
    ),

    parameter: $ => choice(
      $.rest_parameter,
      seq(
        field('name', $.identifier),
        optional($.type_annotation),
        optional(seq('=', $._expression)),
      ),
    ),

    rest_parameter: $ => seq(
      '...',
      field('name', $.identifier),
    ),

    type_annotation: $ => seq(
      ':',
      $.type_expression,
    ),

    return_type: $ => seq(
      '->',
      $.type_expression,
    ),

    type_expression: $ => seq(
      $.identifier,
      optional('[]'),
    ),

    // ═══════════════════════════════════════
    // STATEMENTS
    // ═══════════════════════════════════════

    _statement: $ => choice(
      $.expression_statement,
      $.block,
      $.if_statement,
      $.while_statement,
      $.do_while_statement,
      $.for_in_statement,
      $.for_statement,
      $.switch_statement,
      $.try_catch_statement,
      $.return_statement,
      $.throw_statement,
      $.break_statement,
      $.continue_statement,
      $.defer_statement,
      $.extend_statement,
      $.elevate_statement,
    ),

    expression_statement: $ => seq(
      $._expression,
      ';',
    ),

    block: $ => seq(
      '{',
      repeat($._statement_or_declaration),
      '}',
    ),

    if_statement: $ => prec.right(seq(
      'if',
      '(',
      $._expression,
      ')',
      $.block,
      repeat($.else_if_clause),
      optional($.else_clause),
    )),

    else_if_clause: $ => seq(
      'else',
      'if',
      '(',
      $._expression,
      ')',
      $.block,
    ),

    else_clause: $ => seq(
      'else',
      $.block,
    ),

    while_statement: $ => seq(
      'while',
      '(',
      $._expression,
      ')',
      $.block,
    ),

    do_while_statement: $ => seq(
      'do',
      $.block,
      'while',
      '(',
      $._expression,
      ')',
      ';',
    ),

    for_in_statement: $ => prec(1, seq(
      'for',
      '(',
      'let',
      choice(
        seq(field('index', $.identifier), ',', field('value', $.identifier)),
        field('value', $.identifier),
      ),
      optional($.type_annotation),
      'in',
      $._expression,
      ')',
      $.block,
    )),

    for_statement: $ => seq(
      'for',
      '(',
      optional(choice(
        $.variable_declaration,
        seq($._expression, ';'),
      )),
      optional($._expression),
      ';',
      optional($._expression),
      ')',
      $.block,
    ),

    switch_statement: $ => seq(
      'switch',
      '(',
      $._expression,
      ')',
      '{',
      repeat($.switch_case),
      optional($.switch_default),
      '}',
    ),

    switch_case: $ => seq(
      'case',
      commaSep1($._expression),
      ':',
      $.block,
    ),

    switch_default: $ => seq(
      'default',
      ':',
      $.block,
    ),

    try_catch_statement: $ => seq(
      'try',
      $.block,
      optional(seq('catch', '(', field('error', $.identifier), ')', $.block)),
      optional(seq('finally', $.block)),
    ),

    return_statement: $ => seq(
      'return',
      optional($._expression),
      ';',
    ),

    throw_statement: $ => seq(
      'throw',
      $._expression,
      ';',
    ),

    break_statement: $ => seq('break', ';'),

    continue_statement: $ => seq('continue', ';'),

    defer_statement: $ => seq(
      'defer',
      choice(
        $.block,
        seq(optional('await'), $._expression, ';'),
      ),
    ),

    extend_statement: $ => seq(
      'extend',
      field('type', $.identifier),
      $.struct_body,
    ),

    elevate_statement: $ => seq(
      'elevate',
      optional(seq('(', $._expression, ')')),
      $.block,
    ),

    // ═══════════════════════════════════════
    // EXPRESSIONS
    // ═══════════════════════════════════════

    _expression: $ => choice(
      $.identifier,
      $.number,
      $.float,
      $.hex_number,
      $.binary_number,
      $.octal_number,
      $.string,
      $.interpolated_string,
      $.triple_string,
      $.boolean,
      $.null,
      $.array_expression,
      $.dict_expression,
      $.binary_expression,
      $.unary_expression,
      $.update_expression,
      $.assignment_expression,
      $.ternary_expression,
      $.call_expression,
      $.member_expression,
      $.optional_member_expression,
      $.index_expression,
      $.pipe_expression,
      $.lambda_expression,
      $.range_expression,
      $.is_expression,
      $.command_expression,
      $.struct_init_expression,
      $.parenthesized_expression,
      $.await_expression,
      $.try_expression,
      $.switch_expression,
      $.retry_expression,
      $.timeout_expression,
      $.duration_literal,
      $.byte_size_literal,
      $.semver_literal,
      $.ip_address_literal,
    ),

    parenthesized_expression: $ => seq('(', $._expression, ')'),

    // --- Literals ---

    boolean: $ => choice('true', 'false'),
    null: $ => 'null',

    number: $ => /[0-9][0-9_]*/,

    float: $ => token(prec(1, choice(
      /[0-9][0-9_]*\.[0-9][0-9_]*/,
      /[0-9][0-9_]*[eE][+-]?[0-9]+/,
      /[0-9][0-9_]*\.[0-9][0-9_]*[eE][+-]?[0-9]+/,
    ))),

    hex_number: $ => /0[xX][0-9a-fA-F][0-9a-fA-F_]*/,
    binary_number: $ => /0[bB][01][01_]*/,
    octal_number: $ => /0[oO][0-7][0-7_]*/,

    duration_literal: $ => token(prec(2, /[0-9]+(?:h(?:[0-9]+m(?:[0-9]+s)?)?|m(?:[0-9]+s)?|ms|s|d)/)),

    byte_size_literal: $ => token(prec(2, /[0-9]+(?:\.[0-9]+)?(?:PB|TB|GB|MB|KB|B)/)),

    semver_literal: $ => /@v[0-9]+(?:\.[0-9]+(?:\.[0-9]+)?)?(?:-[a-zA-Z0-9.]+)?/,

    ip_address_literal: $ => /@(?:[0-9]{1,3}(?:\.[0-9]{1,3}){3}(?:\/[0-9]{1,2})?|[0-9a-fA-F:]+(?:\/[0-9]{1,3})?)/,

    // --- Strings ---

    string: $ => seq(
      '"',
      repeat(choice(
        $.string_interpolation,
        $.escape_sequence,
        $.string_content,
        '$',
      )),
      '"',
    ),

    interpolated_string: $ => seq(
      '$"',
      repeat(choice(
        $.interpolation,
        $.string_interpolation,
        $.escape_sequence,
        $.interpolated_string_content,
        '$',
      )),
      '"',
    ),

    triple_string: $ => seq(
      '"""',
      optional($.triple_string_content),
      '"""',
    ),

    triple_string_content: $ => repeat1(choice(
      /[^"]/,
      /"[^"]/,
      /""[^"]/,
    )),

    string_content: $ => /[^"\\\n$]+/,

    interpolated_string_content: $ => /[^"\\\n{$]+/,

    string_interpolation: $ => seq(
      '${',
      $._expression,
      '}',
    ),

    interpolation: $ => seq(
      '{',
      $._expression,
      '}',
    ),

    escape_sequence: $ => token(prec(1, /\\[nrt0\\/"']/)),

    // --- Command expressions ---

    command_expression: $ => choice(
      seq('$(', optional($.command_content), ')'),
      seq('$>(', optional($.command_content), ')'),
      seq('$!(', optional($.command_content), ')'),
      seq('$!>(', optional($.command_content), ')'),
    ),

    // --- Collections ---

    array_expression: $ => seq(
      '[',
      optional(commaSep1($._array_element)),
      optional(','),
      ']',
    ),

    _array_element: $ => choice(
      $.spread_expression,
      $._expression,
    ),

    dict_expression: $ => prec(-1, seq(
      '{',
      commaSep1($._dict_entry),
      optional(','),
      '}',
    )),

    _dict_entry: $ => choice(
      $.dict_pair,
      $.spread_expression,
    ),

    dict_pair: $ => seq(
      field('key', choice($.identifier, $.string, $.interpolated_string, $.number)),
      ':',
      field('value', $._expression),
    ),

    // --- Binary expressions ---

    binary_expression: $ => {
      const operators = [
        ['+', PREC.ADD],
        ['-', PREC.ADD],
        ['*', PREC.MULT],
        ['/', PREC.MULT],
        ['%', PREC.MULT],
        ['**', PREC.POWER],
        ['==', PREC.EQUALITY],
        ['!=', PREC.EQUALITY],
        ['<', PREC.COMPARISON],
        ['>', PREC.COMPARISON],
        ['<=', PREC.COMPARISON],
        ['>=', PREC.COMPARISON],
        ['&&', PREC.AND],
        ['||', PREC.OR],
        ['and', PREC.AND],
        ['or', PREC.OR],
        ['|', PREC.BITOR],
        ['^', PREC.BITXOR],
        ['&', PREC.BITAND],
        ['<<', PREC.SHIFT],
        ['>>', PREC.SHIFT],
        ['??', PREC.NULL_COALESCE],
        ['in', PREC.TYPE_TEST],
      ];

      return choice(
        ...operators.map(([op, prec_level]) =>
          prec.left(prec_level, seq(
            field('left', $._expression),
            field('operator', op),
            field('right', $._expression),
          ))
        ),
      );
    },

    unary_expression: $ => prec.right(PREC.UNARY, seq(
      field('operator', choice('!', '-', '~')),
      field('operand', $._expression),
    )),

    update_expression: $ => choice(
      prec.right(PREC.UNARY, seq(
        field('operator', choice('++', '--')),
        field('operand', $._expression),
      )),
      prec.left(PREC.POSTFIX, seq(
        field('operand', $._expression),
        field('operator', choice('++', '--')),
      )),
    ),

    assignment_expression: $ => prec.right(PREC.ASSIGN, seq(
      field('left', $._expression),
      field('operator', choice('=', '+=', '-=', '*=', '/=', '%=', '??=', '&=', '|=', '^=', '<<=', '>>=')),
      field('right', $._expression),
    )),

    ternary_expression: $ => prec.right(PREC.TERNARY, seq(
      field('condition', $._expression),
      '?',
      field('consequence', $._expression),
      ':',
      field('alternative', $._expression),
    )),

    is_expression: $ => prec.left(PREC.TYPE_TEST, seq(
      field('value', $._expression),
      'is',
      field('type', $.type_expression),
    )),

    pipe_expression: $ => prec.left(PREC.PIPE, seq(
      field('left', $._expression),
      '|>',
      field('right', $._expression),
    )),

    range_expression: $ => prec.left(PREC.RANGE, seq(
      field('start', $._expression),
      '..',
      field('end', $._expression),
      optional(seq('..', field('step', $._expression))),
    )),

    spread_expression: $ => prec.right(PREC.UNARY, seq(
      '...',
      $._expression,
    )),

    // --- Call / Member / Index ---

    call_expression: $ => prec.left(PREC.CALL, seq(
      field('function', $._expression),
      $.argument_list,
    )),

    argument_list: $ => seq(
      '(',
      optional(commaSep1($._argument)),
      optional(','),
      ')',
    ),

    _argument: $ => choice(
      $.spread_expression,
      $.named_argument,
      $._expression,
    ),

    named_argument: $ => seq(
      field('name', $.identifier),
      ':',
      field('value', $._expression),
    ),

    member_expression: $ => prec.left(PREC.CALL, seq(
      field('object', $._expression),
      '.',
      field('property', $.identifier),
    )),

    optional_member_expression: $ => prec.left(PREC.CALL, seq(
      field('object', $._expression),
      '?.',
      field('property', $.identifier),
    )),

    index_expression: $ => prec.left(PREC.CALL, seq(
      field('object', $._expression),
      '[',
      field('index', $._expression),
      ']',
    )),

    // --- Lambda ---

    lambda_expression: $ => prec.right(PREC.ASSIGN, seq(
      optional('async'),
      choice(
        seq('fn', $.parameter_list),
        $.parameter_list,
      ),
      '=>',
      field('body', choice($.block, $._expression)),
    )),

    // --- Struct init ---

    struct_init_expression: $ => prec.dynamic(1, seq(
      field('name', choice(
        $.identifier,
        seq($.identifier, '.', $.identifier),
      )),
      '{',
      optional(commaSep1($.struct_init_field)),
      optional(','),
      '}',
    )),

    struct_init_field: $ => seq(
      field('name', $.identifier),
      ':',
      field('value', $._expression),
    ),

    // --- Await, Try prefix, Switch expression, Retry, Timeout ---

    await_expression: $ => prec.right(PREC.UNARY, seq(
      'await',
      $._expression,
    )),

    try_expression: $ => prec.right(PREC.UNARY, seq(
      'try',
      $._expression,
    )),

    switch_expression: $ => prec.left(PREC.POSTFIX, seq(
      $._expression,
      'switch',
      '{',
      optional(commaSep1($.switch_arm)),
      optional(','),
      '}',
    )),

    switch_arm: $ => seq(
      field('pattern', choice('_', $._expression)),
      '=>',
      field('value', $._expression),
    ),

    retry_expression: $ => seq(
      'retry',
      '(',
      commaSep1(choice($.named_argument, $._expression)),
      ')',
      optional(seq('onRetry', choice($.block, $._expression))),
      optional(seq('until', choice($.block, $._expression))),
      $.block,
    ),

    timeout_expression: $ => seq(
      'timeout',
      $._expression,
      $.block,
    ),

    // --- Destructuring ---

    destructure_pattern: $ => choice(
      $.array_pattern,
      $.dict_pattern,
    ),

    array_pattern: $ => seq(
      '[',
      optional(commaSep1($._pattern_element)),
      optional(','),
      ']',
    ),

    dict_pattern: $ => seq(
      '{',
      optional(commaSep1($._pattern_element)),
      optional(','),
      '}',
    ),

    _pattern_element: $ => choice(
      $.rest_pattern,
      $.identifier,
    ),

    rest_pattern: $ => seq('...', $.identifier),

    // --- Comments ---

    line_comment: $ => token(prec(-1, /\/\/[^\n]*/)),

    doc_comment: $ => /\/\/\/[^\n]*/,

    // --- Identifier ---

    identifier: $ => /[a-zA-Z_][a-zA-Z0-9_]*/,
  },
});

/**
 * Creates a rule to match one or more occurrences of `rule` separated by commas.
 */
function commaSep1(rule) {
  return seq(rule, repeat(seq(',', rule)));
}
