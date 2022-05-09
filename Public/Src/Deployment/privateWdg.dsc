// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Branding from "BuildXL.Branding";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";

namespace PrivateWdg {
    export declare const qualifier : {
        configuration: "debug" | "release";
        targetFramework: "net6.0",
        targetRuntime: "win-x64"
    };

    const deployment : Deployment.Definition = {
        contents: [
            {
                subfolder: r`bxlaslibrary/net6.0`,
                contents: [
                    importFrom("Private.Wdg").deployment,

                    // assemblies referned by WDG for BuildXL as a library
                    importFrom("BuildXL.Engine").Scheduler.dll,
                    importFrom("BuildXL.FrontEnd").Sdk.dll,
                    importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
                    importFrom("BuildXL.Pips").dll,
                    importFrom("BuildXL.Tools").BxlScriptAnalyzer.exe,
                    importFrom("BuildXL.Tools").Execution.Analyzer.exe,
                    importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
                    importFrom("BuildXL.Utilities").dll,
                    importFrom("Private.Wdg.ExecutionLogSdk").dll,
                ]
            }
        ]
    };

    @@public
    export const deployed = BuildXLSdk.Flags.isMicrosoftInternal
        ? BuildXLSdk.DeploymentHelpers.deploy({
                definition: deployment, 
                targetLocation: r`${qualifier.configuration}/private/wdg`, 
            })
        : undefined;
}