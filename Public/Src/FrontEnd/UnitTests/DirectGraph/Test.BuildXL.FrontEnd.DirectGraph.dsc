// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Test.DirectGraph {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    // The sources of the DirectGraph example, built here as well so the integration test has an emitter. Not recursive:
    // a `dotnet build` of the example leaves generated sources in subfolders.
    export const graphEmitter = BuildXLSdk.executable({
        assemblyName: "GraphEmitter",
        skipDefaultReferences: true,
        sources: glob(d`${Context.getMount("SourceRoot").path}/Examples/DirectGraph/GraphEmitter`, "*.cs"),
        references: [
            importFrom("BuildXL.Engine").DirectGraph.dll,
            importFrom("Newtonsoft.Json").pkg,
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
        ],
    });

    // Not on macOS: Grpc.Core, which the DirectGraph server uses, has no macOS arm64 native library.
    @@public
    export const dll = !BuildXLSdk.isTargetRuntimeOsx && BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.DirectGraph",
        sources: [
            ...globR(d`SchedulingTests`, "*.cs"),
            ...globR(d`IntegrationTests`, "*.cs"),
        ],
        runTestArgs: {
            unsafeTestRunArguments: {
                // The integration tests run sandboxed processes themselves, so the test runner is not detoured
                runWithUntrackedDependencies: !BuildXLSdk.Flags.IsEBPFSandboxForTestsEnabled,
            },
        },
        references: [
            importFrom("BuildXL.Engine").DirectGraph.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").DirectGraph.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnitV3.dll,
            Script.dll,
            Core.dll,
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
        ],
        runtimeContent: [
            {
                // CODESYNC: IntegrationTests/DirectGraphIntegrationTests.cs
                subfolder: r`tools/GraphEmitter`,
                contents: [graphEmitter],
            },
        ],
    });
}
