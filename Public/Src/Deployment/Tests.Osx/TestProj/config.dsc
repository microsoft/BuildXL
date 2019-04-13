// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// NOTE: CoreDotNetTests.dsc (as part of BuildXL's deployment) completes this build by:
//   (1) generating 'tests/main.dsc' spec
//   (2) copying test deployments into 'tests' folder
//   (3) copying Public/Sdk/Public/DotNetCore into 'sdk' folder
config({
    modules: [
        d`sdk`,
        d`tests`,
    ].mapMany(dir => ["module.config.dsc", "package.config.dsc"].mapMany(moduleConfigFileName => globR(dir, moduleConfigFileName))),

    mounts: Context.getCurrentHost().os === "macOS" ? [
        {
            name: a`usrbin`,
            path: p`/usr/bin`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        {
            name: a`usrlib`,
            path: p`/usr/lib`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        {
            name: a`usrinclude`,
            path: p`/usr/include`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        {
            name: a`library`,
            path: p`/Library`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
    ] : [],
});
