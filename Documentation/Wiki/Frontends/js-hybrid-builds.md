# Integrating JavaScript with other technologies
BuildXL has multi-resolver architecture, where JavaScript [resolvers](../Frontends.md) can coexist with other kind of resolvers. All defined resolvers can collaborate to define a single build where each piece of work being executed may have a different provenance.

Each resolver can decide to expose a collection of modules, where each module contains values that can be references across resolvers. For details, check [DScript](../DScript/Introduction.md) section. 


## Exports
JavaScript resolvers provide some facilities to interact with other DScript modules by exposing selected values into a single module. A collection of values can be defined, each value containing the outputs of a collection of projects. For example:

 ```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        exports: [{
           symbolName: "fooAndBar", content: ["@ms/foo", "@ms/bar"]
        },
        {
            symbolName: "baaz", content: ["@ms/baaz"]
        }],
      }
  ]
});
```
Here each symbol will instruct the resolver to expose a public value with the corresponding name containing the outputs of the selected projects. In addition to those, there is always an implicit exported name `all` that contains the outputs of all projects in the build. 

The type of each exposed value is `StaticDirectory[]` containing all the outputs of the selected projects, grouped by directory.

These values can then be consumed from other resolvers that are part of the same build. For example, let's assume we have a service that produces a manifest of all the produced packages written as a DScript SDK. Then we could define:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        moduleName: "MyJSRepo"
        ...
        exports: [{
           symbolName: "fooAndBar", content: ["@ms/foo", "@ms/bar"]
        },
        {
           symbolName: "baaz", content: ["@ms/baaz"]
        }],
      },
      {
          kind: "DScript",
          modules: [{moduleName: "Manifest.Generator", projects: [f`manifest.dsc`]}]
      }
  ]
});
```

```typescript
// manifest.dsc
import {fooAndBar} from "MyJSRepo";
import * as ManifestGenerator from "SDK.Manifest"

const manifest = ManifestGenerator.generate(fooAndBar);
```
In this example we are defining two resolvers. A JavaScript one that will make sure the JS code is built, and a DScript one that will produce a manifest for a set of selected projects.

The DScript resolver references the module "MyJSRepo", defined by the Rush resolver and imports one of the exported values, `fooAndBar`, to generate a manifest for it.

## Custom scheduling
In a regular scenario, each JavaScript project reported by the corresponding coordinator is automatically scheduled by the configured JavaScript resolver. However, in some circumstances users may need more control over how a project gets scheduled. This can mean either enhancing what a particular script command does, or by actually changing the behavior altogether.

Of course the simplest way to achieve this is by actually changing what build scripts do. But there are scenarios where it is very useful to 'inject' behavior from the outside without actually changing the repo. For example, let's assume that there is a wrapper tool for jest tests that uploads test telemetry, which is only available in a particular lab environment. We can then define a custom scheduler for tests that calls this test wrapper, only when available, and avoid polluting the repository with this information, which may not make a lot of sense for people outside this particular lab build environment.

Let's see how this can be achieved. A scheduling callback can be specified that can perform arbitrary executions for each JavaScript project that is part of the build:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        moduleName: "sp-client",
        ...
        customScheduling: {module: "test-telemetry", schedulingFunction: "runJestWithTelemetry"}
      }
      {
          kind: "DScript",
          modules: {moduleName: "test-telemetry", projects: [f`test-telemetry.dsc`]}
      }
  ]
});
```

With this configuration, every time there is a JavaScript project to schedule, the resolver will call `runJestWithTelemetry` instead of scheduling the project in the usual way. The scheduling function is expected to have a particular signature:

```typescript
// test-telemetry.dsc

@@public export function runJestWithTelemetry(JavaScriptProject project) => TransformerExecuteResult {
    ...
}
```
The argument represents all the information the resolver has about a particular project + script command that needs to be scheduled:


```typescript
interface JavaScriptProject {
    name: string;
    scriptCommandName: string;
    scriptCommand: string;
    projectFolder: Directory;
    inputs: (File | StaticDirectory)[];
    outputs: (Path | Directory)[];
    environmentVariables: {name: string, value: string}[];
    passThroughEnvironmentVariables: string[];
    tempDirectory?: Directory;
}
```
The result type matches the type of `Transformer.execute`, the basic building block for executing processes in DScript (check [here](../../../Public/Sdk/Public/Transformers/Transformer.Execute.dsc)).

Let's see now how this custom scheduler can be implemented:

```typescript
// test-telemetry.dsc
import * as JestTelemetry from "Sdk.JestTelemetry";

@@public export function runJestWithTelemetry(JavaScriptProject project) : TransformerExecuteResult {
    if (scriptCommandName !== "test") {
        return undefined;
    }

    return JestTelemetry.runJestWithTelemetry({
        script: project.scriptCommandName,
        workingDirectory: project.projectFolder,
        outputs: project.Outputs,
    });
}
```

The callback can return `undefined` to indicate it has not interest in custom scheduling a particular project + script command. In this case, anything that is not a test can be scheduled in the regular way. For the sake of simplicity, we assume here there is an existing SDK that can wrap jest executions adding temeletry to it. `runJestWithTelemetry` only needs the jest script to execute, the working directory and the output directories where the JavaScript resolver was expecting outputs from the script. This function will eventually call `Transformer.execute` as part of its implementation and return that.
