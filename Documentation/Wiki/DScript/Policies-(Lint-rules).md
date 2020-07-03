# Policy Rules

BuildXL has a built-in static analysis tool that checks DScript code for certain restriction or to enforce compliance. The tool is embedded into BuildXL and runs on every invocation that requires spec processing (this means that the analyzers won't run if the pip graph can be reused from a previous BuildXL invocation).

BuildXL supports following rules:

* "No glob" rule
* "No ambient Transformers" rule
* "Use explicit type" rule

## Configuration & Usage

All policies are disabled by default. To enable them the main configuration file should turn them on explicitly:

```ts
config({
  frontEnd: {
    enabledPolicyRules: [
        'NoGlob', // Turn on "No glob" rule
        'NoTransformers', // Turn on "No ambient Transformers" rule
        'RequireTypeAnnotationsOnDeclarations', // Turn on "Use explicit type" rule
    ],
  }
});
```

## "No glob" rule
[Globbing](https://en.wikipedia.org/wiki/Glob_(programming)) can significantly affect determinism and build performance for large code bases. To prevent this from happening engineering team can use this policy and disable usages of any glob functions (`glob`, `globFolders`, `globR`, `globRecursively`) in build specification files.

If the rule is on and a build specification references a glob function, the error will occur:

```ts
const sources = globR(d`.`, '*.cs'); // [NoGlob] Globbing is not allowed.
```

## "No ambient Transformers" rule
In some cases, engineering team wants to control a set of modules that can create tool invocation wrappers (like Csc runner). The "No ambient Transformer" rule prevents a user in the codebase to use `Transformer` namespace defined in the Prelude module:

```ts
// [NoTransformers] Transformers namespace from the prelude is not allowed.
// Use 'Sdk.Transformers' module instead
const result = Transformer.copyFile(f`a.txt`, p`b.txt`);
```

## "Use explicit type" rule
DScript relies on the type inference for variable declarations and function return types. This can simplify authoring scenarios but make the specs less readable, especially without IDE support.

The "Use explicit type" rule enforces all top-level declarations have an explicit type:

```ts
// Type annotation is missing fro top-level variable declaration 'x'
const x = 42;
```

```ts
// Ok
const x: number = 42;
```



