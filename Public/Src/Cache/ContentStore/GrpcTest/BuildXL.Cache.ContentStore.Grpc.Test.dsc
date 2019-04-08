// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace GrpcTest {
    @@public
    export const dll = BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.cacheTest({
        assemblyName: "Microsoft.ContentStore.Grpc.Test",
        sources: globR(d`.`,"*.cs"),
        runTestArgs: {
            parallelGroups: [ "Integration" ],
        },
        skipTestRun: BuildXLSdk.restrictTestRunToDebugNet461OnWindows,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            ...(BuildXLSdk.isDotNetCoreBuild
                // TODO: This is to get a .Net Core build, but it may not pass tests
                ? [importFrom("System.Data.SQLite").withQualifier({targetFramework: "net461"}).pkg]
                : [importFrom("System.Data.SQLite").pkg]
            ),
            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,
        
            Grpc.dll,
            Interfaces.dll,
            Hashing.dll,
            UtilitiesCore.dll,
            InterfacesTest.dll,
            Library.dll,
            Test.dll,
            App.exe, // Tests launch the server, so this needs to be deployed.
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
        runtimeContent: [
            importFrom("Sdk.Protocols.Grpc").runtimeContent,
        ],
    });
}
