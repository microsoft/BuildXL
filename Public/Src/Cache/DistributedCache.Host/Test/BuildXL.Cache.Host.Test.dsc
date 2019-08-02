// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.Host.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Xml.dll
            ]),
            Configuration.dll,
            Service.dll,
        ]
    });
}
