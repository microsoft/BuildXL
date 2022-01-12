// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.Host.Test",
        sources: globR(d`.`,"*.cs"),
        runTestArgs: {
            // Need to untrack the test output directory, because redis server tries to write some pdbs.
            untrackTestDirectory: true,
            parallelBucketCount: 4,
            unsafeTestRunArguments: {
                untrackedPaths: [
                    f`D:\a\1\s\msvs\x64\RELEASE_DEVELOPER\memurai-services.pdb`,
                    f`D:\a\1\s\msvs\x64\RELEASE_DEVELOPER\redis-server.pdb`,
                ],
            },
        },
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
            ]),
            ...importFrom("BuildXL.Cache.ContentStore").getSerializationPackages(true),
            Configuration.dll,
            Service.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.ContentStore").DistributedTest.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Test.dll,
            importFrom("BuildXL.Cache.ContentStore").InterfacesTest.dll,
            importFrom("BuildXL.Cache.ContentStore").Grpc.dll,
            ...BuildXLSdk.fluentAssertionsWorkaround,

            // Used by Launcher integration test
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Cache.ContentStore").App.exe,
            ...addIfLazy(!BuildXLSdk.isFullFramework, () => [LauncherServer.exe]
            )
        ],
        tools: {
            csc: {
                noWarnings: [
                    8002, // References ContentStoreApp.exe which is not signed because it uses CLAP
                ]
            },

        },
    });
}
