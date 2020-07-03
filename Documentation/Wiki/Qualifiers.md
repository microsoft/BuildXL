# Qualifiers

## What are qualifiers?
Qualifiers are a way to parametrize your build. A typical example is building for x86/x64, or for debug/release. When calling BuildXL, a set of qualifiers can be provided to specify this parametrization. For example, BuildXL can be asked to build for x86 and x64 at the same time:

`bxl.exe /q:x86 /q:x64`

Often times there are dependencies between these builds. i.e., the x64 build needs some files from the x86 build, or a managed platform agnostic build needs some x64 and x86 native binaries. BuildXL allows users to specify dependencies that cross these qualifiers. 

By being able to build multiple qualifiers at the same time, BuildXL can create a much better schedule than existing build engines. This is achieved by using a single engine that can schedule both workloads at the same time, and optimizing for the combined critical path.

## Qualifiers: the basics
At a high-level, each declaration in DScript has associated a set of qualifiers that are valid for it. So, for example, a variable representing a DLL may only allow x86 or x64 as platforms and reject any other platform. We call this the **qualifier type** for that declaration. On the other hand, there is always a current qualifier used for evaluating values. For example the current platform can be x86. We call this the **qualifier instance**. So it only makes sense to evaluate a value if the qualifier instance is one of the valid qualifiers defined by the associated qualifier type of that value. The next sections describes this in more detail.

### Qualifier type
This defines set of legal instances of a qualifier. In other words, the set of qualifiers that are valid for the given scope. A qualifier type is expressible by a TypeScript interface declaration. An example is:

```typescript
interface MyQualifier extends Qualifier {
    configuration: "debug" | "release",
    platform: "x64" | "x86" | "arm32" | "arm64"
}
```

This defines a type with two keys. The `configuration` key with legal values `debug` and `release`, and `platform` key with legal values `x64`, `x86`, `arm32` and `arm64`. 

Observe that this definition is fully extensible and user-definable. Users could declare a qualifer type with pets:

```typescript
interface MyQualifier extends Qualifier {
    pets: "dog" | "cat" | "hamster",
    toy: "ball" | "plush" | "treadmill"
}
```

The type is restricted to an interface that must inherit from `Qualifier` and can only have fields that are typed as Union Type of String Literal. It is not allowed to use custom types like `number` or other interfaces as the type of the qualifier fields. This allows the engine to optimize and keep track of all possible combinations as well as provide a consistent commandline experience. See [Enums vs typed strings](DScript/Enums-vs-typed-strings.md) on why we use this rather than an Enum.

The scope of a qualifier type is a namespace, where all declaration in that namespace will be associated with a particular qualifier type. To declare a qualifier type for a given namespace you must assign a type to the "special" `qualifier` variable:

```typescript
export declare const qualifier : MyQualifier;
```

-or-

```typescript
export declare const qualifier : {
    configuration: "debug" | "release"
};
```

A named type reference (as in the first example) can be used or a type literal (as in the second example).

### Qualifier instance
The qualifier instance represents the current qualifier in use while "building". In DScript a qualifier instance is a plain runtime object accessible via the `qualifier` keyword. When the user wants to make a decision based on the qualifier, they can access the qualifier variable in expressions. 

For example:

```typescript
const optimize = qualifier.configuration === "release";
```

Observe that even though BuildXL can be invoked with a collections of qualifiers to build, at a given time, there is **only one** qualifier instance. This instance is typed, meaning that in order to do 'qualifier.configuration' the qualifier type in that context needs to contain 'configuration' as a key.  What this means in our example above where two qualifiers are specified (/q:x86 /q:x64) is that a project will be evaluated once with the x86 qualifier and then again with the x64 qualifier.  If a project does not support one of them, then it will be skipped during the evaluation for that qualifier instance.

## Working with qualifiers
### Passing qualifiers around
Users can pass in the starting qualifier instance via command line, but the qualifier can be changed inside DScript as well. I.e., you have an x64 build that needs to refer to a binary from the x86 build, or you have a module that doesn't depend on a particular platform, but it collects the files from all the other platforms.

When users build from the command line, they specify which qualifier they will be built by using the `/qualifier:` or short `/q:` parameter. Teams often have shared qualifier instances that are commonly used. You can define predefined named qualifier instances for the command line in the configuration file and then a user can refer to them by name on the command line.

```typescript
config({
    qualifiers: {
        namedQualifiers: {
            "qualifier1": {configuration: "debug", platform: "x64"},
            "qualifier2": {configuration: "debug", platform: "x86"},
        }
    }
});
```
```
bxl.exe /q:qualifier2
```

#### Default qualifier for the command line

If someone doesn't specify a qualifier on the command line, BuildXL checks the config file of the build to see if the user has specified a default qualifier.

```typescript
config({
    qualifiers: {
        defaultQualifier: {configuration: "debug", platform: "x86"}
    }
});
```

#### Explicit qualifiers on the command line
The named qualifiers mentioned earlier are a convenience feature. You can specify the qualifier explicitly on the command line as well.

```
domino /q:configuration=debug;platform=x64
```

Semicolon (`;`)separates the keyvalue pairs and the equals sign (`=`) separates they key from the value.
When specified this way, the qualifier is always merged with the default qualifier. So if the user specifies: `domino /q:platform=x64`  and the config file hasthe defaultQualifier defined as in the previous paragraph, then the actual qualifier used for evaluation will be: `{ configuration: "debug", platform:"x64" }`.

You can remove keys from the qualifier by not stating a value i.e. `/q:configuration=;platform=x64` will remove configuration from the default qualifier and evaluation will only use `{platform: "x64"}`.
If multiple keys are used, the last one wins i.e. `/q:configuration=debug;platform=x86;platform=x64` will result in a qualifier used of:  `{ configuration: "debug", platform:"x64" }`.


#### Multiple qualifiers on the command line

BuildXL allows merging builds of different qualifiers into a single graph as explained before. So you can do a build of x86 and x64 at the same time in a single graph where the engine takes care of optimally using your machine(s) resources to get the result for you as soon as possible. In fact, if you have the x64 build refer to artifacts from the x86 build, and you do an x86 build a the same time, BuildXL ensures the shared work only happens once.

You can specify this on the command line by passing multiple qualifiers in either named style:
```
bxl.exe /q:qualifier1 /q:qualifier2 /q:qualifier3
```

In the future we hope to add support for explicit qualifiers on the commandline with keyvalue pattern:
```
bxl.exe /q:configuration=release:platform=x64
```



### Inheriting the qualifier type
The qualifier type is implicitly inherited across namespaces of a given module, following the namespace hierarchy. For example:

```typescript
namespace A {
   // All declarations under this namespace will build for debug or release
   export declare const qualifier : {
       configuration: "debug" | "release",
   };
}

namespace A.B {
    // This namespace has the same qualifier type as A, since the namespace A.B is nested with respect to A
   // so any declaration here will also build for debug or release
}

namespace A.B.C {
    // This namespace is overriding the qualifier type inherited from A, its parent namespace, and only allowing to build under 'release'
   export declare const qualifier : {
       configuration: "release",
   };
}

namespace A.B.C.D {
   // All declarations in this namespace will only build for release, since its qualifier type is being inherited from A.B.C, its parent namespace
}
```

Since qualifiers are typically meant to be the same across a module, or even an entire build, the qualifier type can be declared using the same sharing techniques inside a module. If you want the entire module to have a certain qualifier type, you declare it at the root of your module not in any namespace. Then all the namespaces in the module will automatically inherit it.

```typescript
// ..../myModule/myModule.dsc
export declare const qualifier : {
    configuration: "debug" | "release",
    platform: "x86" | "x64"
};

// ..../myModule/folderA/folderB/myProject/myProject.dsc
namespace FolderA.FolderB.MyProject {
    // This has the same QualifierType as declared in myModule.dsc
}
```

In case you are confused of what the current qualifier is for a given project, please open the spec in VsCode with the the DScript plugin installed and there will be a tooltip on the namespace what the current selected qualifier type is.

### Declaring qualifier for cross-project references
When you use a value in a BuildXL spec, you have the option to change the qualifier. E.g., you are currently building for x64, but you need to refer to an x86 binary. This holds true for any value, be it from the same namespace or not. If you don't explicitly specify a qualifier value, the current qualifier instance will be implicitly passed. 

Using the `withQualifier`, it is possible to reference a value with a different instance of a qualifier.

```typescript
namespace Foo {
    export declare const qualifier : {
            configuration: "debug" | "release"
    };
    export const myValue = qualifier.configuration === "debug" ? 10 : 20;
}

const myTen    = Foo.withQualifier( {configuration: "debug"}   ).myValue;
const myTwenty = Foo.withQualifier( {configuration: "release"} ).myValue;
```

 Notice that the `withQualifier` call is done on the namespace not on the variable itself.
#### Qualifying values that are not in a namespace
Not all values in your current module might be under a namespace.  A prime example of which are all the values that are defined at the root of the module.  For these, there is an implicit `$` namespace to represent the root namespace of each module.  Specifying an instance of a qualifier to access those variables would look like:

```typescript
const dbgOther = $.withQualifier({configuration: "debug"}).valueWithoutNamespace;
```

###Empty Qualifier
In this sample there is no qualifier type defined at the outer scope, so myTen and myTwenty are in the empty qualifier. It would be an error to use `Foo.myValue` because the current qualifier (`{}`) would be used to access myValue which has a QualifierType that requires configuration.

The empty qualifier can be used to produce platform and configuration independent results.  An example may be copying the help text or C headers to an form a Software Developer Kit (SDK). 

```typescript
namespace A {
    export declare const qualifier : {platform: "x64" | "x86"};
    const x = ...; //This value exists for x64 and x86
    namespace B {
        export declare const qualifier : {};
        const y = ...; //There is only a single value of this in the entire build.
    }
}
```

#### Qualifying values from import statements
When a user uses the `import` statement or `importfrom` is under the covers the same as a namespace reference. Therefore, the same pattern to change the qualifier can be used as values inside the given module using `withQualifier`.


```typescript
import * as Other from "Other";

const dbgOther = Other.withQualifier({configuration: "debug"}).value;
const relOther = importFrom("Other").withQualifier({configuration: "release"}).value;
```


If you use the implicit namespace transition the engine will take care of automatically dropping extra fields. If you use `withQualifier` you must properly type the object and therefore if you define extra fields, it will result in a type error.
