// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Script.Debugger {

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Script.Debugger",
        sources: globR(d`.`, "*.cs"),
        skipTestRun: true, // TODO: Debugger test takes a very long time.
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.Compression.dll
            ),
            Core.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Ide").VSCode.DebugProtocol.dll,
            importFrom("BuildXL.Ide").VSCode.DebugAdapter.dll,
            importFrom("BuildXL.Ide").Script.Debugger.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnit.dll,
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("System.Collections.Immutable").pkg
            ),
        ],
        runtimeContent: [
            {
                subfolder: a`Libs`,
                contents: glob(d`../Libs`),
            },
        ],
    });
}
