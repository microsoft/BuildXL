# Rush resolver options

Rush offers two alternatives for constructing the build graph. 
* [rush-lib](https://rushstack.io/pages/api/) can be used to build a project-to-project graph.
* More recently, the [@rushstack/rush-build-graph-plugin](https://github.com/microsoft/rushstack/pull/4626) plugin was made available, which can be used to build a script-to-script dependency graph.

## Configuring rush-lib
The Rush resolver can use [rush-lib](https://rushstack.io/pages/api/) to discover the project-to-project graph. If not specified, BuildXL tries to find `rush` in `PATH` and the instance of rush-lib that ships with Rush.

The version of this library matters for cache hits, since different versions might produce different graphs. Similarly to the environment, pinning the version is a good idea to keep things under control. It will also improve the chances of getting cache hits across builds:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        rushLibBaseLocation: d`${Environment.getDirectoryValue("RUSHTOOL_INSTALL_LOCATION")}/node_modules/@microsoft/rush/node_modules`,

      }
  ]
});
```

So assuming `RUSHTOOL_INSTALL_LOCATION` is an env var containing the directory where Rush is installed, here we point to the node_modules directory that contains `rush-lib`.

## Configuring @rushstack/rush-build-graph-plugin
The `@rushstack/rush-build-graph-plugin` plugin produces a finer-grained build graph, where each node represents a script under a given project. Observe that the same level of granularity can be achieved with *rush-lib*, specifying the [script-to-script dependencies](js-commands.md) in the BuildXL main configuration file. However, the main advantages of using the plugin are 
* The source of truth that defines the extent of the build comes entirely from Rush, making BuildXL and non-BuildXL builds equivalent. 
* It reduces the onboarding cost by removing the cognitive load and configuration requirements around defining script-to-script dependencies at the BuildXL configuration level.
* It enables a finer-grained control over script dependencies, where JavaScript-specific optimizations that reflect in point scripts can be expressed and driven from Rush, a domain-specific JavaScript coordinator.

In order to use the plugin, a rush instance needs to be provided to BuildXL:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        rushLocation: f`${Environment.getDirectoryValue("RUSHTOOL_INSTALL_LOCATION")}/bin/rush`,
      }
  ]
});
```

Assuming `RUSHTOOL_INSTALL_LOCATION` is an env var containing the directory where Rush is installed, we point to the Rush binary. It is assumed @rushstack/rush-build-graph-plugin is installed on the provided Rush instance.

Equivalently to [lage commands](js-lage-options.md#specifying-what-to-execute), `execute` in this case only takes script command names (or command groups), but without the option of establishing a dependency across them. This information is already part of what the plugin provides:

```typescript
config({
  resolvers: [
      {
        kind: "Rush",
        ...
        rushLocation: f`${Environment.getDirectoryValue("RUSHTOOL_INSTALL_LOCATION")}/bin/rush`,
        execute: ["build", "test"],
      }
  ]
});
```

Here we are specifying `build` and `test` as a way to define the build extent, but there is no implicit dependency between these commands and the order is not important.

## Improving perf with shrinkwrap-deps.json
Rush produces a project-level *shrinkwrap* file named `shrinkwrap-deps.json`. This file contains the transitive closure of all dependency names, including versions, for the corresponding project. This file can be used to track if any dependency changed, and therefore the associated project has to be re-run, instead of the real dependencies. The BuildXL Rush resolver exposes an option to do this:

```typescript
config({
    resolvers: [
    {
        kind: "Rush",
        ...
         trackDependenciesWithShrinkwrapDepsFile: true,
    }]
});
```
When enabled, this option will make BuildXL ignore any changes in the project dependencies and instead use `shrinkwrap-deps.json` as a witness for that. The net effect is that BuildXL will need to track for changes a potentially much smaller set of files, improving cache lookups and post-processing times. Observe, however, this option opens the door to some level of unsafety since using `shrinkwrap-deps.json` will do the trick as long as it is up-to-date. If any `package.json` file is modified and `rush update` is not run, this file might not be appropriately updated and therefore package dependencies not properly reflected. Therefore, an underbuild is possible, where a true dependency that is not listed in `shrinkwrap-deps.json` changes. Therefore, the recommendation is to turn this option on for lab builds, where this type of prerequisites are usually met, but leave it off (the default) for local builds.

**Note:** This option is not compatible with `usePnpmStoreAwarenessTracking` (see below), since the latter relies on observing accesses under the common temp folder, which this option untracks entirely.

## Improving perf with pnpm store awareness tracking
Rush uses pnpm as its package manager, which stores installed packages in a content-addressable store, typically under `common/temp/node_modules/.pnpm`. The Rush resolver can be made aware of this store structure with a resolver option:

```typescript
config({
    resolvers: [
    {
        kind: "Rush",
        ...
        usePnpmStoreAwarenessTracking: true,
    }]
});
```

When enabled, BuildXL assumes the name of each directory immediately under the pnpm store (e.g. `common/temp/node_modules/.pnpm/@babylonjs-core@7.54.3`) determines the file layout and content under it. The net effect of this option is that BuildXL will need to track a potentially smaller set of files, improving cache lookups and post-processing times. Observe, however, this option opens the door to some level of unsafety. If any file is modified under a package in the pnpm store after the install step happens, BuildXL will not be aware of it and an underbuild would be possible. Therefore, the recommendation is to turn this option on for lab builds, where this type of prerequisites are usually met, but leave it off (the default) for local builds. Even in the case of a lab build, care should be exercised if there is any step in between package install and the start of the build that can modify the package store.

Turning on pnpm store awareness tracking also adds by default an enforcement where no writes should happen under the pnpm store, since after package install this should be a read-only directory. To turn this restriction off, you can pass `disallowWritesUnderPnpmStore: false`.

**Note:** This option is not compatible with `trackDependenciesWithShrinkwrapDepsFile` (see above), since that option untracks the entire common temp folder, which this option needs to observe for reclassification.