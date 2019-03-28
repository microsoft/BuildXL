// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TestGenerator {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Script.Testing.TestGenerator",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            exe,
        ],
    });
}
