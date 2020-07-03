Templates can be defined or used in project files to specify default values (or augment an existing one). The values and logic defined in a template can be consumed by all projects in the namespace and any namespace children.

Using templates avoids manually passing the values as a parameter through the graph. For example, a common compiler flag like `/nowarn:` or `/define:` can be defined using a template in a project at the top of the module and consumed where the compiler is called for each of its dependent projects.

## Declare
The syntax for declaring the template is as follows:

```ts
export declare const template : SampleTemplateInterface = {
    name: "foobar.cs",
    library: {
        enableStaticAnalysis: true
    }
}

export interface SampleLibraryTemplateInterface {
    enableStaticAnalysis: boolean;
};

export interface SampleTemplateInterface {
    name: string;
    library: SampleLibraryTemplateInterface;
};
```

This is slightly different from the typical declaration in DScript. Templates must be defined with the variable name `template`, and add the keyword `declare` to its modifiers (e.g. `export` and `const`). Otherwise, template declarations resemble object declarations.

Note: the right-hand side of each field declaration is a standard DScript expression. This means that in-line expressions (e.g. `addIf(...)`, `...myArray`, `glob`, `x ? y : z`, `importFrom(...)`, qualifiers, built-in methods, etc) are valid here.

Template declarations may only be preceded by namespace declarations and import/export statements.

## Consume
To consume the template, call `getTemplate` using the template's type as the type parameter:
```ts
function getCurrentName(): string {
    return Context.getTemplate<SampleTemplateInterface>().name.toString();
```

Templates are not type-safe by default. In the interest of type safety, pass the expected template type to `getTemplate` like so:
```ts
export declare const template : SampleTemplateInterface = {
    name: "foobar.cs",
    library: {
        enableStaticAnalysis: true
    }
};
    
export interface SampleLibraryTemplateInterface {
    enableStaticAnalysis: boolean;
};

export interface SampleTemplateInterface {
    name: string;
    library: SampleLibraryTemplateInterface;
};

function getCurrentName(): string {
    return Context.getTemplate<SampleTemplateInterface>().name.toString();
}

function isStaticAnalysisEnabled(): boolean {
    return Context.getTemplate<SampleTemplateInterface>().library.enableStaticAnalysis;
}
```

## Merge and Override Template Declarations

Each namespace can declare its own template, so each inner template is merged into the outer template. The mechanics of this merge is defined in [Merge and Override](./Merge-and-override.md).

While this logic isn't overly complex, merging many times can make the expected template values non-obvious. Due to this, it is recommended that templates only be used if the number of merges is low.

# Merge semantics

Templates may be defined in more than one place. Subsequent declarations do not _replace_ the existing definition, but are instead _merged_ with the template defined at that point in the stack.

Template merging utilizes the built-in method `merge`, which is defined on every object. `merge` returns a deep recursive union operation for arrays, sets, and maps, and otherwise replaces the original value. For example:

```ts
// The new value for field 'b' replaces the original value, and both unique fields 'a' and 'c' persist
{ a: "al", b: "bl" }.merge({ b: "br", c: "cr" }) === { a: "al", b: "br", c: "cr" }

// Same for a nested object
{ obj: { a: "al", b: "bl" } }.merge({ obj: { b: "br", c: "cr" } }) === { obj: { a: "al", b: "br", c: "cr" } }

// Merging arrays returns their union
{ a: ["al"], b: ["bl"] }.merge({ b: ["br"], c: ["cr"] } ) === { a: ["al"], b: ["bl", "br"], c: ["cr"] }
```

# Use cases

## Hierarchy based (commonality throughout a cone)
This is the most common case. In this pattern, a default value is defined in a single module or directory and consumed in all projects underneath it. This is recommended if the vast majority of the projects share these common values. Every exception _can_ define a new template to override the default value, but this can get very messy very quickly.


## SDK based (commonality across a project type)
Sometimes, sets of template values are not logically related to a directory cone, but more to a project type whose usage is widely applicable in a large build.

For instance, consider the differences between the following:

- Console app
- Windows app
- Universal app
- Kernel library
- Shell GUI library

The best way may be to create a new SDK for each of these that encapsulates their behavior and composes the underlying generic SDK to fill in the templates. Although not recommended, you could choose to model these different projects by using a generic compile function and then use templates to tweak the behavior for the individual types, but this will likely lead to more unreadable projects.

There is no clear boundary defining when you should consider something for a new SDK or not, but if the templates constitute a logical output type and you have more than 100-500 projects that are not hierarchically bound in a directory cone then this is worth doing.


## Scattered (commonality of value)
Suppose a common value is consumed in multiple disparate places (~5%) across a large build tree. It could make sense to define this value once at the root of the tree and pass it to those consumers, rather than add dependencies to each other or declare it in multiple places.


# Context.template and Function Calls
When a function is called in a top-level const, the current template object is put on the callstack. Any function called after this can access that template object via Context.template.

It is important to realize that the 'binding' of the template variable to the call-stack only happens for function calls where it is not yet bound. For example:

```ts
namespace Product {
    export declare const template = {
        sample: "FromProduct"
    }

    export const dll = Sdk.library();
    export const c = Sdk.compile("case1");
}

namespace Sdk {
    export declare const template = {
        sample: "FromSdk"
    }

    export const fromSdk = compile("case3");

    export function library() {
        const a1 = template.sample; // "FromSdk" this is just a regular bound value
        const b1 = Context.template.sample; // "FromProduct". This value was bound when the library function was called.
        compile("case2");
    }

    function compile(caller: string) {
        const a1 = template.sample;         // "FromSdk" this is just a regular bound value

        const b1 = Context.template.sample;      // This depends on how 'compile' is called:
        // caller == "case1" && b1 = "FromProduct". In this case, compile is called directly from top-level value Product.c
        //                                          and uses the template declared in Product (which has "FromProduct")
        // caller == "case2" && b1 = "FromProduct". In this case, compile is called from Sdk.library() which was called by 
        //                                          Product.dll and uses the template declared in Product (which has "FromProduct")
        // caller == "case3" && b1 = "FromSdk".     In this case, compile is called directly from top-level value Sdk.fromSdk
        //                                          and uses the template declared in Sdk (which has "FromSdk")
    }
}
```