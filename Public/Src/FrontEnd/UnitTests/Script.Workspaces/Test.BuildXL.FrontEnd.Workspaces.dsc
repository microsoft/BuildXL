// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Workspaces {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Workspaces",
        sources: globR(d`.`, "*.cs"),
        references: [
            Core.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            Workspaces.Utilities.dll,
        ],
        runtimeContent: [
            {
                subfolder: a`Libs`,
                contents: glob(d`../Libs`),
            },
        ],
    });
}
