// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Script {

    @@public
    export const categoriesToRunInParallel = [
        "Parsing",
        "Office",
        "PartialEvaluation",
        "Transformers",
    ];

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Script",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            parallelGroups: categoriesToRunInParallel,
            weight: 2, //increase weight for frequent timeout pip
        },
        references: [
            Script.TestBase.dll,
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Reflection.dll,
                NetFx.System.IO.Compression.dll,
                NetFx.System.Text.RegularExpressions.dll
            ),
            Core.dll,
            Workspaces.dll,
            Workspaces.Utilities.dll,
            importFrom("BuildXL.App").Main.exe,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.UnitTests").Configuration.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("System.Collections.Immutable").pkg
            ),
        ],
    });
}
