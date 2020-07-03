# Rush performance tips

* Cache hits is one of the main sources of good performance. Being able to reuse outputs from previous builds is key. Take a look at section [Getting cache hits in a distributed scenario](rush-cachehits.md) for additional guidance about this.
* [Filters](../How-To-Run-BuildXL/Filtering.md) are another way to scope down what is actually needed. 
     * Rush-based builds support regular spec filtering. E.g. ```bxl /f:spec='./spfx-tools/sp-build-node'``` will request a build where only `sp-build-node` project will be built, and its required dependencies.
     * Every script command is exposed as a build tag. This means that a pip executing a `'build'` script command will tagged with `'build'`, and equivalently for any arbitrary script command. This enables to easily filter by it. For example, if we want to exclude tests, we can run ```bxl /f:~(tag='test')```
* Rush produces a project-level *shrinkwrap* file named `shrinkwrap-deps.json`. This file contains the transitive closure of all dependency names, including versions, for the corresponding project. This file can be used to track if any dependency changed, and therefore the associated project has to be re-run, instead of the real dependencies. The BuildXL Rush resolver exposes an option to do this:

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
When enabled, this option will make BuildXL ignore any changes in the project dependencies and instead use `shrinkwrap-deps.json` as a witness for that. The net effect is that BuildXL will need to track for changes a potentially much smaller set of files, improving cache lookups and post-processing times. Observe, however, this option opens the door to some level of unsafety since using `shrinkwrap-deps.json` will do the trick as long as it is up-to-date. If any `package.json` file is modified and `rush update` is not run, this file might not be appropriately updated and an underbuild is possible. Therefore, the recommendation is to turn this option on for lab builds, where this type of prerequisites are usually met, but leave it off (the default) for local builds.

