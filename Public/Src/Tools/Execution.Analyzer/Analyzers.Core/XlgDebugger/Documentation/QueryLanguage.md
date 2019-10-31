# Query Language

For full ANTRL grammar, see [JPath.g4](/Public/Src/Tools/Execution.Analyzer/Analyzers.Core/XlgDebugger/JPath/JPath.g4).

For full evaluation semantics (albeit in the form of a C# implementation) see [Evaluator.cs](/Public/Src/Tools/Execution.Analyzer/Analyzers.Core/XlgDebugger/JPath/Evaluator.cs).

## Key Concepts
  - every syntax term is an expression
  - terms are untyped
  - all expressions are pure except for [Assign Statement](#Assign-Statement)
  - every expression evaluates to a value or fails
  - every value is a vector
  - scalars are vector with a single element
  - scalar value types are:
    - integers
    - strings
    - regular expressions
    - objects
  - some operators are overloaded
    - when `+` is applied to two scalar integers, it is interpreted as arithmetic addition; otherwise, it is set union;
    - when `-` is applied to two scalar integers, it is interpreted as arithmetic subtraction; otherwise, it is set difference.

## Evaluation Environment

Every expression is evaluated in the context of an *environment* (`Env`) and the evaluation returns a *value* of type `Result`.  An environment stores (1) a current value (in the context of which property names are resolved), (2) a list of variables, and (3) a pointer to a parent environment.
```
  Result := (Values: object[])
  Env    := (Current: Result, Vars: string->Result, Parent: Env)
```

## Literals

  - *integers*: denoted in standard decimal (base 10) system
    - examples: `0`, `-0`, `42`, `-23424`, ...
  - *strings*: 
    - any sequence of non-single-quote characters enclosed in single quotes: 
      - example: `'double quotes (") are ok in single-quoted strings'`
    - any sequence of non-double-quote characters enclosed in double quotes:
      - example: `"single quotes (') are ok in double-quoted strings"`
    - NOTE: impossible to write a string literal containing both single and double quotes
      - but it's possible to construct such a string: `$str('"', "'")` returns string `"'`
  - *regular expressions*:
    - any sequence of non-`/` characters enclosed in `/`
      - example: `/(?i)^.*\.txt$/`
    - any sequence of non-`!` characters enclosed in `!`
      - example: `!(?i)^.*\.txt$!`
  - *objects*:
    - similar to object literals in TypeScript except that:
      - property names can be omitted
      - must have at least 1 property
    - property name is a [Property Identifier](#Property-Identifier) and its value can be any expression
    - examples:
      - `{key: 123, val: 'value'}`
      - `{123, 'value'}` --> same as `{Item1: 123, Item2: 'value'}`
      - `{sub: {123, 'value'}}`
      - ``{`property name with spaces`: 123}``

## Property Identifier

**Syntax**
  - typical identifier: `[a-zA-Z_][a-zA-Z0-9_]*`
    - examples: `id1`, `Id1`, `_id1`
  - any non-backtick sequence of characters enclosed in backticks
    - example: `anything except backticks goes`

**Semantics**

Property name is resolved against the value in the current environment (`Env.Current`).  If not found in the current environment, parent environments are **not** considered and an empty vector is returned.  Environment variables are not considered either.

```javascript
[[ p ]]{Current: {p: 1}}            = [1]
[[ p ]]{Current: 1}                 = []
[[ p ]]{Current: 1, Parent: {p: 2}} = []
[[ p ]]{Current: 1, Vars: ['p': 2]} = []
```

## Variable Identifier

**Syntax**

Like a typical identifier but it **must** start with `$`, e.g., `$id1`, `$_Id1`, ...

**Semantics**

The variable is first looked up in the current environment; if not found, the lookup continues in parent environments.

```javascript
[[ $v ]]{Vars: ['$v': 1]}                            = [1]
[[ $v ]]{Vars: ['$v': 1], Parent: {Vars: ['$v': 2]}} = [1]
[[ $v ]]{Vars: [], Parent: {Vars: ['$v': 2]}}        = [2]
[[ $v ]]{Vars: [], Parent: {Vars: []}}               = []
```

## Map Expression

**Syntax**

`<lhs>.<rhs>`

**Semantics**

`lhs` is evaluated first.  Then, for every value in the result, a child environment is created against which `rhs` is then evaluated.  Finally, the results are aggregated into a single vector (similar to how `SelectMany` works in C#) and returned.

```javascript
[[ a.b ]]{Current: {a: [{b: 1}, {b: [2, 3]}, {c: 4}]}} = [1, 2, 3]
[[ a.b ]]{Current: {c: 1}                              = []
[[ a.b ]]{Current: {a: 1}                              = []
[[ a.b ]]{Current: {a: [1, {b: 1}, 2]}                 = [1]
```

## Filter Expression

**Syntax**

`<lhs>[<filter>]`

**Semantics**

`lhs` is evaluated first.  Then, if `filter` is an integer literal, the element at position `filter` from the `lhs` result is returned; otherwise, for every value in the `lhs` result, a child environment is created against which `filter` then evaluated, and only those values for which `filter` returns true are returned.

```javascript
[[ a[0] ]]{Current: {a: [1, 2, 3]}}  = [1]
[[ a[2] ]]{Current: {a: [1, 2, 3]}}  = [2]
[[ a[3] ]]{Current: {a: [1, 2, 3]}}  = []
[[ a[-1] ]]{Current: {a: [1, 2, 3]}} = [3]
[[ a[-3] ]]{Current: {a: [1, 2, 3]}} = [1]
[[ a[-4] ]]{Current: {a: [1, 2, 3]}} = []

[[ a[b > 1] ]]{Current: {a: [{b: 1}, {b: 2}, {c: 4}]}} = [{b: 2}]
[[ a[b > 1] ]]{Current: {a: 1}}                        = []
[[ a[b > 1] ]]{Current: {c: 1}}                        = []
[[ a[b > 1] ]]{Current: {a: {b: [2, 3]}}}              = []
```

## Range Expression

**Syntax**

`<lhs>[<begin> .. <end>]`

**Semantics**

```javascript
[[ a[0..0] ]]{Current: {a: [1, 2, 3]}}   = [1]
[[ a[0..1] ]]{Current: {a: [1, 2, 3]}}   = [1, 2]
[[ a[1..0] ]]{Current: {a: [1, 2, 3]}}   = []
[[ a[0..2] ]]{Current: {a: [1, 2, 3]}}   = [1, 2, 3]
[[ a[0..-1] ]]{Current: {a: [1, 2, 3]}}  = [1, 2, 3]
[[ a[-2..-1] ]]{Current: {a: [1, 2, 3]}} = [2, 3]
[[ a[5..8] ]]{Current: {a: [1, 2, 3]}}   = []
```

## Cardinality Expression

**Syntax**

`#<expr>`

**Semantics**

Evaluates `expr` and returns the number of elements in it.

```javascript
[[ #a ]]{Current: {a: [1, 2, 3]}} = [3]
[[ #a ]]{Current: {a: [2]}}       = [1]
[[ #a ]]{Current: {a: 'abc'}}     = [1]
[[ #a ]]{Current: {b: 'abc'}}     = [0]
```

## Function Application

**Syntax**

`<func> <switches> ( <arg> [, <arg>]* )`

`<expr> | <func>`

Examples:
```javascript
$str(a, a.b, 'hi', 123)    // concatenates all args
$join -d ", " (1, 2, 3, 4) // concatenates all args using ', ' as separator
$sort -n -r (1, 5, 3, 7)   // sorts args in reverse using numeric comparison

expr | $sort | $uniq       // sorts and dedupes all results
expr | $head -n 10         // takes first 10 elements
expr | $toJson             // exports to JSON string
expr | $toCsv              // exports to CSV string
```

**Semantics**

A number of [library functions](#Library-Functions) is provided, and each has its own semantics.  Each function accepts a number of switches and a number of arguments. 

## Output Redirection

**Syntax**

  - saving to file: `<expr> |> <str-lit>` 
  - appending to file: `<expr> |>> <str-lit>`

Examples:
```javascript
expr |> "out.txt"   // overwrites out.txt
expr |>> "out.txt"  // appends to out.txt
```

**Semantics**

Saving/appending output to file is implemented via the `$save` and `$append` library functions.  Whatever the result is, it is converted to string and saved to file.

## Let Binding

**Syntax**

`let <var> := <var-expr> in <sub-expr>`

**Semantics**

Standard let binding.  Evaluates `var-expr`, creates a child environment in which variable `var` is assigned the result of evaluating `var-expr`, and evaluates `sub-expr` in that new environment.

```javascript
[[ let $a := 1 in $a + 2 ]]  = [3]    // + is arithmetic addition
[[ let $a := 1 in $a ++ 2 ]] = [1, 2] // ++ is array concat
```

## Assign Statement

**Syntax**

`<var> := <expr> ;?`

**Semantics**

This is the only expression that mutates the state.  It evaluates `expr` and assigns the value to the `var` variable in the **root** environment.  All the variables in the root environment are shown in the debugger inside the "variables" pane.

```javascript
[[ $a := 42 ]] = [42] // side effect: [42] assigned to $a in the root environment
```

## Match Operator

**Syntax**

`<lhs> ~ <rhs>`

**Semantics**

The semantics is overloaded based on the actual types of the arguments:

  - if `rhs` evaluates to a string, the semantics is case-insensitive string containment (`lhs` contains `rhs`)
  - if `rhs` evaluates to a regular expression, the semantics is regular expression match (`lhs` matches `rhs`)
  - otherwise, it's a type error

In all cases, if `lhs` is not a scalar, the match succeeds if any value from `lhs` matches `rhs`.

```javascript
[[ "Hello World" ~ "wor" ]]     = True
[[ "Hello World" ~ /wor/ ]]     = False
[[ "Hello World" ~ /Wor/ ]]     = True
[[ "Hello World" ~ /(?i)wor/ ]] = True

[[ "Hello World" ~ "word" ]]                = False
[[ ("Hello World" ++ "ms word") ~ "word" ]] = True
```

## Binary Operators

| Operator  | Semantics |
| ---  | ------------- |
| `+`  | either arithmetic addition or set union  |
| `-`  | either arithmetic subtraction or set difference  |
| `*`  | arithmetic multiplication |
| `/`  | arithmetic division |
| `*`  | arithmetic modulo |
|||
| `@+` | set union |
| `@-` | set difference |
| `&`  | set intersection |
| `++` | array concatenation |
|||
| `>=`, `>`, `<=`, `<` | arithmetic comparison; false if either operand is not a 
number |
| `=`, `!=` | equality check |
| `~`, `!~` | [match checks](#Match-Operator) |
|||
| `not` | logic negation |
| `and` | logic conjunction |
| `or`  | logic disjunction |
| `xor` | logic exclusive disjunction |
| `iff` | logic equivalence (if and only if) |

## Operator Precedence

As a result of expressions being untyped, operator precedence is weird and typically not what's common in other languages.

For example:
  - the parse tree of `1 + 2 < 3 * 4` is `(((1 + 2) < 3) * 4)`
  - the parse tree of `a < b and a < c` is `(((a < b) and a) < c)`

Whenever in doubt (which should be most of the time), use parentheses.

## Library Functions

For the most up-to-date list of library functions see [LibraryFunctions.cs](/Public/Src/Tools/Execution.Analyzer/Analyzers.Core/XlgDebugger/LibraryFunctions.cs)

| Function  | Switches | Semantics |
| --- | --- | --- | 
| `$sum` | | Converts every arg to number and computes their sum.  Fails if any argument is not a number. |
| `$cut` | `-d <delim> -f <fld1>,...,<fldN>` | Similar to `/usr/bin/cut` |
| `$count` | | Flattens all arguments and returns their count. |
| `$uniq` | `-c` | Flattens all arguments and dedupes them.  When `-c` is provided, the output contains the count of each returned value. |
| `$sort` | `-n -r` | Sorts the elements.  `-n` implies numeric sorting, and `-r` sorting in descending order |
| `$join` | `-d <delim>` | Joins all elements by `delim`; when `delim` is not provided, platform-specific EOL is used |
| `$grep` | `-v` | The first argument is a pattern; from the rest of the arguments, selects those that [match](#Match-Operator) the pattern.  `-v` implies inverse selection. |
| `$str` | | Concatenates all args into a string |
| `$head` | `-n <num>` | Flattens all args and takes first `num`. |
| `$tail` | `-n <num>` | Flattens all args and takes last `num`. |
| `$toJson` | | Flattens all args and converts them to JSON.  Arguments' properties are traversed only 1 level deep. |
| `$toCsv` | | Flattens all args and converts them to CSV. |
| `$save` | | The first argument is the file name; all other arguments are flattened, rendered to string, and saved to the file with that name |
| `$append` | | The first argument is the file name; all other arguments are flattened, rendered to string, and append to the file with that name |