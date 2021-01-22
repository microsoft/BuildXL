# Rush resolver options

## Configuring rush-lib
The Rush resolver uses [rush-lib](https://rushstack.io/pages/api/) to discover the project-to-project graph. And the version of this library also matters for cache hits, since different versions might produce different graphs. If not specified, BuildXL tries to find `rush` in `PATH` and the instance of rush-lib that ships with Rush. Similarly to the environment, pinning the version is a good idea to keep things under control. It will also improve the chances of getting cache hits across builds:

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

