# Getting cache hits in a distributed scenario
BuildXL's safe caching relies on closely monitoring what each tool is doing, including the environment the tool accesses. This means any difference there can block cache hits from ocurring. In a distributed build, machine differences in layout, user account, etc. can cause trouble in this respect.

## Controlling the environment
The Rush resolver allows a way to control the environment that is exposed to tools. When not specified, tools get the current process environment. Restricting the environment to variables that are actually needed by the build improves the chance of cache hits. It is also a good practice to avoid hidden behavior to 'leak' into the build through env variables.

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