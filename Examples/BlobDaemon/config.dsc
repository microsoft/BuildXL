// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Minimal BlobDaemon validation build.
// A single pip produces a file which is then uploaded to Azure Blob Storage via BlobDaemon.
// The in-box BlobDaemon SDK (and its tool binaries under Sdk/Daemon.Bin) ships with the BuildXL deployment,
// so we reference it from the build engine directory.
config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                { moduleName: "blobdaemon-validation", projects: [f`blobupload.dsc`] },
                f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.BlobDaemon/module.config.dsc`,
                f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Transformers/package.config.dsc`,
            ]
        },
    ],
    mounts: [
        // Make the BlobDaemon SDK specs readable and non-scrubbable.
        {
            name: a`BlobDaemon-Sdk`,
            path: f`${Context.getBuildEngineDirectory()}/Sdk/Sdk.BlobDaemon`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        // The BlobDaemon tool binaries live in the shared Daemon.Bin folder (referenced by the tool literal via
        // '../Daemon.Bin'); make them readable and non-scrubbable.
        {
            name: a`BlobDaemon-Bin`,
            path: f`${Context.getBuildEngineDirectory()}/Sdk/Daemon.Bin`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
    ]
});
