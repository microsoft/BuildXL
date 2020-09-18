// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";
import * as MemoizationStore from "BuildXL.Cache.MemoizationStore";

namespace RoxisTest {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.Roxis.Test",
        sources: globR(d`.`, "*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        runTestArgs: {
            // TODO: without this, we get DFAs on each test run because of RocksDb. Do we really need it?
            untrackTestDirectory: true,
        },
        assemblyBindingRedirects: BuildXLSdk.cacheTestBindingRedirects(),
        appConfig: f`App.config`,
        references: [
            // Assertions
            importFrom("RuntimeContracts").pkg,
            ...BuildXLSdk.fluentAssertionsWorkaround,
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            ...addIf(BuildXLSdk.isFullFramework,
                // This and the assembly redirects above are needed to get GetSelectorsGivesSelectors* tests passing 
                // in .NET framework
                importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({ targetFramework: "netstandard2.0" }).pkg
            ),

            // Project libraries
            Server.dll,
            Client.dll,
            Common.dll,
            Grpc.dll,

            // IClock, etc
            ContentStore.Distributed.dll,
            ContentStore.Hashing.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Interfaces.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Library.dll,
            ContentStore.Test.dll,
            ContentStore.Grpc.dll,
            MemoizationStore.Interfaces.dll,
            MemoizationStore.InterfacesTest.dll,
            MemoizationStore.Library.dll,
            MemoizationStore.Distributed.dll,
            

            importFrom("BuildXL.Utilities").dll,
            ...importFrom("BuildXL.Cache.ContentStore").redisPackages,
            ...BuildXLSdk.bclAsyncPackages,
            ...BuildXLSdk.fluentAssertionsWorkaround,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            

            // Property-based testing
            importFrom("FsCheck").pkg,
            importFrom("FSharp.Core").pkg,
        ],
        skipDocumentationGeneration: true,
        nullable: true,
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume FsCheck
            },
        },
    });
}
