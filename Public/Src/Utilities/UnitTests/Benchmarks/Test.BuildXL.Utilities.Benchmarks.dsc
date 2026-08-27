// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnitV3 from "Sdk.Managed.Testing.XUnitV3";

namespace Benchmarks {
    export declare const qualifier: BuildXLSdk.Net8PlusQualifier;

    // Benchmarks use xUnit as a manual runner but must not be scheduled by automation unless explicitly enabled.
    const runBenchmarks = Environment.getFlag("[Sdk.BuildXL]runBenchmarks");

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.Benchmarks",
        sources: globR(d`.`, "*.cs"),
        testFramework: XUnitV3.framework,
        skipTestRun: !runBenchmarks,
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
