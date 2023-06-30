# Workflow SDK

The workflow SDK simplifies writing build logic in DScript. It provides abstractions that allow users to
model their build logic as tasks and projects, as well as dependencies between them.

## Why use workflow SDK?

The SDK is introduced to lower the cognitive load when the user needs to write and maintain DScript specifications
when onboarding to BuildXL. Without workflow SDK, the user needs to model the build logic as low-level
processes and file/directory dependencies between them. The user may also be exposed to details of DScript and BuildXL, e.g.,
the structured command-line arguments, shared-opaque vs. exclusive-opaque directories, the structure of process pip's outputs, etc.
Further, the user may need to know the knobs in the arguments of `Transformer.Execute` to get the right behavior.
Indeed, using vanilla DScript gives users higher control, but it requires expertise and a steep learning curve, particularly
for those who just started to onboard.

## Core concepts: Task, Project, and Workflow

### Task

A *task* is an abstraction of a single work. Creating a simple task can be as simple as specifying the tool and the arguments that the task will execute,
e.g., 
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runTask({
    executable:       f`src/foo.exe`,
    arguments:        ["--opt", "bar", "--out", p`out/out.txt`],
});
```
The SDK interprets a task into one or more pips. For the above simple task, the SDK will interpret it into a process pip by invoking
`Transformer.execute`. Another task allows users for write batch/bash script inline:
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runScriptTask({
    script:           ["echo foo > out.txt"],
    workingDirectory: d`src`
});
```
The SDK will interpret this task into two pips, one write-file pip for writing the inline script into a temporary script file, and one process pip
for executing the script call by calling `cmd.exe` (in Windows) or `bash` (in Linux).

This notion of task here is similar to the same notion but in different settings, for example, the notion of task in MsBuild or the notion of step in YAML pipelines.

### Workflow

A *workflow* is basically a chain of tasks. A workflow is typically encoded as a function that calls several tasks in order:
```typescript
import * as Workflow from "Sdk.Workflow";

function workflow() : Workflow.WorkflowOutput
{
    const taskA = Workflow.runTask({
        executable:       f`src/fooA.exe`,
        arguments:        ["--opt", "bar", "--out", p`out/outA.txt`],
    });
    const taskB = Workflow.runTask({
        executable:       f`src/fooB.exe`,
        arguments:        ["--opt", "bar", "--out", p`out/outB.txt`],
        dependencies:     [taskA]
    });
    const taskC = Workflow.runTask({
        executable:       f`src/fooC.exe`,
        arguments:        ["--opt", "bar", "--out", p`out/outC.txt`],
        dependencies:     [taskA]
    });
    const taskD = Workflow.runTask({
        executable:       f`src/fooD.exe`,
        arguments:        ["--opt", "bar", "--out", p`out/outD.txt`],
        dependencies:     [taskB]
    });

    return [taskB, taskD]
}
```
The above workflow has 4 tasks, `taskA`, `taskB`, `taskC`, and `taskD`. Both `taskB` and `taskC` depend on `taskA`, so they will execute only after
`taskA` is done, but they can execute in parallel. The `taskD` only depends on `taskB`, so it has to execute after `taskB` is done.

Notice that the SDK allows for chaining tasks based on task dependencies instead of file/directory dependencies. The SDK infers the 
file/directory dependencies from the task dependencies. By default, the SDK will compute transitive dependency of files and directories.
In the above example, even though `taskD` only says that it depends on `taskB`, but it can also access files/directories produced by `taskA`.
If we try to write this with vanilla DScript, then we need to specify `taskA` as a dependence of `taskD` as well. The user often find
it cumbersome to specify such a transitive dependency. However, if the user does not want this transitive dependency
computation, then he or she can set the property `disableTransitiveTaskOutputDependency` of `runTask` to `true`.

### Project

A *project* describes a division in the build organization. The notion of project here is the same as other notions of project in other
build systems, like MsBuild. The workflow SDK provides an interface to describe information about a project, like the name, the root path,
and dependence projects. The workflow SDK also provides a function to run a specific workflow on a project.

Consider the following workflow that can be run against a project:
```typescript
import * as Workflow from "Sdk.Workflow";

function workflow(repoConfig: Workflow.RepoConfig, project: Workflow.Project) : Workflow.WorkflowOutput
{
    const taskA = Workflow.runTask({
        executable:       f`src/fooA.exe`,
        arguments:        ["--opt", "barA", "--out", p`out/${project.name}/outA/outA.txt`],
        workingDirectory: d`src/${project.name}`
    });
    const taskB = Workflow.runTask({
        executable:       f`src/fooB.exe`,
        arguments:        ["--opt", "barB", "--out", p`out/${project.name}/outB/outB.txt`],
        workingDirectory: d`src/${project.name}`,
        dependencies:     [taskA, project]
    });
    return [taskB];
}
```
The workflow has two tasks, `taskA` and `taskB`, that will be run relative against a project specified as its argument.
The workflow can take a repository config, `repoConfig`, containing information about a repository.
The `taskB` in the workflow depends not only on `taskA`, but also on the specified `project`. The latter dependency relation
establishes the dependency between the `taskB` of the current `project` and the `taskB` of the projects that the current `project`
depends on.

For example, in the below spec, we have two projects, `P` and `Q`, and `Q` says that it depends on the outputs of
running `workflow` on `P`.
```typescript
import * as Workflow from "Sdk.Workflow";

function workflow(repoConfig: Workflow.RepoConfig, project: Workflow.Project) : Workflow.WorkflowOutput { /* ... */ }

const repoConfig: Workflow.RepoConfig = { root: d`src` };
const projectP = Workflow.run(repoConfig, {
    root: d`src/P`,
    name: "P"
}, workflow);
const projectQ = Workflow.run(repoConfig, {
    root:           d`src/Q`,
    name:           "Q",
    runtimeDeps:    [projectP]
}, workflow);
```
The function `Workflow.run` runs the `workflow` function on a project and a repo configuration, and returns a project output that can be consumed by another project as a dependency.
The computation of task dependency in the SDK has project boundary, i.e., if a task depends on other projects, then it will only
depend on the tasks returned by the workflow that produces the project output. In the above example, since `workflow` returns 
only `taskB`, the `taskB` of `Q` will depend not only on the `taskA` of `Q`, but also on the `taskB` of `P`, but not the `taskA` of `P`.

## Tasks
Currently, the SDK offers the following kinds of task (other kinds of task will be added in the future as needed).

### Simple task

Creating a simple task can be as simple as specifying the tool that the task will execute and the arguments passed to the tool
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runTask({
    executable:       f`src/foo.exe`,
    arguments:        ["--opt", "bar", "--out", p`out/out.txt`],
});
```

### Script task
A script task runs batch/bash script. This task is similar to the script step in YAML pipeline. The user can specify the script
in a file or provide an inline script:
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runScriptTask({
    scriptFile:       f`foo.sh`,
    arguments:        ["--opt", "bar", "--out", p`out/out.txt`],
    workingDirectory: d`src`
});
```
or
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runScriptTask({
    script:           ["echo > out.txt", "echo > foo.txt"],
    workingDirectory: d`src`
});
```
On Windows the script task is run using `cmd.exe`, and on Linux the task is run using `bash`. When specifying the script
as a file, the user is responsible to ensure that the file is executable. When specifying the script inline, the SDK
will create a temporary script file for that inline script, and ensure that the file is executable.

### Powershell script task
This task is similar to script task, but the task will run using `powershell`. The user can specify the script in a file or
provide an inline script:
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runPowerShellTask({
    script:           ["Get-Process | Tee-Object -FilePath foo.txt"],
    workingDirectory: d`src`
});
```

### NuGet restore task
This task is used to restore NuGet pacakages:
```typescript
const task = Workflow.NuGet.restore({
    packages:               [
                                {
                                    kind:           "NuGet",
                                    name:           "PackageA",
                                    version:        "1.0.1",
                                    directories:    [d`src/distrib/PackageA.1.0.1`]
                                },
                                {
                                    kind:           "NuGet",
                                    name:           "PackageB",
                                    version:        "2.0.1",
                                    directories:    [d`src/distrib/PackageB.2.0.1`]
                                }
                            ],
    targetFramework:        "net472",
    noWarns:                ["CS0436"],
    sourceRoot:             d`src/distrib`,
    feeds:                  [{ name: "MyFeed", location: "https://myfeed.pkgs.visualstudio.com/DefaultCollection/_packaging/MyFeed/nuget/v3/index.json" }],
    restoreDirectory:       d`out/restore`
});
```

### Transformer task
This task takes `Transformer.execute` arguments as an input, invokes `Transformer.execute`, and wraps the output as a task output:
```typescript
import * as Workflow from "Sdk.Workflow";

const task = Workflow.runTransformerTask({
    tool:                       { exe: f`src/foo.exe` },
    arguments:                  [
                                    Cmd.option("--opt ", "bar"),
                                    Cmd.option("--out ", Artifact.output(p`out/out.txt`))
                                ],
    workingDirectory:           d`src`,
    dependencies:               [f`src/input.txt`],
    outputs:                    [{kind: "shared", directory: d`out/OutDir`}],
    tempDirectory:              Context.getTempDirectory("foo"),
    allowUndeclaredSourceReads: true
});
```
This task is introduced to give more power to the user to specify the process in case that existing kinds of task are not sufficient.
Also, "wrapping up" `Transformer.execute` as a task ensure substitutability.

## Implementation of abstractions

The task, project, and workflow can be written in a simple way because the workflow SDK does all the heavy lifting.
In the end, each task creates one or more pips by invoking `Transformer` functions, like `Transformer.execute`. First, when invoking
`Transformer.execute`, the SDK provides fixed values for certain properties that ease the job of specifying tasks. For example, to not burden
the user in specifying pip inputs, the SDK by default enable allowed undeclared source reads. In this way, the user does not need to specify
what source files that the task will read, but, for caching purpose, simply specify the dependence tasks and the directories to untrack.

The SDK hides the details of directory outputs of BuildXL, e.g., [shared opaque vs. exclusive opaque](https://github.com/microsoft/BuildXL/blob/main/Documentation/Wiki/Advanced-Features/Sealed-Directories.md), and their properties.
The user can specify outputs as paths and directories. For paths, the SDK will turn them into output files,
and for directories, the SDK will turn them into shared opaque directories. When the user does not specify any output, the SDK will take
the repo root, if specified as `RepoRoot` mount by the user, or the working directory as the output directory.

The SDK allows the user to write tool arguments naturally as an array of string or path, without thinking about DScript argument value. The SDK
will turn the array into an array of argument value that can be passed to `Transformer.execute`.

Task outputs are wrapped as `TaskOutput` and project outputs are wrapped as `ProjectOutput`. This allows the user to create a chain of task/project
dependency, and let the SDK infers the file/directory (transitive or not) dependency from them. This makes the user easy to specify the dependency because
he/she only needs to focus on the task/project order.

In some tasks like the script of PowerShell tasks, the SDK provide some defaults for tools, tool dependencies, and untracked directories. This makes
the specification of these task succint.

With all of these abstractions, there still can be possibilities that the build encounters disallowed file accesses (DFAs). This can happen
if a task tries to read a file produced by another task, but those two tasks are independent of each other. Another kind of DFA is two tasks
write/modify the same file. For the former DFA, the user needs to fix the dependency between tasks. For the latter DFA, the user may need
to merge those two tasks into a single task. Learn more about DFAs [here](https://github.com/microsoft/BuildXL/blob/main/Documentation/Wiki/Error-Codes/DX0500.md).