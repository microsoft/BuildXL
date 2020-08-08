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
        assemblyBindingRedirects: [
            // System.Memory 4.5.4 is a bit weird, because net461 version references System.Buffer.dll v.4.0.3.0
            // but System.Memory.dll from netstandard2.0 references Ssstem.Buffer.dll v.4.0.2.0!
            // And the rest of the world references System.Buffer.dll v.4.0.3.0
            // So we need to have a redirect to solve this problem.
            {
                name: "System.Buffers",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "4.0.3.0",
            },
            // Different packages reference different version of this assembly.
            {
                name: "System.Runtime.CompilerServices.Unsafe",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "4.0.6.0",
            },
            {
                name: "System.Numerics.Vectors",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-4.1.4.0",
                newVersion: "4.1.4.0",
            },
        ],
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
            importFrom("System.Data.SQLite.Core").pkg,
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
