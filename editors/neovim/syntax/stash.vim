if exists("b:current_syntax")
  finish
endif

" Control flow keywords
syntax keyword stashConditional if else switch case default
syntax keyword stashRepeat      while do for in break continue
syntax keyword stashException   try catch finally throw defer
syntax keyword stashStatement   return elevate

" Declaration keywords
syntax keyword stashDeclaration let const fn async await
syntax keyword stashStructure   struct enum interface extend import from as
syntax keyword stashOperator    and or is typeof delete spawn retry timeout

" Literals
syntax keyword stashBoolean true false
syntax keyword stashNull    null
syntax keyword stashSelf    self

" Numbers
syntax match stashNumber   '\<\d\+\>'
syntax match stashFloat    '\<\d\+\.\d*\>'
syntax match stashHex      '\<0[xX][0-9a-fA-F]\+\>'
syntax match stashBinary   '\<0[bB][01]\+\>'
syntax match stashOctal    '\<0[oO][0-7]\+\>'

" Strings
syntax region stashString  start='"' end='"' skip='\\"' contains=stashEscape,stashInterp
syntax region stashString  start="'" end="'" skip="\\'"  contains=stashEscape
syntax region stashString  start='`' end='`'             contains=stashEscape,stashInterp
syntax match  stashEscape  '\\.' contained
syntax region stashInterp  start='\${' end='}' contained contains=TOP

" Comments
syntax match  stashLineComment  '//.*$'
syntax region stashBlockComment start='/\*' end='\*/' extend

" Identifiers
syntax match stashIdentifier '\<[a-zA-Z_][a-zA-Z0-9_]*\>'

" Highlight links
highlight default link stashConditional  Conditional
highlight default link stashRepeat       Repeat
highlight default link stashException    Exception
highlight default link stashStatement    Statement
highlight default link stashDeclaration  Keyword
highlight default link stashStructure    Structure
highlight default link stashOperator     Operator
highlight default link stashBoolean      Boolean
highlight default link stashNull         Constant
highlight default link stashSelf         Identifier
highlight default link stashNumber       Number
highlight default link stashFloat        Float
highlight default link stashHex          Number
highlight default link stashBinary       Number
highlight default link stashOctal        Number
highlight default link stashString       String
highlight default link stashEscape       SpecialChar
highlight default link stashInterp       PreProc
highlight default link stashLineComment  Comment
highlight default link stashBlockComment Comment
highlight default link stashIdentifier   Identifier

let b:current_syntax = "stash"
