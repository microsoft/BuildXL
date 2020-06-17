# Namespaces
Namespaces in DScript serve the same purpose as in other languages like C#. Namespaces can help control the scope of a type name, which becomes useful in large projects. Use the `namespace` keyword to declare a namespace:

```ts
namespace Ns {
    export const x = 42;
}

export const y = Ns.x;
```

## Namespaces Overview

Namespaces have the following properties:
* Unlike in TypeScript, namespaces in DScript are implicitly exported and visible in all projects in the same module.
* Namespaces can be nested or delimited by using the `.` operator.
* Aliases can be created by using local variables, e.g., `const ns = Ns.NestedNs;`.
* Namespaces in DScript are "partial" and merged together.
* The global namespace can be referenced using `$` symbol.

## Namespaces are explicitly exported
By default all the namespaces are visible inside the current module:

```ts
namespace X {
    const x = 42; // x is "private" to the namespace X
    namespace Y {
        export const y = 42;
    }
}

const x = X.x; // error "x" is not "exposed" to other namespaces
const y = X.Y; // OK, namespaces are "exported" by default.
```

## Nested namespaces

Namespaces can be nested or the name of the namespace can be delimited by the `.` character:

```ts
namespace Outer {
    namespace Middle {
        namespace Inner {
            export const x = 42;
        }
    }
}

namespace Outer2.Middle2.Inner2 {
    export const x = 42;
}

const x1 = Outer.Middle.Inner.x; // 42
const x2 = OUter2.Middle2.Inner2.x; //42 as well
```

## Namespace aliases
There is no `using directive` in DScript, but the same behavior can be achieved differently. Namespaces in DScript are first class citizens: they can be stored in a variable, returned from a function or passed as an argument. This means that `using directive` behavior can be achieved simply by using a local variable:

```ts
// Using the same namespace structure from the previous example
const ns = Outer.Middle.Inner;
const x = ns.x;
```

## Namespaces are "partial" and merged together

All namespaces in DScript within one module are partial and merged together:

```ts
// project1.bc
namespace A {
   export const x = 42;
}

// project2.bc
namespace A {
    // export const x = 42; // error: "x" is already defined
    export const y = 42;
}

// project3.bc
namespace A.B {
  export const z = 42;
}

// project4.bc
const r1 = A.x + A.y + A.B.z;
```

## Naming guidelines

It is encouraged for namespaces to follow the directory structure. This aids name discoverability across large codebases in the absence of IDE or searchable index. For more details on naming guidelines see [Style guidelines](/BuildXL/Reference-Guide/Style-guidelines)

### Do not use module names in the namespaces

Namespaces should NOT include the name of the module. 

Strongly discouraged:
```ts
// module "DominoSdk"
namespace DominoSdk.Managed {
}
```

Ok:
```ts
// module "DominoSdk"
namespace Managed {
}
```

### Do not duplicate namespace names

Namespaces in a module should NOT be duplicated.

Strongly discouraged:
```ts
namespace Managed.Sdk.Managed {
}
```

Ok:
```ts
namespace ManagedSdk.Managed {
}
```

The reason being that DScript name lookup for expressions starts with the nearest namespace to the current position:

```
namespace Managed {
    export const x = 42;
}

namespace Managed.Sdk.Managed.Runner {
   const y = Managed.x; // Error: Property 'x' does not exist on type 'typeof Managed'.
}
```
When the expression `Managed.x` is analyzed, BuildXL resolves the name `Managed`. In this case, the name resolution starts from the innermost namespace and `Managed` name resolves to `$.Managed.Sdk.Managed` but not to `$.Managed`.

### Avoid using root namespace identifier `$`
The root namespace identifier `$` should be used only to disambiguate the namespace name when the namespace name has a duplicated name in the hierarchy. **Having project files with many usages of `$.` likely indicates a need to refactor your namespace hierarchy.**

### Do not use namespaces that match the common names

BuildXL has a set of built-in types and functions available everywhere without any explicit import declarations. Custom types and namespaces have a precedence and can hide built-in ones. This can lead to confusion and hard-to-understand errors. For example, BuildXL has a built-in namespace `Context` (used often for pip construction); when the same namespace is declared in a project file, like in

```ts
namespace A.Context.B {
    const x = Context.getMount("SourceRoot").path; // error: Can't find getMount in namespace A.Context.
}
```

it becomes **impossible** to use the built-in namespace `Context`! Even the root namespace identifier wouldn't help, because `$` "points" to the root of the current module, but not to the root of the global scope:

```ts
namespace A.Context.B {
    const x = $.Context.getMount("SourceRoot").path; // error: Can't find getMount in the global scope of the current module
}
```


# Const
All values in DScript must be labeled as immutable with the `const` keyword (see functions and `let` below). This is a notable restriction on top of TypeScript.

```ts
const arg = 42;                   // OK.

const array = [1, 2, 3];
array[0] = 0;                     // Not OK. Array elements are immutable.
array = [4, 5, 6];                // Not OK. Cannot be redefined.
const newArray = [4, 5, 6];       // OK.

const o = { a: 1, b: 2 };
o.b = 3                           // Not OK. Object properties are immutable.
o = { a: 4, b: 5 };               // Not OK. Cannot be redefined.
const newObject = { a: 4, b: 5 }; // OK.
```

# Let

Variables may be defined with `let` within functions and loops. `let` variables may be reassigned. However, if the `let` variable is an object or array, its innards (object properties and array elements) cannot be modified.

```ts
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
# Visibility
There are three classes of visibility in DScript: **public**, **internal**, and **private**.
* **private:** only visible in the same scope
    * This is the default visibility
    * Not visible to other project files in the same module (even if they share a namespace)
    * Not visible in an outer namespaces (see [Namespaces](/BuildXL/User-Guide/Script/Reusing-Declarations-(Factoring)/Namespaces))

```ts
namespace MyTest {
    namespace InnerNamespace {
        // private, only visible within MyTest.InnerNamespace namespace
        const a = "A";
    }
}
```

* **internal:** only visible in the same module
    * Requires the `export` modifier (see [Imports and Exports](/BuildXL/User-Guide/Script/Imports-and-Exports))
    * Visible to other project files in the same module in the same namespace and child namespaces
    * Not visible in outer namespaces or other modules

```ts
namespace MyTest {
    namespace InnerNamespace {
        // internal, visible within the same module
        export const b = "B";
    }
}
```
* **public:** visible across modules
    * Requires the `@@public` access modifier decoration and `export` modifier
    * Visible within this module
    * Visible within other modules which import the module the public value belongs to

```ts
namespace MyTest {
    namespace InnerNamespace {
        // public, visible across modules
        @@public
        export const c = "C";
    }
}
```

Example:
```ts
namespace MyTest {
    namespace InnerNamespace {
        // private, only visible within MyTest.InnerNamespace namespace
        const a = "A";

        // internal, visible within the same module
        export const b = "B";

        // public, visible across modules
        @@public
        export const c = "C";

        // Invalid: @@public values MUST be exported
        @@public
        const d = "D";
    }

    // Invalid: 'a' is not visible here
    const e = InnerNamespace.a;

    // public and internal values are visible here
    const f = InnerNamespace.b;
    const g = InnerNamespace.c;
}
```