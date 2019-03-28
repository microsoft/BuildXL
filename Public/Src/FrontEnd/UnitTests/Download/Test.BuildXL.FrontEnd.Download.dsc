// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Download {
    @@public
    export const dll = BuildXLSdk.test({
        // TODO: QTest does not respect single threadded runs.
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        assemblyName: "Test.BuildXL.FrontEnd.Download",
        sources: globR(d`.`, "*.cs"),
        references: [
            Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Download.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("SharpZipLib").pkg,
        ],
        runTestArgs: {
            tools: {
                exec: {
                    acquireMutexes: ["Test.BuildXL.FrontEnd.Download.HttpServer"]
                }
            }
        }
    });
}
