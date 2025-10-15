# Nx resolver options

The Nx resolver has the option to pin the Nx libraries to a specific instance on disk. This is the recommended setting in a lab build scenario. If not specified, BuildXL will try to find Nx under PATH

```typescript
config({
  resolvers: [
      {
        kind: "Nx",
        ...
        nxLibLocation: f`/home/user/repo-root/node_modules/nx`,
      }
  ]
});
```

# Specifying what to execute
Nx is similar to Lage in the sense that each script command is already a unit of work, where dependencies can be declared against it. This reflects in the Nx resolver options to only take script command names (or command groups), but without the option of establishing a dependency across them. This information is already part of what Nx provides:

```typescript
config({
  resolvers: [
      {
        kind: "Nx",
        ...
        execute: ["build", "test"],
      }
  ]
});
```