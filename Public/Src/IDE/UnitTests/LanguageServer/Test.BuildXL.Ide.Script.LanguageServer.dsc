// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as FrontEndTests from "BuildXL.FrontEndUnitTests";
import * as Managed from "Sdk.Managed";

namespace LanguageService.Server {
    /**
     * We need to deploy the prelude SDK with this test so the
     * workspace parses successfully.
     * 
     * So, we will just grab it from the BuildXL Bin directory and create
     * a deployment definition for it.
     */

    const buildEngineDirectory = Context.getBuildEngineDirectory();

    const sdkPreludePath = buildEngineDirectory.combine(r`Sdk\Sdk.prelude`);

    @@public
    export const test = BuildXLSdk.test({
        assemblyName: "BuildXL.DScript.LanguageServer.UnitTests",
        sources: globR(d`.`, "*.cs"),
        skipAssemblySigning: true,
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        runtimeContent: [
            {
                subfolder: a`testdata`,
                contents: [
                    Deployment.createFromDisk(d`testdata/TestProject`),
                    {
                        subfolder: a`prelude`,
                        contents: [
                            Deployment.createFromDisk(d`${sdkPreludePath}`),
                        ]
                    }
                ]
            },
            {
                subfolder: a`Libs`,
                contents: FrontEndTests.Script.TestBase.preludeFiles,
            }
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll
            ),
            artifact,
            IDE.Shared.JsonRpc.dll,
            FrontEndTests.Core.dll,
            LanguageService.Protocol.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.FrontEndUnitTests").Workspaces.Utilities.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,

            importFrom("StreamJsonRpc").pkg,
            importFrom("Microsoft.VisualStudio.Validation").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.VisualStudio.Threading").pkg,
            importFrom("VisualStudio.LanguageServer.Protocol").pkg,
            importFrom("Nerdbank.FullDuplexStream").pkg,
            importFrom("Validation").pkg,
        ],
    });
}
