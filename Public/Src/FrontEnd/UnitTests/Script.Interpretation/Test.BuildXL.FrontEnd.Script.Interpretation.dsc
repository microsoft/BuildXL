// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Script.Interpretation {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Script.Interpretation",
        sources: globR(d`.`, "*.cs"),
        references: [
            Core.dll,
            Script.TestBase.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
        ],
        //increase weight for frequent timeout pip
        runTestArgs: { weight: 2 },
    });
}
