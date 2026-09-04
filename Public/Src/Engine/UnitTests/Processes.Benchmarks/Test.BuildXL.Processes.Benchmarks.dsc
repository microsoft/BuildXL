// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnitV3 from "Sdk.Managed.Testing.XUnitV3";

namespace Processes.Benchmarks {
    export declare const qualifier: BuildXLSdk.Net8PlusQualifier;

    // Compile in normal builds, but run only when explicitly requested.
    const runBenchmarks = Environment.getFlag("[Sdk.BuildXL]runBenchmarks");

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Processes.Benchmarks",
        sources: globR(d`.`, "*.cs"),
        testFramework: XUnitV3.framework,
        skipTestRun: !runBenchmarks,
        references: [
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
