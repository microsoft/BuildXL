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