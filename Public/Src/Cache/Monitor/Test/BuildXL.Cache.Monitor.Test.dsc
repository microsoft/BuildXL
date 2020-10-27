// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";
import * as XUnit from "Sdk.Managed.Testing.XUnit";
import * as ManagedSdk from "Sdk.Managed";

namespace MonitorTest {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.Monitor.Test",
        sources: globR(d`.`, "*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            // Needed to get Fluent Assertions
            ...BuildXLSdk.fluentAssertionsWorkaround,

            // Needed to access the app's classes
            Library.dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            // Needed to get TestWithOutput
            importFrom("BuildXL.Cache.ContentStore").InterfacesTest.dll,

            // Used for TestGlobal.Logger
            importFrom("BuildXL.Cache.ContentStore").Test.dll,
            
            importFrom("RuntimeContracts").pkg,
            ...azureSdk,
            ...importFrom("BuildXL.Cache.ContentStore").kustoPackages,
        ],
        runTestArgs: {
            tools: {
                exec: {
                    unsafe: {
                        passThroughEnvironmentVariables: [
                            // Used to ensure tests against Azure can run whenever this environment variable is 
                            // available
                            "CACHE_MONITOR_APPLICATION_KEY"
                        ]
                    },
                }
            }
        },
        skipDocumentationGeneration: true,
        nullable: true,
    });
}
