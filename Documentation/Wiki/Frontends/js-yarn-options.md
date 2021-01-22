# Yarn resolver options

The Yarn resolver has the option to pin yarn to a specific file on disk. This is the recommended setting in a lab build scenario. If not specified, BuildXL will try to find Yarn under PATH

```typescript
config({
  resolvers: [
      {
        kind: "Yarn",
        ...
        yarnLocation: f`path/to/yarn.cmd`,
      }
  ]
});
```
