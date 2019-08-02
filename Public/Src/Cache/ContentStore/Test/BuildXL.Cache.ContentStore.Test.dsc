// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    @@public
    export const categoriesToRunInParallel = [ "Integration1", "Integration2" ];

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.ContentStore.Test",
        sources: globR(d`.`,"*.cs"),
        runTestArgs: {
            parallelGroups: categoriesToRunInParallel,
        },
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.Runtime.Serialization.dll
            ),
            // TODO: This needs to be renamed to just utilities... but it is in a package in public/src
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            UtilitiesCore.dll,
            Hashing.dll,
            Distributed.dll,
            InterfacesTest.dll,
            Interfaces.dll,
            Library.dll,
            App.exe, // Tests launch the server, so this needs to be deployed.
            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),

            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
        runtimeContent: [
            Library.dll,
            importFrom("Grpc.Core").pkg,
        ],
    });
}
