# Lage resolver options

The Lage resolver has the option to pin Npm tool to a specific file on disk. During graph construction, npm is used to get lage. This is the recommended setting in a lab build scenario. If not specified, BuildXL will try to find Npm under PATH

```typescript
config({
  resolvers: [
      {
        kind: "Lage",
        ...
        npmLocation: f`path/to/npm.cmd`,
      }
  ]
});
```

# Specifying what to execute
Lage is slightly different than other coordinators in the sense that each script command is already a unit of work, where dependencies can be declared against it. Other coordinators usually use the project, a coarser-grained unit of work.

This reflects in the Lage resolver options to only take script command names (or command groups), but without the option of establishing a dependency across them. This information is already part of what Lage provides:

```typescript
config({
  resolvers: [
      {
        kind: "Lage",
        ...
        execute: ["build", "test"],
      }
  ]
});
```

Here we are specifying `build` and `test` as a way to define the build extent, but there is no implicit dependency between these commands and the order is not important: the dependency information will be provided by Lage.

## Improving perf with yarn strict awaress tracking
When doing package install via [Yarn strict](https://classic.yarnpkg.com/en/package/yarn-strict), the Lage resolver can be made aware of it with a resolver option:

```typescript
config({
  resolvers: [
      {
        kind: "Lage",
        ...
        useYarnStrictAwarenessTracking: true,
      }
  ]
});
```
When enabled, BuildXL assumes the name of each directory immediately under the yarn strict store (e.g. <repo_root>/.store/@babylonjs-core@7.54.3-d93831e7ae9116fa2dd7) determines the file layout and content under it. The net effect of this option is that BuildXL will need to track a potentially smaller set of files, improving cache lookups and post-processing times. Observe, however, this option opens the door to some level of unsafety. If any file is modified under a package in the yarn strict store after the install step happens, BuildXL will not be aware of it and an underbuild would be possible. Therefore, the recommendation is to turn this option on for lab builds, where this type of prerequisites are usually met, but leave it off (the default) for local builds. Even in the case of a lab build, care should be exercised if there is any step in between package install and the start of the build that can modify the package store.

Turning on yarn strict awareness tracking also adds by default an enforcement where no writes should happen under the Yarn strict store, since after package install this should be a read-only directory. To turn this restriction off, you can pass `disallowWritesUnderYarnStrictStore: false`.