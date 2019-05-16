// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

namespace Server {

    export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "bxp-server",
        rootNamespace: "BuildXL.Explorer.Server",
        skipDocumentationGeneration: true,
        // We filter out obj and bin folders since we sometimes still develop with an msbuild file for F5 debugging of aspnet apps which is not yet available in BuildXL's IDE integraiotn.
        sources: globR(d`.`, "*.cs").filter(f => !f.isWithin(d`obj`) && !f.isWithin(d`bin`)),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
              // TODO: revisit this!
              Managed.Factory.createBinary(importFrom("Microsoft.NETCore.App").Contents.all, r`ref/netcoreapp3.0/netstandard.dll`)
            ),

            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Tools").Execution.Analyzer.exe,

            // Aspnet assemlbies
            importFrom("Microsoft.AspNetCore").pkg,
            importFrom("Microsoft.AspNetCore.Diagnostics").pkg,
            importFrom("Microsoft.AspNetCore.Hosting").pkg,
            importFrom("Microsoft.AspNetCore.Hosting.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Http").pkg,
            importFrom("Microsoft.AspNetCore.Http.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Http.Features").pkg,
            importFrom("Microsoft.AspNetCore.HttpsPolicy").pkg,
            importFrom("Microsoft.AspNetCore.Mvc").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.ApiExplorer").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Core").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Cors").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.DataAnnotations").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Formatters.Json").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.ViewFeatures").pkg,
            importFrom("Microsoft.AspNetCore.Razor.Runtime").pkg,
            importFrom("Microsoft.Extensions.Caching.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Caching.Memory").pkg,
            importFrom("Microsoft.Extensions.Configuration").pkg,
            importFrom("Microsoft.Extensions.Configuration.Abstractions").pkg,
            importFrom("Microsoft.Extensions.DependencyInjection.Abstractions").pkg,
            importFrom("Microsoft.Extensions.DependencyInjection").pkg,
            importFrom("Microsoft.Extensions.FileProviders.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Primitives").pkg,

            importFrom("Newtonsoft.Json").pkg,
        ],
    });
}
