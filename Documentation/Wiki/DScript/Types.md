# Primitive types
These are the primitive types supported by DScript. Note that this contains a subset of the literals in TypeScript, along with some new path types.

## ECMAScript literals

### number
This represents an integer and is _not_ identical to the double precision float type `number` in TypeScript. Example: `42`.
DScript follows the [ecmascript section 8.5](http://www.ecma-international.org/ecma-262/5.1/Ecma-262.pdf) number semantics.

### string
This is identical to the TypeScript type `string`. Example: `"Hello world!"`.
Strings can be encoded with double quote `"` and single quote `'`. Full Unicode is supported in string literals. You can escape characters with `\` per [ecmascript section 7.1](http://www.ecma-international.org/ecma-262/5.1/Ecma-262.pdf) 

You can also use template strings, which can span multiple lines and have embedded expressions. These strings are surrounded by the backtick/backquote (`` ` ``) character. The embedded expressions are of the form `${ expr }`. For example:
```ts
let x = 42;
let sentence = `Hello, the answer is ${ x }.`;
```
results in sentence being `"Hello, the answer is 42."`

### boolean
This is identical to the TypeScript type `boolean`. Examples: `true` or `false`.

## Path literals

### Motivation
Files and Directories are an extremely common currency in builds. Many build engines use strings and string computation to produces paths. There are a few problems with this approach.
1. Who is responsible for the path separator: The directory string, or the one doing the `+` operator on the strings.
1. Memory: When these are all strings they'll take a lot of memory and are often duplicated in many files. 
1. Cross platform: If you operate on strings you have to standardize on a path separator and know which strings needs conversion
1. Validation and error reporting: Not all characters are valid in paths. If all strings are computed on the fly errors are caught later when they could have been caught earlier.

Who hasn't see paths with patterns like: `c:\folder/folder..\folder\\file.txt` 

BuildXL solves these by providing primitives for these. The main ones you'll see in projects being [File](#File) and [Directory](#Directory). 
We also have [Path](#Path), [RelativePath](#RelativePath) and [PathAtom](#PathAtom) that you will see when building SDKs. BuildXL leverages the custom [tagged template literal](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Template_literals) mechanism. So while these might look like custom syntax, they are actually standard ECMAScript 6. 

### Types

Note that all path-related types use the backtick character: ``` ` ```

#### File
The tag used for the file path literal is `f` (for "file") and the template is the relative path to a file that declares it.
Example: ``f`folder/myFile.txt` ``.

Files are the units that define the dependencies between processes in the build.
The type File represents input as well as intermediate and output files. If you use the literal version with ``f` `` they will always be source files.

#### Directory
The tag used for the directory path literal is `d` (for "directory") and the template is the relative path to a directory. Example: ```d`path/to/my/directory` ```.

You should not see too many of these in project files as Directories themselves are just the references not the dependencies. Those are Sealed Directories, which do have to be constructed with a Directory.

#### Path
The tag used for the path literal id `p` (for "path") and the template is a relative path. Example: ```p`path/to/something` ```. The engine will then automatically combine this path with the folder of the DScript file that declares it. 

A path does not represent a file or a directory. You can think this more as a **P**romise to an output file.

Generally, this shouldn't be used in build files. This should only be used in SDKs when predicting output files. The SDKs then return handles to the Files to those paths.

> Note: WDG uses wrappers where the project files are **p**redicting the outputs, so you'll see them there in the outputs.

#### PathAtom
A PathAtom is a part of a path think of it as just the name of a file or folder. You can create one using the `a` tag. Example ```a`file.txt` ```. 
Creating this will validate that the name is a proper path fragment. It will result in an error if you use a path separator or any other illegal filename character.

You typically won't see these in project files and might see them in SDKs.
The benefit of a PathAtom is that it is already validated to be a valid filename and also in memory deduplicated. So when used to combine paths is more efficient than a plain string.
There has been feedback from users that seeing ```a``` is confusing to them. To reduce their cognitive overhead we recommend that SDKs expose these values as strings to the end user and convert to a PathAtom internally so it looks familiar to the end user.
Passing an invalid file character in the string will throw at the conversion point rather than the project, but it should be obvious from the callstack and the error what the user did wrong.

You should not see these in project files. You'll see these predominantly in SDKs. Unless your users are not easily confused SDK authors can choose to let their users convert to PathAtoms.

#### Relative paths
Sometimes you need to use relative paths in your project or SDK. You can do so using the `r` tag. Example ```r`some/relative/file.txt` ```. 
If this was a file, path or directory it would be combined with the folder the build spec is in. This maintains it as a relative path and can be used later to combine or look up files in [Sealed Directories](/BuildXL/User-Guide/Advanced-Features/Sealed-Directories).
You can think of this as a shortcut for a list of PathAtoms.

You typically won't see these in project files, you'll see these predominantly in SDKs when tools are defined in other packages and they are referenced through the sealed directory of that package.
 

### Interpolation
Since DScript modules the path literals using the standard tagged template functionally we support interpolation here as well. You can safely and easily combine paths.
one can use expression interpolation as well to compute paths like is possible for strings.
```ts
let dir = d`d1/d2`;
let relative = r`s1/s2`;
let file = f`${dir}/f1/${relative}/f2.txt`
```
BuildXL requires you to be explicit, so it is not allowed to combine two paths like: ``d`${dir}${relative}` ``  since it is a very hard to read and b it is ambiguous. If this is `d1/d2/s1/s2` or `d1/d2s1/s3`. BuildXL requires you place a `/` literal between expressions. if you really want to have the latter you are required to do the computation yourself. Therefore the correct way is `` d`${dir}/${relative}` ``.



### Path separators & Encoding
These primitives have path separators. In these examples we have used the unix style separator `/` to separate folders and files. DScript does support windows style path separators `\`. But to do so we had to break compatibility with TypeScript as the `\` character is the escape character for strings to support Unicode encoding character and to escape the backtick (`` ` ``) itself as well as common characters like newline etc.
DScript therefore now does not support any `\` escaping and you are required to put in the actual Unicode character. The easiest way to enter the Unicode character if you don't know how to type it is to copy-paste it from the actual file on disk. Alternatively if your favorite text editor doesn't have a good Unicode selection mechanism you can find many Unicode tools on the web from which you can copy-paste.
To escape the backtick DScript supports double backtick. (``` `` ```).
This behavior is at this point in time not configurable.

## Sealed Directories

Directory literals can be 'sealed'. They return a primitive value of `StaticDirectory`. Sealed Directories are the way in BuildXL to pass along a set of files that are shared between processes.

They serve two other purposes:
1. Sealed Directories help with [incrementality](#Incrementality) as they allow you to over-specify the dependencies while not having it affect incremenality. I.e. processes will only run if the subset of files they actually read from the sealed directory has changed. If a source file - that the tool did not read - is changed, BuildXL does not rerun the tool. This is different from direct input files. If those change BuildXL will rerun the tool regardless if it read it or not.
1. Sealed Directories allow grouping of the set of files that can be read by the process

# Composite Types
## Arrays
Arrays in DScript are defined with the same syntax and type safety as TypeScript:

```ts
// Standard array declaration with explicit type:
const strings : string[] = ["one", "two", "three"];

// List of files with inferred type:
const files = [
    f`file1.txt`,
    f`folder/file2.txt`,
];

// Example with optional explicitly typed element values
const arr: [number, string, number] = [1, "hello", 42];

// Array access
const firstElement = arr[0];

// Array access beyond the end of the array results in `undefined`
const undefinedElement = arr[15];
```

The spread operator `...` helps to easily combine lists. Think of this as expanding the list so that each element is added. See the TypeScript proposal for the spread operator [here](https://github.com/tc39/proposal-object-rest-spread) for more details.
```ts
const files1 = [
    f`file1.txt`,
    f`folder/file2.txt`,
];

// files2 contains elements of files1 and file3.txt.
// [ f`file1.txt`, f`folder/file2.txt`, f`file3.txt` ]
const files2 = [
    ...files1,
    f`file3.txt`,
];
```

Arrays are also immutable once created. One therefore has to be carefull when modifying them:
```ts
let numbers = [1];
numbers.push(2); 
// push will return a new array references with 1 and 2.
// now numbers is still [1]. If you want to update numbers you have to reassign the variable.
numbers = numbers.push(3);
// now numbers will have [1,3];
```


## Objects
Objects in DScript are defined with the same syntax and type safety as TypeScript:

```ts
const obj = { a: 42, s: "hello", b: 42 };

// Field access
const a: number = obj.a;
```

Objects are immutable too just like arrays. There are some helpers functions like `merge` and `override` that help you modify fields or combine objects together.

# Out of scope, waiting for SDK Author doc
* Set
* Map
* LinkO (??)


# User Specified types

## Interfaces
In DScript you can define custom types via Interfaces.  And this is the bread and butter of how values are typed and flow between the projects and the Sdk's as well as between the Sdk's.

```ts
interface MyInterface {
    myField: SomeType;
    otherField: string;
}
```

interfaces can extend multiple interfaces via the `extends` keyword:

```ts
interface A {
   a: string;
}

interface B {
   b: number;
}

interface C extends A, B {
    c: Boolean;
}
```

Interfaces have the same [Visibility](/BuildXL/User-Guide/Script/Reusing-declarations-(factoring)/Visibility) rules as other declarations using `export` and `@@public`.
Interfaces are highly recommended to be named using [PascalCase](https://en.wikipedia.org/wiki/PascalCase).

See typescriptlang.org for more details on [Interfaces](http://www.typescriptlang.org/docs/handbook/interfaces.html)

## Classes
DScript explicitly does not support Classes to allow for ease of declaration on arguments and we rely on the structural typing against these interfaces to provide a great balance between strong type guarantees and checks while still allowing for flexibility and extensibility.

## Union Types
A union type allows a value to be declared with two distinct types. You can think of `X | Y` union type as an 'or': the type is X or Y.

```ts
const x : number | string = 1; // okay
const y: number | string = "2"; // okay
const err : number | string = true; // error, Boolean is not number or string.
```

See typescriptlang.org for more details on [Union Types](http://www.typescriptlang.org/docs/handbook/advanced-types.html#union-types)

## Type alias declaration
DScript allows you to declare named types using the `type` keyword. This allows you to declare aliases or compose types. Example:

```ts
interface Foo { x: number; }

type AliasToFoo = Foo;
type AliasToString = string;
type StringOrNumber = string | number;
```

Type aliases have the same [Visibility](/BuildXL/User-Guide/Script/Reusing-declarations-(factoring)/Visibility) rules as other declarations using `export` and `@@public`.
Type aliases are highly recommended to be named using [PascalCase](https://en.wikipedia.org/wiki/PascalCase).

## Enumerations
A common pattern is for tools to take a value that is a part of a list of well-defined values. TypeScript (and therefore DScript) has two options:

* Enumeration
* Union of String Literal types.

### Enumerations
There are a few types of enums in TypeScript but BuildXL only allows the safe `const` one.
```ts
/**
* Enumerate the supported languages.
*/
export const enum LanguageE {
    /**
    * Specify the C# language.
    */
    cSharp,

    /**
    * Specify the C++ language.
    */
    cPlusPlus,

    /**
    * Specify the Visual Basic language.
    */
    visualBasic
}
```

Enum declarations have the same Visibility rules as other declarations using `export` and `@@public`.
Enums type names are recommended to be name using [PascalCase](https://en.wikipedia.org/wiki/PascalCase). Where the enum members are named using [camelCase](https://en.wikipedia.org/wiki/Camel_case)

See typescriptlang.org for more details on [Enums](http://www.typescriptlang.org/docs/handbook/basic-types.html#enum)

### Union type of String Literal Types
TypeScript supports a string literal as a type declaration. For example:
```ts
type OnlyA = "a";
const ok : OnlyA= "a"; // This is valid as the 'string literal' "a" is assignable to the 'string literal type' "a".
const err: OnlyA = "b"; // This will fail as the 'string literal' "b" is assignable to the 'string literal type' "a".
```
The type name of a String Literal type is recommended to be using [PascalCase](https://en.wikipedia.org/wiki/PascalCase).  If you are defining the names for the string literals yourself it is recommended to use [camelCase](https://en.wikipedia.org/wiki/Camel_case) unless the literals are predefined by your tool.

See typescriptlang.org for more details on [String Literal Types](http://www.typescriptlang.org/docs/handbook/advanced-types.html#string-literal-types)

TypeScript also supports union types. This is kind of like an or type. Values can have either one or the other type. For example:
```ts
type NumberOrBoolean = number | boolean;
const ok1 = 1; // This is valid since 1 is a number a
const ok2 = true; // This is valid since `true` is a Boolean.
const err = "failure"; // This will fail as a string is not a number nor a Boolean.
```
See typescriptlang.org for more details on [Union Types](http://www.typescriptlang.org/docs/handbook/advanced-types.html#union-types)

These can be conveniently combined to form legal lists of values:

```ts
export type LanguageS = "cs" | "c++" | "vb";
```

### Which should I use when?

The recommendation is for nearly all cases to use a union of string literal types rather than an enum.

* ### Easier for consumers
   When used in arguments of functions it is typically a lot easier to just use a string than an enum since you don't have to prefix the name. For example:
   ```ts
   const code = MySdk.generate({
      languageS: "cs"
      languageE: MySdk.LangaugeE.cSharp,
   });
   ```
* ### Easier to match commandline
   The String literals allow all strings. Enums restrict you to valid identifiers and subject you to the recommended casing and naming guidelines. This often might mean that those names don't line up directly with the tools commandlines. For example if the tool expects: `/language:c++` to be passed. `"c++"` is a valid string literal type, but not a valid enum name, so you end up with 
   ```ts
   export function generate(args: Args) : Result {
      ...
      Cmd.option("/languageS:", args.languageS), // Short
      Cmd.option("/languageE:", convertLanguageToArgString(args.languageE)), // Short
      ...
   }

   function convertLanguageToArgString(language: LanguageE) : string {
     switch (language)
         case LangaugeE.cSharp:
             return "cs";
         case LangaugeE.cPlusPlus:
             return "c++";
         case LangaugeE.VisualBasic:
             return "vb";
         default:
             Contract.fail("Unexpected enum value"
   }
   ```

* ### More composable.
   It is very easy to combine to string literal types into one without re defining all values. 
   String literal types compose over | as well as & type operations.
   This is not possible in enums, you have to redeclare all values leading to duplication.

* ### Easier to restrict
   Because of structural typing an SDK for a division say WDG can easily restrict the set of legal values they want to allow in their SDk by declaring a new type i.e. 
```ts
 type WdgLanguage = "cs" | "c++";
```
   And in their sdk they can simply take that argument and pass it along to the underlying more permissive Sdk. With Enums this will require a new enum type with a custom conversion function.

## Intersection Types
An intersection type allows multiple types be merged. It is like an and. `X & Y` is a type that contains the members of X and Y.

```ts
interface A {
    a: number,
}

interface B {
    b: string,
}

type C = A & B;

const x: C = {
    a: 1,
    b: "1",
}
```
See typescriptlang.org for more details on [Intersection Types](http://www.typescriptlang.org/docs/handbook/advanced-types.html#intersection-types)

## Generics
DScript supports the generics as supported by TypeScript.
See typescriptlang.org for more details on [Generics](http://www.typescriptlang.org/docs/handbook/generics.html)

 