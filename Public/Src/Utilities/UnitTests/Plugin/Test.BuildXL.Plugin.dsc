// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace Plugin {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Plugin",
        appConfig: f`App.Config`,
        sources: globR(d`.`, "*.cs"),
        // This disables using QTest for this test. For an unknown reason, QTest breaks the test.
        testFramework: XUnit.framework,
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").PluginGrpc.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(false),
        ],
        runtimeContent: [
            importFrom("Sdk.Protocols.Grpc").runtimeContent,
        ]
    });
}
