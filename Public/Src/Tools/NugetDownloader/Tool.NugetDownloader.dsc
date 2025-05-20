// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Shared from "Sdk.Managed.Shared";

namespace NugetDownloader {

    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;
 
    // These are the specs that define the nuget downloader SDK. Since this is a bxl-provided tool
    // and customers don't have direct exposure to it, they are not places under the usual SDK folder, but
    // deployed as part of bxl binaries
    @@public
    export const specDeployment: Deployment.Definition = {
        contents: [
            {subfolder: r`Sdk/Sdk.NugetDownloader`, contents: [
                {file: f`LiteralFiles/Tool.NugetDownloader.dsc.literal`, targetFileName: a`Tool.NugetDownloader.dsc`},
                {file: f`LiteralFiles/module.config.dsc.literal`, targetFileName: a`module.config.dsc`},]
            }
        ]
    };    

    @@public
    export const dll = BuildXLSdk.executable({
        assemblyName: "NugetDownloader",
        skipDocumentationGeneration: true,
        skipDefaultReferences: true,
        sources: globR(d`.`, "*.cs"),
        references:[
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.Netstandard.dll,
                NetFx.System.Net.Http.dll
            ),
            importFrom("BuildXL.Utilities").VstsAuthentication.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,

            importFrom("NuGet.Versioning").pkg,
            importFrom("NuGet.Protocol").pkg,
            importFrom("NuGet.Configuration").pkg,
            importFrom("NuGet.Common").pkg,
            importFrom("NuGet.Frameworks").pkg,
            importFrom("NuGet.Packaging").pkg,
            importFrom("System.Security.Cryptography.Pkcs").pkg
        ],
        runtimeContent: [
            f`App.config`,
            specDeployment
        ],
    });
}


