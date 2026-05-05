config(
  {
    resolvers: [
      // A simple Lage-based build
      {
        kind: "Lage",
        moduleName: "test-build",
        root: d`.`,
        nodeExeLocation: f`/opt/hostedtoolcache/node/22.18.0/x64/bin/node`,
        lageLocation: f`node_modules/.bin/lage`,
        execute: ["build", "test"],
        environment: Map.empty<string, EnvironmentData | Unit>()
          .add("PATH", {contents: [
            d`/opt/hostedtoolcache/node/22.18.0/x64/bin`,
            d`/usr/local/bin`, 
            d`/usr/bin`,
            d`/usr/bin/bash`]})
          .add("NO_UPDATE_NOTIFIER", "1")
          .add("npm_config_update_notifier", "false"),
        // Export all the content of build verbs
        exports: [{
             symbolName: "buildContent",
             includeProjectMapping: true,
             content: [{packageNameRegex: ".*", commandRegex: "build" }]
        }],
        untrackedDirectoryScopes: [
          d`/home/cloudtest/.npm/`,
        ]
      },
      {
         kind: "DScript",
         modules: [
          { moduleName: "cloudtest-config", projects:[f`ctest.dsc`] },
          f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.CloudTestClient/module.config.dsc`,
          f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Drop/package.config.dsc`
        ]
      },
    ],
    mounts: [
      {
            name: a`CT-Client`,
            path: f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.CloudTestClient`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
    ]
  });