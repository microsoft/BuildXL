config(
  {
    resolvers: [
      // A Lage-based monorepo with 3 packages:
      //   A (shared math lib) - build, lint
      //   B (depends on A)    - build, lint, test:e2e (depends on build)
      //   C (depends on A)    - build, lint, test:component (depends on build)
      // build: tsc transpilation; lint and test:* verbs are no-ops.
      // test:* verbs represent the points where CT jobs can be scheduled.
      {
        kind: "Lage",
        moduleName: "test-build",
        root: d`.`,
        nodeExeLocation: f`/opt/hostedtoolcache/node/22.18.0/x64/bin/node`,
        lageLocation: f`node_modules/.bin/lage`,
        execute: ["build", "lint", "test:e2e", "test:component"],
        environment: Map.empty<string, EnvironmentData | Unit>()
          .add("PATH", {contents: [
            d`/opt/hostedtoolcache/node/22.18.0/x64/bin`,
            d`/usr/local/bin`, 
            d`/usr/bin`,
            d`/usr/bin/bash`]})
          .add("NO_UPDATE_NOTIFIER", "1")
          .add("npm_config_update_notifier", "false"),
        // Export the verbs representing CT jobs.
        exports: [{
             symbolName: "ctverbs",
             includeProjectMapping: true,
             content: [{packageNameRegex: ".*", commandRegex: "test:e2e|test:component" }]
        }],
        // Just for debugging.
        keepProjectGraphFile: true,
        untrackedDirectoryScopes: [
          d`/home/cloudtest/.npm/`,
        ]
      },
      {
         kind: "DScript",
         modules: [
          // This file defines how to 'glue' together the Lage build and the CT job submission part.
          { moduleName: "cloudtest-config", projects:[f`ctest.dsc`] },
          // This file provides some utilities to interact with the CloudTest service, such as creating sessions and submitting jobs. This is something
          // different 1JS repos (e.g. Midgard and OOUI) can share.
          { moduleName: "cloudtest-1js-sdk", projects:[f`1js-ct-sdk.dsc`] },
          f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.CloudTestClient/module.config.dsc`,
          f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Drop/package.config.dsc`,
          f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Transformers/package.config.dsc`,
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