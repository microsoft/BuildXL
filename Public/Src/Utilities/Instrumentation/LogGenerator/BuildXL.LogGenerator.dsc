// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Deployment from "Sdk.Deployment";

namespace LogGenerator {
    export declare const qualifier: BuildXLSdk.NetStandardQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.LogGenerator",
        sources: globR(d`.`, "*.cs"),
        nullable: true,
        references: [
            Core.dll,
            Common.dll,
            
            // Using Newtonsoft.Json for deserializing Log Gen config, because it self-contained.
            importFrom("Newtonsoft.Json").pkg,

            importFrom("Microsoft.CodeAnalysis.Common.ForVBCS").pkg,
            importFrom("Microsoft.CodeAnalysis.CSharp.ForVBCS").pkg,
            importFrom("System.Collections.Immutable.ForVBCS").pkg,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
        ],
        analyzers: [importFrom("Microsoft.CodeAnalysis.Analyzers").pkg]
    });

    const deployment: Deployment.Definition = {
        contents: [
            dll,
            Core.dll,
            Common.dll,
            
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
        ]
    };

    const frameworkSpecificPart = BuildXLSdk.isDotNetCoreBuild
        ? qualifier.targetFramework + qualifier.targetRuntime
        : qualifier.targetFramework;

    /** Source generator should be referenced with all the required direct dependencies and all of that should be deployed into a separate folder. */
    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`SourceLogGenerator/${qualifier.configuration}/${frameworkSpecificPart}`,
        deploymentOptions: {
            // Deploying only the dll's itself without extra dependencies
            skipReferences: true
        },
    });
}
