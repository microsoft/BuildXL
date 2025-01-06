// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace NugetPacker {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "NugetPacker",
        skipDocumentationGeneration: true,
        skipDefaultReferences: true,
        sources: globR(d`.`, "*.cs"),
        references:[
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,

            importFrom("NuGet.Versioning").pkg,
            importFrom("NuGet.Protocol").pkg,
            importFrom("NuGet.Configuration").pkg,
            importFrom("NuGet.Common").pkg,
            importFrom("NuGet.Frameworks").pkg,
            importFrom("NuGet.Packaging").pkg,
        ],
        runtimeContent: [
            f`App.config`,
        ],
    });

    @@public
    export const tool : Transformer.ToolDefinition = BuildXLSdk.deployManagedTool({
        tool: exe,
        options: {
            description: "BuildXL NuGet Packer",
            prepareTempDirectory: true,
        },
     });
}