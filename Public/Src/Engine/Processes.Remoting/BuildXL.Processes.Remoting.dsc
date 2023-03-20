// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Processes.Remoting {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Processes.Remoting",
        sources: [
            ...globR(d`.`, "*.cs"),
            ...GrpcSdk.generateCSharp({
                    proto: [f`Proto/Remote.proto`]
                }).sources
            ],
        references: [
            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                importFrom("System.Text.Json").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            ]),

            ...addIf(BuildXLSdk.isFullFramework,
                BuildXLSdk.NetFx.System.IO.Compression.dll,
                BuildXLSdk.NetFx.System.Management.dll,
                BuildXLSdk.NetFx.System.Net.Http.dll,
                NetFx.Netstandard.dll
            ),

            Processes.dll,

            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            
            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                  importFrom("AnyBuild.SDK").pkg,
            ]),
            ...importFrom("BuildXL.Cache.ContentStore").getProtobufPackages(),
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Processes",
            "ExternalToolTest.BuildXL.Scheduler",
            "BuildXL.ProcessPipExecutor",
        ],
    });
}
