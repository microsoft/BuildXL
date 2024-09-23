// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace Fuzzing {
    export declare const qualifier : {
        configuration: "debug",
        targetRuntime: "win-x64",
        // Only build this for the latest target framework
        targetFramework: "net8.0"
    };
    
    const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.App.Fuzzing",
        sources: globR(d`.`, "*.cs"),
        addNotNullAttributeFile: true,
        references: [
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            Main.exe,
        ],
    });

    const deployment : Deployment.Definition = {
        contents: [
            dll,
            f`${Context.getMount("SourceRoot").path}/.azdo/onefuzz/OneFuzzConfig.json`,
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        // This path only includes the configuration because we only want to build this for a single framework
        targetLocation: r`${qualifier.configuration}/onefuzz`
    });
}

