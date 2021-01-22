# Specify what to build
By default BuildXL will execute the 'build' command on each project that is part of the build whenever that script is present. But when multiple script commands need to be executed, a JavaScript resolver needs to be told explicitly to build them.
In a relatively simple configuration, a list of script commands to execute can be specified like this:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        execute: ["build", "test"],
      }
  ]
});
```

The `execute` option here specifies that both `build` and `test` script commands are to be executed. If any of those commands are not present in a given project, they are just skipped (BuildXL will however flag it as an error if the command name doesn't exist at all). BuildXL will create a pip for each script command and append the command as part of the pip name. So for a project `my-project`, two pips `my-project_build` and `my-project_test` will be generated.

What about dependencies between these script commands? When specified as a simple list of strings, each element of the list will depend on the previous one. So in the example above, `my-project_test` will depend on `my-project_build`.

In a more advanced scenario, dependencies can be declared in a finer-grained manner. There are two kinds of depedendencies that can be specified `local` and `package`. `local` means a dependency on a script command defined on the same project. `package` a dependency on the script commands of dependencies.

Let's see an example. Let's say we have scripts `build-debug` and `build-release`, which can run concurrently but need their corresponding debug and release outputs from their dependencies. So this can be specified as follows:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        execute: [
            {
                command: "build-debug", 
                dependsOn: [kind: "package", command: "build-debug"]
            },
            {
                command: "build-release", 
                dependsOn: [kind: "package", command: "build-release"]
            },
        ],
      }
  ]
});
```
Let's say that later we add a `test` script that tests both debug and release artifacts. So the configuration looks like this:
```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        execute: [
            {
                command: "build-debug", 
                dependsOn: [{kind: "package", command: "build-debug"}]
            },
            {
                command: "build-release", 
                dependsOn: [{kind: "package", command: "build-release"}]
            },
            {
                command: "test",
                dependsOn: [
                    {kind: "local", command: "build-debug"}, 
                    {kind: "local", command: "build-release"}
                ]
            }
        ],
      }
  ]
});
```
## Grouping script commands
By default, BuildXL will create a pip for each script command and project, as specified in the 'execute' entry of the resolver configuration. However, in some cases it is handful to group a series of build commands into the same pip as a way to avoid rewrite errors. 

For example, let's assume that execute looks like this:

```typescript
config({
  resolvers: [
    {
      kind: "Rush",
      ...
      execute: ["prebuild", "build", "postbuild"],
    }
  ]
})
```
If both `prebuild` and `build` writes different content to `foo.txt`, BuildXL will flag this as an error, as explained in [common issues](js-onboarding.md#most_common_issues). By grouping `prebuild` and `build` into the same pip, the read and write interplay between these two script commands becomes opaque to BuildXL, and BuildXL will only observe the final outcome after both commands executed:

```typescript
config({
  resolvers: [
    {
      kind: "Rush",
      ...
      execute: [
        {commandName: "prebuild-and-build", commands: ["prebuild", "build"]},
        "postbuild"],
    }
  ]
})
```

Here we are defining a command group `prebuild-and-build`, that contains `prebuild` and `build`. These two commands will be executed in the given order. The command `postbuild` now depends on the command group.

The downside of grouping script commands is that all commands in a group will be executed as a monolithic block: caching and distribution won't be able to operate independently on each script command member. This may have some performance impact, depending on the characteristics of each script.

## Augmenting the script command arguments
There are some scenarios that require massaging the script commands that come with the repo. Setting a common verbosity levels to all projects is a common example of this. BuildXL provides a way to globally extend the script commands being executed:

```typescript
config({
  resolvers: [
    {
        kind: "Rush",
        ...
        execute: ["build", "test"],
        customCommands: [
            {command: "build", extraArguments: "--production --notest"},
            {command: "test", extraArguments: "--verbose"},
        ],
     }
  ]
});
```
This configuration is extending all `build` commands so `--production --notest` will be appended and equivalently with `test` and `--verbose`. 