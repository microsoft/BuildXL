// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

namespace Server {

    export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

    @@public
    export const exe = BuildXLSdk.Flags.excludeBuildXLExplorer ? undefined : BuildXLSdk.executable({
        assemblyName: "bxp-server",
        rootNamespace: "BuildXL.Explorer.Server",
        skipDocumentationGeneration: true,
        // We filter out obj and bin folders since we sometimes still develop with an msbuild file for F5 debugging of aspnet apps which is not yet available in BuildXL's IDE integraiotn.
        sources: (<File[]>globR(d`.`, "*.cs")).filter(f => !f.isWithin(d`obj`) && !f.isWithin(d`bin`)),
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
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Tools").Execution.Analyzer.exe,

            // Aspnet assemlbies
            importFrom("Microsoft.AspNetCore.Antiforgery").pkg,
            importFrom("Microsoft.AspNetCore.Authentication.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Authentication.Core").pkg,
            importFrom("Microsoft.AspNetCore.Authorization.Policy").pkg,
            importFrom("Microsoft.AspNetCore.Authorization").pkg,
            importFrom("Microsoft.AspNetCore.Connections.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Cors").pkg,
            importFrom("Microsoft.AspNetCore.Cryptography.Internal").pkg,
            importFrom("Microsoft.AspNetCore.DataProtection.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.DataProtection").pkg,
            importFrom("Microsoft.AspNetCore.Diagnostics.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Diagnostics").pkg,
            importFrom("Microsoft.AspNetCore.HostFiltering").pkg,
            importFrom("Microsoft.AspNetCore.Hosting.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Hosting.Server.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Hosting").pkg,
            importFrom("Microsoft.AspNetCore.Html.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Http.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Http.Extensions").pkg,
            importFrom("Microsoft.AspNetCore.Http.Features").pkg,
            importFrom("Microsoft.AspNetCore.Http").pkg,
            importFrom("Microsoft.AspNetCore.HttpOverrides").pkg,
            importFrom("Microsoft.AspNetCore.HttpsPolicy").pkg,
            importFrom("Microsoft.AspNetCore.JsonPatch").pkg,
            importFrom("Microsoft.AspNetCore.Localization").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.ApiExplorer").pkg,
            importFrom("Microsoft.AspNetCore.Mvc").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Core").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Cors").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.DataAnnotations").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Formatters.Json").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Localization").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Razor.Extensions").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.Razor").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.RazorPages").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.TagHelpers").pkg,
            importFrom("Microsoft.AspNetCore.Mvc.ViewFeatures").pkg,
            importFrom("Microsoft.AspNetCore.Razor.Language").pkg,
            importFrom("Microsoft.AspNetCore.Razor.Runtime").pkg,
            importFrom("Microsoft.AspNetCore.Razor").pkg,
            importFrom("Microsoft.AspNetCore.ResponseCaching.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Routing.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Routing").pkg,
            importFrom("Microsoft.AspNetCore.Server.IIS").pkg,
            importFrom("Microsoft.AspNetCore.Server.IISIntegration").pkg,
            importFrom("Microsoft.AspNetCore.Server.Kestrel.Core").pkg,
            importFrom("Microsoft.AspNetCore.Server.Kestrel.Https").pkg,
            importFrom("Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions").pkg,
            importFrom("Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets").pkg,
            importFrom("Microsoft.AspNetCore.Server.Kestrel").pkg,
            importFrom("Microsoft.AspNetCore.WebUtilities").pkg,
            importFrom("Microsoft.AspNetCore").pkg,
            importFrom("Microsoft.CodeAnalysis.Razor").pkg,
            importFrom("Microsoft.DotNet.PlatformAbstractions").pkg,
            importFrom("Microsoft.Extensions.Caching.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Caching.Memory").pkg,
            importFrom("Microsoft.Extensions.Configuration.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Configuration.Binder").pkg,
            importFrom("Microsoft.Extensions.Configuration.CommandLine").pkg,
            importFrom("Microsoft.Extensions.Configuration.EnvironmentVariables").pkg,
            importFrom("Microsoft.Extensions.Configuration.FileExtensions").pkg,
            importFrom("Microsoft.Extensions.Configuration.Json").pkg,
            importFrom("Microsoft.Extensions.Configuration.UserSecrets").pkg,
            importFrom("Microsoft.Extensions.Configuration").pkg,
            importFrom("Microsoft.Extensions.DependencyInjection.Abstractions").pkg,
            importFrom("Microsoft.Extensions.DependencyInjection").pkg,
            importFrom("Microsoft.Extensions.DependencyModel").pkg,
            importFrom("Microsoft.Extensions.FileProviders.Abstractions").pkg,
            importFrom("Microsoft.Extensions.FileProviders.Composite").pkg,
            importFrom("Microsoft.Extensions.FileProviders.Physical").pkg,
            importFrom("Microsoft.Extensions.FileSystemGlobbing").pkg,
            importFrom("Microsoft.Extensions.Hosting.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Localization.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Localization").pkg,
            importFrom("Microsoft.Extensions.Logging.Abstractions").pkg,
            importFrom("Microsoft.Extensions.Logging.Configuration").pkg,
            importFrom("Microsoft.Extensions.Logging.Console").pkg,
            importFrom("Microsoft.Extensions.Logging.Debug").pkg,
            importFrom("Microsoft.Extensions.Logging.EventSource").pkg,
            
            importFrom("Microsoft.Extensions.Logging").pkg,
            importFrom("Microsoft.Extensions.ObjectPool").pkg,
            importFrom("Microsoft.Extensions.Options.ConfigurationExtensions").pkg,
            importFrom("Microsoft.Extensions.Options").pkg,
            importFrom("Microsoft.Extensions.Primitives").pkg,
            importFrom("Microsoft.Extensions.WebEncoders").pkg,

            importFrom("Microsoft.Net.Http.Headers").pkg,
            importFrom("Newtonsoft.Json").pkg,

            importFrom("System.Buffers").pkg,
            importFrom("System.ComponentModel.Annotations").pkg,
            importFrom("System.Diagnostics.DiagnosticSource").pkg,
            importFrom("System.IO.Pipelines").pkg,
            importFrom("System.Memory").pkg,
            importFrom("System.Numerics.Vectors").pkg,
            importFrom("System.Reflection.Metadata").pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
            importFrom("System.Security.Cryptography.Cng").pkg,
            importFrom("System.Security.Principal.Windows").pkg,
            importFrom("System.Text.Encodings.Web").pkg,
            importFrom("System.Threading.Tasks.Extensions").pkg,
        ],
    });
}
