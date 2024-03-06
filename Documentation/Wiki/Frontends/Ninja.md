# Building a project from a Ninja specification with BuildXL
BuildXL supports building [Ninja](https://ninja-build.org/) projects with a frontend that knows how to translate the Ninja specification to a BuildXL build graph. 
This can be used to build [CMake projects](cmake-builds-with-ninja.md), but any project that can be built with the Ninja engine is supported. 

## Configuration
Moving an existing repo to BuildXL may need some adjustments to conform to extra build guardrails BuildXL imposes to guarantee safe caching and distribution.
Many repos though may need little or no changes, but that really depends on how well 'disciplined' a repo is.

To specify the build, a `config.dsc` BuildXL configuration file needs to become part of the repo and the Ninja resolver needs to be added to it.
Most of the onboarding work is about adjusting the configuration of this resolver to the particularities of a repository.
The configuration file must indicate the kind of a resolver used ('Ninja') and the location of the repo root. 
Here is an example of such a file:
```typescript
config({
    resolvers: [
    {
        kind: "Ninja",
        moduleName: "my-repo",
        root: d`.`,
        // specFile: f`./build.ninja` [Implicit, see below]
    }
  ]
});
```

Here, the directory pointed by `root` is assumed to have a `build.ninja` file with the specification.
If this is not the case, a `specFile` property (commented out in the example above) is available, where one can point to the `.ninja` file that is the entry point for the specification.

The full set of options to configure the `Ninja` resolver can be found [here](../../../Public/Sdk/Public/Prelude/Prelude.Configuration.Resolvers.dsc), defined under `interface NinjaResolver`.

## Building with BuildXL
After [installing BuildXL](../../Wiki/Installation.md), one can run the project pointing to the `config.dsc` configuration file: `bxl /c:config.dsc`. 

## Examples
In the Examples folder in this repository you can find:
- an example [plain Ninja project](../../../Examples/NinjaHelloWorld/). that you can build following the [instructions](../../../Examples/NinjaHelloWorld/README.md)
- an example [CMake project](../../../Examples/CMakeNinjaHelloWorld) to be build with the Ninja resolver following [these instructions](../../../Examples/CMakeNinjaHelloWorld/README.md).