# DScript (Ds) vs TypeScript (Ts)

We compare DScript (Ds) and TypeScript (Ts). In particular we compare Ts scripts that are targeted for Node.js, i.e., 
scripts that will be compiled with "`-m commonjs`" option.

There are plenty of differences between Ds and Ts. In this document we describe only the major differences. 

## Modules (or files) and namespaces

In Ts, modules (or files) and namespaces are code blocks that are executed from top to bottom. The code block 
can consist of variable and type declarations, as well as, statements (or expression statements). In Ds, modules (or files) and namespace 
are unordered sets of variable and type declarations. 

For example, in Ts the following script prints `Hello`:
```javascript
function print(s: string): number { 
  Console.writeLine(s); // console.log(s) -- Node.js
  return s === undefined ? -1 : s.length;
}
let x = "Hello";
print(x);
``` 
In Ds, this script is syntactically invalid because `print(x)` is not a declaration; it's an expression statement.

Similarly, in Ts the following script prints `Hello`, but is syntactically invalid Ds script:
```javascript
namespace N {
  function print(s: string): number { 
    Console.writeLine(s); // console.log(s) -- Node.js
    return s === undefined ? -1 : s.length;
  }
  let x = "Hello";
  print(x);
}
```

To make the above script valid Ds script, we put `print(x)` in a variable declaration:
```javascript
namespace N {
  function print(s: string): number { 
    Console.writeLine(s); // console.log(s) -- Node.js
    return s === undefined ? -1 : s.length;
  }
  let x = "Hello";
  let y = print(x);
}
```
Now, Ds will print `Hello`. 

The order of declarations/statements in Ts matters, while in Ds, the declarations are unordered.
For example, if we swap the order of declarations of `x` and `y`, i.e.,
```javascript
namespace N {
  function print(s: string): number { 
    Console.writeLine(s); // console.log(s) -- Node.js
    return s === undefined ? -1 : s.length;
  }
  let y = print(x);
  let x = "Hello";
}
```
in Ts the evaluation prints `undefined`, while in Ds the evaluation prints `Hello`.

In Ds, one can think of module (or file) and namespace as symbol tables mapping symbols (the left-hand side variables)
to expressions.

## Cached and on-demand evaluations

In Ds the evaluations of the right-hand side expressions of variable declarations are cached and on-demand. Let's have
two files:
```javascript
// file: project1.ts
import * as N from './project2.ts';
let x = N.y + N.y;
```  
```javascript
// file: project2.ts
function print(s: string): number { 
  Console.writeLine(s); // console.log(s) -- Node.js
  return s === undefined ? -1 : s.length;
}
let x = "Hello";
export let y = print(x);
let z = "World";
export let w = print(z);
```
Let's evaluate `project1.ts`. In Ts the evaluation prints `Hello` followed by `World` in the subsequent line, while
in Ds the evaluation prints only `Hello`. That is, since `w` in `project2.ts` is not needed by `project1.ts`,
the evaluation doesn't print `World`. Moreover, although there are two references of `N.y` in `project1.ts`, `y` in
`project2.ts` is not evaluated twice. It's evaluated once, and the result is cached.

## Flat namespaces

In Ts, namespaces can be nested, e.g.,
```javascript
namespace M {
  namespace N {
  }
}
```
which can be written as: where
```javascript
namespace M.N {
}
```
In Ts, namespaces are mergable, e.g., 
```javascript
namespace M {
  let w = -1;
}
namespace M.N {
  let x = 0;
}
namespace M.N {
  let y = 1;
}
namespace M.O {
  let z = N.y;
}
```
can be written as
```javascript
namespace M {
  let w = -1;
  namespace N {
    let x = 0;
    let y = 1;
  }
  namespace O {
    let z = N.y;
  }
}
```

In Ds, namespaces are flat. A namespace in Ds can have dotted identifiers, like `M.N`, as its name, but that does not mean
that there is a namespace `N` nested inside a namespace `M`. One can think of the namespace as having the string `"M.N"` as
its name. Such a name is called a namespace identifier in Ds. 

One can still write namespaces like
```javascript
namespace M {
  let w = -1;
  namespace N {
    let x = 0;
    let y = 1;
  }
  namespace O {
    let z = M.N.y;
  }
}
```
but Ds will translate it to
```javascript
namespace M {
  let w = -1;
}
namespace M.N {
  let x = 0;
}
namespace M.N {
  let y = 1;
}
namespace M.O {
  let z = M.N.y;
}
```
Note that, in the declaration of `z`, we can no longer refer to `y` as `N.y` because there is no namespace with name `N`; `y` resides
in a namespace name `M.N`. 

## Two kinds of indentifier

In Ds there are two kinds of identifier: standard identifier and namespace identifier. A standard identifier is like identifiers in
typical programming languages, but it cannot start with an upper-case letter. This kind of identifier is used by variable and
function declarations. A namespace identifier is dotted identifiers, each starts with an upper-case letter, as exemplified in the
previous section. Thus, this script
```javascript
namespace m {
    let X = 0;
}
```
is not a valid Ds script because the namespace name starts with a small-case letter `m`, and the declared variable `X` starts with
an upper-case letter.

One peculiar aspect of having module identifiers is the selector/projection operator `<expr>.selector`. The projection `x.p` consists
of `x` as the lhs expression and `p` as the selector, and `x` should evaluate to an javascript object literal `{ ... }`. If `p` 
is not in that object literal, then the evaluation results in `undefined`. The projection `M.N.x` consists of `M.N` as the lhs expression, 
and `x` as the selector. `M.N` should evaluate to a namespace, and `x` must exist in that namespace, otherwise an error is thrown.

Enum types are treated as namespaces, and as a consequence, its enum elements cannot start with upper-case letters, e.g.,
```javascript
enum E {
  e1,
  e2,
  e3
}
```

## Import-export

In Ts, all imports must be named. Let's have three scripts:
```javascript
// file: project1.ts
import * as Other from './project2.ts';
namespace P1 {
  export let x = Other.P2.y + Other.w;
}
```
```javascript
// file: project2.ts
import {P3 as OtherP, v as otherV} from './project3.ts';
export let w = 7;
namespace P2 {
  export let y = 1;
  export let p3z = OtherP.z; 
}
```
```javascript
// file: project3.ts
export let v = 13;
namespace P3 {
  export let z = 0;
}
```
In `project1.ts` we import the file `project2.ts` as a module, and name it `Other`. In `project2.ts` we import `project3.ts`,
but we specifically choose to import the namespace `P3` and the variable `v`, and name them `OtherP` and `otherV`, respectively.

In Ds we introduce the infamous unnamed import `import * from <file>`. Now, we can modify `project1.ts` as follows:
```javascript
// file: project1.ts
import * from './project2.ts';
namespace P1 {
  export let x = P2.y + w;
}
```
Suddenly all exported values in `project2.ts` become available in `project1.ts`. One can think of this unnamed import as `C`'s `#include`. 
Admittedly, unnamed imports makes authoring build spec convenient, and have been used pervasively during our self-host effort.

For convenience, unlike Ts, Ds allows an import to be re-exported. Let's change `project2.ts` as follows:
```javascript
// file: project2.ts
export import {P3 as OtherP, v as otherV} from './project3.ts';
export import * from './project4.ts';
export let w = 7;
namespace P2 {
  export let y = 1;
  export let p3z = OtherP.z; 
}
```
where `project4.ts` is as follows:
```javascript
// file: project3.ts
export var p = 0;
namespace P4 {
  export let q = 0;
}
```
The `export` modifier of `import` means re-export what are imported by the `import` declaration. Now, in `project1.ts`, we can access
more values as shown below:
```javascript
// file: project1.ts
import * from './project2.ts';
namespace P1 {
  export let x = P2.y + w + OtherP.z + otherV + p + P4.q;
}
```

To achieve the same thing in Ts, one has to re-export manually all imported values, which can be cumbersome.

Note that, with unnamed imports and re-exported imports, we completely lose discoverability of values. For example, if you look at `project1.ts` only,
you have to follow the transitive relation of export-import to find where `p` is declared.

## Lazy namespace merging

In Ts, when you target Node.js, imports do not cause namespace merging because all imports are named. For example,
```javascript
// file: project1.ts
import {N} from './project2.ts';
namespace N {
  export let x = y + z;
  export let z = 42;
}
```
```javascript
// file: project2.ts
namespace N {
  export let y = 0;
  export let z = 0;
}
```
causes duplicate declaration of `N` in `project1.ts`. In Ds, we merge the namespace `N` in `project1.ts`. Thus, for the expression `y + z`
in `project1.ts`, `y` resolves to `N.y` in `project2.ts`, while `z` resolves to the `N.z` in `project1.ts`.

Let's extend the example a bit to three files:
```javascript
// file: project1.ts
import {N} from './project2.ts';
import {N} from './project3.ts';
namespace N {
  export let x = y + z + w;
  export let z = 42;
}
```
```javascript
// file: project2.ts
namespace N {
  export let y = 0;
  export let z = 0;
}
```
```javascript
// file: project3.ts
namespace N {
  export let w = 0;
}
```
Due to merging of namespaces in `project1.ts`, `w` in `y + z + w` resolves to `N.w` in `project3.ts`. If we now slightly change `project3.ts`
to 
```javascript
// file: project3.ts
namespace N {
  export let y = 0;
}
```
then the resolution of `y` in `y + z + w` results in ambiguity; is it `N.y` in `project2.ts` or in `project3.ts`? But if, instead of `y + z + w`,
we just have `z + w`, then the evaluation in Ds doesn't give rise to an error. That is, ambiguous references in Ds are detected lazily upon uses.

## Import with qualifiers

Ds' imports can be parameterized with qualifiers. For example:
```javascript
import * as X86 from './detours.ts' with {platform: "x86"};
import * as X64 from './detours.ts' with {platform: "x64"};
namespace N {
  export let dlls = {x86: X86.dll, x64: X64.dll};
}
```

## Immutability

In Ds, globally declared variables are not modifiable. For example, this script
```javascript
let x = 0;
let x = 1;
```
results in duplicate declaration in Ds, while in Ts, you can modify `x` as follows:
```javascript
let x = 0;
x = 1;
console.log(x);
```
The above script prints `1`. 

In Ds, in a function body, one can only modify the values of local variables. That is, the left-hand side of 
an assignment must be an identifier expression. Object and arrays are immutable in Ds. For example,
```javascript
function f(arg) {
  arg = 42;                // OK
  
  let x = 0;
  x = 7;                   // OK
  
  let array = [1, 2, 3];
  array[0] = 0;            // Not OK.
  array = [4, 5, 6];       // OK.
  
  let o = { a: 1, b: 2 };
  o.b = 3                  // Not OK.
  o = { a: 4, b: 5 };      // OK
}
```

Captured variables are not modifiable by the capturing lambda. For example:
```javascript
function f() {
  let x = 0;
  let g = () => { let y = x; return y; };        // OK
  let h = () => { let y = 0; x = y; return y; }; // Not OK, x is captured and modified.
}
``` 

## Path vs String

Unlike Ts, Ds distinguishes paths from strings, syntactically and semantically. Paths cannot be converted to strings, and strings
cannot be converted to paths. Paths in Ds are absolute. Ds denotes paths by single quotes `'` and strings by double quotes `"`.
In a file `C:/path/to/file.ts`, an absolute path can be written as a relative path, for example, in this script
```javascript
// file: C:/path/to/file.ts
let x = 'path/to/somewhere';
let y = "path/to/somewhere";
```
`x` evaluates to the absolute path `'C:/path/to/relative/to/somewhere'`, while `y` evaluates to string `"path/to/somewhere"`.

A relative denotation of path in Ds can start with `'/'`, e.g., `'/path/to/another'`. The leading `/` means the directory of
the root of the file. The root in Ds is identified by the configuration file or a package file.

Ds also distinguishes path atoms from strings. That is, in the following script:
```javascript
let x = 'path/to/somewhere';
let y = x.getName() === "somewhere";
```
`y` evaluates to `false` because `x.getName()` evaluates to the path atom `somewhere` which is different from string `"somewhere"`.
To create a path atom, one uses `PathAtom.create("somewhere")`, and then in this script
```javascript
let x = 'path/to/somewhere';
let y = x.getName() === PathAtom.create("somewhere");
```
`y` evaluates to `true`.

Distinguishing paths and path atoms from strings is beneficial for cross platform; one OS compares files case-insensitively, while others
case-sensitively. Moreover, it is also important for (distributed) caches.

## Minors

Some minor differences:
- Numbers are integers in Ds, but double precision float in Ts.

## Build organization

Ds has a well-defined build organization consisting of configuration, package/package descriptor, and projects. Ts is not a build system, and hence
has no build organization.

# DScript compatibility

This document represents thoughts and requirements related to the DScript compatibilities with TypeScript and JavaScript features.

*TODO: change the structure of this document to reflect TypeScript spec structure. This will provide more systematic overview of the language and will simplify readability and future changes*.

Additional information about TypeScript features can be found in the [TypeScript Specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md).

## Core requirements
DScript is a subset of the TypeScript language with minor (if any) syntax differences. Although the syntax is compatible, semantic could be different from TypeScript language in some regards.

### Stronger requirements
DScript is a subset of the TypeScript with stronger requirements for type safety and immutability.

All objects in DScript considered immutable (or, rather, init-once). Unfortunately there is no way to express immutability in TypeScript type system, so we'll make additional changes to TypeScript compiler/IDE integration to enforce immutability (*TBD: should we check immutability in runtime? We'll definitely 'fix' TypeScript compiler to emit errors in the IDE for any assignments but runtime semantic is still not clear for me!*)

As a general rule, any uses of non-supported features should lead to build failure (*TBD: should we separate 'compile-time build failure' from 'runtime build failure'?*) and should lead to errors from custom language service in IDE.

## Syntax Compatibility

### Statement separation

*Automatic Semicolon Insertion at [ECMA-262 spec section 7.9](http://www.ecma-international.org/ecma-262/5.1/#sec-7.9).*

JavaScript and TypeScript has special rules around statement separation, EOL and ';'. In most cases semicolon is optional and EOL symbol could be used to determine a new statement in those languages. But there are cases where JavaScript/TypeScript compilers DOES NOT consider EOL as a statements terminator. This is well-known as Automatic Semicolon Insertion (ASI). Consider the following example:

```javascript
// define a function
var fn = function () {
    //...
} // semicolon missing at this line

// then execute some code inside a closure
(function () {
    //...
})();
``` 

In this case TS/JS compilers will treat this code as one statement because open EOL + paren `(` is not considered as a statement separator in JavaScript.

To avoid this confusion and complications, DScript requires to use semicolon to denote different statements. This lead to minor syntax inconsistency between DScript and TypeScript, but the team intentionally decided to go in this direction to simplify implementation and reduce potential confusion for the user.

This means that following code has different number of statements in DScript and in TypeScript:

```typescript
let x = 21;
let y = x * 2
+x;
```

In TypeScript/JavaScript there is 3 statements, but in DScript there are only 2 of them. This difference means that running this code will produce different results. In TypeScript `y` would be `42` but in DScript it would be `63`.

### Statement separation rules in DScript
*TODO: add a set of rules for this.*

## DScript Primitive Types
*Primitive Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2).*

DScript has almost the same set of primitive types that TypeScript has, but with minor differences.

### The Number Type
*The Number Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.1).*

The `Number` primitive type corresponds to the `System.Int32` type from .NET Framework and represents signed 32-bit integer. 

Using floating point literals in any form will lead to an error.

Unlike TypeScript/JavaScript all arithmetic operations on numbers are running in 'checked' context. If the result of an operation does not fits in 32 bits integer, runtime exception will occur. 

### The Boolean Type
*The Boolean Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.2).*

The `Boolean` type corresponds to the similar JavaScript/TypeScript primitive type and represents logical values that are either `true` or `false`.

### The String Type
*The String Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.3).*

The `String` primitive type corresponds to the similar JavaScript/TypeScript primitive type and represents an immutable sequence of characters stored as Unicode UTF-16 code units.
String literals could be restricted to double-quotes and backticks only and single quotes could be used to represent a `Path` type!

### [NOT SUPPORTED] The Symbol Type
*The Symbol Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.4).*

The `Symbol` type is not supported for now.

### The Path Type
The `Path` type is a new primitive type introduced by DScript.

Currently the `Path` literal is represented by the custom format function name `P` and later single-quoted strings could be used instead. To support single-quoted string as path literals we will add an extension to the typescript intellisense in the IDE's to support the user with this.

### The Void Type
*The Void Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.5).*

The `void` type, referenced by the `void` keyword, represents the absence of the value and is used as the return type of the function with no return value.
Unlike TypeScript, DScript disallows variable with `void` type. Following code is a valid TypeScript code but should lead to an error from DScript:

`let x: void;`

### [NOT SUPPORTED] The Null Type
*The Null Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.6).*

The `Null` type corresponds to the similar named JavaScript primitive type and it is not supported by the DScript. DScript users should use `undefined` to denote absence of the value.

### The Undefined Type
*The Undefined Type at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.2.7).*

The `Undefined` type corresponds to the similarly named JavaScript primitive type and is the type of the `undefined` literal. **This is the only way to represent absence of a value in DScript**.

### [NOT SUPPORTED] Regular Expression Types
*RegExp objects at [ECMA Script specification](http://www.ecma-international.org/ecma-262/5.1/#sec-15.10).*

JavaScript and TypeScript has an ability to use special string literals (wrapped in slashes, '/') for regular expressions:

```typescript
// Looking for 0 or more digits in case-insensitive manner
let regEx = /\d*/i
// matched
console.log(`'42' is '${regEx.test('42')}'`);
// not matched
console.log(`'not a number' is '${regEx.test('not a number')}'`);
```

DScript does not support this feature, but should emit a clear error message for them.

Notes for implementer: potentially, this could be a tricky deal, because regular expression literals introduces ambiguity. For instance, following regular expression literal contains a closing comment token: `/\d+*/i`.

### Primitive Types, aliases and Wrapper Objects

TypeScript and JavaScript has a notion of primitive types, aliases and wrapper objects. From TypeScript/JavaScript perspective `String` is a primitive type and `string` is an alias. Primitive types and aliases are very similar concepts but they have semantic differences.

#### Assignability
*TypeScript assignment compatibility at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.11.4).*

From TypeScript perspective primitive types and aliases are **almost** interchangeable and equivalent from runtime perspective:

```typescript
let n1: number = 42;
let n2: Number = 42;

let s1: string = "foo";
let s2: String = "foo";
```

But primitive types and aliases are not equivalent from assignment compatibility:

```typescript
let n1: number = 42;
let n2: Number = 42;

let n3: Number = n1; // OK
let n4: number = n2; // Error "Type 'Number' is not assignable to type 'number'"
```

#### Equality
Primitive types and aliases are very similar from type system perspective, but they're not the same when instances are involved created via `new` operator.

Consider following examples:

```typescript
let b = new Boolean(false);
if (b) {
	console.log('true');
}
else {
	console.log('false');
}
```

Previous code will print `true` because any objects (and instances of primitive types are objects) are Truthy.

The same equality issues could be faced with other primitive types as well:

```typescript
let n1: Number = 42;
let n2: Number = new Number(42);
let n3: number = 42;

alert(n1 == n2); // true
alert(n1 === n2); // false
alert(n1 === n3); // true
```

**To avoid confusion and unnecessary complexity, DScript will only allow aliases like `number`, `string`, `boolean` and `path` but not equivalent primitive types - `Number`, `String`, `Boolean` or `Path`.**

### Binary Operators
*Binary Operators at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.19).*

DScript does support only small subset of all available TypeScript/JavaScript operators. Following sections describes what operators are supported and what are not.

#### The * operator
The binary `*` operator supported only for numbers. For any other types it compile time error will occur (including `any`).

#### The + operator
The binary `+` operator supported only for numbers and strings. Using this operator with any other types (including `any`) will lead to compile-time error.
Any attempts to mix types in this operators (including strings and numbers) also lead to an error.

```typescript
// Numbers
let x = 42;
let n1 = x + 2; // OK
let n2 = x + '2'; // Compile time error
let n3 = x + true; // Compile time error
let n4 = x + undefined; // Compile time error
let y : number = undefined;
let n5 = x + y; // Runtime error, n5 IS NOT undefined!

// Strings
let s1 = y.toString() + '2'; // OK
let s2 = y.toString() + 2; // Compile time error
```

`+` operator can't be used with enums as well:

```typescript
// Enums
let e = CustomEnum.Value1;
let n6 = x + e; // Compile time error!
``` 

Please note, that using `+` operator when any operand is `undefined` will lead to an error during runtime!

#### Other Arithmetic Operators (/, %, -)
Those binary operators are not supported. Yes, multiplication is supported but division - is not! The reason for this decision is rounding problem.

#### Unary +, -
Unary `+` and unary `-` are not supported.

#### Bitwise operators (<<, >>, >>>, |, &, ^)
Following operators are supported only on numbers and const enum members: `<<`, `>>`, `|`, `&`, `^`.

*TBD: we should decide which version of the right shift to support: `>>` or `>>>`. First uses sign bit and another one - `0` for shifted bits.*
*TBD: does it means that for consistency reasons we should support arithmetic operations on enum values? Or enums will have only bitwise operators?*
 
#### Comparison operators

* Following operators are supported: `===`, `!===`, `<`, `<=`, `>`, `>=`.
*TODO: Add description for `<` operators: how they'll behave for strings.*
* Following operators are not supported: `==`, `!=`. 

#### Logical Operators: &&, ||
Logical operators (`&&` and `||`) in boolean context (if statement, ternary operator, while loops) are supported only for booleans. There is no implicit conversion from objects to booleans!. Using this operator with any other types (including `any`) else inside boolean context will lead to compile time error.

Outside the boolean context, following operators could be used to emulate Null-coalescing operator a.k.a. `??` and Null-propagating operator a.k.a. `?.`:

```typescript
let obj = {
	x: {
		y: 42,
		z: undefined,
	}
}

let z = (obj && obj.x && obj.x.z) || 0;
```

This code is semantically equivalent to the following C# code:

```csharp
var obj = new { X = new { Z = (int?)null }};
var z = obj?.X?.Z ?? 0;
```

Please note, that operators `&&` and `||` should follow common short-circuiting rules.

#### The Conditional operator
*The Conditional operator at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.20).*

Conditional operator is supported by DScript but requires that `test` should be a boolean expression: `test ? foo : boo`

#### Assignment Operators
*Assignment Operators at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.21).*

An assignment of the form `v = expr` requires `v` to be an identifier and `expr` should be assignable to the type of `v` (see Assignment Compatibility section), otherwise compile-time error occurs.

*TODO: refine this section. The idea that types in DScript should be immutable (or should allow one-time-assignment, because object literals technically uses assignment as well).*
*TODO: add this as a valid case: `a = b = 42;`*

#### [NOT SUPPORTED] The `++` and `--` operators
Those operators are not supported in DScript.

#### [NOT SUPPORTED] Compound assignment operators
**All compound assignment operators (`*=`, `/=`, `%=`, `+=`, `-=`, `<<=`, `>>=`, `>>>=`, `&=`, `^=`, `|=`) are prohibited in DScript.**

#### [NOT SUPPORTED] Destructuring Assignment

*Destructuring Assignment at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.21.1).*

A **destructuring assignment** is an assignment operation in which the left hand operand is a destructuring assignment pattern as defined by the *AssignmentPattern* production in the ECMAScript 6 specification. 

Destructuring Assignment is not supported in DScript. At least in the near future.

#### Other operators
* Comma operator (`,`): NOT SUPPORTED.
* `instanceof`: NOT SUPPORTED.
* `typeof`: SUPPORTED. (*TBD: Maybe we need to support this if we need it for typesafe module declaration. I saw an example of typeof in our examples*).
* `void`: NOT SUPPORTED. Allows to discard expression results.
* `in`: NOT SUPPORTED. Allows to check that property exists in the object (`let b = "foo" in customObject;`).
* `delete`: NOT SUPPORTED. This is a JavaScript operator that allows to delete properties from objects. 

## Literals in DScript

Following literals are supported by DScript:

* Decimal literals (only based on 10)
* String Literals
  - `'Single-quoted string'`
  - `"Double-quoted string"`
  - ``Backticked string with interpolation``
* Path Literal
  - P`foo.xml` (this could be changed to single-quoted string in the future).
* Object Literals (with some restrictions) (*Object Literals at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.8.3)*). 

Following literals are not supported:
* Floating points (in any notation)
* Regular Expression Literals

## DScript Type System
Object types are composed from properties, call signatures, and index signatures, collectively called members.

Class and interface type references, array types, tuple types, function types, and constructor types are all classified as object types.

Not all of the existing object types from TypeScript/JavaScript are supported in the DScript.
*TODO: add more stuff here. We can mention key concepts here as well, like union types, interfaces lack of classes etc.*

### Structural Typing Nature
*Structuring Subtyping at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#14-structural-subtyping).*

DScript inherits structural typing nature from TypeScript. This has its own pros and cons and DScript (and their users) should live with that.

In structural typing world notions of sub-type/super-type/type-equality are not based on explicit inheritance chain and name equivalence. The easiest way to describe structural typing is to show few examples:

```typescript
// 1. Type equivalence
interface Foo {
	x: number;
}
function foo(f: Foo): void {}

// Object literal is assignable to Foo interface
// because both of them have the same structure
foo({x: 42});

// 2. Subtyping
// Newly created object is a subtype of Foo because
// it declares at least the same members that are required by Foo
// TODO: this will lead to an error with TypeScript 1.6! Refine the sample!
foo({x: 42, y: -1});

```

The same idea is working for functions as well:

```typescript
function foo(x: number, y: number): number {return 42;}

let f1 : (x: number, y: number, z: string) => void;

// f1 is compatible with foo
f1 = foo;

let f2: (x: number) => void;
// f2 is incompatible with foo
 f2 = foo;

let f3: (x: number, y: number) => string;
// f3 is incompatible with foo, because there is no
// sub-type relationship between return types string and number
f3 = foo;

interface Foo {
	print(f: Foo);
}

let fooObj : Foo = {
	// print() is compatible with foo(Foo)!
	print() {}
}
```

### Assignment compatibility
*Assignment Compatibility at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.11.4).* 

*TODO: Finish this section!*
Just to note, assignment compatibility is relies on structuring typing. 

#### Optional properties and assignment compatibility

TypeScript type system is not sound and there is a lots of ways how to violate type safety. One way to do this is by using optional properties:

```typescript
interface Foo {
	x?: number;
}

interface Boo {
	x: number;
}

function consumeBoo(b: Boo) {}

// TODO: It seems that this behavior has been changed in 1.6! Double check this example! 
let f : Foo = {
}

// Argument of type Foo is not assignable to parameter of type Boo
consumeBoo(f);

// Just fine!
consumeBoo({x: f.x});
```

*TBD: are we going to restrict this behavior in runtime somehow?* 

#### Excess Properties
*Excess Properties at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.11.5).*

The subtype and assignment compatibility relationships require that source types **have no excess properties with respect to their target types**. The purpose of this check is to detect excess or misspelled properties in object literals.

Consider following example. Suppose we have following API for running C# compiler:

```typescript
interface CscArguments {
	sources: File[],
	references?: File[],
}

function compile(args: CscArguments): Result {
	// ... implementation
}
```

Starting from TypeScript 1.6 user would not be able specify any properties that are not in `CscArguments` during `compile` method call:

```typescript
// OK
let ok = Csc.compile({
	sources: ['foo.cs']
});

// Error, there is no property called refs!
let failure = Csc.compile({
	sources: ['foo.cs'],
	refs: ['System.dll']
});
``` 

### Array Types
*Array Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.3.2).*

*Array types* represent immutable arrays in the DScript. Unlike JavaScript/TypeScript arrays in DScript are immutable and all mutation functions always producing a new instance.

Any attempts to mutate an array instance via `ar[0] = 42` will lead to compile-time error from both DScript interpreter and from custom DScript language service.
To avoid common errors with immutable types, custom analysis could be added to emit warning/error when the result of the operation is ignored.
For instance, DScript interpreter, custom language service or both should emit warning/error for this code:

```typescript
let ar = [42];
// push in DScript returns new array
// Warning/Error should be provided to the user: return object was ignored for pure operation.
ar.push(1);
``` 

#### Spread Operator (Array Accumulation)

Array literals can contain spread elements to invoke `concat` method. For example, following assignments:

```typescript
let s = ['file1.cs'];
let sources = ['file2.cs', 'file3.cs', ...s, 'file4.cs'];
```

Are equivalent to:

```typescript
let s = ['file1.cs'];
let sources = ['file2.cs', 'file3.cs'].concat(s, ['file4.cs']);
```

**Null-ness semantic and spread operator**
Due to ECMA Script specification (TODO: proof?) when spread object is `null` or `undefined` runtime exception should occur. In current TypeScript implementation (version 1.6) `null`/`undefined` element would be inserted into the target array.
TBD: DScript should decide on runtime behavior: We can treat `undefined` arrays as an empty array (this can simplify some use cases) or follow ECMA Script semantic and throw runtime error. 


#### [TBD] Custom Array Type with at least one element
Team is considering to add special array type that should hold at least one element. This is TBD for now.

### [NOT SUPPORTED] Classes
*Classes at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#8).*

Classes are not supported by DScript.

### Interfaces
*Interfaces at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#7).*

Interfaces are the main building blocks for DScript and they provide the ability to name and parameterize object types and to compose existing named object types into new ones.

DScript inherits structural nature of the type system and follows TypeScript rules in terms of assignment compatibility and type inference. Also, interfaces in DScript (as in TypeScript) are "open-ended". This means that interface declarations with the same qualified name relative to a common root contributes to a single interface.

#### String-based property names in string literals

DScript supports following object literals:

```typescript
interface Foo {
	"x": number;
}
let f : Foo = {
	"x": 42,
}
```

#### [NOT SUPPORTED] Indexer-based access to properties
DScript DOES NOT SUPPORT brackets (`[]`) to access interface members:

```typescript
interface Foo {
	x: number;
}
let f : Foo = {
	x: 42;
}
console.log(f['x']);
``` 

(Please note, that TypeScript still checks and infers types for this type of property access, but it gives up if the property is unknown).

#### [NOT SUPPORTED] getters and setters
TypeScript/JavaScript supports getter and setters in object literals. Consider the following example:

```typescript
interface Foo {
	x: number;
}

function print(f: Foo): void {
	console.log(`x: ${f.x}`);
} 

let f = {
	// Defining read-only property
	get x() {return 42;}
}

print(f);
```

**This code is absolutely legitimate, but DScript does not support it.**

#### [NOT SUPPORTED] Callable interfaces
Interfaces in TypeScript could behave similar to functors from C++, i.e. they could be callable:

```typescript
interface Func<T> {
	() : T;
}

// Callable interfaces are assignable from functions with compatible signature 
let f : Func<number> = () => 42;
f(); //42
``` 

But more interesting stuff will happen when callable interfaces will declare additional members:

```typescript
interface Callable {
	x: number;
	() : number;
}

let stub : any = () => 42;
stub.x = -1;
let callable : Callable = stub;

callable(); // 42
callable.x; // -1
```

To avoid potential confusion callable interfaces are not supported by DScript.

### [NOT SUPPORTED] Local Interfaces
In TypeScript interfaces could be declared in a local scope. This feature is not supported in DScript.

### The `this` keyword
*The `this` Keyword at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#42-the-this-keyword).*

The `this` keyword is prohibited in DScript.

There are few reasons for that. First of all, `this` keyword is ubiquitous in OO world and because DScript does not support classes, this concept is no longer critical for DScript. `this` keyword also can be used in any function declaration and this feature is a well-known source of confusion in both JavaScript and TypeScript.

Even outside a free functions `this` keyword could be confusing. For instance, interface methods could be passed around (they are a first class citizens) and legitimate `this` keyword will lead to weird behavior in runtime. Consider the following example:

```typescript
interface Foo {
	x: number;
	y: number;
	print();
}

let f : Foo = {
	x: 42,
	y: -1,
	print() {
		console.log(`x: ${this.x}, y: ${this.y}`);
	}
}
```

Calling `f.print()` will lead to appropriate and highly expected result but user still can capture `print` method and assign it to any other compatible function:

```typescript
let func : () => void;
func = f.print;

// x: undefined, y: undefined
func();
```

**To avoid such confusion, `this` keyword is prohibited.**

To emulate member functions, following pattern could be used:

```typescript
interface Foo {
	x: number;
	y: number;
	print(f: Foo);
}

let f : Foo = {
	x: 42,
	y: -1,
	print(self: Foo) {
		alert(`x: ${f.x}, y: ${f.y}`);
	}
}

f.print(f);
```

### [NOT SUPPORTED] The `new` operator
*The `new` Operator at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.14).*

DScript does not support classes so `new` operator is not supported as well.

This means that following code is also illegal:

```typescript
// Constructors for wrapper objects are not supported
let s = new String();

// Custom factory methods are not supported
interface CustomFactory {
    new(n: number): string;
}

function foo(f: CustomFactory) : string {
	return new f(42);
}
```

TypeScript/JavaScript uses `new` keyword to instantiate object of a wrapper types. Wrapper objects are very close to primitive types but has slightly different semantic. This code will print 'false' because wrapper objects are not the same as regular primitive types. The same is true for Truthiness/Falsiness, following code is truthy:

```typescript
let b = new Boolean(false);
if (b) {
	console.log('truthy');
}
```

**To prevent such kind of confusion, calls to wrapper's object constructors explicitly should lead to an error in DScript!**

### Generics
*Generic Types and Functions at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#1.9).*

TODO: add more details here, generics are complex!

### Union Types
*Union Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.4).*

TypeScript support a way to declare union types:

```typescript
type Arg = string | number;
type Text = string | { text: string }; 
```

This is very useful way of composition and DScript supports this.

```typescript
type CustomString = string;
```

### [NOT SUPPORTED] Intersected Types
*Intersection Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.5)*

DScript does not support intersection types, at least for now. For more information see [3.5 Intersection Types](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#35-intersection-types) at TypeScript specification.

### [NOT SUPPORTED] Tuple Types
*Tuple Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.3.3).*

Tuple types represent JavaScript arrays with individually tracked element types. Currently this feature is not supported by TypeScript. For more information, see [3.3.3 Tuple Types](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#333-tuple-types).

*TBD: This feature could be useful especially for our immutable design. Consider `pop` method from array that can return a tuple: last element and new array.* 

### Functions
*Function Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#3.3.4).*

An object type containing one or more call signatures is said to be a function type. DScript supports only function type literals and arrow-functions but with some restrictions:
**Function type literals could be used only at the top level and could not be declared in inner scopes. In this case arrow-functions should be used!**

Consider the following examples:

```typescript
// Foo.ds
// Allowed: top-level scope!
function foo() {return 42;}

function foo2() {
	// Is not allowed! Only arrow-functions could be used in the inner contexts!
	function innerFoo() {return 42;}
}

function foo3() {
	// Allowed! Arrow-functions could be declared inside other functions!
	let lambda = () => 42;
}
```

TODO: if we're restricting the language and does not allow `this` keyword, do we really need this restriction for inner functions?

#### [NOT SUPPORTED] Overload resolution
*Overload Resolution at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.15.1).*

DScript does not support overloads.

#### Default Arguments
DScript does support default arguments.

#### Arrow functions as a hints for type system
*Arrow Functions at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.11).*

Just for the sake of completeness, consider the following valid TypeScript code (that could be used as an additional test case for DScript):

```typescript
function foo() {return 42;}

let f : () => number = foo; 
f(); //42
```

This code is valid, because `() => number` is a part of type declaration.

**TBD: parameters reassignment**
What would DScript do in a following case:

```typescript
function foo(x: number): void {
	x = 42; //??
}
```

If this would be an error, then DScript should allow only `const` declarations and prohibit even `let`s. But if it would be possible outer variable should not be modified.

### [NOT SUPPORTED] Constructor Function Types
*Constructor Function Types at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#8.2.5).*

Constructor types and constructor literals are not supported in DScript.

### Enums
*Enums at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#9).*

TypeScript support two types of enums: regular enums and const enums. 
To avoid potential confusion only const enums are supported by the DScript. Regular enums allow arbitrary (non-compile-time) expression on the right hand side, but const enums supports only compile-time-constants.
Consider the following example (typescript):

```typescript
let x = 0;
function getValue() {return x++;}
enum CustomEnum {
	Value1 = getValue(),
	Value2 = getValue(),
	Value3 = getValue(),
} 

enum ConstCustomEnum {
	// In 'const' enum declarations member initialization must be constant expression
	Value1 = getValue(),
}
```

Please note, that 'flag' enums are still supported in a const enums:

```typescript
enum CustomEnum {
	None,
	First = 1,
	Second = 2,
	FirstAndSecond = First | Second,
	Third = 4,
	Fourth = 8,
	All = FirstAndSecond | Third | Fourth,
}

let x = CustomEnum.All;

if ((x & CustomEnum.All) === CustomEnum.All) {
	console.log("All bits were set!")
}
```

Instead of using bitwise operator ambient function could be introduced in the prelude: `Enum.hasFlag(x, CustomEnum.all)`.
TODO: previous statement is subjective. Discussion is required. Maybe we would be able to extend the type system to support `hasFlag` natively.

*TBD: still unclear relationship between enums and numbers. Would DScript support implicit conversion from enum values to numbers?*

## Additional TypeScript language features

### Type Assertions
*Type Assertions at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#4.16).*

TypeScript supports 3 different ways to check/change the object type:
* Cast operator (`<SomeType>expr`)
* As operator (`expr as SomeType`)
* instanceof operator (`expr instanceof SomeType ? expr.member : 'unknown'`)

DScript (at least at the beginning) supports only the first version.

## [NOT SUPPORTED] Optional braces for constructor calls
In JavaScript/TypeScript constructors are not regular functions in many aspects, and one of them is 'braces rule'. For instance, following code is equivalent:

```typescript
let o1 = new Object();
let o2 = new Object;
```

Although DScript will emit an error for primitive type constructor calls, parser should recognize previous constructs properly and emit the same error in both cases.

### Scoping Rules and Locals
*Scopes at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#24-scopes).*

TypeScript support 3 ways of local declarations: `var`, `let` and `const`. 

DScript does not support `var` and will force to use ES6 features with more predictable behavior (just to remind, `var` declarations are lifted at the beginning of the function/module and are not lexically scoped).

This restriction will prohibit some silly errors like this:

```typescript
let y = x + 2;
var z = 42;
```

In TypeScript compiles this code and `y` would be `undefined` during runtime. Changing `z` declaration from `var` to `let` will lead to compile-time error.

*TBD: what about assignments? Are we support following code:*
```typescript
let x = 42;
x = 36;
```

If not, then only `const` declarations should be allowed.


### Modules and namespaces

*This section is under design and implementation right now. DScript has slightly different requirements and existing TypeScript module amd namespace concepts could not be used as is.*.

### Rest parameters and Spread Operator
TypeScript support variable number of arguments using following syntax:

```typescript
function push<T>(...args: T[]) {}
```

This helps to pass arbitrary number of arguments:

```typescript
push(1, 2, 3);
push(1);
```

But the user can't pass an array instance directly. In this case spread operator is required:

```typescript
let ar = [1,2,3];
push(ar); // compile-time error
push(...ar); // fine!
```

This is a very powerful feature that will be used all over the places for fun consumers.

### Loops and switch
* `for` loop: SUPOPRTED (*For statement at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#5.5)*)
* `for ... of`: SUPPORTED (this one is similar to `foreach` construct from C#) (*For-Of Statement at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#5.7)*)
* `switch`: SUPPORTED (TODO: maybe even with additional checks for completeness for enums?) (*Switch Statement at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#5.12)*)
* `for .. in`: NOT SUPPORTED (*For-In Statement at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#5.6).*)
* `while` and `do ... while` - NOT SUPPORTED (*If, Do, and While Statements at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#5.4)*)


### Labels
In TypeScript/JavaScript labels could be used with `if`, `switch`, loops. DScript does not support them at all.

### Exception Handling features
No `try/catch/finally` in DScript. (*Try Statement at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#5.14)*)

### Other TypeScript features
* Iterators/Generators: NO ([Generator Functions](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#67-generator-functions))
* async/await: NO
* Triple slash references: YES
* Ambients: YES (*Ambients at [TypeScript specification](https://github.com/Microsoft/TypeScript/blob/master/doc/spec.md#12)*)

## JavaScript Features
DScript does not support majority of JavaScript features.
* Global variables: NO
* Type Coercion: NO
* `arguments`: NO
* `eval`: NO
* `with`: NO
* getters/setters in object literals: NO
* Prototypes: NO