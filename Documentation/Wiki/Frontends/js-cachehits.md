# Getting cache hits in a distributed scenario
BuildXL's safe caching relies on closely monitoring what each tool is doing, including the environment the tool accesses. This means any difference there can block cache hits from occurring. In a distributed build, machine differences in layout, user account, etc. can cause trouble in this respect.

## Controlling the environment
The JavaScript resolvers allow a way to control the environment that is exposed to tools. When not specified, tools get the current process environment. Restricting the environment to variables that are actually needed by the build improves the chance of cache hits. It is also a good practice to avoid hidden behavior to 'leak' into the build through env variables.

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        environment: Map.empty<string, string>()
                .add("Path", Environment.getStringValue("PATH"))
                .add("RUSH_TEMP_FOLDER", Environment.getStringValue("RUSH_TEMP_FOLDER"))
                .add("RUSH_ABSOLUTE_SYMLINKS", "true"),
      }
  ]
});
```

Here we are allowing only `PATH`, `RUSH_TEMP_FOLDER` and `RUSH_ABSOLUTE_SYMLINKS` for projects to see. For the first two, we are getting their value from the current environment. For the last one, we are hardcoding the value in this config. Observe this environment will also be used when constructing the build graph.

## Pinning tools
Each JavaScript resolver depends on the corresponding JS coordinator to provide the build graph. Different versions of a coordinator may produce different build graphs. In a lab environment, it is recommended that the version is pinned instead of leaving it to what's on PATH. Check the [coordinator specific options](js-coordinator-options.md) to find what that means in each case.

What is common to all resolvers regarding tool pinning is the path to node.exe. Node is used to run the graph construction process and it has an impact on the fingerprint of the graph.

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        nodeExeLocation: f`path/to/node.exe`
      }
  ]
});
```

## Turn on reparse points/symlink resolution
BuildXL does not resolve symlinks/reparse points by default. This option is likely to change and will eventually become the default. In the meantime, enabling full reparse point resolution will ensure pip fingerprints will always get consistent paths, regardless of how files are accessed. This can be turned on via a command line option `\unsafe_IgnoreFullReparsePointResolving+` (the 'unsafe' part is about not enabling them) or with a main config file option:

```typescript
config({
  resolvers: [
      ...
  ],
  sandbox: {unsafeSandboxConfiguration: {enableFullReparsePointResolving: true}},
});
```
