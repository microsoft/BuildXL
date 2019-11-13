// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace GrpcTest {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "Microsoft.ContentStore.Grpc.Test",
        sources: globR(d`.`,"*.cs"),
        runTestArgs: {
            parallelGroups: [ "Integration" ],
        },
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),

            App.exe, // Tests launch the server, so this needs to be deployed.
            Grpc.dll,
            Test.dll,
            Hashing.dll,
            Library.dll,
            Interfaces.dll,
            UtilitiesCore.dll,
            InterfacesTest.dll,

            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
        runtimeContent: [
            importFrom("Sdk.Protocols.Grpc").runtimeContent,
        ],
    });
}
