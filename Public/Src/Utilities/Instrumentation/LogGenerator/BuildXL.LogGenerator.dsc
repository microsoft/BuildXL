// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Deployment from "Sdk.Deployment";

namespace LogGenerator {
    // As of March 2024, Roslyn Source Generators only supported netstandard2.0 assemblies.
    // https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview
    export declare const qualifier: BuildXLSdk.TargetFrameworks.MachineQualifier.CurrentWithStandard;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.LogGenerator",
        sources: globR(d`.`, "*.cs"),
        nullable: true,
        references: [
            Core.dll,
            AriaCommon.dll,
            
            // Using Newtonsoft.Json for deserializing Log Gen config, because it self-contained.
            importFrom("Newtonsoft.Json").pkg,

            importFrom("Microsoft.CodeAnalysis.Common").pkg,
            importFrom("Microsoft.CodeAnalysis.CSharp").pkg,
            importFrom("System.Collections.Immutable").pkg,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
        ],
        analyzers: [importFrom("Microsoft.CodeAnalysis.Analyzers").pkg]
    });

    const deployment : Deployment.Definition = {
        contents: [
            dll,
            Core.dll,
            AriaCommon.dll,

            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll
        ]
    };

    const frameworkSpecificPart = BuildXLSdk.isDotNetCoreOrStandard
        ? qualifier.targetFramework + qualifier.targetRuntime
        : qualifier.targetFramework;

    /** Source generator should be referenced with all the required direct dependencies and all of that should be deployed into a separate folder. */
    @@public
    export const deployed = Deployment.deployToDisk({
        definition: deployment,
        deploymentOptions: <Managed.Deployment.FlattenOptions>{
            skipReferences: true,
            skipXml: true
        },
        targetDirectory: d`${Context.getNewOutputDirectory("SourceLogGenerator").path}/${qualifier.configuration}/${frameworkSpecificPart}`
    });
}
